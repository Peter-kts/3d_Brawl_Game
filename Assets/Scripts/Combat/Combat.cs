/*
 * ============================================================================
 * COMBAT.CS - Deliberate attack system (NO auto-aim, NO distance closing)
 * ============================================================================
 * 
 * COMBAT PHILOSOPHY:
 * ------------------
 * 
 * CORE RULE: Attacks NEVER solve spacing or targeting.
 * 
 * If attacking ever:
 *   - Moves the player forward
 *   - Snaps to face an enemy
 *   - Auto-corrects bad positioning
 * Then the system collapses into button spam.
 * 
 * WHAT ATTACKS DO:
 *   - Check if in Combat Mode (required)
 *   - Sample stick direction at commit time
 *   - Apply small torso rotation (≤20°) based on stick
 *   - Create hitbox where player is ALREADY facing
 *   - Whiff if spacing is wrong
 * 
 * WHAT ATTACKS DON'T DO:
 *   - Move player forward
 *   - Auto-aim toward enemies
 *   - Correct bad facing
 *   - Close distance
 * 
 * The player must:
 *   - Enter Combat Mode
 *   - Step into range (footwork)
 *   - Maintain facing (positioning)
 *   - Commit to attacks (timing)
 * 
 * ============================================================================
 */

using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;

public partial class Combat : MonoBehaviour
{
    /// <summary>Debug visualization state for hitbox. Used by CombatHitboxDebugVisual.</summary>
    public struct HitboxDebugState
    {
        public bool showActive;
        public bool showPreview;
        public Vector3 center;
        public float radius;
        public Color color;
    }

    // ========================================================================
    // REFERENCES
    // ========================================================================
    
    [Header("References")]
    [Tooltip("Where attacks originate from. If null, uses player's center.")]
    public Transform hitOrigin;
    
    [Tooltip("Reference to PlayerController to check Combat Mode state")]
    public PlayerController playerController;
    
    [Tooltip("Reference to threat system for interaction registration")]
    public LockOnSystem threatSystem;
    
    [Tooltip("Animator for playing attack animations. Auto-finds on this object or children if not set.")]
    public Animator animator;
    
    [Header("Throw (grab socket)")]
    [Tooltip("Empty child transform at hands/chest. Victim is parented here during hold so they ride the throw anim. Add Animation Event 'OnThrowRelease' at the chuck frame.")]
    public Transform grabSocket;
    
    [Header("VFX (optional)")]
    [Tooltip("Optional. Spawned when the attack animation starts (e.g. swing trail).")]
    public GameObject attackStartVfxPrefab;
    
    [Tooltip("Optional. Spawned at hitbox center when the attack connects with a target.")]
    public GameObject hitConnectVfxPrefab;
    
    // ========================================================================
    // ATTACK DIRECTION SETTINGS
    // ========================================================================
    
    [Header("Attack Direction")]
    [Tooltip("Maximum torso rotation when attacking with stick input (degrees). Attacks can adjust facing slightly, but NOT snap to targets.")]
    [Range(0f, 45f)]
    public float maxTorsoRotation = 20f;
    
    [Tooltip("Minimum stick input magnitude to apply directional rotation")]
    public float stickDeadzone = 0.3f;
    
    // ========================================================================
    // COMBO / MOVES (edit in ComboSet asset; Create > Combat > Combo Set)
    // ========================================================================
    
    [Tooltip("All attacks and combo timing. Create via right-click > Create > Combat > Combo Set if missing.")]
    public ComboSet comboSet;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================
    
    private float nextAttackTime;
    private float currentAttackEndTime;
    private float currentAttackRange;
    private float currentAttackRadius;
    private Color currentAttackColor;
    private bool isAttacking;
    public bool IsAttacking => isAttacking;
    /// <summary>True while the current attack lock is active (player cannot dash until this is false).</summary>
    public bool IsInAttackLock => isAttacking && Time.time < currentAttackEndTime;
    
    // Store the rotation applied during attack (to revert torso rotation)
    private Quaternion preAttackRotation;
    private bool hasAppliedTorsoRotation;
    
