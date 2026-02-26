/*
 * Throw system: grab attempt -> hitbox -> hold (root motion) -> release (bake pose, damage or get-up).
 * Lives in a partial of Combat so it shares comboSet, animator, grabSocket, threatSystem, etc.
 */

using UnityEngine;
using System.Linq;

public partial class Combat
{
    #region Throw state (synced: grab attempt -> hitbox -> hold -> release)
    private bool pendingThrowHitbox;
    private float throwHitboxTriggerTime;
    private IDamageable currentThrowVictim;
    private float nextThrowTime;
    private bool _throwVictimRootMotionRestore;
    private bool _throwVictimRootMotionChanged;
    private bool _playerThrowRootMotionRestore;
    private bool _playerThrowRootMotionChanged;
    private bool _deferThrowReleaseToLateUpdate;
    private int _deferThrowReleaseProfileIndex;
    private bool _currentThrowIsBack;
    private bool _deferThrowDamageToLateUpdate;
    private int _deferThrowDamageProfileIndex;
    private Transform _reapplyThrowBakeTransform;
    private Vector3 _reapplyThrowBakePosition;
    private Quaternion _reapplyThrowBakeRotation;
    private bool _reapplyThrowBakeNextFrame;
    private Vector3 _throwVictimMeshLocalPosition;
    private Quaternion _throwVictimMeshLocalRotation;
    private Vector3 _throwVictimParentWorldPosition;
    private Quaternion _throwVictimParentWorldRotation;
    private Vector3 _throwPlayerMeshLocalPosition;
    private Quaternion _throwPlayerMeshLocalRotation;
    #endregion

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
        _currentThrowIsBack = IsBackThrowStickInput();
        currentAttackStartTime = Time.time;
        nextThrowTime = Time.time + t.throwCooldown;
        currentAttackEndTime = Time.time + t.attemptLockDuration;
        currentAttackStateName = t.grabAttemptAnimationTrigger;
        currentThrowVictim = null;
        isAttacking = true;
        hitboxPending = false;
        hitboxHasFired = false;
        pendingThrowHitbox = true;
        throwHitboxTriggerTime = Time.time + t.hitboxDelay;
        if (animator != null && !string.IsNullOrEmpty(t.grabAttemptAnimationTrigger))
            animator.Play(t.grabAttemptAnimationTrigger, 0, 0f);
    }

    bool TryFindThrowVictim(ThrowData t, out EnemyHealth victim, out IDamageable victimDamageable)
    {
        victim = null;
        victimDamageable = null;
        Vector3 center = CalculateThrowHitboxCenter(t);
        Collider[] hits = Physics.OverlapSphere(center, t.hitboxRadius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in hits)
        {
            var damageable = c.GetComponentInParent<IDamageable>();
            if (damageable == null || (damageable as Component)?.gameObject == gameObject) continue;
            var eh = (damageable as Component)?.GetComponent<EnemyHealth>();
            if (eh == null) continue;
            victim = eh;
            victimDamageable = damageable;
            return true;
        }
        return false;
    }

    void AttachVictimToGrabSocket(Transform victimTransform, Animator victimAnim)
    {
        if (grabSocket == null) return;
        if (victimAnim != null && victimAnim.transform != victimTransform)
        {
            _throwVictimParentWorldPosition = victimTransform.position;
            _throwVictimParentWorldRotation = victimTransform.rotation;
            _throwVictimMeshLocalPosition = victimAnim.transform.localPosition;
            _throwVictimMeshLocalRotation = victimAnim.transform.localRotation;
        }
        Vector3 worldScaleBefore = victimTransform.lossyScale;
        victimTransform.SetParent(grabSocket, false);
        victimTransform.localPosition = Vector3.zero;
        Vector3 p = grabSocket.lossyScale;
        if (p.x != 0f && p.y != 0f && p.z != 0f)
            victimTransform.localScale = new Vector3(worldScaleBefore.x / p.x, worldScaleBefore.y / p.y, worldScaleBefore.z / p.z);

        var victimCC = victimTransform.GetComponent<CharacterController>();
        if (victimCC != null) victimCC.enabled = false;
        var victimRb = victimTransform.GetComponent<Rigidbody>();
        if (victimRb != null) victimRb.isKinematic = true;

        Vector3 toPlayer = transform.position - victimTransform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.001f)
        {
            toPlayer.Normalize();
            victimTransform.rotation = Quaternion.LookRotation(toPlayer);
        }

        if (victimAnim != null)
        {
            _throwVictimRootMotionRestore = victimAnim.applyRootMotion;
            victimAnim.applyRootMotion = true;
            _throwVictimRootMotionChanged = true;
        }
    }

    string GetThrownStateName(ThrowData t, EnemyHealth victim)
    {
        if (_currentThrowIsBack && !string.IsNullOrEmpty(t.backEnemyThrownStateName))
            return t.backEnemyThrownStateName;
        var victimAI = victim.GetComponent<SimpleEnemyAI>();
        return (victimAI != null && !string.IsNullOrEmpty(victimAI.thrownStateName)) ? victimAI.thrownStateName : t.enemyThrownStateName;
    }

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

    void SpawnGrabConnectVfx(ThrowData t, Vector3 center)
    {
        if (t.grabConnectVfxPrefab == null) return;
        Quaternion rot = (center - transform.position).sqrMagnitude > 0.001f ? Quaternion.LookRotation(center - transform.position) : transform.rotation;
        var go = Instantiate(t.grabConnectVfxPrefab, center, rot);
        PlayVfx(go);
    }

    void StartPlayerThrowAnimation(string playerThrowTrigger)
    {
        if (animator == null || string.IsNullOrEmpty(playerThrowTrigger)) return;
        animator.Rebind();
        animator.speed = 1f;
        animator.Play(playerThrowTrigger, 0, 0f);
        _playerThrowRootMotionRestore = animator.applyRootMotion;
        animator.applyRootMotion = true;
        _playerThrowRootMotionChanged = true;
        if (animator.transform != transform)
        {
            _throwPlayerMeshLocalPosition = animator.transform.localPosition;
            _throwPlayerMeshLocalRotation = animator.transform.localRotation;
        }
    }

    /// <summary>Runs when the grab hitbox connects: find victim, parent to socket, enable root motion, hit stop, play throw anim.</summary>
    void ExecuteThrowHitbox()
    {
        ThrowData t = comboSet.throwData;
        if (!t.enableThrow) return;

        if (!TryFindThrowVictim(t, out EnemyHealth victim, out IDamageable victimDamageable)) return;

        currentThrowVictim = victimDamageable;
        Transform victimTransform = (victimDamageable as Component).transform;
        SetThrowVictimCollisionIgnore(victimTransform, true);

        var victimAnim = victimTransform.GetComponentInChildren<Animator>();
        AttachVictimToGrabSocket(victimTransform, victimAnim);

        string thrownState = GetThrownStateName(t, victim);
        victim.StartThrowVictim(t.throwPhaseDuration, thrownState);

        ApplyGrabHitStop(t.grabHitStopDuration, victimTransform);

        Vector3 center = CalculateThrowHitboxCenter(t);
        SpawnGrabConnectVfx(t, center);

        currentAttackEndTime = Time.time + t.grabHitStopDuration + t.throwPhaseDuration;
        string playerThrowTrigger = (_currentThrowIsBack && !string.IsNullOrEmpty(t.backThrowAnimationTrigger)) ? t.backThrowAnimationTrigger : t.throwAnimationTrigger;
        StartPlayerThrowAnimation(playerThrowTrigger);

        if (threatSystem != null)
            threatSystem.RegisterInteraction((victimDamageable as Component).transform);
    }

    /// <summary>Bake player's Animator root-motion result into transform and restore applyRootMotion. Call when throw ends.</summary>
    void BakePlayerThrowRootMotionAndRestore()
    {
        // Only run if we turned on root motion for the throw and have a valid animator
        if (!_playerThrowRootMotionChanged || animator == null) return;
        // Snapshot where the animator root ended up in world space (root motion moved it during throw)
        Vector3 bakePosition = animator.transform.position;
        UnityEngine.Debug.Log($"[Throw] Baking player root position: {bakePosition}");
        Quaternion bakeRotation = animator.transform.rotation;
        // Turn root motion back off and restore whatever it was before the throw
        animator.applyRootMotion = _playerThrowRootMotionRestore;
        _playerThrowRootMotionChanged = false;
        // Keep player feet at current ground height; only take XZ from the baked pose
        bakePosition.y = transform.position.y;
        // Use only Yaw from baked rotation so the character stands upright (no tilt/roll)
        Quaternion standingRotation = Quaternion.Euler(0f, bakeRotation.eulerAngles.y, 0f);
        // Apply baked pose to the Combat/controller transform (the actual player root)
        transform.position = bakePosition;
        transform.rotation = standingRotation;
        // If the Animator lives on a child (e.g. model root), restore its local pose so the mesh doesn't drift
        if (animator.transform != transform)
        {
            animator.transform.localPosition = _throwPlayerMeshLocalPosition;
            animator.transform.localRotation = _throwPlayerMeshLocalRotation;
        }
    }

    /// <summary>Bake victim's Animator root-motion result into victim transform and restore applyRootMotion + mesh local. Call when releasing from throw.</summary>
    /// <returns>Baked world position and rotation for optional reapply-next-frame.</returns>
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

    /// <summary>Bake victim throw root motion, unparent from socket, re-enable CharacterController/Rigidbody, clear knockback; schedule reapply next frame if not launching.</summary>
    void ReleaseThrowVictimFromSocket()
    {
        if (currentThrowVictim == null) return;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;
        UnityEngine.Debug.Log($"[Throw] Release victim: vt={vt.name}, pos={vt.position}");

        (Vector3 bakePosition, Quaternion bakeRotation) = BakeVictimThrowRootMotionAndRestore(vt);

        vt.SetParent(null);

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

    void CompleteThrowRelease(Transform victimTransform, int releaseProfileIndex, bool damageAlreadyAppliedThisFrame, bool applyReleaseEffects = true)
    {
        if (applyReleaseEffects && comboSet != null && comboSet.throwData.enableThrow)
        {
            if (comboSet.throwData.launchVictimOnRelease && !damageAlreadyAppliedThisFrame)
                ApplyThrowEndDamage(releaseProfileIndex);
            else
            {
                var victimAI = victimTransform.GetComponent<SimpleEnemyAI>();
                if (victimAI != null) victimAI.TriggerGetUpFromThrow();
            }
        }
        SetThrowVictimCollisionIgnore(victimTransform, false);
        ClearThrowState();
    }

    void ClearThrowState()
    {
        currentThrowVictim = null;
        isAttacking = false;
        hitboxPending = false;
        pendingThrowHitbox = false;
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

    public void OnThrowRelease()
    {
        OnThrowRelease(-1);
    }

    public void OnThrowRelease(int releaseProfileIndex)
    {
        if (currentThrowVictim == null || comboSet == null || !comboSet.throwData.enableThrow) return;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;
        _deferThrowReleaseToLateUpdate = true;
        _deferThrowReleaseProfileIndex = releaseProfileIndex;
    }

    public void OnThrowDamage()
    {
        OnThrowDamage(-1);
    }

    public void OnThrowDamage(int profileIndex)
    {
        if (currentThrowVictim == null || comboSet == null || !comboSet.throwData.enableThrow) return;
        _deferThrowDamageToLateUpdate = true;
        _deferThrowDamageProfileIndex = profileIndex;
    }

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

    void ApplyThrowEndDamage(int releaseProfileIndex = -1)
    {
        ThrowData t = comboSet.throwData;
        if (!t.enableThrow || currentThrowVictim == null) return;
        Transform victimTransform = (currentThrowVictim as Component)?.transform;
        if (victimTransform == null) return;
        Vector3 horizontalDir = (victimTransform.position - transform.position);
        horizontalDir.y = 0f;
        if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = transform.forward;
        horizontalDir.Normalize();
        bool faceDirection;
        int damage;
        float knockback, knockbackUp, hitstun, airborneDuration;
        if (releaseProfileIndex >= 0 && t.releaseProfiles != null && releaseProfileIndex < t.releaseProfiles.Length)
        {
            var p = t.releaseProfiles[releaseProfileIndex];
            faceDirection = p.faceTowardThrowDirection;
            damage = p.endDamage;
            knockback = p.endKnockback;
            knockbackUp = p.endKnockbackUp;
            hitstun = p.endHitstun;
            airborneDuration = p.endAirborneDuration;
        }
        else
        {
            faceDirection = t.faceVictimTowardThrowDirection;
            damage = t.endDamage;
            knockback = t.endKnockback;
            knockbackUp = t.endKnockbackUp;
            hitstun = t.endHitstun;
            airborneDuration = t.endAirborneDuration;
        }
        if (faceDirection)
            victimTransform.rotation = Quaternion.LookRotation(-horizontalDir);
        Vector3 knockbackVector = (horizontalDir * knockback) + (Vector3.up * knockbackUp);
        currentThrowVictim.TakeHit(damage, knockbackVector, hitstun, airborneDuration);
        if (t.throwEndVfxPrefab != null)
        {
            var go = Instantiate(t.throwEndVfxPrefab, victimTransform.position, Quaternion.identity);
            PlayVfx(go);
        }
        if (threatSystem != null)
            threatSystem.RegisterInteraction(victimTransform);
    }

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
