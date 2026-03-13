/*
 * Throw system: grab attempt -> hitbox -> hold (root motion) -> release (bake pose, damage or get-up).
 * Lives in a partial of Combat so it shares comboSet, animator, grabSocket, threatSystem, etc.
 *
 * Flow: DoThrow() starts attempt -> Update checks throwHitboxTriggerTime -> ExecuteThrowHitbox() on connect
 *       -> victim pseudo-parent follows grabSocket position -> animation events OnThrowUnparent / OnThrowDamage / OnThrowRelease
 *       -> LateUpdate runs deferred release: ReleaseThrowVictimFromSocket, BakePlayer, CompleteThrowRelease.
 */

using UnityEngine;
using System.Linq;

public partial class Combat
{
    #region Throw state (synced: grab attempt -> hitbox -> hold -> release)
    // True when we're in a throw attempt and the grab hitbox hasn't fired yet; Update will trigger it at throwHitboxTriggerTime.
    private bool pendingThrowHitbox;
    // Time at which the grab hitbox should be evaluated (DoThrow sets this to now + hitboxDelay).
    private float throwHitboxTriggerTime;
    // The enemy we're currently holding in a throw (null when not throwing). Used for release, damage, and collision ignore.
    private IDamageable currentThrowVictim;
    // Next time we're allowed to start a new throw (cooldown).
    private float nextThrowTime;
    // Stored applyRootMotion value for victim animator so we can restore it when we release.
    private bool _throwVictimRootMotionRestore;
    // True if we changed victim's applyRootMotion during this throw (so we know to restore in BakeVictimThrowRootMotionAndRestore).
    private bool _throwVictimRootMotionChanged;
    // Same for player animator during throw.
    private bool _playerThrowRootMotionRestore;
    private bool _playerThrowRootMotionChanged;
    // Set by OnThrowRelease animation event; consumed in LateUpdate to run release after root motion has been applied this frame.
    private bool _deferThrowReleaseToLateUpdate;
    // Which release profile (damage/knockback) to use when we run the deferred release; -1 = default.
    private int _deferThrowReleaseProfileIndex;
    // True when the throw was committed with stick back (back throw); used for back vs neutral anim/state names.
    private bool _currentThrowIsBack;
    // Set by OnThrowDamage animation event; consumed in LateUpdate to apply throw damage on the exact frame.
    private bool _deferThrowDamageToLateUpdate;
    private int _deferThrowDamageProfileIndex;
    // When we release without launching, we reapply the baked victim position next frame so physics doesn't snap them.
    private Transform _reapplyThrowBakeTransform;
    private Vector3 _reapplyThrowBakePosition;
    private Quaternion _reapplyThrowBakeRotation;
    private bool _reapplyThrowBakeNextFrame;
    // Victim mesh (animator transform) local pose before we parented; restored when we bake and release.
    private Vector3 _throwVictimMeshLocalPosition;
    private Quaternion _throwVictimMeshLocalRotation;
    // Position-only pseudo-parent state while throw hold is active.
    private Transform _throwVictimPseudoParentTarget;
    private bool _throwVictimPseudoParentActive;
    private Vector3 _throwVictimPseudoParentOffset;
    // Player mesh local pose before throw root motion; restored in BakePlayerThrowRootMotionAndRestore.
    private Vector3 _throwPlayerMeshLocalPosition;
    private Quaternion _throwPlayerMeshLocalRotation;
    #endregion

    // Stick Y below this (camera-relative) counts as "back" for back-throw variant.
    const float BackThrowStickThreshold = 0.4f;

    /// <summary>True when stick is clearly backward (camera-relative). Used at throw commit for back vs neutral.</summary>
    bool IsBackThrowStickInput()
    {
        Vector2 stick = playerController != null ? playerController.CombatStickInput : GetRawStickInput();
        return stick.y < -BackThrowStickThreshold;
    }