    // Combo state
    private int lightComboCount = 0;        // 0 = ready, 1 = in first jab (can cancel), 2 = in second jab (must wait)
    private float comboWindowStart = 0f;    // When the cancel window opens
    private float comboWindowEnd = 0f;      // When the cancel window closes
    private bool isNeutralCombo = false;    // True = neutral jab chain, False = forward jab chain
    
    // Attack lunge state (shared by all attacks)
    private bool lungePending = false;
    private float lungeTriggerTime = 0f;
    private float lungeEndTime = 0f;
    private float currentLungeDistance = 0f;
    private float currentLungeDuration = 0f;
    private Vector3 lungeDirection;
    
    // Tracking state (rotate toward soft target for trackingDuration after attack start)
    private float trackingEndTime = 0f;
    private float currentTrackingSpeed = 0f;
    
    // Delayed hitbox state (fires after hitboxDelay, similar to lunge scheduling)
    private bool hitboxPending;
    private float hitboxTriggerTime;
    private AttackData pendingAttackData;
    private bool hitboxHasFired;         // True once the hitbox has been checked this attack
    
    // Hit stop state (animator-only freeze on hit)
    private struct FrozenAnimator
    {
        public Animator animator;
        public float originalSpeed;
    }
    private float hitStopEndTime;
    private List<FrozenAnimator> frozenAnimators = new List<FrozenAnimator>();
    
    // Current attack offset for debug visualization API
    private Vector3 currentAttackOffset;

    // When the current attack started (for timing debug)
    private float currentAttackStartTime;
    
    // Start-up and recovery (play first/last portion of attack animation slower)
    private float currentStartUpLength;
    private float currentStartUpSpeed;
    private float currentRecoveryLength;
    private float currentRecoverySpeed;
    private string currentAttackStateName;  // Only apply speed when we're still in this state

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    void Awake()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (threatSystem == null) threatSystem = GetComponent<LockOnSystem>();
        
        // Auto-find animator on this object or in children (e.g., on the visual model)
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void LateUpdate()
    {
        // Re-apply baked position one frame after release (no-launch only) so enemy scripts/gravity don't overwrite it
        if (_reapplyThrowBakeNextFrame && _reapplyThrowBakeTransform != null)
        {
            _reapplyThrowBakeTransform.position = _reapplyThrowBakePosition;
            _reapplyThrowBakeTransform.rotation = _reapplyThrowBakeRotation;
            _reapplyThrowBakeNextFrame = false;
            _reapplyThrowBakeTransform = null;
        }
        // Deferred throw damage (from OnThrowDamage animation event): apply on exact frame, then clear so release path doesn't double-apply
        bool throwDamageAppliedThisFrame = false;
        if (_deferThrowDamageToLateUpdate && currentThrowVictim != null && comboSet != null && comboSet.throwData.enableThrow)
        {
            ApplyThrowEndDamage(_deferThrowDamageProfileIndex);
            _deferThrowDamageToLateUpdate = false;
            _deferThrowDamageProfileIndex = -1;
            throwDamageAppliedThisFrame = true;
        }
        // Throw release was deferred (from OnThrowRelease or from Update timer) so we run after Animator has applied root motion this frame
        if (!_deferThrowReleaseToLateUpdate || currentThrowVictim == null || comboSet == null || !comboSet.throwData.enableThrow) return;
        _deferThrowReleaseToLateUpdate = false;
        Transform vt = (currentThrowVictim as Component)?.transform;
        if (vt == null) { currentThrowVictim = null; return; }
        BakePlayerThrowRootMotionAndRestore();
        ReleaseThrowVictimFromSocket();
        CompleteThrowRelease(vt, _deferThrowReleaseProfileIndex, throwDamageAppliedThisFrame, applyReleaseEffects: true);
    }

