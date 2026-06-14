/*
 * Throw system: grab attempt -> hitbox -> hold (root motion) -> release (bake pose, damage or get-up).
 * Lives in a partial of Combat so it shares comboSet, animator, grabSocket, threatSystem, etc.
 *
 * Flow: DoThrow() starts attempt -> Update checks throwHitboxTriggerTime -> ExecuteThrowHitbox() on connect
 *       -> victim pseudo-parent follows grabSocket position -> animation events OnThrowUnparent / OnThrowDamage / OnThrowRelease
 *       -> LateUpdate runs deferred release: ReleaseThrowVictimFromSocket, BakePlayer, CompleteThrowRelease.
 */

using System.Collections;
using System.Collections.Generic;
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
    private bool _activeThrowIsExplicit;   // True when DoThrowWithData was called directly (e.g. LB throw); skips connect-time profile re-fetch.
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
    private float throwChargeHitStopScale   = 1f; // baked at release; applied in OnThrowHitStop
    private Animator _throwVictimAnimator;     // cached at charge start so we can mirror speed changes to the victim
    private int _victimThrownAnimLayer;        // animator layer the thrown state plays on; used for NT scrubbing sync
    private string _victimThrownStateName;     // state name on that layer; used to scrub victim NT to match player each frame
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
    // Player facing at grab connect — immune to root motion drift; used as knockback fallback.
    private Vector3 _throwStartDirection;
    // Position-only pseudo-parent state while throw hold is active.
    private Transform _throwVictimPseudoParentTarget;
    private bool _throwVictimPseudoParentActive;
    private Vector3 _throwVictimPseudoParentOffset;
    private Transform _activeGrabSocket;       // Resolved grab socket for current throw (may differ from default grabSocket).
    private Vector3 _activeGrabSocketLocalOffset;  // Local-space offset from ThrowGrabSocket.offset; set by ResolveGrabSocketByIndex, consumed by AttachVictimToGrabSocket.
    // Per-frame additive nudges applied on top of the socket follow (see OnThrowVictimNudge).
    private struct ActiveNudge
    {
        public Vector3 target;          // socket-local target offset
        public float duration;          // total interpolation time in seconds
        public float elapsed;           // accumulated time (scaled by animator speed)
    }
    private readonly List<ActiveNudge> _activeNudges = new List<ActiveNudge>();
    // Standalone nudge state: applies nudges as world-space deltas after pseudo-parent is stopped.
    private Transform _standaloneNudgeTarget;
    private Quaternion _standaloneNudgeRotation;
    private Vector3 _standaloneNudgePrevTotal;
    // Victim waiting for OnThrowAttach animation event before being snapped to the grab socket.
    private Transform _pendingThrowAttachVictim;
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
        // Handle the case where hitbox connected but OnThrowAttach never fired.
        if (_pendingThrowAttachVictim != null)
        {
            var pendingHealth = _pendingThrowAttachVictim.GetComponent<EnemyHealth>();
            if (pendingHealth != null)
                pendingHealth.PlayThrowVictimAnimation();
            Transform pendingVt = _pendingThrowAttachVictim;
            _pendingThrowAttachVictim = null;
            if (currentThrowVictim == null)
            {
                // No current victim either — only cleanup needed.
                ClearThrowState();
                return;
            }
        }

        if (currentThrowVictim == null) return;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null)
        {
            currentThrowVictim = null;
            return;
        }

        // Mirror the standard release order so victim/player transforms and controller state stay consistent.
        ResetThrowChargeState(); // restore animator speed if frozen mid-charge
        // BakePlayerThrowRootMotionAndRestore();
        ReleaseThrowVictimFromSocket();
        CompleteThrowRelease(vt, damageAlreadyAppliedThisFrame: true, applyReleaseEffects: applyReleaseEffects);
    }

    #region Throw (attempted grab -> hitbox -> hold -> release)
    /// <summary>Starts a throw attempt using the default throw profile selected by stick input. Forward/back resolved at connect time.</summary>
    void DoThrow()
    {
        _activeThrowIsExplicit = false;
        DoThrowInternal(GetThrowAttemptData());
    }

    /// <summary>Starts a throw attempt with an explicitly provided ThrowData (e.g. LB throw). Skips connect-time forward/back re-fetch.</summary>
    void DoThrowWithData(ThrowData t)
    {
        _activeThrowIsExplicit = true;
        DoThrowInternal(t);
    }

    void DoThrowInternal(ThrowData t)
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
        _currentThrowIsBack = false;
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

    /// <summary>OverlapSphere at throw hitbox center; returns nearest valid enemy damageable.</summary>
    bool TryFindThrowVictim(ThrowData t, out EnemyHealth victim, out IDamageable victimDamageable)
    {
        victim = null;
        victimDamageable = null;
        Vector3 center = CalculateThrowHitboxCenter(t);
        Collider[] hits = Physics.OverlapSphere(center, t.hitboxRadius, throwTargetLayers.value, QueryTriggerInteraction.Ignore);
        EntityHealth selfHealth = GetComponent<EntityHealth>();
        float bestDistanceSqr = float.PositiveInfinity;
        bool found = false;
        foreach (var c in hits)
        {
            var damageable = c.GetComponentInParent<IDamageable>();
            Component damageableComponent = damageable as Component;
            if (damageableComponent == null) continue;
            if (damageableComponent.transform.root == transform.root) continue; // Skip self

            EntityHealth targetHealth = damageableComponent.GetComponentInParent<EntityHealth>();
            if (selfHealth != null && targetHealth != null && !TeamUtil.AreHostile(selfHealth.team, targetHealth.team))
                continue;

            var eh = damageableComponent.GetComponent<EnemyHealth>();
            if (eh == null) continue; // Only grab enemies that have EnemyHealth (and thus throw/get-up support)

            float distSqr = (damageableComponent.transform.position - center).sqrMagnitude;
            if (distSqr >= bestDistanceSqr) continue;

            bestDistanceSqr = distSqr;
            victim = eh;
            victimDamageable = damageable;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// Returns the grab socket for the current throw. Priority:
    /// 1. Centralized ComboSet.throwGrabSockets[0] (if the array is populated)
    /// 2. Per-throw ThrowData.grabSocketName (legacy, only when centralized array is empty)
    /// 3. Default grabSocket Transform on Combat
    /// </summary>
    Transform ResolveGrabSocket()
    {
        if (comboSet != null && comboSet.throwGrabSockets != null && comboSet.throwGrabSockets.Length > 0)
            return ResolveGrabSocketByIndex(0);

        _activeGrabSocketLocalOffset = Vector3.zero;
        if (_hasActiveThrowData && !string.IsNullOrEmpty(_activeThrowData.grabSocketName))
        {
            Transform found = FindTransformByName(transform, _activeThrowData.grabSocketName);
            if (found != null) return found;
            UnityEngine.Debug.LogWarning($"[Throw] grabSocketName '{_activeThrowData.grabSocketName}' not found in hierarchy — falling back to default grabSocket.", this);
        }
        return grabSocket;
    }

    /// <summary>
    /// Resolves a grab socket by index into ComboSet.throwGrabSockets.
    /// Also stores the entry's local-space offset into _activeGrabSocketLocalOffset for the caller.
    /// All fallback paths return grabSocket directly to avoid mutual recursion with ResolveGrabSocket().
    /// </summary>
    Transform ResolveGrabSocketByIndex(int socketIndex)
    {
        _activeGrabSocketLocalOffset = Vector3.zero;
        if (comboSet == null || comboSet.throwGrabSockets == null || comboSet.throwGrabSockets.Length == 0)
        {
            UnityEngine.Debug.LogWarning($"[Throw] OnThrowAttach({socketIndex}) — ComboSet has no throwGrabSockets configured. Falling back to default.", this);
            return grabSocket;
        }
        if (socketIndex < 0 || socketIndex >= comboSet.throwGrabSockets.Length)
        {
            UnityEngine.Debug.LogWarning($"[Throw] OnThrowAttach({socketIndex}) — index out of range (array length {comboSet.throwGrabSockets.Length}). Falling back to default.", this);
            return grabSocket;
        }
        ThrowGrabSocket entry = comboSet.throwGrabSockets[socketIndex];
        if (string.IsNullOrEmpty(entry.socketName))
        {
            UnityEngine.Debug.LogWarning($"[Throw] throwGrabSockets[{socketIndex}].socketName is empty. Falling back to default.", this);
            return grabSocket;
        }
        Transform found = FindTransformByName(transform, entry.socketName);
        if (found == null)
        {
            UnityEngine.Debug.LogWarning($"[Throw] throwGrabSockets[{socketIndex}].socketName = '{entry.socketName}' not found in hierarchy. Falling back to default.", this);
            return grabSocket;
        }
        _activeGrabSocketLocalOffset = entry.offset;
        return found;
    }

    /// <summary>Depth-first search for a named transform in a hierarchy.</summary>
    static Transform FindTransformByName(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindTransformByName(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Starts position-only pseudo-parent follow to grabSocket (no real parenting, no scale inheritance).</summary>
    void AttachVictimToGrabSocket(Transform victimTransform, Transform socketOverride = null)
    {
        Transform socket = socketOverride ?? ResolveGrabSocket();
        UnityEngine.Debug.Log($"[Throw] OnThrowAttach → socket={socket?.name ?? "NULL"}");

        if (socket == null) return; // If no socket is assigned, we cannot attach.
        _activeGrabSocket = socket;
        _throwVictimPseudoParentTarget = victimTransform;
        _throwVictimPseudoParentOffset = _activeGrabSocketLocalOffset;
        _throwVictimPseudoParentActive = true;
        // If the animator is a separate child mesh, store and reset its local pose so root motion
        // drift or animation offsets don't misalign the visual when snapping to the socket.
        if (_throwVictimAnimator != null && _throwVictimAnimator.transform != victimTransform)
        {
            _throwVictimMeshLocalPosition = _throwVictimAnimator.transform.localPosition;
            _throwVictimMeshLocalRotation = _throwVictimAnimator.transform.localRotation;
            _throwVictimAnimator.transform.localPosition = Vector3.zero;
            _throwVictimAnimator.transform.localRotation = Quaternion.identity;
        }
        // Rotate to face the player BEFORE snapping position so the direction is computed from the victim's real location.
        // Disable root motion briefly so the animator doesn't immediately overwrite the rotation we set;
        // re-enable after a short delay so the facing has time to settle.
        bool hadRootMotion = _throwVictimAnimator != null && _throwVictimAnimator.applyRootMotion;
        if (hadRootMotion) _throwVictimAnimator.applyRootMotion = false;
        Vector3 toPlayer = transform.position - victimTransform.position;
        toPlayer.y = 0f;
        victimTransform.rotation = toPlayer.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(toPlayer.normalized)
            : Quaternion.LookRotation(-transform.forward);
        if (hadRootMotion) StartCoroutine(ReenableVictimRootMotionDelayed(_throwVictimAnimator, 0.1f));
        victimTransform.position = socket.position + socket.rotation * _throwVictimPseudoParentOffset;
    }

    float GetNudgeTimeScale()
    {
        if (animator == null) return 1f;
        return Mathf.Max(animator.speed, 0f);
    }

    void UpdateThrowVictimPseudoParent()
    {
        if (!_throwVictimPseudoParentActive || _throwVictimPseudoParentTarget == null || _activeGrabSocket == null) return;

        float scaledDt = Time.deltaTime * GetNudgeTimeScale();
        Vector3 totalNudge = Vector3.zero;
        for (int i = 0; i < _activeNudges.Count; i++)
        {
            ActiveNudge n = _activeNudges[i];
            n.elapsed += scaledDt;
            float t = n.duration > 0f ? Mathf.Clamp01(n.elapsed / n.duration) : 1f;
            totalNudge += n.target * t;
            _activeNudges[i] = n;
        }

        _throwVictimPseudoParentTarget.position = _activeGrabSocket.position
            + _activeGrabSocket.rotation * (_throwVictimPseudoParentOffset + totalNudge);

        // Generic-rig animators keep root bone position in object space even with applyRootMotion=false,
        // causing the skeleton to drift visually from the root transform. Zero the mesh local position
        // each frame so the visual skeleton stays anchored to Enemy_root while bones animate normally.
        if (_throwVictimAnimator != null && _throwVictimAnimator.transform != _throwVictimPseudoParentTarget)
            _throwVictimAnimator.transform.localPosition = Vector3.zero;
    }

    void StopThrowVictimPseudoParent()
    {
        if (debugNeverDetach) return;
        // Snapshot for standalone nudge only on the first active→inactive transition;
        // later redundant calls (e.g. from ClearThrowState) must not overwrite the snapshot.
        if (_throwVictimPseudoParentActive)
        {
            _standaloneNudgeTarget = _throwVictimPseudoParentTarget;
            _standaloneNudgeRotation = _activeGrabSocket != null ? _activeGrabSocket.rotation : transform.rotation;
            Vector3 currentTotal = Vector3.zero;
            for (int i = 0; i < _activeNudges.Count; i++)
            {
                ActiveNudge n = _activeNudges[i];
                float t = n.duration > 0f ? Mathf.Clamp01(n.elapsed / n.duration) : 1f;
                currentTotal += n.target * t;
            }
            _standaloneNudgePrevTotal = currentTotal;
        }

        _throwVictimPseudoParentActive = false;
        _throwVictimPseudoParentTarget = null;
        _throwVictimPseudoParentOffset = Vector3.zero;
        _activeGrabSocketLocalOffset = Vector3.zero;
        _activeGrabSocket = null;
    }

    void UpdateStandaloneNudges()
    {
        if (_throwVictimPseudoParentActive) return;
        if (_standaloneNudgeTarget == null || _activeNudges.Count == 0) return;

        float scaledDt = Time.deltaTime * GetNudgeTimeScale();
        Vector3 totalNudge = Vector3.zero;
        for (int i = 0; i < _activeNudges.Count; i++)
        {
            ActiveNudge n = _activeNudges[i];
            n.elapsed += scaledDt;
            float t = n.duration > 0f ? Mathf.Clamp01(n.elapsed / n.duration) : 1f;
            totalNudge += n.target * t;
            _activeNudges[i] = n;
        }

        Vector3 delta = totalNudge - _standaloneNudgePrevTotal;
        _standaloneNudgePrevTotal = totalNudge;
        _standaloneNudgeTarget.position += _standaloneNudgeRotation * delta;
    }

    void ClearStandaloneNudgeState()
    {
        _activeNudges.Clear();
        _standaloneNudgeTarget = null;
        _standaloneNudgePrevTotal = Vector3.zero;
    }

    IEnumerator ReenableVictimRootMotionDelayed(Animator anim, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (anim != null && _throwVictimPseudoParentActive)
            anim.applyRootMotion = true;
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
        if (isAttacking)
            currentAttackEndTime += duration;
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

        // If the throw was explicitly set (e.g. LB throw), use _activeThrowData directly.
        // Otherwise resolve forward/back at connect time from stick input.
        ThrowData attemptData = _activeThrowIsExplicit ? _activeThrowData : GetThrowAttemptData();
        if (!attemptData.enableThrow) return; // Safety: profile may have changed since throw attempt started.

        // No victim found means whiff: keep existing attack lock behavior and exit quietly.
        if (!TryFindThrowVictim(attemptData, out EnemyHealth victim, out IDamageable victimDamageable)) return;

        if (!_activeThrowIsExplicit)
        {
            // Resolve forward/back throw at connect time using current stick input.
            _currentThrowIsBack = IsBackThrowStickInput();
            ThrowData connectData = GetThrowDataForInput(_currentThrowIsBack);
            if (connectData.enableThrow)
            {
                _activeThrowData = connectData;
                _hasActiveThrowData = true;
            }
        }
        ThrowData t = _activeThrowData;

        currentThrowVictim = victimDamageable;
        Transform victimTransform = (victimDamageable as Component).transform;

        // Snap the player to face the victim at grab connect so both animations align consistently.
        Vector3 toVictim = victimTransform.position - transform.position;
        toVictim.y = 0f;
        if (toVictim.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(toVictim.normalized, Vector3.up);
            // Optionally step the player to a fixed distance from the victim for consistent spacing.
            if (t.grabSnapDistance > 0f)
                transform.position = victimTransform.position - toVictim.normalized * t.grabSnapDistance;
        }

        _throwStartDirection = transform.forward;
        _throwStartDirection.y = 0f;
        _throwStartDirection.Normalize();

        SetThrowVictimCollisionIgnore(victimTransform, true);  // Prevent player and victim colliders from fighting during throw

        // Store victim root motion state before StartThrowVictim disables it, so BakeVictimThrowRootMotionAndRestore can restore it.
        var victimAnimator = victimTransform.GetComponentInChildren<Animator>();
        if (victimAnimator != null)
        {
            _throwVictimRootMotionRestore = victimAnimator.applyRootMotion;
            _throwVictimRootMotionChanged = true;
        }

        // Don't snap to socket yet — wait for OnThrowAttach animation event on the player's throw clip.
        // This lets the animation drive when the hands close around the victim.
        _pendingThrowAttachVictim = victimTransform;

        string thrownState = GetThrownStateName(t, victim);
        victim.StartThrowVictim(t.throwPhaseDuration, thrownState);  // Enemy enters thrown state and plays thrown anim

        // ApplyGrabHitStop(t.grabHitStopDuration, victimTransform);

        // Vector3 center = CalculateThrowHitboxCenter(t);
        // SpawnGrabConnectVfx(t, center);

        // nextThrowTime = Time.time + t.throwCooldown; // Use the connected throw profile's cooldown.
        currentAttackEndTime = Time.time + t.grabHitStopDuration + t.throwPhaseDuration;
        // Cache victim animator now (charge start may not happen for uncharged throws).
        _throwVictimAnimator = victimTransform.GetComponentInChildren<Animator>();
        // Layer detection deferred to OnThrowAttach — animation isn't playing yet.
        _victimThrownAnimLayer = 0;
        _victimThrownStateName = null;
        StartPlayerThrowAnimation(t.throwAnimationTrigger);
        // Switch startup/recovery tracking to the throw animation so UpdateAttackStartUpSpeed applies correctly.
        currentAttackStateName = t.throwAnimationTrigger;
        currentStartUpLength   = t.throwStartUpLength;
        currentStartUpSpeed    = t.throwStartUpSpeed > 0f ? t.throwStartUpSpeed : 1f;
        currentRecoveryLength  = t.throwRecoveryLength;
        currentRecoverySpeed   = t.throwRecoverySpeed > 0f ? t.throwRecoverySpeed : 1f;
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
            // Snap the victim's animation to its final frame before baking so root-motion
            // result is deterministic regardless of when the release event fires.
            var stateInfo = victimAnim.GetCurrentAnimatorStateInfo(0);
            victimAnim.Play(stateInfo.fullPathHash, 0, 1f);
            victimAnim.Update(0f);

            bakePosition = victimAnim.transform.position;
            bakeRotation = victimAnim.transform.rotation;
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
        if (applyReleaseEffects && _hasActiveThrowData && _activeThrowData.enableThrow)
        {
            ThrowData t = _activeThrowData;
            bool goingAirborne = t.launchOnRelease || t.airborneOnRelease;
            if (goingAirborne)
            {
                // Snap victim facing to the knockback direction so liftoff root motion aligns consistently.
                Vector3 knockDir = _hasCommittedThrowDirection ? _committedThrowDirection : _throwStartDirection;
                if (t.invertKnockbackDirection) knockDir = -knockDir;
                Vector3 facing = t.faceVictimTowardPlayerOnRelease ? -knockDir : knockDir;
                if (facing.sqrMagnitude > 0.001f)
                {
                    Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
                    victimTransform.rotation = rot;
                    _reapplyThrowBakeRotation = rot;
                }

                var victimAI = victimTransform.GetComponentInParent<SimpleEnemyAI>();
                if (victimAI?.ProneSystem != null)
                {
                    float dur = t.proneDuration > 0f ? t.proneDuration : (victimAI != null ? victimAI.groundedDuration : 1f);
                    victimAI.ProneSystem.OverrideNextProneDuration(dur);
                    victimAI.ProneSystem.OverrideNextProneVariant(t.proneVariant);
                }
            }
            else if (ShouldEnterProneOnThrowRelease(victimTransform))
            {
                EnterThrowProne(victimTransform);
            }
        }
        StartCoroutine(RestoreCollisionAfterDelay(victimTransform, 1f));

        var victimHealth = victimTransform != null ? victimTransform.GetComponent<EnemyHealth>() : null;
        if (victimHealth != null)
        {
            if (_hasActiveThrowData && (_activeThrowData.launchOnRelease || _activeThrowData.airborneOnRelease)
                && _activeThrowData.endAirborneDuration > 0f)
            {
                victimHealth.SetAirborne(_activeThrowData.endAirborneDuration);
            }
            victimHealth.EndThrowVictimState();
        }

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
        ClearStandaloneNudgeState();
        _pendingThrowAttachVictim = null;
        currentThrowVictim = null;
        isAttacking = false;
        pendingThrowHitbox = false;
        _hasActiveThrowData = false;
        ResetChargeState();
        ResetThrowChargeState();
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

    /// <summary>
    /// Animation event: snap the grabbed victim to the player's grab socket and start pseudo-parent follow.
    /// Place this on the player throw animation at the frame the hands close around the enemy.
    /// If the event never fires (e.g. no event authored), the victim stays in place until OnThrowRelease.
    /// </summary>
    public void OnThrowAttach()
    {
        OnThrowAttachInternal(null);
    }

    /// <summary>
    /// Animation event (int): snap the grabbed victim to a specific grab socket by index into
    /// ComboSet.throwGrabSockets. Can be called multiple times during a throw to swap
    /// between attachment points mid-animation.
    /// Renamed from OnThrowAttach(int) to avoid Unity animation event overload ambiguity —
    /// Unity treats intParameter=0 the same as "no int" and may call the void overload instead.
    /// </summary>
    public void OnThrowAttachSocket(int socketIndex)
    {
        Transform socket = ResolveGrabSocketByIndex(socketIndex);
        OnThrowAttachInternal(socket);
    }

    /// <summary>
    /// Animation event (Object): smoothly nudge the throw victim by a local-space offset over time.
    /// Drag a ThrowVictimNudge asset into the event's Object field. Multiple nudges accumulate
    /// additively — each independently interpolates from zero to its target, then holds there
    /// until the throw ends. Safe to fire multiple times on the same clip.
    /// </summary>
    public void OnThrowVictimNudge(Object nudgeAsset)
    {
        var nudge = nudgeAsset as ThrowVictimNudge;
        if (nudge == null) return;
        _activeNudges.Add(new ActiveNudge
        {
            target   = nudge.offset,
            duration = nudge.duration,
            elapsed  = 0f,
        });
    }

    void OnThrowAttachInternal(Transform socketOverride)
    {
        if (_pendingThrowAttachVictim != null)
        {
            var victimHealth = _pendingThrowAttachVictim.GetComponent<EnemyHealth>();
            if (victimHealth != null)
                victimHealth.PlayThrowVictimAnimation();

            AttachVictimToGrabSocket(_pendingThrowAttachVictim, socketOverride);
            _pendingThrowAttachVictim = null;

            DetectVictimThrownAnimLayer();
        }
        else if (currentThrowVictim != null)
        {
            Transform vt = (currentThrowVictim as Component)?.transform;
            if (vt != null)
            {
                if (_throwVictimPseudoParentActive)
                {
                    // Mid-throw socket swap: update the active socket so LateUpdate follows the new point.
                    // ResolveGrabSocket/ResolveGrabSocketByIndex also stores the new offset in _activeGrabSocketLocalOffset.
                    Transform newSocket = socketOverride ?? ResolveGrabSocket();
                    if (newSocket != null)
                    {
                        _activeGrabSocket = newSocket;
                        _throwVictimPseudoParentOffset = _activeGrabSocketLocalOffset;
                        vt.position = newSocket.position + newSocket.rotation * _throwVictimPseudoParentOffset;
                    }
                }
                else
                {
                    AttachVictimToGrabSocket(vt, socketOverride);
                }
            }
        }
    }

    /// <summary>
    /// Finds which animator layer the victim's thrown state is playing on and stores it
    /// for per-frame NT scrubbing in UpdateAttackStartUpSpeed.
    /// Must be called after PlayThrowVictimAnimation() so the state is active.
    /// </summary>
    void DetectVictimThrownAnimLayer()
    {
        if (_throwVictimAnimator == null || currentThrowVictim == null) return;
        var victimHealth = (currentThrowVictim as Component)?.GetComponent<EnemyHealth>();
        if (victimHealth == null) return;
        var victimAI = victimHealth.GetComponent<SimpleEnemyAI>();
        ThrowData t = _activeThrowData;
        string thrownStateName = GetThrownStateName(t, victimHealth);
        if (string.IsNullOrEmpty(thrownStateName)) return;
        for (int l = 0; l < _throwVictimAnimator.layerCount; l++)
        {
            if (_throwVictimAnimator.GetCurrentAnimatorStateInfo(l).IsName(thrownStateName))
            {
                _victimThrownAnimLayer = l;
                _victimThrownStateName = thrownStateName;
                return;
            }
        }
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
        // If OnThrowAttach never fired (event not authored on this clip), attach + play anim as fallback.
        if (_pendingThrowAttachVictim != null)
        {
            var victimHealth = _pendingThrowAttachVictim.GetComponent<EnemyHealth>();
            if (victimHealth != null)
                victimHealth.PlayThrowVictimAnimation();
            AttachVictimToGrabSocket(_pendingThrowAttachVictim);
            _pendingThrowAttachVictim = null;
            DetectVictimThrownAnimLayer();
        }
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

    /// <summary>
    /// Animation event (int): freeze both thrower and victim animators using a
    /// ThrowHitStopProfile from ComboSet.throwHitStops[index]. Reuses the existing
    /// frozenAnimators / hitStopEndTime pipeline so UpdateHitStop handles restoration.
    /// </summary>
    public void OnThrowHitStop(int index)
    {
        if (comboSet == null || comboSet.throwHitStops == null) return;
        if (index < 0 || index >= comboSet.throwHitStops.Length)
        {
            UnityEngine.Debug.LogWarning($"[Throw] OnThrowHitStop({index}) — index out of range (array length {comboSet.throwHitStops.Length}). Ignored.", this);
            return;
        }
        ThrowHitStopProfile profile = comboSet.throwHitStops[index];
        if (profile.duration <= 0f) return;

        float scaledDuration = profile.duration * throwChargeHitStopScale;
        hitStopEndTime = Time.time + scaledDuration;
        if (isAttacking)
            currentAttackEndTime += scaledDuration;

        if (animator != null && !IsAnimatorFrozen(animator))
        {
            frozenAnimators.Add(new FrozenAnimator { animator = animator, originalSpeed = animator.speed });
            animator.speed = 0f;
        }

        Animator victimAnim = _throwVictimAnimator;
        if (victimAnim == null && currentThrowVictim != null)
            victimAnim = (currentThrowVictim as Component)?.GetComponentInChildren<Animator>();
        if (victimAnim != null && !IsAnimatorFrozen(victimAnim))
        {
            frozenAnimators.Add(new FrozenAnimator { animator = victimAnim, originalSpeed = victimAnim.speed });
            victimAnim.speed = 0f;
        }

        if (profile.rumble && UnityEngine.InputSystem.Gamepad.current != null)
            StartCoroutine(RumbleForSeconds(scaledDuration));
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
            horizontalDir = _committedThrowDirection;
        else
            horizontalDir = _throwStartDirection;
        int damage = t.endDamage;
        float knockback = t.endKnockback;
        float knockbackUp = t.endKnockbackUp;
        // Apply throw charge scaling (1x when no charge was held, up to charge multipliers at full hold)
        int scaledDamage = Mathf.RoundToInt(damage * throwChargeDamageScale);
        float scaledKnockback = knockback * throwChargeKnockbackScale;
        float scaledKnockbackUp = knockbackUp * throwChargeKnockbackScale;

        if (t.invertKnockbackDirection) horizontalDir = -horizontalDir;

        Vector3 knockbackVector = applyKnockback
            ? ((horizontalDir * scaledKnockback) + (Vector3.up * scaledKnockbackUp))
            : Vector3.zero;

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
        float airborneDur = ((applyKnockback && t.launchOnRelease) || t.airborneOnRelease) ? t.endAirborneDuration : 0f;
        currentThrowVictim.TakeHit(damageToApply, knockbackVector, 0f, airborneDur);
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
        victimAI?.KickDownwardVelocity();

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

        // Slow both the player and victim animations while holding (skip if hit-stopped)
        float chargeSpeed = t.chargeAnimatorSpeed > 0f ? t.chargeAnimatorSpeed : 0.05f;
        if (animator != null && !IsAnimatorFrozen(animator)) animator.speed = chargeSpeed;
        if (_throwVictimAnimator != null && !IsAnimatorFrozen(_throwVictimAnimator)) _throwVictimAnimator.speed = chargeSpeed;

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

        // Snap victim to face opposite the player's new direction so they
        // rotate as a unit. Using -dir avoids stale-position issues (victim
        // position isn't updated to the new socket location until LateUpdate).
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) return;

        vt.rotation = Quaternion.LookRotation(-dir, Vector3.up);

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
        throwChargeHitStopScale   = Mathf.Lerp(1f, t.chargeHitStopMultiplier   > 0f ? t.chargeHitStopMultiplier   : 1f, normalized);

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
        if (animator != null && !IsAnimatorFrozen(animator)) animator.speed = 1f;
        if (_throwVictimAnimator != null && !IsAnimatorFrozen(_throwVictimAnimator)) _throwVictimAnimator.speed = 1f;
    }

    // Clears all throw charge state. Called on attack end, stun interrupt, and ForceThrowReleaseFallback.
    void ResetThrowChargeState()
    {
        if (isChargingThrow)
        {
            if (animator != null && !IsAnimatorFrozen(animator)) animator.speed = 1f;
            if (_throwVictimAnimator != null && !IsAnimatorFrozen(_throwVictimAnimator)) _throwVictimAnimator.speed = 1f;
        }
        isChargingThrow                    = false;
        throwChargeWindowOpen              = false;
        throwChargeKnockbackScale          = 1f;
        throwChargeDamageScale             = 1f;
        throwChargeHitStopScale            = 1f;
        _directionalThrowRotationActive    = false;
        _hasDirectionalThrowTarget         = false;
        _hasCommittedThrowDirection        = false;
        _directionalThrowVictimTransform   = null;
        _throwVictimAnimator               = null;
        _victimThrownAnimLayer             = 0;
        _victimThrownStateName             = null;
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