    #region Throw (attempted grab -> hitbox -> hold -> release)
    /// <summary>Starts a throw attempt: sets attack state, schedules the grab hitbox after hitboxDelay, plays grab-attempt anim.</summary>
    void DoThrow()
    {
        ThrowData t = comboSet.throwData;
        if (!t.enableThrow) return;
        _currentThrowIsBack = IsBackThrowStickInput(); // could use improvement, for other types of throws using different input methods
        currentAttackStartTime = Time.time;
        nextThrowTime = Time.time + t.throwCooldown;           // Cooldown so we can't immediately throw again (could use improvement, for other types of throws using different cooldown methods)
        currentAttackEndTime = Time.time + t.attemptLockDuration; // Fallback end time; real end set when hitbox connects
        currentAttackStateName = t.grabAttemptAnimationTrigger;
        currentThrowVictim = null;                              // No victim until hitbox connects
        isAttacking = true;
        hitboxPending = false;
        hitboxHasFired = false;
        pendingThrowHitbox = true;                              // Update will call ExecuteThrowHitbox at throwHitboxTriggerTime
        throwHitboxTriggerTime = Time.time + t.hitboxDelay;
        if (animator != null && !string.IsNullOrEmpty(t.grabAttemptAnimationTrigger))
            animator.Play(t.grabAttemptAnimationTrigger, 0, 0f);
        PlayThrowCues(AttackSfxTriggerType.OnAttackStart, 0);
    }

    /// <summary>OverlapSphere at throw hitbox center; returns first IDamageable with EnemyHealth (excluding self).</summary>
    bool TryFindThrowVictim(ThrowData t, out EnemyHealth victim, out IDamageable victimDamageable)
    {
        victim = null;
        victimDamageable = null;
        Vector3 center = CalculateThrowHitboxCenter(t);
        Collider[] hits = Physics.OverlapSphere(center, t.hitboxRadius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            var damageable = c.GetComponentInParent<IDamageable>();
            if (damageable == null || (damageable as Component)?.gameObject == gameObject) continue; // Skip self
            var eh = (damageable as Component)?.GetComponent<EnemyHealth>();
            if (eh == null) continue; // Only grab enemies that have EnemyHealth (and thus throw/get-up support)
            victim = eh;
            victimDamageable = damageable;
            return true;
        }
        return false;
    }