    void Update()
    {
        if (isAttacking && Time.time >= currentAttackEndTime)
        {
            // We're in a throw hold and the throw phase duration expired: defer release to LateUpdate so we read pose after Animator updates (same as OnThrowRelease)
            if (currentThrowVictim != null && comboSet != null && comboSet.throwData.enableThrow)
            {
                if (!_deferThrowReleaseToLateUpdate)
                {
                    _deferThrowReleaseToLateUpdate = true;
                    _deferThrowReleaseProfileIndex = -1;  // Timer path uses default release profile
                }
            }
            else
            {
                // Attack ended without deferring (e.g. no victim or throw disabled); release victim if any, then clean up
                if (currentThrowVictim != null && comboSet != null && comboSet.throwData.enableThrow)
                {
                    Transform vt = (currentThrowVictim as Component)?.transform;
                    BakePlayerThrowRootMotionAndRestore();
                    ReleaseThrowVictimFromSocket();
                    CompleteThrowRelease(vt, -1, damageAlreadyAppliedThisFrame: false, applyReleaseEffects: true);
                }
                if ((currentStartUpLength > 0f || currentRecoveryLength > 0f) && animator != null && !frozenAnimators.Any(f => f.animator == animator))
                    animator.speed = 1f;
                isAttacking = false;
                hitboxPending = false;
                pendingThrowHitbox = false;
            }
        }
        var damageableForStun = GetComponentInParent<IDamageable>();
        if (damageableForStun != null && damageableForStun.IsStunned && (isAttacking || hitboxPending))
        {
            // Player stunned (e.g. hit during throw): release victim without damage/get-up, then clear state
            if (currentThrowVictim != null)
            {
                Transform vt = (currentThrowVictim as Component)?.transform;
                BakePlayerThrowRootMotionAndRestore();
                ReleaseThrowVictimFromSocket();
                CompleteThrowRelease(vt, -1, damageAlreadyAppliedThisFrame: true, applyReleaseEffects: false);
            }
            else
                ClearThrowState();
            hasAppliedTorsoRotation = false;
        }
        UpdateComboState();
        UpdateAttackTracking();
        UpdateAttackLunge();
        UpdatePendingHitbox();
        UpdateHitStop();
        UpdateAttackStartUpSpeed();
        
        // Revert torso rotation after attack window
        if (hasAppliedTorsoRotation && Time.time >= currentAttackEndTime)
        {
            hasAppliedTorsoRotation = false;
        }
        
        // Check attack inputs (gamepad: light=RB, heavy=RT)
        bool lightAttackInput = 
            (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame) ||
            (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame);
        
        bool heavyAttackInput = 
            (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);
        
        bool throwInput =
            (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
        
        // Ninja Gaiden style: attacks allowed anytime (soft lock only; no combat-mode gate)
        bool canAttack = playerController != null;
        var damageable = GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.IsStunned)
            canAttack = false;
        
        if (!canAttack)
        {
            lightComboCount = 0;
            return;
        }
        
        if (comboSet == null) return;
        
        // Throw: dedicated input (G / X button); requires lock-on; blocked if already attacking or on cooldown
        bool canThrow = threatSystem != null && threatSystem.HasSoftTarget;
        if (throwInput && canThrow && !isAttacking && Time.time >= nextThrowTime && comboSet.throwData.enableThrow)
        {
            DoThrow();
            return;
        }
        
        // Check if we're in the cancel window for combo
        bool inCancelWindow = (lightComboCount == 1) && 
                              (Time.time >= comboWindowStart) && 
                              (Time.time <= comboWindowEnd);
        
        // Block attacks if in cooldown (UNLESS we're in the cancel window)
        if (Time.time < nextAttackTime && !inCancelWindow) return;
        
        DebugSettings debug = DebugSettings.Instance;
        
        if (lightAttackInput)
        {
            DoLightAttack(debug.lightAttackColor, inCancelWindow);
        }
        else if (heavyAttackInput)
        {
            // Heavy attack resets combo
            lightComboCount = 0;
            DoAttack(comboSet.heavyAttack, debug.heavyAttackColor);
        }
    }
    
    // ========================================================================
    // COMBO SYSTEM
    // ========================================================================
    
    /// <summary>
    /// Reads raw stick input directly (bypasses CombatStickInput which doesn't update during attacks)
    /// </summary>
    Vector2 GetRawStickInput()
    {
        Vector2 input = Vector2.zero;
        
        if (Keyboard.current != null)
        {
            float h = 0f, v = 0f;
            if (Keyboard.current.aKey.isPressed) h -= 1f;
            if (Keyboard.current.dKey.isPressed) h += 1f;
            if (Keyboard.current.sKey.isPressed) v -= 1f;
            if (Keyboard.current.wKey.isPressed) v += 1f;
            input = new Vector2(h, v);
        }
        
        if (Gamepad.current != null && input.sqrMagnitude < 0.01f)
        {
            input = Gamepad.current.leftStick.ReadValue();
        }
        
        return input;
    }
    
