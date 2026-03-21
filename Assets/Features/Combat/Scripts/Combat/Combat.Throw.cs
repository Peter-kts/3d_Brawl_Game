/*
 * Throw system: grab attempt -> hitbox -> hold (root motion) -> release (bake pose, damage or get-up).
 * Lives in a partial of Combat so it shares comboSet, animator, grabSocket, threatSystem, etc.
 *
 * Flow: DoThrow() starts attempt -> Update checks throwHitboxTriggerTime -> ExecuteThrowHitbox() on connect
 *       -> victim pseudo-parent follows grabSocket position -> animation events OnThrowUnparent / OnThrowDamage / OnThrowRelease
 *       -> LateUpdate runs deferred release: ReleaseThrowVictimFromSocket, BakePlayer, CompleteThrowRelease.
 */

using System.Collections;
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
    // True when the throw was committed with stick back (back throw); used for back vs neutral anim/state names.
    private bool _currentThrowIsBack;
    // Active throw config selected at throw start (forward vs back), used for the full throw lifecycle.
    private ThrowData _activeThrowData;
    private bool _hasActiveThrowData;
    // Set by OnThrowDamage animation event; consumed in LateUpdate to apply throw damage on the exact frame.
    private bool _deferThrowDamageToLateUpdate;
    // When we release without launching, we reapply the baked victim position next frame so physics doesn't snap them.
    private Transform _reapplyThrowBakeTransform;
    private Vector3 _reapplyThrowBakePosition;
    private Quaternion _reapplyThrowBakeRotation;
    private bool _reapplyThrowBakeNextFrame;
    // Victim mesh (animator transform) local pose before we parented; restored when we bake and release.
    private Vector3 _throwVictimMeshLocalPosition;
    private Quaternion _throwVictimMeshLocalRotation;

    // Throw charge state — active between OnThrowChargeWindowStart and release/auto-release.
    private bool throwChargeWindowOpen;        // true while the charge window is open (animation event brackets this)
    private bool isChargingThrow;              // player is actively holding the button during the charge window
    private float throwChargeStartTime;        // Time.time when the hold began
    private float throwChargeKnockbackScale = 1f; // baked at release; applied in ApplyThrowDamage
    private float throwChargeDamageScale    = 1f; // baked at release; applied in ApplyThrowDamage
    private Animator _throwVictimAnimator;     // cached at charge start so we can mirror speed changes to the victim
    // True while directional throw rotation is actively steering — OnAnimatorMove skips
    // rotation root motion so our manual RotateTowards is not overwritten each frame.
    private bool _directionalThrowRotationActive;
    // Last computed world-space throw direction; re-enforced in LateUpdate to survive
    // anything that writes transform.rotation between Update and LateUpdate.
    private Quaternion _directionalThrowTargetRotation;
    private bool _hasDirectionalThrowTarget;
    // Same enforcement for the victim — enemy AI/animator can write its rotation back after we set it.
    private Transform _directionalThrowVictimTransform;
    private Quaternion _directionalThrowVictimRotation;
    // The exact world-space direction the throw was committed in — set at charge release and
    // used as the authoritative knockback direction, ignoring socket offsets and bake artifacts.
    private Vector3 _committedThrowDirection;
    private bool _hasCommittedThrowDirection;
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
        // Use raw input here because CombatStickInput can be stale during attack lock.
        Vector2 stick = GetRawStickInput();
        return stick.y < -BackThrowStickThreshold;
    }

    bool IsAnyThrowEnabled()
    {
        // Both forward and back throw are independently configurable now.
        // Treat throw as available if either profile is enabled in ComboSet.
        return comboSet != null && (comboSet.throwData.enableThrow || comboSet.backThrowData.enableThrow);
    }

    ThrowData GetThrowAttemptData()
    {
        if (comboSet == null) return default;
        if (comboSet.throwData.enableThrow)
            return comboSet.throwData;
        if (comboSet.backThrowData.enableThrow)
            return comboSet.backThrowData;
        return comboSet.throwData;
    }

    ThrowData GetThrowDataForInput(bool backThrowRequested)
    {
        if (comboSet == null) return default;
        if (backThrowRequested && comboSet.backThrowData.enableThrow)
            return comboSet.backThrowData;
        if (comboSet.throwData.enableThrow)
            return comboSet.throwData;
        if (comboSet.backThrowData.enableThrow)
            return comboSet.backThrowData;
        return comboSet.throwData;
    }

    bool IsThrowInProgress()
    {
        // "In progress" intentionally includes pre-connect, hold, and deferred-release frames.
        // We use this broad gate to block normal attacks so they cannot corrupt throw ownership.
        return pendingThrowHitbox
            || currentThrowVictim != null
            || _throwVictimPseudoParentActive
            || _deferThrowReleaseToLateUpdate;
    }

    /// <summary>
    /// True when this Combat currently owns the provided throw victim.
    /// Used by victim-side animation-event relay methods (fallback path).
    /// </summary>
    public bool IsHoldingThrowVictim(IDamageable victim)
    {
        if (victim == null) return false;
        if (!_hasActiveThrowData || !_activeThrowData.enableThrow) return false;
        return currentThrowVictim == victim;
    }

    void ForceThrowReleaseFallback(bool applyReleaseEffects)
    {
        // Used when expected animation events are interrupted/missed.
        // This keeps transforms/collision/state from getting stuck in throw mode.
        if (currentThrowVictim == null) return;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null)
        {
            currentThrowVictim = null;
            return;
        }

        // Mirror the standard release order so victim/player transforms and controller state stay consistent.
        ResetThrowChargeState(); // restore animator speed if frozen mid-charge
        BakePlayerThrowRootMotionAndRestore();
        ReleaseThrowVictimFromSocket();
        CompleteThrowRelease(vt, damageAlreadyAppliedThisFrame: true, applyReleaseEffects: applyReleaseEffects);
    }

    #region Throw (attempted grab -> hitbox -> hold -> release)
    /// <summary>Starts a throw attempt: sets attack state, schedules the grab hitbox after hitboxDelay, plays grab-attempt anim.</summary>
    void DoThrow()
    {
        // Snap to face the nearest enemy in the stick direction, same as regular attacks.
        if (threatSystem != null && !threatSystem.IsLockedOn)
        {
            Transform snapTarget = GetBestThreatInFront();
            if (snapTarget != null)
            {
                Vector3 toSnap = snapTarget.position - transform.position;
                toSnap.y = 0f;
                if (toSnap.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(toSnap.normalized, Vector3.up);
            }
        }

        // New committed throw clears the previous interrupt-suppression window.
        suppressHitboxActivationsUntilNextCommit = false;
        _currentThrowIsBack = false; // Throw direction now resolves on grab connect (not at throw begin).
        ThrowData t = GetThrowAttemptData();
        if (!t.enableThrow) return; // Hard gate: do not enter throw flow if no throw profile is active.
        if (!TryConsumeMomentum(t.consumesMomentum, t.momentumCost)) return;
        _activeThrowData = t; // Keep attempt data active for start cues/timing until connect resolves profile.
        _hasActiveThrowData = true;
        currentAttackStartTime = Time.time;
        nextThrowTime = Time.time + t.throwCooldown;           // Cooldown so we can't immediately throw again (could use improvement, for other types of throws using different cooldown methods)
        currentAttackEndTime = Time.time + t.attemptLockDuration; // Fallback end time; real end set when hitbox connects
        currentAttackStateName = t.grabAttemptAnimationTrigger;
        currentThrowVictim = null;                              // No victim until hitbox connects
        isAttacking = true;
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

    /// <summary>Steps the player toward the grab target while the throw hitbox is still pending.</summary>
    void UpdateThrowSuck()
    {
        if (!pendingThrowHitbox) return;
        if (!_hasActiveThrowData || !_activeThrowData.suckToTarget) return;
        if (_activeThrowData.hitboxDelay <= 0f) return;

        Transform target = GetSuckTarget();
        if (target == null) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.001f) return;

        float moveAmount = (_activeThrowData.throwSuckDistance / _activeThrowData.hitboxDelay) * Time.deltaTime;
        ApplyLungeMove(toTarget.normalized, moveAmount, target, _activeThrowData.throwSuckStopDistance);
    }

    /// <summary>Returns animator state name for victim (active throw profile first, then per-AI fallback).</summary>
    string GetThrownStateName(ThrowData t, EnemyHealth victim)
    {
        if (!string.IsNullOrEmpty(t.enemyThrownStateName))
            return t.enemyThrownStateName;

        var victimAI = victim.GetComponent<SimpleEnemyAI>();
        string perEnemyState = (victimAI != null && victimAI.animationConfig != null) ? victimAI.animationConfig.thrownStateName : null;
        return perEnemyState;
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
        if (IsHitboxActivationSuppressed) return; // Ignore stale throw-hitbox timing after damage interrupt.
        ThrowData attemptData = GetThrowAttemptData();
        if (!attemptData.enableThrow) return; // Safety: profile may have changed since throw attempt started.

        // No victim found means whiff: keep existing attack lock behavior and exit quietly.
        if (!TryFindThrowVictim(attemptData, out EnemyHealth victim, out IDamageable victimDamageable)) return;

        // Resolve forward/back throw at connect time using current stick input.
        _currentThrowIsBack = IsBackThrowStickInput();
        ThrowData connectData = GetThrowDataForInput(_currentThrowIsBack);
        if (connectData.enableThrow)
        {
            // Commit to the connect-time profile so subsequent events (damage/release/vfx/prone)
            // use the same throw variant consistently for the rest of this throw.
            _activeThrowData = connectData;
            _hasActiveThrowData = true;
        }
        ThrowData t = _activeThrowData;

        currentThrowVictim = victimDamageable;
        Transform victimTransform = (victimDamageable as Component).transform;
        SetThrowVictimCollisionIgnore(victimTransform, true);  // Prevent player and victim colliders from fighting during throw

        AttachVictimToGrabSocket(victimTransform);

        string thrownState = GetThrownStateName(t, victim);
        victim.StartThrowVictim(t.throwPhaseDuration, thrownState);  // Enemy enters thrown state and plays thrown anim

        ApplyGrabHitStop(t.grabHitStopDuration, victimTransform);

        Vector3 center = CalculateThrowHitboxCenter(t);
        SpawnGrabConnectVfx(t, center);

        nextThrowTime = Time.time + t.throwCooldown; // Use the connected throw profile's cooldown.
        currentAttackEndTime = Time.time + t.grabHitStopDuration + t.throwPhaseDuration;
        StartPlayerThrowAnimation(t.throwAnimationTrigger);
        PlayThrowCues(AttackSfxTriggerType.OnHitConfirm, 0);

        if (threatSystem != null)
            threatSystem.RegisterInteraction((victimDamageable as Component).transform);
    }

    void PlayThrowCues(AttackSfxTriggerType trigger, int eventId)
    {
        if (!_hasActiveThrowData) return; // Guard against stale animation events firing after throw was aborted.
        ThrowData t = _activeThrowData;
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
            // Use grab socket world position as release origin so charge duration doesn't affect launch position.
            bakePosition = grabSocket != null ? grabSocket.position : victimAnim.rootPosition;
            bakeRotation = victimAnim.rootRotation;
            if (_throwVictimRootMotionChanged)
            {
                victimAnim.applyRootMotion = _throwVictimRootMotionRestore;
                _throwVictimRootMotionChanged = false;
            }
        }
        Quaternion standingRotation = Quaternion.Euler(0f, bakeRotation.eulerAngles.y, 0f);
        vt.position = bakePosition;
        vt.rotation = standingRotation;
        if (victimAnim != null && victimAnim.transform != vt)
        {
            victimAnim.transform.localPosition = _throwVictimMeshLocalPosition;
            victimAnim.transform.localRotation = _throwVictimMeshLocalRotation;
        }
        return (bakePosition, standingRotation);
    }

    /// <summary>
    /// Bake victim root motion, stop pseudo-parent follow, re-enable CC/Rigidbody.
    /// Optionally clear existing knockback (useful for cleanup paths, but should stay false for throw-launch release).
    /// If not launching, schedule reapply of baked pose next frame.
    /// </summary>
    void ReleaseThrowVictimFromSocket(bool clearKnockback = true)
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
        if (victimHealth != null && clearKnockback)
            victimHealth.ClearKnockback();

        _reapplyThrowBakeTransform = vt;
        _reapplyThrowBakePosition = bakePosition;
        _reapplyThrowBakeRotation = bakeRotation;
        _reapplyThrowBakeNextFrame = true;
    }

    /// <summary>Apply release outcome: optionally enter prone, then clear collision ignore and throw state. Damage is applied only by OnThrowDamage events.</summary>
    void CompleteThrowRelease(Transform victimTransform, bool damageAlreadyAppliedThisFrame, bool applyReleaseEffects = true)
    {
        if (applyReleaseEffects && _hasActiveThrowData && _activeThrowData.enableThrow && ShouldEnterProneOnThrowRelease(victimTransform))
        {
            EnterThrowProne(victimTransform);
        }
        StartCoroutine(RestoreCollisionAfterDelay(victimTransform, 1f));
        ClearThrowState();
    }

    // Throw release should not force prone if release impact has explicitly routed victim into knockback-stun.
    bool ShouldEnterProneOnThrowRelease(Transform victimTransform)
    {
        if (victimTransform == null) return false;
        EnemyStunMeter victimStun = victimTransform.GetComponent<EnemyStunMeter>();
        if (victimStun == null) return true;
        return !(victimStun.IsStandingStunned && victimStun.TriggerWasHeavy);
    }

    /// <summary>Clears throw-related state: victim ref, pseudo-parent follow, isAttacking, hitbox pending, restores animator speeds and clears hit stop.</summary>
    void ClearThrowState()
    {
        StopThrowVictimPseudoParent();
        currentThrowVictim = null;
        isAttacking = false;
        pendingThrowHitbox = false;
        _hasActiveThrowData = false;
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
        // No active victim to toggle.
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;
        // Victim has no animator in hierarchy.
        var victimAnim = vt.GetComponentInChildren<Animator>();
        if (victimAnim == null) return;

        bool enable = enabled != 0;
        if (enable)
        {
            // Cache original state once so "off" can restore the real value.
            if (!_throwVictimRootMotionChanged)
                _throwVictimRootMotionRestore = victimAnim.applyRootMotion;
            victimAnim.applyRootMotion = true;
            _throwVictimRootMotionChanged = true;
            return;
        }

        // Restore only if we changed it in this throw flow.
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

    /// <summary>Animation event: defers full release to LateUpdate so bake/release/CompleteThrowRelease run after root motion this frame.</summary>
    public void OnThrowRelease()
    {
        // Ignore stale release events after interrupts/cleanup.
        // Without this guard, old clip events could release/apply prone on the wrong target.
        if (currentThrowVictim == null || !_hasActiveThrowData || !_activeThrowData.enableThrow) return;
        // Idempotency guard for fail-safe dual authoring (player + victim clip):
        // once release is queued for this frame, ignore duplicate release events.
        if (_deferThrowReleaseToLateUpdate) return;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;
        _deferThrowReleaseToLateUpdate = true;
    }

    /// <summary>Animation event: defers throw damage (no knockback) to LateUpdate so it applies on the exact frame. Knockback is applied at OnThrowRelease.</summary>
    public void OnThrowDamage()
    {
        // Damage is event-driven; this guard prevents phantom damage after a canceled throw.
        if (currentThrowVictim == null || !_hasActiveThrowData || !_activeThrowData.enableThrow) return;
        _deferThrowDamageToLateUpdate = true;
    }

    /// <summary>Animation event: spawns throw-end VFX at current victim position (or in front of player if victim is missing).</summary>
    public void OnThrowEndVfxEvent()
    {
        // Guard against VFX event timing after throw teardown.
        if (!_hasActiveThrowData || !_activeThrowData.enableThrow) return;
        ThrowData t = _activeThrowData;
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

    IEnumerator RestoreCollisionAfterDelay(Transform victimTransform, float delay)
    {
        yield return new WaitForSeconds(delay);
        SetThrowVictimCollisionIgnore(victimTransform, false);
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

    /// <summary>
    /// Apply throw impact to currentThrowVictim.
    /// For throw flow we can split impact timing:
    /// - OnThrowDamage event: damage only (no knockback)
    /// - OnThrowRelease event: knockback (and optional fallback damage if no damage event fired)
    /// Prone is entered separately via EnterThrowProne at release.
    /// </summary>
    void ApplyThrowDamage(bool applyDamage = true, bool applyKnockback = true)
    {
        ThrowData t = _activeThrowData;
        // Final safety gate for deferred damage execution.
        if (!t.enableThrow || currentThrowVictim == null) return;
        Transform victimTransform = (currentThrowVictim as Component)?.transform;
        if (victimTransform == null) return;
        // If a directional throw direction was committed at charge release, use it as the
        // authoritative knockback direction — it's immune to socket offsets, baking, and
        // any rotation that happened after the player released the charge button.
        // Otherwise fall back to the player's current facing.
        Vector3 horizontalDir;
        if (_hasCommittedThrowDirection)
        {
            horizontalDir = _committedThrowDirection;
        }
        else
        {
            horizontalDir = transform.forward;
            horizontalDir.y = 0f;
            if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = Vector3.forward;
            horizontalDir.Normalize();
        }
        int damage = t.endDamage;
        float knockback = t.endKnockback;
        float knockbackUp = t.endKnockbackUp;
        // Apply throw charge scaling (1x when no charge was held, up to charge multipliers at full hold)
        int scaledDamage = Mathf.RoundToInt(damage * throwChargeDamageScale);
        float scaledKnockback = knockback * throwChargeKnockbackScale;
        float scaledKnockbackUp = knockbackUp * throwChargeKnockbackScale;

        Vector3 knockbackVector = applyKnockback
            ? ((horizontalDir * scaledKnockback) + (Vector3.up * scaledKnockbackUp))
            : Vector3.zero;

        // TEMP DEBUG — show exactly what direction the knockback is fired in
        Debug.Log($"[Throw knockback] horizontalDir={horizontalDir}, transform.fwd={transform.forward}, committedDir={_committedThrowDirection}, hasCommitted={_hasCommittedThrowDirection}");
        Debug.DrawRay(transform.position + Vector3.up, horizontalDir * 4f, Color.magenta, 3f, false);

        // Throw release can explicitly route into knockback-stun animation path based on release knockback.
        // This guarantees IsKnockbackStun can activate even if the previous standing-stun phase has ended.
        if (applyKnockback)
        {
            EnemyStunMeter victimStun = victimTransform.GetComponent<EnemyStunMeter>();
            if (victimStun != null)
            {
                float minKnockbackStunWindow = Mathf.Max(0.1f, victimStun.standingStunEntryDuration);

                // If already in standing stun, just extend the timer first so release impact doesn't drop out early.
                if (victimStun.IsStandingStunned)
                    victimStun.ExtendStandingStunMinRemaining(minKnockbackStunWindow);

                if (knockbackVector.magnitude >= victimStun.knockbackStunThreshold)
                {
                    bool retriggered = victimStun.TryRetriggerAsKnockback(knockbackVector.magnitude);
                    if (!retriggered)
                    {
                        // If not currently standing stunned, start a short standing-stun window in knockback mode.
                        victimStun.ForceStandingStun(minKnockbackStunWindow, asKnockback: true);
                    }
                }
            }
        }

        int damageToApply = applyDamage ? scaledDamage : 0;
        currentThrowVictim.TakeHit(damageToApply, knockbackVector, 0f, 0f);
    }

    /// <summary>Enter prone on the throw victim. Called at release (OnThrowRelease) regardless of whether damage was already applied mid-throw.</summary>
    void EnterThrowProne(Transform victimTransform)
    {
        // Only enter prone from an actively owned throw victim.
        if (!_hasActiveThrowData || victimTransform == null) return;
        ThrowData t = _activeThrowData;
        var victimAI = victimTransform.GetComponentInParent<SimpleEnemyAI>();
        float dur = t.proneDuration > 0f ? t.proneDuration : (victimAI != null ? victimAI.groundedDuration : 1f);
        Vector3 toPlayer = transform.position - victimTransform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.001f)
        {
            toPlayer.Normalize();
            Vector3 desiredFacing = t.faceVictimTowardPlayerOnRelease ? toPlayer : -toPlayer;
            victimTransform.rotation = Quaternion.LookRotation(desiredFacing, Vector3.up);
        }
        victimAI?.ProneSystem?.Enter(dur, t.invertProneRotation, t.proneVariant, preserveCurrentFacing: true);

        // Keep deferred one-frame bake reapply aligned with the final prone-facing rotation.
        if (_reapplyThrowBakeTransform == victimTransform)
            _reapplyThrowBakeRotation = victimTransform.rotation;
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

    // =========================================================================
    // THROW CHARGE
    // =========================================================================
    // After grab connects and the throw animation reaches OnThrowChargeWindowStart,
    // the player can hold the throw button to scale knockback (and optionally damage).
    // Mirrors the weapon charge system but operates on ThrowData instead of AttackData.

    // Called every frame from Update while a throw charge is active.
    void UpdateThrowCharge()
    {
        if (!isChargingThrow) return;
        ThrowData t = _activeThrowData;

        // Slow both the player and victim animations while holding
        float chargeSpeed = t.chargeAnimatorSpeed > 0f ? t.chargeAnimatorSpeed : 0.05f;
        if (animator != null) animator.speed = chargeSpeed;
        if (_throwVictimAnimator != null) _throwVictimAnimator.speed = chargeSpeed;

        // Directional throw: pivot thrower and victim together toward the held movement direction.
        // Because animator.speed is nearly 0 during charge, root-motion delta per frame is negligible
        // and our manual rotation dominates cleanly.
        if (t.enableDirectionalThrow)
        //When you want to unmess this up this is the starting point here for directional throw
            UpdateDirectionalThrowRotation(t);

        // Auto-release when max hold time is reached (prevents holding forever)
        if (t.maxChargeTime > 0f && Time.time - throwChargeStartTime >= t.maxChargeTime)
            StopThrowCharge();
        else if (!IsThrowInputStillHeld()) // Player released the button
            StopThrowCharge();
    }

    /*
     * Rotates the thrower toward the camera-relative stick direction,
     * then snaps the victim rotation to keep them facing the thrower.
     * The victim's POSITION is already anchored to grabSocket (which is a child
     * of the player rig), so rotating the player pivots the socket — and thus
     * the victim — around the player's body automatically.  We only need to
     * manually fix the victim's FACING so they don't spin away from the camera.
     */
    void UpdateDirectionalThrowRotation(ThrowData t)
    {
        // Read raw stick (not CombatStickInput — that's zeroed during attacks)
        Vector2 rawStick = GetRawStickInput();
        if (rawStick.magnitude < 0.2f) return; // ignore dead zone; no direction held

        // Convert stick axes to world-space direction relative to camera
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 camFwd   = cam.transform.forward; camFwd.y   = 0f; camFwd.Normalize();
        Vector3 camRight = cam.transform.right;   camRight.y = 0f; camRight.Normalize();

        Vector3 dir = (camFwd * rawStick.y + camRight * rawStick.x).normalized;
        if (dir.sqrMagnitude < 0.001f) return;

        // TEMP DEBUG: red = target throw direction, blue = current player facing, green = camera forward
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Debug.DrawRay(origin, dir           * 3f, Color.red,   0f, false); // where we want to face
        Debug.DrawRay(origin, transform.forward * 3f, Color.blue,  0f, false); // where we're actually facing
        Debug.DrawRay(origin, camFwd        * 2f, Color.green, 0f, false); // camera forward (reference)

        // Rotate the thrower (player root) toward the held direction
        float rotSpeed   = t.directionalThrowRotationSpeed > 0f ? t.directionalThrowRotationSpeed : 360f;
        Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = targetRot;
        

        // Store for LateUpdate enforcement — if anything clobbers this between Update and LateUpdate we re-apply it
        _directionalThrowTargetRotation = transform.rotation;
        _hasDirectionalThrowTarget      = true;

        // Keep victim facing the thrower so they look like a single rotating unit.
        // The victim's world position already tracks the grabSocket (child of our rig),
        // so a pure facing correction here is all that's needed.
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;

        Vector3 toPlayer = transform.position - vt.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion victimTarget = Quaternion.LookRotation(toPlayer, Vector3.up);
            vt.rotation = Quaternion.RotateTowards(vt.rotation, victimTarget, rotSpeed * Time.deltaTime);
        }

        // Store victim rotation for LateUpdate enforcement (enemy AI/animator may clobber it)
        _directionalThrowVictimTransform = vt;
        _directionalThrowVictimRotation  = vt.rotation;
    }

    // Check whether the throw button is currently held (not just tapped).
    // Mirrors IsChargeInputStillHeld() from the weapon charge system.
    bool IsThrowInputStillHeld()
    {
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.gKey.isPressed) return true;
        if (UnityEngine.InputSystem.Gamepad.current != null)
        {
            bool yHeld = UnityEngine.InputSystem.Gamepad.current.buttonNorth.isPressed;
            bool bHeld = UnityEngine.InputSystem.Gamepad.current.buttonEast.isPressed;
            if (yHeld && bHeld) return true;
        }
        return false;
    }

    // Called by OnThrowChargeWindowStart animation event. If the button is still held,
    // starts slowing the animation and accumulating charge time.
    void StartThrowChargeIfInputHeld()
    {
        ThrowData t = _activeThrowData;
        if (!t.enableCharge) return;
        if (!IsThrowInputStillHeld()) return;
        isChargingThrow       = true;
        throwChargeWindowOpen = true;
        throwChargeStartTime  = Time.time;
        // When directional throw is enabled, OnAnimatorMove will suppress rotation root motion
        // so UpdateDirectionalThrowRotation can steer without being overwritten.
        _directionalThrowRotationActive = t.enableDirectionalThrow;
        // Cache victim animator so UpdateThrowCharge can mirror speed changes to them each frame
        Transform vt = (currentThrowVictim as Component)?.transform;
        _throwVictimAnimator = vt != null ? vt.GetComponentInChildren<Animator>() : null;
    }

    // Bakes the charge scales, extends the attack lock so the throw anim has time to play out,
    // and restores normal animator speed. Called on button release OR auto-release at max time.
    void StopThrowCharge()
    {
        if (!isChargingThrow) return;
        ThrowData t = _activeThrowData;
        float held       = Mathf.Clamp(Time.time - throwChargeStartTime, 0f, t.maxChargeTime > 0f ? t.maxChargeTime : float.MaxValue);
        float normalized = t.maxChargeTime > 0f ? held / t.maxChargeTime : 1f;

        // Lerp multipliers from 1x (no charge) to configured max (full charge)
        throwChargeKnockbackScale = Mathf.Lerp(1f, t.chargeKnockbackMultiplier > 0f ? t.chargeKnockbackMultiplier : 1f, normalized);
        throwChargeDamageScale    = Mathf.Lerp(1f, t.chargeDamageMultiplier    > 0f ? t.chargeDamageMultiplier    : 1f, normalized);

        // Extend the attack lock to cover the time we held — the throw animation was frozen,
        // so it still needs that much time to finish playing after the release.
        currentAttackEndTime += held;

        // Bake the intended throw direction at the moment the player releases the charge.
        // Using the target rotation (not transform.forward) so baking / root-motion restoration
        // that happens later in the same frame cannot corrupt the knockback direction.
        if (_hasDirectionalThrowTarget)
        {
            _committedThrowDirection    = _directionalThrowTargetRotation * Vector3.forward;
            _committedThrowDirection.y  = 0f;
            _committedThrowDirection.Normalize();
            _hasCommittedThrowDirection = true;
        }

        isChargingThrow       = false;
        throwChargeWindowOpen = false;
        // NOTE: _directionalThrowRotationActive is intentionally NOT cleared here.
        // The throw animation's root motion rotation would snap the player back to the
        // clip's authored direction the moment the charge ends. We keep suppressing
        // rotation root motion until the full throw is over (ResetThrowChargeState clears it).
        if (animator != null) animator.speed = 1f;
        if (_throwVictimAnimator != null) _throwVictimAnimator.speed = 1f; // resume victim throw animation speed
    }

    // Clears all throw charge state. Called on attack end, stun interrupt, and ForceThrowReleaseFallback.
    void ResetThrowChargeState()
    {
        if (isChargingThrow)
        {
            if (animator != null) animator.speed = 1f;
            if (_throwVictimAnimator != null) _throwVictimAnimator.speed = 1f; // restore victim speed if still frozen
        }
        isChargingThrow                    = false;
        throwChargeWindowOpen              = false;
        throwChargeKnockbackScale          = 1f;
        throwChargeDamageScale             = 1f;
        _directionalThrowRotationActive    = false;
        _hasDirectionalThrowTarget         = false;
        _hasCommittedThrowDirection        = false;
        _directionalThrowVictimTransform   = null;
        _throwVictimAnimator               = null;
    }

    // =========================================================================
    // ROOT MOTION OVERRIDE
    // =========================================================================
    /*
     * Defining OnAnimatorMove() takes full control of root motion application.
     * Normally Unity would apply both deltaPosition and deltaRotation automatically
     * when applyRootMotion = true.  Here we selectively suppress rotation root
     * motion during a directional throw charge so our manual steering owns facing
     * without being overwritten each frame.
     *
     * Outside of directional throw (flag is false), we replicate Unity's default
     * behaviour exactly — both position and rotation deltas are applied — so no
     * other system is affected.
     */
    void OnAnimatorMove()
    {
        if (animator == null) return;
        transform.position += animator.deltaPosition;
        transform.rotation *= animator.deltaRotation;
    }
}