    /// <summary>Starts position-only pseudo-parent follow to grabSocket (no real parenting, no scale inheritance).</summary>
    void AttachVictimToGrabSocket(Transform victimTransform) // Snap the grabbed enemy to our hold socket.
    {
        if (grabSocket == null) return; // If no socket is assigned, we cannot attach.
        _throwVictimPseudoParentTarget = victimTransform;
        _throwVictimPseudoParentOffset = Vector3.zero;
        _throwVictimPseudoParentActive = true;
        victimTransform.position = grabSocket.position; // Start snapped to socket; follow updates in LateUpdate.
        Vector3 toPlayer = transform.position - victimTransform.position; // One-time facing snap at throw start.
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.0001f)
            victimTransform.rotation = Quaternion.LookRotation(toPlayer);
        else
            victimTransform.rotation = Quaternion.LookRotation(-transform.forward);
    }

    void UpdateThrowVictimPseudoParent()
    {
        if (!_throwVictimPseudoParentActive || _throwVictimPseudoParentTarget == null || grabSocket == null) return;
        _throwVictimPseudoParentTarget.position = grabSocket.position + _throwVictimPseudoParentOffset;
    }

    void StopThrowVictimPseudoParent()
    {
        _throwVictimPseudoParentActive = false;
        _throwVictimPseudoParentTarget = null;
        _throwVictimPseudoParentOffset = Vector3.zero;
    }

    /// <summary>Returns animator state name for victim (back throw vs default, or per-AI thrownStateName).</summary>
    string GetThrownStateName(ThrowData t, EnemyHealth victim)
    {
        if (_currentThrowIsBack && !string.IsNullOrEmpty(t.backEnemyThrownStateName))
            return t.backEnemyThrownStateName;
        var victimAI = victim.GetComponent<SimpleEnemyAI>();
        return (victimAI != null && !string.IsNullOrEmpty(victimAI.thrownStateName)) ? victimAI.thrownStateName : t.enemyThrownStateName;
    }

    /// <summary>Freezes player and victim animators for duration (hit stop); they're unfrozen in ClearThrowState / hit stop logic.</summary>
    void ApplyGrabHitStop(float duration, Transform victimTransform)
    {
        if (duration <= 0f) return;
        hitStopEndTime = Time.time + duration;
        if (animator != null)
        {
            if (!frozenAnimators.Any(f => f.animator == animator))
                frozenAnimators.Add(new FrozenAnimator { animator = animator, originalSpeed = animator.speed });
            animator.speed = 0f;
        }
        var targetAnim = victimTransform.GetComponentInChildren<Animator>();
        if (targetAnim != null && !frozenAnimators.Any(f => f.animator == targetAnim))
        {
            frozenAnimators.Add(new FrozenAnimator { animator = targetAnim, originalSpeed = targetAnim.speed });
            targetAnim.speed = 0f;
        }
    }

    /// <summary>Spawns grab-connect VFX at hitbox center, facing from player toward center.</summary>
    void SpawnGrabConnectVfx(ThrowData t, Vector3 center)
    {
        if (t.grabConnectVfxPrefab == null) return;
        Quaternion rot = (center - transform.position).sqrMagnitude > 0.001f ? Quaternion.LookRotation(center - transform.position) : transform.rotation;
        var go = Instantiate(t.grabConnectVfxPrefab, center, rot);
        PlayVfx(go);
    }

    /// <summary>Plays the player throw animation (or back throw), enables root motion, stores mesh local pose for restore on release.</summary>
    void StartPlayerThrowAnimation(string playerThrowTrigger)
    {
        if (animator == null || string.IsNullOrEmpty(playerThrowTrigger)) return;
        animator.Rebind();
        animator.speed = 1f;
        animator.Play(playerThrowTrigger, 0, 0f);
        _playerThrowRootMotionRestore = animator.applyRootMotion;
        animator.applyRootMotion = true;  // Throw clip drives player position during hold
        _playerThrowRootMotionChanged = true;
        if (animator.transform != transform)
        {
            _throwPlayerMeshLocalPosition = animator.transform.localPosition;
            _throwPlayerMeshLocalRotation = animator.transform.localRotation;
        }
    }

    /// <summary>Runs when the grab hitbox connects: find victim, start pseudo-parent hold, start victim thrown state, hit stop, VFX, play player throw anim and set end time.</summary>
    void ExecuteThrowHitbox()
    {
        ThrowData t = comboSet.throwData;
        if (!t.enableThrow) return;

        if (!TryFindThrowVictim(t, out EnemyHealth victim, out IDamageable victimDamageable)) return;

        currentThrowVictim = victimDamageable;
        Transform victimTransform = (victimDamageable as Component).transform;
        SetThrowVictimCollisionIgnore(victimTransform, true);  // Prevent player and victim colliders from fighting during throw

        AttachVictimToGrabSocket(victimTransform);

        string thrownState = GetThrownStateName(t, victim);
        victim.StartThrowVictim(t.throwPhaseDuration, thrownState);  // Enemy enters thrown state and plays thrown anim

        ApplyGrabHitStop(t.grabHitStopDuration, victimTransform);

        Vector3 center = CalculateThrowHitboxCenter(t);
        SpawnGrabConnectVfx(t, center);

        currentAttackEndTime = Time.time + t.grabHitStopDuration + t.throwPhaseDuration;
        string playerThrowTrigger = (_currentThrowIsBack && !string.IsNullOrEmpty(t.backThrowAnimationTrigger)) ? t.backThrowAnimationTrigger : t.throwAnimationTrigger;
        StartPlayerThrowAnimation(playerThrowTrigger);
        PlayThrowCues(AttackSfxTriggerType.OnHitConfirm, 0);

        if (threatSystem != null)
            threatSystem.RegisterInteraction((victimDamageable as Component).transform);
    }

    void PlayThrowCues(AttackSfxTriggerType trigger, int eventId)
    {
        if (comboSet == null) return;
        ThrowData t = comboSet.throwData;
        if (!t.enableThrow || t.sfxCues == null || t.sfxCues.Count == 0) return;
        for (int i = 0; i < t.sfxCues.Count; i++)
        {
            AttackSfxCue cue = t.sfxCues[i];
            if (cue == null || cue.trigger != trigger) continue;
            if (trigger == AttackSfxTriggerType.OnAnimEvent && cue.eventId != eventId) continue;

            AudioClip chosenClip = null;
            if (cue.clips != null && cue.clips.Length > 0)
                chosenClip = cue.clips[Random.Range(0, cue.clips.Length)];
            if (chosenClip == null) continue;

            PlayAttackSfxClip(chosenClip, cue.volume);
        }
    }

    /// <summary>Bake player's Animator root-motion result into transform and restore applyRootMotion. Call when throw ends (from LateUpdate after deferred release).</summary>
    void BakePlayerThrowRootMotionAndRestore()
    {
        if (!_playerThrowRootMotionChanged || animator == null) return;
        // Snapshot where the animator root ended up in world space (root motion moved it during throw)
        Vector3 bakePosition = animator.transform.position;
        UnityEngine.Debug.Log($"[Throw] Baking player root position: {bakePosition}");
        Quaternion bakeRotation = animator.transform.rotation;
        animator.applyRootMotion = _playerThrowRootMotionRestore;
        _playerThrowRootMotionChanged = false;
        bakePosition.y = transform.position.y;  // Keep feet at ground; only take XZ from baked pose
        Quaternion standingRotation = Quaternion.Euler(0f, bakeRotation.eulerAngles.y, 0f);  // Upright, no tilt/roll
        transform.position = bakePosition;
        transform.rotation = standingRotation;
        if (animator.transform != transform)
        {
            animator.transform.localPosition = _throwPlayerMeshLocalPosition;
            animator.transform.localRotation = _throwPlayerMeshLocalRotation;
        }
    }

    /// <summary>Bake victim's Animator root-motion result into victim transform; restore applyRootMotion and mesh local pose. Call when releasing from throw. Returns baked world pos/rot for optional reapply next frame.</summary>
    (Vector3 bakePosition, Quaternion bakeRotation) BakeVictimThrowRootMotionAndRestore(Transform vt)
    {
        if (vt == null) return (Vector3.zero, Quaternion.identity);
        var victimAnim = vt.GetComponentInChildren<Animator>();
        Vector3 bakePosition = vt.position;
        Quaternion bakeRotation = vt.rotation;
        if (victimAnim != null)
        {
            bakePosition = victimAnim.rootPosition;
            bakeRotation = victimAnim.rootRotation;
            if (_throwVictimRootMotionChanged)
            {
                victimAnim.applyRootMotion = _throwVictimRootMotionRestore;
                _throwVictimRootMotionChanged = false;
            }
        }
        Quaternion standingRotation = Quaternion.Euler(0f, 0f, 0f);
        vt.position = bakePosition;
        vt.rotation = standingRotation;
        if (victimAnim != null && victimAnim.transform != vt)
        {
            victimAnim.transform.localPosition = _throwVictimMeshLocalPosition;
            victimAnim.transform.localRotation = _throwVictimMeshLocalRotation;
        }
        return (bakePosition, standingRotation);
    }

    /// <summary>Bake victim root motion, stop pseudo-parent follow, re-enable CC/Rigidbody, clear knockback; if not launching, schedule reapply of baked pose next frame.</summary>
    void ReleaseThrowVictimFromSocket()
    {
        if (currentThrowVictim == null) return;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;
        StopThrowVictimPseudoParent();
        UnityEngine.Debug.Log($"[Throw] Release victim: vt={vt.name}, pos={vt.position}");

        (Vector3 bakePosition, Quaternion bakeRotation) = BakeVictimThrowRootMotionAndRestore(vt);

        var cc = vt.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;
        var rb = vt.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        var victimHealth = vt.GetComponent<EnemyHealth>();
        if (victimHealth != null)
            victimHealth.ClearKnockback();

        if (comboSet != null && !comboSet.throwData.launchVictimOnRelease)
        {
            _reapplyThrowBakeTransform = vt;
            _reapplyThrowBakePosition = bakePosition;
            _reapplyThrowBakeRotation = bakeRotation;
            _reapplyThrowBakeNextFrame = true;
        }
    }

    /// <summary>Apply release outcome: if launchVictimOnRelease and damage not yet applied, call ApplyThrowDamage; else trigger victim get-up. Then clear collision ignore and ClearThrowState.</summary>
    void CompleteThrowRelease(Transform victimTransform, int releaseProfileIndex, bool damageAlreadyAppliedThisFrame, bool applyReleaseEffects = true)
    {
        if (applyReleaseEffects && comboSet != null && comboSet.throwData.enableThrow)
        {
            if (comboSet.throwData.launchVictimOnRelease && !damageAlreadyAppliedThisFrame)
                ApplyThrowDamage(releaseProfileIndex);  // Launch path: damage/knockback from release profile
            else
            {
                var victimAI = victimTransform.GetComponent<SimpleEnemyAI>();
                if (victimAI != null) victimAI.TriggerGetUpFromThrow();  // Non-launch: just get up
            }
        }
        SetThrowVictimCollisionIgnore(victimTransform, false);
        ClearThrowState();
    }

    /// <summary>Clears throw-related state: victim ref, pseudo-parent follow, isAttacking, hitbox pending, restores animator speeds and clears hit stop.</summary>
    void ClearThrowState()
    {
        StopThrowVictimPseudoParent();
        currentThrowVictim = null;
        isAttacking = false;
        hitboxPending = false;
        pendingThrowHitbox = false;
        ResetChargeState();
        if (animator != null && !frozenAnimators.Any(f => f.animator == animator))
            animator.speed = 1f;
        foreach (var frozen in frozenAnimators)
        {
            if (frozen.animator != null)
                frozen.animator.speed = frozen.originalSpeed;
        }
        frozenAnimators.Clear();
        hitStopEndTime = 0f;
    }

    /// <summary>Animation event compatibility hook: stops pseudo-parent follow early. Full release still runs on OnThrowRelease.</summary>
    public void OnThrowUnparent()
    {
        StopThrowVictimPseudoParent();
    }

    /// <summary>Animation event: toggle victim root motion (0 = off/restore, non-zero = on).</summary>
    public void OnThrowVictimRootMotion(int enabled)
    {
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;
        var victimAnim = vt.GetComponentInChildren<Animator>();
        if (victimAnim == null) return;

        bool enable = enabled != 0;
        if (enable)
        {
            if (!_throwVictimRootMotionChanged)
                _throwVictimRootMotionRestore = victimAnim.applyRootMotion;
            victimAnim.applyRootMotion = true;
            _throwVictimRootMotionChanged = true;
            return;
        }

        if (_throwVictimRootMotionChanged)
        {
            victimAnim.applyRootMotion = _throwVictimRootMotionRestore;
            _throwVictimRootMotionChanged = false;
        }
    }

    /// <summary>Animation event convenience method: turns victim root motion on.</summary>
    public void OnThrowVictimRootMotionOn()
    {
        OnThrowVictimRootMotion(1);
    }

    /// <summary>Animation event convenience method: turns victim root motion off/restores original value.</summary>
    public void OnThrowVictimRootMotionOff()
    {
        OnThrowVictimRootMotion(0);
    }

    /// <summary>Animation event: toggle player throw root motion (0 = off/restore, non-zero = on).</summary>
    public void OnThrowPlayerRootMotion(int enabled)
    {
        if (animator == null) return;

        bool enable = enabled != 0;
        if (enable)
        {
            if (!_playerThrowRootMotionChanged)
                _playerThrowRootMotionRestore = animator.applyRootMotion;
            animator.applyRootMotion = true;
            _playerThrowRootMotionChanged = true;
            return;
        }

        if (_playerThrowRootMotionChanged)
        {
            animator.applyRootMotion = _playerThrowRootMotionRestore;
            _playerThrowRootMotionChanged = false;
        }
    }

    /// <summary>Animation event convenience method: turns player throw root motion on.</summary>
    public void OnThrowPlayerRootMotionOn()
    {
        OnThrowPlayerRootMotion(1);
    }

    /// <summary>Animation event convenience method: turns player throw root motion off/restores original value.</summary>
    public void OnThrowPlayerRootMotionOff()
    {
        OnThrowPlayerRootMotion(0);
    }

    /// <summary>Animation event (no arg): defers full release to LateUpdate with default profile index -1.</summary>
    public void OnThrowRelease()
    {
        OnThrowRelease(-1);
    }

    /// <summary>Animation event (with profile index): defers full release to LateUpdate so bake/release/CompleteThrowRelease run after root motion this frame.</summary>
    public void OnThrowRelease(int releaseProfileIndex)
    {
        if (currentThrowVictim == null || comboSet == null || !comboSet.throwData.enableThrow) return;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;
        _deferThrowReleaseToLateUpdate = true;
        _deferThrowReleaseProfileIndex = releaseProfileIndex;
    }

    /// <summary>Animation event: defers throw damage to LateUpdate so it applies on the exact frame; profileIndex selects release profile or -1 for default.</summary>
    public void OnThrowDamage(int profileIndex)
    {
        if (currentThrowVictim == null || comboSet == null || !comboSet.throwData.enableThrow) return;
        _deferThrowDamageToLateUpdate = true;
        _deferThrowDamageProfileIndex = profileIndex;
    }

    /// <summary>Animation event: spawns throw-end VFX at current victim position (or in front of player if victim is missing).</summary>
    public void OnThrowEndVfxEvent()
    {
        if (comboSet == null || !comboSet.throwData.enableThrow) return;
        ThrowData t = comboSet.throwData;
        if (t.throwEndVfxPrefab == null) return;

        Transform vt = (currentThrowVictim as Component)?.transform;
        Vector3 pos = vt != null ? vt.position : (transform.position + transform.forward);
        Vector3 dir = vt != null ? (vt.position - transform.position) : transform.forward;
        dir.y = 0f;
        Quaternion rot = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : transform.rotation;
        var go = Instantiate(t.throwEndVfxPrefab, pos, rot);
        PlayVfx(go);
    }

    public void OnThrowSfxEvent(int eventId)
    {
        PlayThrowCues(AttackSfxTriggerType.OnAnimEvent, eventId);
    }

    public void OnThrowSfxEvent()
    {
        OnThrowSfxEvent(0);
    }

    /// <summary>Ignore or re-enable collisions between all player colliders and all victim colliders (avoids overlap during throw).</summary>
    void SetThrowVictimCollisionIgnore(Transform victimTransform, bool ignore)
    {
        if (victimTransform == null) return;
        Collider[] playerCols = GetComponentsInChildren<Collider>();
        Collider[] victimCols = victimTransform.GetComponentsInChildren<Collider>();
        foreach (var pc in playerCols)
        {
            if (pc == null || !pc.enabled) continue;
            foreach (var vc in victimCols)
            {
                if (vc == null || !vc.enabled || pc == vc) continue;
                Physics.IgnoreCollision(pc, vc, ignore);
            }
        }
    }

    /// <summary>Apply throw damage/knockback to currentThrowVictim using releaseProfileIndex (or default throw data if -1). No facing, VFX, or threat.</summary>
    void ApplyThrowDamage(int releaseProfileIndex = -1)
    {
        ThrowData t = comboSet.throwData;
        if (!t.enableThrow || currentThrowVictim == null) return;  // Guard: throw disabled or no victim
        Transform victimTransform = (currentThrowVictim as Component)?.transform;
        if (victimTransform == null) return;  // Victim may have been destroyed
        // Direction from player to victim (XZ only) for knockback; used so victim is pushed away from player
        Vector3 horizontalDir = (victimTransform.position - transform.position);
        horizontalDir.y = 0f;
        if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = transform.forward;  // Fallback if player and victim overlap
        horizontalDir.Normalize();
        int damage;
        float knockback, knockbackUp, hitstun;
        if (releaseProfileIndex >= 0 && t.releaseProfiles != null && releaseProfileIndex < t.releaseProfiles.Length)
        {
            var p = t.releaseProfiles[releaseProfileIndex];  // Use per-event profile (e.g. from OnThrowDamage(int))
            damage = p.endDamage;
            knockback = p.endKnockback;
            knockbackUp = p.endKnockbackUp;
            hitstun = p.endHitstun;
        }
        else
        {
            // Use default throw data when profile index is -1 or invalid
            damage = t.endDamage;
            knockback = t.endKnockback;
            knockbackUp = t.endKnockbackUp;
            hitstun = t.endHitstun;
        }
        Vector3 knockbackVector = (horizontalDir * knockback) + (Vector3.up * knockbackUp);  // Horizontal push + vertical (e.g. launch)
        currentThrowVictim.TakeHit(damage, knockbackVector, hitstun, airborneDuration: 0f);  // No airborne from throw
    }

    /// <summary>World position for the grab hitbox sphere (hitOrigin or transform + range and hitboxOffset).</summary>
    Vector3 CalculateThrowHitboxCenter(ThrowData t)
    {
        Transform origin = hitOrigin != null ? hitOrigin : transform;
        return origin.position
            + transform.forward * t.range
            + transform.right   * t.hitboxOffset.x
            + transform.up      * t.hitboxOffset.y
            + transform.forward * t.hitboxOffset.z;
    }
    #endregion
}