    void UpdateComboState()
    {
        // Reset combo if cancel window expired without chaining
        if (lightComboCount == 1 && Time.time > comboWindowEnd)
        {
            lightComboCount = 0;
        }
    }
    
    void UpdateAttackLunge()
    {
        if (!lungePending) return;
        // Freeze attacker position during hitstop
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime) return;
        
        // Check if we've reached the trigger frame
        if (Time.time >= lungeTriggerTime && Time.time < lungeEndTime)
        {
            // During tracking window, lunge direction follows current facing so step and hitbox stay aligned
            if (Time.time < trackingEndTime)
                lungeDirection = transform.forward;
            // Calculate how much to move this frame
            float lungeProgress = (Time.time - lungeTriggerTime) / currentLungeDuration;
            if (lungeProgress <= 1f)
            {
                float moveAmount = (currentLungeDistance / currentLungeDuration) * Time.deltaTime;
                CharacterController cc = GetComponent<CharacterController>();
                if (cc != null)
                {
                    cc.Move(lungeDirection * moveAmount);
                }
            }
        }
        
        // Stop tracking when lunge window is over
        if (Time.time >= lungeEndTime)
        {
            lungePending = false;
        }
    }
    
    void UpdateAttackTracking()
    {
        if (!isAttacking || trackingEndTime <= 0f || Time.time >= trackingEndTime) return;
        if (threatSystem == null || !threatSystem.HasSoftTarget) return;
        
        Transform target = threatSystem.SoftTarget;
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;
        toTarget.Normalize();
        
        Vector3 currentForward = transform.forward;
        currentForward.y = 0f;
        if (currentForward.sqrMagnitude < 0.0001f) return;
        currentForward.Normalize();
        
        float maxRadians = currentTrackingSpeed * Mathf.Deg2Rad * Time.deltaTime;
        Vector3 newForward = Vector3.RotateTowards(currentForward, toTarget, maxRadians, 0f);
        newForward.y = 0f;
        newForward.Normalize();
        if (newForward.sqrMagnitude < 0.0001f) return;
        
        transform.rotation = Quaternion.LookRotation(newForward);
    }
    
    void SetupLunge(AttackData attack)
    {
        lungePending = true;
        // Use lockDuration for timing since lungeFrame is a normalized time within the attack animation
        lungeTriggerTime = Time.time + (attack.lockDuration * attack.lungeFrame);
        lungeEndTime = lungeTriggerTime + attack.lungeDuration;
        currentLungeDistance = attack.lungeDistance;
        currentLungeDuration = attack.lungeDuration;
        lungeDirection = transform.forward;
    }
    
    void DoLightAttack(Color visualColor, bool inCancelWindow)
    {
        /*
         * LIGHT ATTACK COMBO (Cancel System):
         * 
         * Two jab types based on input:
         * - Forward Jab: Hold forward + attack (lunges forward)
         * - Neutral Jab: No direction + attack (stays in place)
         * 
         * Each type has its own combo chain (jab 1 → jab 2)
         * Once a combo starts, it stays on that track.
         * 
         * Timeline:
         * [Jab 1 starts]---[window opens]---[window closes]---[cooldown ends]
         *                  ^               ^
         *                  Can cancel here to Jab 2
         */
        
        if (lightComboCount == 0)
        {
            // Starting a new combo - check if holding forward
            bool holdingForward = playerController != null && GetRawStickInput().y > 0.3f;
            isNeutralCombo = !holdingForward;
            Debug.Log($"[Combat] holdingForward: {holdingForward}");
            
            // First jab - starts the combo
            AttackData jab1 = isNeutralCombo ? comboSet.neutralJab : comboSet.forwardJab;
            DoAttack(jab1, visualColor);
            
            // Set up cancel window (during the animation)
            lightComboCount = 1;
            float delay = jab1.comboWindowDelay >= 0f ? jab1.comboWindowDelay : comboSet.comboWindowDelay;
            float duration = jab1.comboWindowDuration >= 0f ? jab1.comboWindowDuration : comboSet.comboWindowDuration;
            comboWindowStart = Time.time + delay;
            comboWindowEnd = comboWindowStart + duration;
        }
        else if (lightComboCount == 1 && inCancelWindow)
        {
            // Second jab - read raw input directly (CombatStickInput is stale during attacks)
            bool holdingForwardNow = GetRawStickInput().y > 0.3f;
            AttackData jab2 = holdingForwardNow ? comboSet.forwardJab2 : comboSet.neutralJab2;
            Color jab2Color = holdingForwardNow ? Color.cyan : Color.yellow;
            DoAttack(jab2, jab2Color);
            
            // Combo finished - must wait full cooldown now
            lightComboCount = 0;
        }
        // If not in cancel window, attack is blocked by cooldown check in Update()
    }

    // ========================================================================
    // ATTACK EXECUTION
    // ========================================================================
    
    void DoAttack(AttackData attack, Color visualColor)
    {
        currentAttackStartTime = Time.time;
        // Set cooldown (when you can attack again) and lock duration (when you can move again)
        nextAttackTime = Time.time + attack.cooldown;
        currentAttackEndTime = Time.time + attack.lockDuration;
        currentAttackRange = attack.range;
        currentAttackRadius = attack.hitboxRadius;
        currentAttackColor = visualColor;
        currentAttackOffset = attack.hitboxOffset;
        currentStartUpLength = attack.startUpLength;
        currentStartUpSpeed = attack.startUpSpeed;
        currentRecoveryLength = attack.recoveryLength;
        currentRecoverySpeed = attack.recoverySpeed;
        currentAttackStateName = !string.IsNullOrEmpty(attack.animationTrigger) ? attack.animationTrigger : null;
        isAttacking = true;
        
        // Clear attacker's hit stop so the new animation and lunge run immediately (keeps F1→F2 in sync)
        if (animator != null)
        {
            for (int i = frozenAnimators.Count - 1; i >= 0; i--)
            {
                if (frozenAnimators[i].animator == animator)
                {
                    frozenAnimators[i].animator.speed = frozenAnimators[i].originalSpeed;
                    frozenAnimators.RemoveAt(i);
                    break;
                }
            }
        }
        
        // Cancel any pending hitbox from a previous attack
        hitboxPending = false;
        hitboxHasFired = false;
        
        // Set up tracking window (optional: rotate toward soft target for a short time)
        if (attack.trackingDuration > 0f)
        {
            trackingEndTime = Time.time + attack.trackingDuration;
            currentTrackingSpeed = attack.trackingSpeed > 0f ? attack.trackingSpeed : 540f;
        }
        else
        {
            trackingEndTime = 0f;
        }
        
        // Set up lunge based on attack data (neutral jabs have lungeDistance=0, so no movement)
        if (attack.lungeDistance > 0)
        {
            SetupLunge(attack);
        }
        
        // Play attack animation (no crossfade — keeps hitbox timing consistent)
        if (animator != null && !string.IsNullOrEmpty(attack.animationTrigger))
        {
            if (DebugSettings.Instance != null && DebugSettings.Instance.logAttackTiming)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[AttackStart] trigger='{attack.animationTrigger}' hitboxDelay={attack.hitboxDelay:F3} startUp=({attack.startUpLength:F2},{attack.startUpSpeed:F2}) recovery=({attack.recoveryLength:F2},{attack.recoverySpeed:F2}) | fromStateHash={state.shortNameHash} fromNT={state.normalizedTime:F3}");
            }
            else
                Debug.Log($"Playing animation trigger: '{attack.animationTrigger}'");
            animator.Play(attack.animationTrigger, 0, 0f);
        }
        
        if (attackStartVfxPrefab != null)
        {
            Transform origin = hitOrigin != null ? hitOrigin : transform;
            Vector3 pos = origin.position + attack.attackStartVfxPositionOffset;
            Quaternion rot = transform.rotation * Quaternion.Euler(attack.attackStartVfxRotationOffset);
            var go = Instantiate(attackStartVfxPrefab, pos, rot);
            PlayVfx(go);
        }
        
        // --------------------------------------------------------------------
        // DIRECTIONAL INPUT (not auto-aim)
        // --------------------------------------------------------------------
        
        /*
         * ATTACK DIRECTION FROM STICK:
         * 
         * The stick direction at the moment of attack commit determines
         * a SMALL torso rotation adjustment. This is NOT auto-aim.
         * 
         * - If stick is neutral: attack straight forward
         * - If stick is left/right: rotate up to maxTorsoRotation degrees
         * - This simulates the character adjusting their swing direction
         * 
         * The key difference from auto-aim:
         * - This is relative to CHARACTER facing, not camera
         * - It's capped at a small angle (20°)
         * - It doesn't seek out enemies
         * - Wrong facing = whiff
         */
        
        preAttackRotation = transform.rotation;
        
        if (playerController != null)
        {
            Vector2 stickInput = playerController.CombatStickInput;
            
            if (stickInput.magnitude > stickDeadzone)
            {
                // Calculate desired rotation adjustment based on stick
                // Stick X = lateral adjustment
                float rotationAdjustment = stickInput.x * maxTorsoRotation;

                // Apply torso rotation
                transform.Rotate(0f, rotationAdjustment, 0f);
                hasAppliedTorsoRotation = true;
            }
        }
        
        // --------------------------------------------------------------------
        // HITBOX (scheduled with optional delay)
        // --------------------------------------------------------------------
        
        /*
         * HITBOX SCHEDULING:
         * 
         * The hitbox can fire immediately (hitboxDelay = 0) or after a delay
         * to match the animation wind-up. This lets you sync the damage check
         * with the exact frame the punch/kick connects visually.
         * 
         * If the enemy is too far → whiff
         * If the enemy is behind → whiff
         * If the player isn't facing the enemy → whiff
         * 
         * This is deliberate. Spacing and facing are the player's job.
         */
        
        if (attack.hitboxDelay > 0f)
        {
            // Schedule hitbox for later (syncs with animation)
            hitboxPending = true;
            hitboxTriggerTime = Time.time + attack.hitboxDelay;
            pendingAttackData = attack;
        }
        else
        {
            // Fire immediately (backward compatible, delay = 0)
            ExecuteHitbox(attack);
        }
    }

    // ========================================================================
    // HITBOX HELPERS
    // ========================================================================
    
    static void PlayVfx(GameObject instance)
    {
        if (instance == null) return;
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play();
    }
    
    /// <summary>
    /// Returns light and heavy hitbox center/radius for editor Gizmos.
    /// </summary>
    public (Vector3 lightCenter, float lightRadius, Vector3 heavyCenter, float heavyRadius) GetEditorHitboxCenters()
    {
        if (comboSet == null) return (transform.position, 0f, transform.position, 0f);
        Transform origin = hitOrigin != null ? hitOrigin : transform;
        AttackData light = comboSet.forwardJab;
        AttackData heavy = comboSet.heavyAttack;
        Vector3 centerL = origin.position
            + transform.forward * light.range
            + transform.right * light.hitboxOffset.x
            + transform.up * light.hitboxOffset.y
            + transform.forward * light.hitboxOffset.z;
        Vector3 centerH = origin.position
            + transform.forward * heavy.range
            + transform.right * heavy.hitboxOffset.x
            + transform.up * heavy.hitboxOffset.y
            + transform.forward * heavy.hitboxOffset.z;
        return (centerL, light.hitboxRadius, centerH, heavy.hitboxRadius);
    }

    /// <summary>
    /// Returns current hitbox state for debug visualization. No side effects.
    /// </summary>
    public HitboxDebugState GetHitboxDebugState()
    {
        var state = new HitboxDebugState();
        bool inCombatMode = playerController != null && playerController.IsInCombatMode;
        state.showActive = isAttacking && hitboxHasFired;
        state.showPreview = !isAttacking && inCombatMode;

        if (state.showActive)
        {
            Transform origin = hitOrigin != null ? hitOrigin : transform;
            state.center = origin.position
                + transform.forward * currentAttackRange
                + transform.right * currentAttackOffset.x
                + transform.up * currentAttackOffset.y
                + transform.forward * currentAttackOffset.z;
            state.radius = currentAttackRadius;
            state.color = currentAttackColor;
        }
        else if (state.showPreview && comboSet != null)
        {
            state.center = CalculateHitboxCenter(comboSet.forwardJab);
            state.radius = comboSet.forwardJab.hitboxRadius;
            state.color = DebugSettings.Instance.lightAttackColor;
            state.color.a = 0.15f;
        }
        return state;
    }

    /// <summary>
    /// Calculate the hitbox center position using range + local-space offset.
    /// X = right, Y = up, Z = additional forward (on top of range).
    /// </summary>
    Vector3 CalculateHitboxCenter(AttackData attack)
    {
        Transform origin = hitOrigin != null ? hitOrigin : transform;
        return origin.position
            + transform.forward * attack.range
            + transform.right   * attack.hitboxOffset.x
            + transform.up      * attack.hitboxOffset.y
            + transform.forward * attack.hitboxOffset.z;
    }
    
    /// <summary>
    /// Fires the hitbox: OverlapSphere, damage dealing, knockback, and hit stop.
    /// Called immediately (delay=0) or after hitboxDelay from UpdatePendingHitbox().
    /// Each damageable is only hit once per hitbox fire (multiple colliders on same object are deduplicated).
    /// </summary>
    void ExecuteHitbox(AttackData attack)
    {
        hitboxHasFired = true;
        hitboxPending = false;
        
        if (DebugSettings.Instance != null && DebugSettings.Instance.logAttackTiming)
        {
            float realTimeSinceStart = Time.time - currentAttackStartTime;
            string stateName = "";
            float nt = -1f;
            if (animator != null)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                nt = state.normalizedTime;
                stateName = state.shortNameHash.ToString();
            }
            Debug.Log($"[HitboxFire] realTimeSinceStart={realTimeSinceStart:F3} (expected ~{attack.hitboxDelay:F3}) | animatorStateHash={stateName} normalizedTime={nt:F3}");
        }
        
        Vector3 center = CalculateHitboxCenter(attack);
        
        // Find hits
        Collider[] hits = Physics.OverlapSphere(center, attack.hitboxRadius, ~0, QueryTriggerInteraction.Ignore);
        
        // #region agent log
        try { var t = (long)(Time.realtimeSinceStartup * 1000); File.AppendAllText(@"c:\Users\peter\3dbrawlerlearn\3dbrawlerlearn\.cursor\debug.log", "{\"location\":\"Combat.cs:ExecuteHitbox\",\"message\":\"ExecuteHitbox\",\"data\":{\"colliderCount\":" + hits.Length + "},\"timestamp\":" + t + ",\"hypothesisId\":\"H2\"}\n"); } catch { }
        // #endregion
        
        bool didHit = false;
        var alreadyHit = new HashSet<Component>();
        
        foreach (var c in hits)
        {
            var damageable = c.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;
            if ((damageable as Component)?.gameObject == gameObject) continue;
            
            var comp = damageable as Component;
            if (comp != null && alreadyHit.Contains(comp)) continue;
            if (comp != null) alreadyHit.Add(comp);
            
            Transform targetTransform = (damageable as Component)?.transform;
            if (targetTransform == null) continue;
            
            // Calculate knockback direction (away from attacker)
            Vector3 horizontalDir = (targetTransform.position - transform.position);
            horizontalDir.y = 0f;
            if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = transform.forward;
            horizontalDir.Normalize();
            
            Vector3 knockbackVector = (horizontalDir * attack.knockback) + (Vector3.up * attack.knockbackUp);
            
            // Apply damage
            float airborne = attack.makesAirborne ? attack.airborneDuration : 0f;
            // #region agent log
            try { var tn = (targetTransform?.gameObject?.name ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); File.AppendAllText(@"c:\Users\peter\3dbrawlerlearn\3dbrawlerlearn\.cursor\debug.log", "{\"location\":\"Combat.cs:TakeHit\",\"message\":\"TakeHit\",\"data\":{\"target\":\"" + tn + "\"},\"timestamp\":" + (long)(Time.realtimeSinceStartup * 1000) + ",\"hypothesisId\":\"H1\"}\n"); } catch { }
            // #endregion
            damageable.TakeHit(attack.damage, knockbackVector, attack.hitstun, airborne, attack.hitStopDuration);
            
            // Register interaction with threat system (boosts this enemy's priority)
            if (threatSystem != null)
            {
                threatSystem.RegisterInteraction(targetTransform);
            }
            
            // Freeze target's animator for hit stop
            if (attack.hitStopDuration > 0f)
            {
                Animator targetAnim = targetTransform.GetComponentInChildren<Animator>();
                if (targetAnim != null && !frozenAnimators.Any(f => f.animator == targetAnim))
                {
                    frozenAnimators.Add(new FrozenAnimator { animator = targetAnim, originalSpeed = targetAnim.speed });
                    targetAnim.speed = 0f;
                }
            }
            
            didHit = true;
        }
        
        if (didHit && hitConnectVfxPrefab != null)
        {
            Quaternion rot = (center - transform.position).sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(center - transform.position)
                : transform.rotation;
            rot = rot * Quaternion.Euler(attack.hitConnectVfxRotationOffset);
            var go = Instantiate(hitConnectVfxPrefab, center + attack.hitConnectVfxPositionOffset, rot);
            PlayVfx(go);
        }
        
        // Apply hit stop to attacker if we hit something
        if (didHit && attack.hitStopDuration > 0f)
        {
            hitStopEndTime = Time.time + attack.hitStopDuration;
            
            if (animator != null && !frozenAnimators.Any(f => f.animator == animator))
            {
                frozenAnimators.Add(new FrozenAnimator { animator = animator, originalSpeed = animator.speed });
                animator.speed = 0f;
            }
            
            if (Gamepad.current != null)
                StartCoroutine(RumbleForSeconds(attack.hitStopDuration));
        }
    }
    
    /// <summary>
    /// Check if a scheduled hitbox is ready to fire (normal attack or throw).
    /// </summary>
    void UpdatePendingHitbox()
    {
        // Throw grab: fire once when delay elapsed; if no enemy in sphere, we whiff and attack lock ends at currentAttackEndTime
        if (pendingThrowHitbox && Time.time >= throwHitboxTriggerTime)
        {
            ExecuteThrowHitbox();
            pendingThrowHitbox = false;
            return;
        }
        if (!hitboxPending) return;
        
        if (Time.time >= hitboxTriggerTime)
        {
            ExecuteHitbox(pendingAttackData);
        }
    }
    
    // ========================================================================
    // HIT STOP
    // ========================================================================
    
    /*
     * Hit Stop (Animator-only):
     * 
     * When an attack connects, both the attacker's and target's animators
     * are briefly frozen (speed = 0). This creates the satisfying "impact
     * freeze" effect common in fighting games.
     * 
     * Unlike Time.timeScale, this only affects animations - physics,
     * timers, and other enemies continue normally.
     */
    void UpdateHitStop()
    {
        if (frozenAnimators.Count > 0 && Time.time >= hitStopEndTime)
        {
            foreach (var frozen in frozenAnimators)
            {
                if (frozen.animator != null)
                {
                    frozen.animator.speed = frozen.originalSpeed;
                }
            }
            frozenAnimators.Clear();
        }
    }
    
    /// <summary>
    /// During attack, set animator speed: start-up and recovery portions can play slower, middle at 1.
    /// Only applies while we're still in the attack state (so recovery works; animator must not exit early).
    /// </summary>
    void UpdateAttackStartUpSpeed()
    {
        if (!isAttacking || animator == null) return;
        if (frozenAnimators.Any(f => f.animator == animator)) return;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!string.IsNullOrEmpty(currentAttackStateName) && !state.IsName(currentAttackStateName))
        {
            animator.speed = 1f;
            return;
        }
        bool useStartUp = currentStartUpLength > 0f && currentStartUpSpeed < 1f;
        bool useRecovery = currentRecoveryLength > 0f && currentRecoverySpeed < 1f;
        if (!useStartUp && !useRecovery)
        {
            animator.speed = 1f;
            return;
        }
        float nt = state.normalizedTime;
        if (nt >= 1f)
            animator.speed = 1f;
        else if (useStartUp && nt < currentStartUpLength)
            animator.speed = currentStartUpSpeed;
        else if (useRecovery && nt >= (1f - currentRecoveryLength))
            animator.speed = currentRecoverySpeed;
        else
            animator.speed = 1f;
    }
    
    IEnumerator RumbleForSeconds(float duration)
    {
        var gamepad = Gamepad.current;
        if (gamepad == null) yield break;
        gamepad.SetMotorSpeeds(0.25f, 0.5f);
        yield return new WaitForSecondsRealtime(duration);
        gamepad.SetMotorSpeeds(0f, 0f);
    }

}
