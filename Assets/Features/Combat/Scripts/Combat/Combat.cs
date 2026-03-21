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

// Run before PlayerController so lunge velocity is queued before ApplyGravity drains it.
[DefaultExecutionOrder(-100)]
public partial class Combat : MonoBehaviour
{
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
    [Tooltip("Animator bool used by attack->idle/locomotion transitions when states do not use Exit Time.")]
    public string attackingBoolParameter = "IsAttacking";
    
    [Header("Throw (grab socket)")]
    [Tooltip("Empty child transform at hands/chest. Victim is parented here during hold so they ride the throw anim. Add Animation Event 'OnThrowRelease' at the chuck frame.")]
    public Transform grabSocket;
    
    [Header("VFX (optional)")]
    [Tooltip("Optional. Spawned when the attack animation starts (e.g. swing trail).")]
    public GameObject attackStartVfxPrefab;
    
    [Tooltip("Optional. Spawned at hitbox center when the attack connects with a target.")]
    public GameObject hitConnectVfxPrefab;

    [Header("SFX (optional)")]
    [Tooltip("Audio source used for attack sounds. Auto-finds on this object/children if not assigned.")]
    public AudioSource sfxSource;
    [Tooltip("Lowest random pitch used for attack SFX.")]
    [Range(0.5f, 1.5f)]
    public float sfxPitchMin = 0.96f;
    [Tooltip("Highest random pitch used for attack SFX.")]
    [Range(0.5f, 1.5f)]
    public float sfxPitchMax = 1.04f;
    [Tooltip("Fade-out time when charge release stops startup SFX.")]
    [Min(0f)]
    public float chargeReleaseSfxFadeOutDuration = 0.12f;
    [Header("Charge SFX (optional)")]
    [Tooltip("Dedicated looping SFX played while charge is actively held.")]
    public AudioClip chargeLoopSfx;
    [Tooltip("Volume scale for dedicated charge loop SFX.")]
    [Range(0f, 1f)]
    public float chargeLoopSfxVolume = 0.7f;
    [Tooltip("Pitch for dedicated charge loop SFX.")]
    [Range(0.5f, 1.5f)]
    public float chargeLoopSfxPitch = 1f;
    [Tooltip("Fade-out time when dedicated charge loop stops.")]
    [Min(0f)]
    public float chargeLoopSfxFadeOutDuration = 0.12f;
    private AudioSource attackStartSfxSource;          // Dedicated AudioSource for attack-start SFX; cloned from sfxSource so it can fade independently
    private Coroutine attackStartSfxFadeCoroutine;     // Active fade coroutine (if any); cancelled before starting a new fade to avoid overlap
    private float attackStartSfxTargetVolume = 1f;     // Volume to restore after fade; mirrors the source's max volume
    private AudioSource chargeLoopSfxSource;           // Dedicated AudioSource for the charge-hold loop SFX; looping, separate from hit SFX
    private Coroutine chargeLoopSfxFadeCoroutine;      // Active fade coroutine for the charge loop; cancelled on new fade
    private float chargeLoopSfxTargetVolume = 1f;      // Volume to restore after fade ends
    
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
    
    private float nextAttackTime;              // Earliest Time.time a new attack can start (set to Time.time + attack.cooldown after each commit)
    private float currentAttackEndTime;        // Time.time when the attack lock expires; IsAttacking stays true until this passes
    private bool isAttacking;                  // True from attack commit until currentAttackEndTime; blocks new attacks and dashes
    private float lastAttackEndTime = -999f;   // Time.time when the last attack lock expired naturally; used by RecentlyAttacked()
    public bool IsAttacking => isAttacking;
    /// <summary>True while the current attack lock is active (player cannot dash until this is false).</summary>
    public bool IsInAttackLock => isAttacking && Time.time < currentAttackEndTime;
    /// <summary>Seconds remaining in the current attack lock. 0 when not attacking. Used by cautious enemies to decide whether to interrupt.</summary>
    public float AttackLockTimeRemaining => isAttacking ? Mathf.Max(0f, currentAttackEndTime - Time.time) : 0f;
    /// <summary>True if the player's attack lock ended within the last windowSeconds. Mirrors RecentlyDodged() — enemies use this to punish recovery frames.</summary>
    public bool RecentlyAttacked(float windowSeconds) => lastAttackEndTime > 0f && (Time.time - lastAttackEndTime) <= windowSeconds;

    private Quaternion preAttackRotation;      // Player rotation before torso rotation was applied; used to revert after attack ends
    private bool hasAppliedTorsoRotation;      // True if torso rotation was applied this attack; gates the revert logic
    
    // Combo state
    private int lightComboCount = 0;           // 0 = ready, 1 = in first jab (can cancel), 2 = in second jab (must wait)
    private float comboWindowStart = 0f;       // When the cancel window opens
    private float comboWindowEnd = 0f;         // When the cancel window closes
    private bool isNeutralCombo = false;       // True = neutral jab chain, False = forward jab chain
    private bool neutralComboLoopPending = false; // True after neutral jab 2 when loopNeutralCombo is on; next cancel fires jab 1 again

    [Tooltip("Max seconds between pressing forward and pressing attack for it to count as a forward attack. " +
             "If forward was already held before this window, the attack is treated as neutral.")]
    [Min(0f)]
    public float forwardAttackInputWindow = 0.15f;
    private float forwardStickPressTime = -999f; // Time.time when stick Y last crossed the forward threshold (rising edge)
    private float prevRawForwardY = 0f;          // Previous frame's raw stick Y; used to detect the rising edge
    private float stickMoveTime = -999f;          // Time.time when stick magnitude last crossed the movement threshold (rising edge)
    private float prevStickMagnitude = 0f;        // Previous frame's stick magnitude; used to detect the rising edge
    [Tooltip("How long light input must be held before release uses charged light move.")]
    [Min(0f)]
    public float lightHoldChargeThreshold = 0.1f;
    private bool lightPressArmed;                  // True from the frame RT/Y is pressed until release; allows hold-duration check
    private float lightPressStartTime;             // Time.time the light button was pressed; compared against lightHoldChargeThreshold on release
    private bool resolvedLightAttackUseCharged;    // Set on release: true if held long enough to be a charged light, false if a tap
    private bool rbXPressArmed;                    // True while RB/X is held; used to detect new RB press each frame
    private bool currentAttackStartedFromRbXInput; // True when the active attack was committed via the RB+X chord; gates charge hold check
    private bool forceChargeForNextAttack;         // When true, the next attack commit will start in charge mode (set by anim events)
    private bool forceChargeForCurrentAttack;      // Latched from forceChargeForNextAttack at commit time; cleared when attack ends
    
    private bool lungePending = false;             // True while a lunge hasn't started yet; cleared when the lunge window begins or attack ends
    private float lungeTriggerTime = 0f;           // Time.time when the lunge starts (attack start + lockDuration * lungeFrame)
    private float lungeEndTime = 0f;               // Time.time when the lunge stops; player moves forward between lungeTriggerTime and lungeEndTime
    private float currentLungeDistance = 0f;       // Total forward distance for this lunge (from attack data)
    private float currentLungeDuration = 0f;       // Time span of the lunge; used to compute per-frame move speed
    private Vector3 lungeDirection;                // World-space forward locked at commit time so the lunge doesn't steer mid-animation

    private float trackingEndTime = 0f;            // Time.time until which the player auto-rotates toward the soft target after attack start
    private float currentTrackingSpeed = 0f;       // Degrees/second for soft-target tracking during attack; 0 = no tracking

    [Tooltip("Without lock-on: max angle from current facing to auto-snap toward a nearby enemy on attack. " +
             "0 = disabled. 90 = snap to anything in front hemisphere.")]
    [Range(0f, 180f)]
    public float softSnapMaxAngle = 90f;

    private bool suppressHitboxActivationsUntilNextCommit; // When true, late animation-event hitbox calls are ignored (e.g. after interrupt)
    protected bool IsHitboxActivationSuppressed => suppressHitboxActivationsUntilNextCommit;
    private AttackData currentAttackData;          // Full data for the active attack; read by hitbox, lunge, charge, and SFX code
    public AttackData CurrentAttackData => currentAttackData;
    
    private struct FrozenAnimator              // Holds an animator and its pre-freeze speed so it can be restored after hit-stop ends
    {
        public Animator animator;
        public float originalSpeed;            // Speed before we set it to 0; restored when hit-stop expires
    }
    private float hitStopEndTime;                                           // Time.time when hit-stop expires; all frozen animators restored at this point
    private List<FrozenAnimator> frozenAnimators = new List<FrozenAnimator>(); // Animators paused for the current hit-stop (attacker + victim)

    bool IsAnimatorFrozen(Animator anim)
    {
        // Manual loop — avoids the closure + enumerator allocation that LINQ Any(lambda) produces every call.
        for (int i = 0; i < frozenAnimators.Count; i++)
            if (frozenAnimators[i].animator == anim) return true;
        return false;
    }

    private float currentAttackStartTime;     // Time.time when the current attack was committed; used for timing debug display
    
    private float currentStartUpLength;            // Normalized time fraction during which start-up speed is active (0 = no start-up phase)
    private float currentStartUpSpeed;             // Animator speed during start-up (< 1 = slow for telegraph / readability)
    private float currentRecoveryLength;           // Normalized time fraction at end of clip during which recovery speed is active
    private float currentRecoverySpeed;            // Animator speed during recovery (< 1 = slow for vulnerability window)
    private string currentAttackStateName;         // State name for the active attack; only apply speed changes while in this state
    private bool currentAttackStartedFromLightInput;  // True if committed from a light button press; used for charge-threshold checks
    private bool currentAttackStartedFromHeavyInput;  // True if committed from heavy input; used to gate charge window entry

    [Header("Charge (Weapon, optional)")]
    [Tooltip("If true, weapon-strike attacks can enter a charge slowdown window via animation events.")]
    public bool enableWeaponCharge = true;
    [Tooltip("Animator speed while charging (set by OnChargeWindowStart animation event).")]
    [Range(0.01f, 1f)]
    public float chargeAnimatorSpeed = 0.2f;
    [Tooltip("Max seconds charge slowdown can be held before auto-release.")]
    public float maxChargeTime = 1.0f;
    [Tooltip("If true, only heavy attacks can charge. If false, light/heavy weapon attacks can charge.")]
    public bool chargeHeavyOnly = false;
    [Tooltip("Top playback speed used for rubber-band release after charging.")]
    [Range(1f, 4f)]
    public float chargeReleaseMaxSpeed = 2.0f;
    [Tooltip("Minimum release speed boost applied when any charge occurred.")]
    [Range(1f, 4f)]
    public float chargeReleaseMinSpeed = 1.2f;
    [Tooltip("How long (seconds) the release speed boost lasts after charge ends.")]
    [Min(0f)]
    public float chargeReleaseBoostDuration = 0.35f;
    [Tooltip("Analog threshold for considering gamepad RT held for heavy charge.")]
    [Range(0f, 1f)]
    public float heavyTriggerHeldThreshold = 0.35f;
    [Header("Charge Rumble (Gamepad, optional)")]
    [Tooltip("If enabled, gamepad rumble ramps up while holding a charge.")]
    public bool enableChargeRumble = true;
    [Tooltip("Low-frequency motor at the start of charge (very low).")]
    [Range(0f, 1f)]
    public float chargeRumbleMinLow = 0.01f;
    [Tooltip("High-frequency motor at the start of charge (very low).")]
    [Range(0f, 1f)]
    public float chargeRumbleMinHigh = 0.015f;
    [Tooltip("Low-frequency motor at full charge.")]
    [Range(0f, 1f)]
    public float chargeRumbleMaxLow = 0.2f;
    [Tooltip("High-frequency motor at full charge.")]
    [Range(0f, 1f)]
    public float chargeRumbleMaxHigh = 0.3f;
    [Tooltip("Rumble ramp curve. Higher = slower start, stronger finish.")]
    [Range(1f, 4f)]
    public float chargeRumbleCurveExponent = 2.2f;
    [Header("Charge Damage & Knockback Scaling")]
    [Tooltip("Damage multiplier at full charge. 1 = no bonus, 2 = double damage at max charge.")]
    [Min(1f)]
    public float chargeDamageMultiplier = 1.5f;
    [Tooltip("Knockback multiplier at full charge. 1 = no bonus, 2 = double knockback at max charge.")]
    [Min(1f)]
    public float chargeKnockbackMultiplier = 1.5f;
    [Tooltip("Launch speed multiplier at full charge. 1 = no bonus, 2 = double launch speed/distance at max charge.")]
    [Min(1f)]
    public float chargeLaunchMultiplier = 2f;
    private bool chargeWindowOpen;             // True between OnChargeWindowStart and OnChargeWindowEnd animation events; player can hold to charge
    private bool isChargingAttack;             // True while the player is actively holding during the charge window
    public bool IsChargingAttack => isChargingAttack;
    public bool IsInChargeFlow => isChargingAttack || (isAttacking && chargeWindowOpen);
    private float chargeStartTime;             // Time.time when charge hold began; determines charge duration on release
    private float currentChargeDuration;       // Seconds held so far; clamped at maxChargeTime; used to compute release speed/damage
    private float chargeReleaseBoostEndTime;   // Time.time when the post-release speed boost expires
    private float chargeReleaseBoostSpeed = 1f; // Animator speed applied during the release boost window (lerped from chargeReleaseMaxSpeed)
    private bool chargeRumbleActive;           // True while gamepad rumble is running for charge; cleared on release
    private float chargeReleaseDamageScale = 1f;   // Damage multiplier baked at release time (1 = no charge, up to chargeDamageMultiplier)
    private float chargeReleaseKnockbackScale = 1f; // Knockback multiplier baked at release time (1 = no charge, up to chargeKnockbackMultiplier)
    private float chargeReleaseLaunchScale = 1f;    // Launch speed multiplier baked at release time (1 = no charge, up to chargeLaunchMultiplier)
    public float ChargeReleaseDamageScale => chargeReleaseDamageScale;
    public float ChargeReleaseKnockbackScale => chargeReleaseKnockbackScale;
    /// <summary>Fired when an attack hits a target. float = chargeReleaseDamageScale (1 = uncharged, higher = charged).</summary>
    public event System.Action<AttackData, float> OnHitConfirmed;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    private BattleMomentum battleMomentum; // Optional — only present on player

    void Awake()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (threatSystem == null) threatSystem = GetComponent<LockOnSystem>();
        battleMomentum = GetComponent<BattleMomentum>();

        if (animator == null) animator = PlayerController.FindAnimator(gameObject);
        if (sfxSource == null) sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = GetComponentInChildren<AudioSource>();
        if (sfxSource != null)
        {
            attackStartSfxSource = gameObject.AddComponent<AudioSource>();
            attackStartSfxSource.outputAudioMixerGroup = sfxSource.outputAudioMixerGroup;
            attackStartSfxSource.spatialBlend = sfxSource.spatialBlend;
            attackStartSfxSource.minDistance = sfxSource.minDistance;
            attackStartSfxSource.maxDistance = sfxSource.maxDistance;
            attackStartSfxSource.rolloffMode = sfxSource.rolloffMode;
            attackStartSfxSource.playOnAwake = false;
            attackStartSfxSource.loop = false;
            attackStartSfxSource.priority = sfxSource.priority;

            chargeLoopSfxSource = gameObject.AddComponent<AudioSource>();
            chargeLoopSfxSource.outputAudioMixerGroup = sfxSource.outputAudioMixerGroup;
            chargeLoopSfxSource.spatialBlend = sfxSource.spatialBlend;
            chargeLoopSfxSource.minDistance = sfxSource.minDistance;
            chargeLoopSfxSource.maxDistance = sfxSource.maxDistance;
            chargeLoopSfxSource.rolloffMode = sfxSource.rolloffMode;
            chargeLoopSfxSource.playOnAwake = false;
            chargeLoopSfxSource.loop = true;
            chargeLoopSfxSource.priority = sfxSource.priority;
        }
    }

    void LateUpdate()
    {
        // Re-enforce directional throw rotation after animator/other scripts may have clobbered it
        if (_hasDirectionalThrowTarget)
        {
            transform.rotation = _directionalThrowTargetRotation;
            if (_directionalThrowVictimTransform != null)
                _directionalThrowVictimTransform.rotation = _directionalThrowVictimRotation;
        }

        UpdateThrowVictimPseudoParent();
        // Re-apply baked position one frame after release (no-launch only) so enemy scripts/gravity don't overwrite it
        if (_reapplyThrowBakeNextFrame && _reapplyThrowBakeTransform != null)
        {
            _reapplyThrowBakeTransform.position = _reapplyThrowBakePosition;  // Restore saved world position
            _reapplyThrowBakeTransform.rotation = _reapplyThrowBakeRotation;  // Restore saved world rotation
            _reapplyThrowBakeNextFrame = false;   // Only re-apply once
            _reapplyThrowBakeTransform = null;    // Clear reference
        }
        // Deferred throw damage (from OnThrowDamage animation event): apply on exact frame, then clear so release path doesn't double-apply
        bool throwDamageAppliedThisFrame = false; // Track so release path can skip applying damage again
        if (_deferThrowDamageToLateUpdate && currentThrowVictim != null && IsAnyThrowEnabled())
        {
            // Damage event applies damage only; release event applies throw knockback.
            ApplyThrowDamage(applyDamage: true, applyKnockback: false);
            _deferThrowDamageToLateUpdate = false;              // Consume deferred flag
            throwDamageAppliedThisFrame = true;                 // Mark so CompleteThrowRelease doesn't double-apply
        }
        // Throw release was deferred (from OnThrowRelease or from Update timer) so we run after Animator has applied root motion this frame
        if (!_deferThrowReleaseToLateUpdate || currentThrowVictim == null || !IsAnyThrowEnabled()) return;  // Skip if not deferred or invalid
        _deferThrowReleaseToLateUpdate = false;  // Consume deferred release flag
        Transform vt = (currentThrowVictim as Component)?.transform;  // Get victim transform for release
        if (vt == null) { currentThrowVictim = null; return; }  // Bail if victim destroyed
        // Release applies throw knockback; if no damage event fired, it also applies fallback damage.
        ApplyThrowDamage(applyDamage: !throwDamageAppliedThisFrame, applyKnockback: true);
        ReleaseThrowVictimFromSocket(clearKnockback: false); // Keep release knockback velocity; only bake/re-enable systems/reapply pose
        BakePlayerThrowRootMotionAndRestore();   // Save player root motion state and restore pre-throw pose
        CompleteThrowRelease(vt, throwDamageAppliedThisFrame, applyReleaseEffects: true);  // Apply release forces/effects and cleanup
    }

    void Update()
    {
        // 1) End active attack lock when its timer expires (throws can stay active until their animation events resolve).
        HandleAttackLockExpiry();
        // 2) Hard interrupt path: if the player gets stunned during an attack, unwind safely this frame.
        HandleStunInterruptDuringAttack();

        // 3) Per-frame combat subsystems (timers and delayed actions) run before processing fresh inputs.
        UpdateComboState();
        UpdateAttackTracking();
        UpdateAttackLunge();
        UpdateThrowSuck();
        UpdatePendingHitbox();
        UpdateHitStop();
        UpdateAttackStartUpSpeed();
        UpdateThrowCharge(); // slows/releases throw charge while button is held after grab connects

        RevertTorsoRotationIfExpired();

        // 4) Read and process attack inputs (light / heavy / throw with gating and dispatch).
        ReadAttackInputs(out bool lightTriggered, out bool heavyPressed, out bool throwPressed, out bool rbXReleased, out bool lightHeld, out bool heavyHeld);
        TryProcessAttackInputs(lightTriggered, heavyPressed, throwPressed, rbXReleased, lightHeld, heavyHeld);
        SyncAnimatorAttackBool();
    }

    void OnDisable()
    {
        // Prevent stale true when component is disabled mid-attack.
        if (animator != null && !string.IsNullOrEmpty(attackingBoolParameter))
            animator.SetBool(attackingBoolParameter, false);
    }

    void SyncAnimatorAttackBool()
    {
        if (animator == null || string.IsNullOrEmpty(attackingBoolParameter)) return;
        animator.SetBool(attackingBoolParameter, isAttacking || IsThrowInProgress());
    }

    void ClearAnimatorAttackBoolImmediate()
    {
        if (animator == null || string.IsNullOrEmpty(attackingBoolParameter)) return;
        animator.SetBool(attackingBoolParameter, false);
    }

    /// <summary>
    /// Ends non-throw attack state when lock duration expires. Throws with an attached victim are released by animation events.
    /// </summary>
    void HandleAttackLockExpiry()
    {
        if (!isAttacking || Time.time < currentAttackEndTime) return; // Attack lock timer expired; resolve/clear active attack state.

        // Throw release is triggered exclusively by the OnThrowRelease animation event.
        // We do not set _deferThrowReleaseToLateUpdate from the timer here; only the animation event does.
        if (currentThrowVictim == null || !IsAnyThrowEnabled()) // Only auto-exit when no valid throw hold needs an animation-event release.
        {
            // Release is exclusively from OnThrowRelease animation event (and stun path below); just clear attack state here.
            if ((currentStartUpLength > 0f || currentRecoveryLength > 0f) && animator != null && !IsAnimatorFrozen(animator)) // Restore normal animator speed if startup/recovery speed scaling was in use.
                animator.speed = 1f;
            lastAttackEndTime = Time.time; // Record when attack lock expired so enemies can punish recovery with RecentlyAttacked()
            isAttacking = false;
            ClearAnimatorAttackBoolImmediate();
            pendingThrowHitbox = false; // Cancel any delayed throw grab hitbox.
            currentAttackData = null;   // Clear current move context (used by SFX/events/debug).
            ResetChargeState();
            ResetThrowChargeState(); // clear throw charge scales and restore animator speed if still frozen
            // Weapon hitbox cleanup: if EndWeaponTipActiveFrames animation event never fired
            // (e.g. charge slow-down caused events to mis-order, or state exited before the
            // end event), the weapon tip collider stays armed and fires as a ghost hit after
            // the animation completes. Force-close it on natural lock expiry as a safety net.
            ForceEndActiveAttackAnimationEventState();
        }
        else if (!_deferThrowReleaseToLateUpdate)
        {
            // Safety: if throw lock expired but release event never arrived (or throw anim was interrupted),
            // force a release with standard release effects to avoid stuck throw state.
            ForceThrowReleaseFallback(applyReleaseEffects: true);
        }
    }

    /// <summary>
    /// If this character is stunned while attacking, abort the current attack/throw flow and clean up safely.
    /// </summary>
    void HandleStunInterruptDuringAttack()
    {
        var damageableForStun = GetComponentInParent<IDamageable>();
        // Gate intentionally requires "in hitstun now" and "combat work active".
        // This avoids clearing combat state every frame while not attacking.
        if (damageableForStun == null || !damageableForStun.IsHitstunned || !isAttacking) return; // Interrupt combat flow only if hitstunned mid-attack.
        InterruptAttackForPlayerAnimationInterrupt(clearThrowStateWhenNoVictim: true);
    }

    /// <summary>
    /// Immediate external interrupt path used when damage lands during charge.
    /// Forces attack/charge state to unwind this frame so stun can take over instantly.
    /// </summary>
    public void InterruptAttackAndChargeForStun()
    {
        // External hard-cancel path (called from PlayerHealth on damage).
        // Early-out keeps this idempotent when multiple hits land in the same window.
        if (!isAttacking && !isChargingAttack) return;
        InterruptAttackForPlayerAnimationInterrupt(clearThrowStateWhenNoVictim: false);
    }

    /// <summary>
    /// Shared hard-interrupt path used when the player is interrupted during an attack animation.
    /// Clears throw/attack state, closes event-driven hitboxes, and prevents late animation events from reactivating them.
    /// </summary>
    void InterruptAttackForPlayerAnimationInterrupt(bool clearThrowStateWhenNoVictim)
    {
        // If a throw victim is attached, release safely without applying release effects.
        if (currentThrowVictim != null)
            ForceThrowReleaseFallback(applyReleaseEffects: false);
        else if (clearThrowStateWhenNoVictim)
            ClearThrowState();

        // Ensure active event-driven attack state is closed immediately (e.g. weapon hitboxes).
        ForceEndActiveAttackAnimationEventState();
        SuppressFurtherHitboxActivationsAfterInterrupt();

        // Fully abort the attack so no delayed hitbox, lunge, or tracking continues.
        // Also restore any temporary animator speed modifications (startup/recovery/charge/hitstop).
        RestoreAnimatorSpeedStateAfterDamageOrStun();
        isAttacking = false;
        ClearAnimatorAttackBoolImmediate();
        pendingThrowHitbox = false;
        lungePending = false;
        ClearAttackSpawn();
        trackingEndTime = 0f;
        lightComboCount = 0;
        neutralComboLoopPending = false;
        hasAppliedTorsoRotation = false;
        currentAttackData = null;
        ResetChargeState();
        ResetThrowChargeState(); // clear throw charge scales and restore animator speed if still frozen
    }

    void SuppressFurtherHitboxActivationsAfterInterrupt()
    {
        // Interrupt may happen while clips still have pending hitbox events later in the same state.
        // This flag blocks those late activations until a fresh attack/throw is explicitly committed.
        suppressHitboxActivationsUntilNextCommit = true;
        pendingThrowHitbox = false;
        _deferThrowDamageToLateUpdate = false;
    }

    /// <summary>
    /// Clears buffered/armed attack inputs so held buttons do not auto-fire after taking damage.
    /// </summary>
    public void CancelBufferedAttackInputs()
    {
        // Clear all armed input state so held buttons don't "replay" attacks post-interrupt.
        lightPressArmed = false;
        rbXPressArmed = false;
        resolvedLightAttackUseCharged = false;
        forceChargeForNextAttack = false;
        forceChargeForCurrentAttack = false;
    }

    void RestoreAnimatorSpeedStateAfterDamageOrStun()
    {
        for (int i = 0; i < frozenAnimators.Count; i++)
        {
            Animator a = frozenAnimators[i].animator;
            if (a != null)
                a.speed = frozenAnimators[i].originalSpeed;
        }
        frozenAnimators.Clear();
        hitStopEndTime = 0f;
        if (animator != null)
            animator.speed = 1f;
    }
    // ========================================================================
    // UPDATE HELPERS (input reading + attack gating/dispatch)
    // ========================================================================

    /// <summary>Clear temporary torso rotation once the attack window expires.</summary>
    void RevertTorsoRotationIfExpired()
    {
        if (hasAppliedTorsoRotation && Time.time >= currentAttackEndTime)
            hasAppliedTorsoRotation = false;
    }

    /// <summary>Sample release/press states across keyboard/mouse/gamepad. Light resolves via hold-threshold or quick release.</summary>
    void ReadAttackInputs(out bool lightTriggered, out bool heavyPressed, out bool throwPressed, out bool rbXReleased, out bool lightHeld, out bool heavyHeld)
    {
        bool gamepadLightPressedThisFrame = Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame;
        bool lightPressedThisFrame =
            (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame) ||
            (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
            gamepadLightPressedThisFrame;
        bool lightReleasedThisFrame =
            (Keyboard.current != null && Keyboard.current.jKey.wasReleasedThisFrame) ||
            (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.wasReleasedThisFrame);

        heavyPressed =
            (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame);

        bool rbPressedThisFrame = Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame;
        bool xPressedThisFrame = Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame;
        bool rbReleasedThisFrame = Gamepad.current != null && Gamepad.current.rightShoulder.wasReleasedThisFrame;
        bool xReleasedThisFrame = Gamepad.current != null && Gamepad.current.buttonWest.wasReleasedThisFrame;
        bool rbHeldNow = Gamepad.current != null && Gamepad.current.rightShoulder.isPressed;
        bool xHeldNow = Gamepad.current != null && Gamepad.current.buttonWest.isPressed;

        if (lightPressedThisFrame && !lightPressArmed)
        {
            // If RB was pressed while X is held, treat this as RB+X combo arming, not light arming.
            if (gamepadLightPressedThisFrame && xHeldNow)
                lightPressArmed = false;
            else
            {
                lightPressArmed = true;
                lightPressStartTime = Time.time;
            }
        }
        lightTriggered = false;
        resolvedLightAttackUseCharged = false;
        if (lightPressArmed && !lightReleasedThisFrame)
        {
            float heldDuration = Mathf.Max(0f, Time.time - lightPressStartTime);
            if (heldDuration >= lightHoldChargeThreshold)
            {
                resolvedLightAttackUseCharged = true;
                lightTriggered = true;
                lightPressArmed = false;
            }
        }
        else if (lightPressArmed && lightReleasedThisFrame)
        {
            float heldDuration = Mathf.Max(0f, Time.time - lightPressStartTime);
            resolvedLightAttackUseCharged = heldDuration >= lightHoldChargeThreshold;
            lightPressArmed = false;
            lightTriggered = true;
        }

        // Simultaneous gamepad shortcut: fires on press (second button down while first is held) so
        // both buttons are still held at commit time, allowing the charge window to detect them.
        rbXReleased = false;
        if (!rbXPressArmed && ((rbPressedThisFrame && xHeldNow) || (xPressedThisFrame && rbHeldNow)))
        {
            rbXPressArmed = true;
            rbXReleased = true;   // Fire immediately on press, not on release.
            lightPressArmed = false;
        }
        if (rbXPressArmed && !rbHeldNow && !xHeldNow)
            rbXPressArmed = false;

        bool yPressedThisFrame = Gamepad.current != null && Gamepad.current.buttonNorth.wasPressedThisFrame;
        bool bPressedThisFrame = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        bool yHeldNow = Gamepad.current != null && Gamepad.current.buttonNorth.isPressed;
        bool bHeldNow = Gamepad.current != null && Gamepad.current.buttonEast.isPressed;
        bool ybChordPressed = (yPressedThisFrame && bHeldNow) || (bPressedThisFrame && yHeldNow);
        throwPressed =
            (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) ||
            ybChordPressed;

        lightHeld =
            (Keyboard.current != null && Keyboard.current.jKey.isPressed) ||
            (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed);

        heavyHeld =
            (Keyboard.current != null && Keyboard.current.kKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed);

        // Track when forward stick crosses the threshold (rising edge only).
        // Holding forward from before the attack window does NOT count as a forward attack.
        float rawY = GetRawStickInput().y;
        if (prevRawForwardY <= 0.3f && rawY > 0.3f)
            forwardStickPressTime = Time.time;
        prevRawForwardY = rawY;

        // Track any stick movement rising edge — used to detect "toward enemy" forward attacks
        // even when the stick Y alone doesn't cross the forward threshold (e.g. diagonal toward enemy).
        float rawMag = GetRawStickInput().magnitude;
        if (prevStickMagnitude <= 0.3f && rawMag > 0.3f)
            stickMoveTime = Time.time;
        prevStickMagnitude = rawMag;
    }

    /// <summary>Gate checks (stun, comboSet, cooldown) then dispatch throw / light / heavy.</summary>
    void TryProcessAttackInputs(bool lightAttackInput, bool heavyAttackInput, bool throwInput, bool rbXAttackInput, bool lightHeld, bool heavyHeld)
    {
        bool canAttack = playerController != null;
        var damageable = GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.IsHitstunned)
            canAttack = false;

        if (!canAttack)
        {
            lightComboCount = 0;
            lightPressArmed = false;
            rbXPressArmed = false;
            forceChargeForNextAttack = false;
            forceChargeForCurrentAttack = false;
            return;
        }

        if (comboSet == null) return;
        if (IsThrowInProgress())
        {
            // Throw flow is single-owner state (victim attach, root-motion bake, deferred release events).
            // Block normal attack dispatch here to prevent mixed state and stuck victims.
            lightPressArmed = false;
            rbXPressArmed = false;
            forceChargeForNextAttack = false;
            forceChargeForCurrentAttack = false;
            return;
        }

        if (rbXAttackInput && !isAttacking && Time.time >= nextAttackTime)
        {
            lightComboCount = 0;
            currentAttackStartedFromLightInput = false;
            currentAttackStartedFromHeavyInput = false;
            currentAttackStartedFromRbXInput = true;
            forceChargeForNextAttack = false;
            DoAttack(comboSet.rbXAttack, Color.magenta);
            return;
        }

        if (throwInput && !isAttacking && Time.time >= nextThrowTime && IsAnyThrowEnabled())
        {
            // Throw has priority over normal attacks when requested and valid.
            DoThrow();
            return;
        }

        bool inCancelWindow = (lightComboCount == 1 || lightComboCount == 2) &&
                              (Time.time >= comboWindowStart) &&
                              (Time.time <= comboWindowEnd);

        if (Time.time < nextAttackTime && !inCancelWindow) return;

        DebugSettings debug = DebugSettings.Instance;

        if (lightAttackInput)
        {
            currentAttackStartedFromLightInput = lightHeld;
            currentAttackStartedFromHeavyInput = false;
            DoLightAttack(debug.lightAttackColor, inCancelWindow, resolvedLightAttackUseCharged);
        }
        else if (heavyAttackInput)
        {
            lightComboCount = 0;
            currentAttackStartedFromLightInput = false;
            currentAttackStartedFromHeavyInput = heavyHeld;
            forceChargeForNextAttack = false;
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

    [Tooltip("Max angle (degrees) between the stick direction and the direction to a soft-lock target " +
             "for the input to count as a forward attack toward that enemy.")]
    [Range(10f, 90f)]
    public float towardEnemyForwardAngle = 60f;

    /// <summary>
    /// Returns true if the stick was recently moved AND is currently pointing toward the soft-lock
    /// target (or the best threat in the stick direction) within towardEnemyForwardAngle degrees.
    /// Used to count diagonal-toward-enemy stick input as a forward attack trigger.
    /// </summary>
    bool IsStickPointingTowardEnemy()
    {
        if (threatSystem == null) return false;
        if ((Time.time - stickMoveTime) > forwardAttackInputWindow) return false; // must be recent

        // Use the locked target if hard-locked, otherwise the closest threat in front.
        Transform target = threatSystem.IsLockedOn ? threatSystem.SoftTarget : GetBestThreatInFront();
        if (target == null) return false;

        Vector2 stick = GetRawStickInput();
        if (stick.sqrMagnitude < 0.09f) return false; // ~0.3 deadzone

        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector3 camForward = cam.transform.forward; camForward.y = 0f; camForward.Normalize();
        Vector3 camRight   = cam.transform.right;   camRight.y   = 0f; camRight.Normalize();
        Vector3 stickWorld = (camForward * stick.y + camRight * stick.x).normalized;

        Vector3 toTarget = target.position - transform.position; toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f) return false;

        return Vector3.Angle(stickWorld, toTarget.normalized) <= towardEnemyForwardAngle;
    }

    void UpdateComboState()
    {
        // Reset combo if cancel window expired without chaining
        if ((lightComboCount == 1 || lightComboCount == 2) && Time.time > comboWindowEnd)
        {
            lightComboCount = 0;
            neutralComboLoopPending = false;
        }
    }
    
    void UpdateAttackLunge()
    {
        if (!lungePending) return;
        // While charge-hold is active, defer lunge movement until release.
        if (isChargingAttack) return;
        // Freeze attacker position during hitstop
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime) return;
        
        // Check if we've reached the trigger frame
        if (Time.time >= lungeTriggerTime && Time.time < lungeEndTime)
        {
            // During tracking window, lunge direction follows current facing so step and hitbox stay aligned.
            // When suck-to-target is active and hard-locked, steer directly at the lock target every frame.
            if (currentAttackData != null && currentAttackData.suckToTarget
                && threatSystem != null && threatSystem.IsLockedOn && threatSystem.SoftTarget != null)
            {
                Vector3 toTarget = threatSystem.SoftTarget.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.001f)
                    lungeDirection = toTarget.normalized;
            }
            else if (Time.time < trackingEndTime)
            {
                lungeDirection = transform.forward;
            }
            // Calculate how much to move this frame
            float lungeProgress = (Time.time - lungeTriggerTime) / currentLungeDuration;
            if (lungeProgress <= 1f)
            {
                float moveAmount = (currentLungeDistance / currentLungeDuration) * Time.deltaTime;
                Transform suckTarget = (currentAttackData != null && currentAttackData.suckToTarget) ? GetSuckTarget() : null;
                float stopDist = currentAttackData != null ? currentAttackData.suckToTargetStopDistance : 0f;
                ApplyLungeMove(lungeDirection, moveAmount, suckTarget, stopDist);
            }
        }
        
        // Stop tracking when lunge window is over
        if (Time.time >= lungeEndTime)
        {
            lungePending = false;
        }
    }
    
    // ── Shared suck-to-target helpers ────────────────────────────────────────

    /// <summary>Returns the best suck target: soft lock-on target first, then nearest threat in front.</summary>
    Transform GetSuckTarget() =>
        (threatSystem != null && threatSystem.SoftTarget != null)
            ? threatSystem.SoftTarget
            : GetBestThreatInFront();

    /// <summary>
    /// Moves the CharacterController in direction by up to maxAmount (horizontal, direct cc.Move).
    /// Used by throws, which are ground moves and don't need gravity combined in.
    /// If suckTarget is non-null, clamps so the player stops stopDistance away (flat XZ).
    /// </summary>
    void ApplySuckMove(Vector3 direction, float maxAmount, Transform suckTarget, float stopDistance)
    {
        if (suckTarget != null)
        {
            float flatDist = new Vector2(
                suckTarget.position.x - transform.position.x,
                suckTarget.position.z - transform.position.z).magnitude;
            if (flatDist <= stopDistance) return;
            maxAmount = Mathf.Min(maxAmount, flatDist - stopDistance);
        }
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.Move(direction * maxAmount);
    }

    /// <summary>
    /// Attack lunge movement. Feeds the horizontal delta into PlayerController.AddLungeVelocity
    /// so it is merged with gravity in a single cc.Move — preventing the CharacterController's
    /// isGrounded snap from killing vertical momentum on aerial attacks.
    /// </summary>
    void ApplyLungeMove(Vector3 direction, float maxAmount, Transform suckTarget, float stopDistance)
    {
        if (suckTarget != null)
        {
            float flatDist = new Vector2(
                suckTarget.position.x - transform.position.x,
                suckTarget.position.z - transform.position.z).magnitude;
            if (flatDist <= stopDistance) return;
            maxAmount = Mathf.Min(maxAmount, flatDist - stopDistance);
        }
        playerController?.AddLungeVelocity(direction * maxAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Animation event. Place on the attack clip at the exact frame the launch should fire.</summary>
    public void OnPlayerLaunch()
    {
        if (currentAttackData == null || !currentAttackData.launchPlayer) return;
        float chargeEffect = chargeReleaseLaunchScale - 1f;
        playerController?.ApplyAttackLaunch(
            currentAttackData.playerLaunchUpSpeed      * (1f + chargeEffect * 0.6f),
            currentAttackData.playerLaunchForwardSpeed * chargeReleaseLaunchScale,
            currentAttackData.playerLaunchForwardDuration,
            transform.forward
        );
    }

    void UpdateAttackTracking()
    {
        // Mid-attack rotation disabled: facing is locked at commit time (DoAttack) for the
        // full duration of the animation. No steering during swings, locked or soft-locked.
    }
    
    /// <summary>
    /// Returns the tracked threat most aligned with the thumbstick direction (camera-relative).
    /// Falls back to player forward when stick is neutral.
    /// Used for soft auto-facing during attacks when not hard-locked.
    /// </summary>
    Transform GetBestThreatInFront()
    {
        if (threatSystem == null || softSnapMaxAngle <= 0f) return null;

        // Convert thumbstick to a camera-relative world direction.
        // This matches the direction the player would actually move if they walked.
        Vector3 referenceDir = transform.forward;
        Vector2 stick = GetRawStickInput();
        if (stick.sqrMagnitude > 0.01f)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 camForward = cam.transform.forward; camForward.y = 0f; camForward.Normalize();
                Vector3 camRight   = cam.transform.right;   camRight.y   = 0f; camRight.Normalize();
                Vector3 dir = camForward * stick.y + camRight * stick.x;
                if (dir.sqrMagnitude > 0.001f)
                    referenceDir = dir.normalized;
            }
        }

        // Collect every enemy inside the aim cone, then return the closest by distance.
        // When two enemies are nearly aligned with the stick, proximity wins.
        Transform best = null;
        float bestDistSq = float.MaxValue;
        foreach (var threat in threatSystem.TrackedThreats)
        {
            Vector3 toThreat = threat.transform.position - transform.position;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude < 0.01f) continue;
            if (Vector3.Angle(referenceDir, toThreat) > softSnapMaxAngle) continue;
            if (toThreat.sqrMagnitude < bestDistSq)
            {
                bestDistSq = toThreat.sqrMagnitude;
                best = threat.transform;
            }
        }
        return best;
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
    
    void DoLightAttack(Color visualColor, bool inCancelWindow, bool useCharged)
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
            // Forward attack only triggers if forward was pressed recently (simultaneous input).
            // Holding forward before pressing attack gives a neutral attack instead.
            // Also counts as forward if the stick is pointing toward the soft-lock target.
            bool forwardPressedRecently = (Time.time - forwardStickPressTime) <= forwardAttackInputWindow;
            bool holdingForward = playerController != null &&
                ((GetRawStickInput().y > 0.3f && forwardPressedRecently) || IsStickPointingTowardEnemy());
            isNeutralCombo = !holdingForward;
            neutralComboLoopPending = false;

            // First jab - starts the combo
            AttackData jab1 = isNeutralCombo
                ? (useCharged ? comboSet.neutralJab : comboSet.neutralJabNormal)
                : (useCharged ? comboSet.forwardJab : comboSet.forwardJabNormal);
            forceChargeForNextAttack = useCharged;
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
            if (neutralComboLoopPending)
            {
                // Loop back to neutral jab 1 (loopNeutralCombo is on and last hit was neutral jab 2).
                AttackData jab1Loop = useCharged ? comboSet.neutralJab : comboSet.neutralJabNormal;
                forceChargeForNextAttack = useCharged;
                DoAttack(jab1Loop, visualColor);
                neutralComboLoopPending = false;

                // Open cancel window so the player can chain into jab 2 again.
                float loopDelay = jab1Loop.comboWindowDelay >= 0f ? jab1Loop.comboWindowDelay : comboSet.comboWindowDelay;
                float loopDuration = jab1Loop.comboWindowDuration >= 0f ? jab1Loop.comboWindowDuration : comboSet.comboWindowDuration;
                comboWindowStart = Time.time + loopDelay;
                comboWindowEnd = comboWindowStart + loopDuration;
                // lightComboCount stays 1 so the next cancel window leads to jab 2 or another loop.
            }
            else
            {
                // Second jab: same simultaneous-input rule — only forward jab if forward was pressed recently,
                // or if the stick is pointing toward the soft-lock target.
                bool forwardPressedRecently = (Time.time - forwardStickPressTime) <= forwardAttackInputWindow;
                bool holdingForwardNow = (GetRawStickInput().y > 0.3f && forwardPressedRecently) || IsStickPointingTowardEnemy();
                AttackData jab2 = holdingForwardNow
                    ? (useCharged ? comboSet.forwardJab2 : comboSet.forwardJab2Normal)
                    : (useCharged ? comboSet.neutralJab2 : comboSet.neutralJab2Normal);
                Color jab2Color = holdingForwardNow ? Color.cyan : Color.yellow;
                forceChargeForNextAttack = useCharged;
                DoAttack(jab2, jab2Color);

                if (!holdingForwardNow)
                {
                    // Neutral jab 2 → open window for jab 3.
                    lightComboCount = 2;
                    float jab2Delay = jab2.comboWindowDelay >= 0f ? jab2.comboWindowDelay : comboSet.comboWindowDelay;
                    float jab2Duration = jab2.comboWindowDuration >= 0f ? jab2.comboWindowDuration : comboSet.comboWindowDuration;
                    comboWindowStart = Time.time + jab2Delay;
                    comboWindowEnd = comboWindowStart + jab2Duration;
                }
                else
                {
                    // Forward jab 2 ends the combo.
                    lightComboCount = 0;
                    neutralComboLoopPending = false;
                }
            }
        }
        else if (lightComboCount == 2 && inCancelWindow)
        {
            // Third neutral jab (finale of the neutral chain).
            AttackData jab3 = useCharged ? comboSet.neutralJab3 : comboSet.neutralJab3Normal;
            forceChargeForNextAttack = useCharged;
            DoAttack(jab3, Color.yellow);

            if (comboSet.loopNeutralCombo)
            {
                // Loop back to jab 1 — reuse the same loop-pending path at count 1.
                neutralComboLoopPending = true;
                lightComboCount = 1;
                float loopDelay = jab3.comboWindowDelay >= 0f ? jab3.comboWindowDelay : comboSet.comboWindowDelay;
                float loopDuration = jab3.comboWindowDuration >= 0f ? jab3.comboWindowDuration : comboSet.comboWindowDuration;
                comboWindowStart = Time.time + loopDelay;
                comboWindowEnd = comboWindowStart + loopDuration;
            }
            else
            {
                lightComboCount = 0;
                neutralComboLoopPending = false;
            }
        }
        // If not in cancel window, attack is blocked by cooldown check in Update()
    }

    // ========================================================================
    // ATTACK EXECUTION
    // ========================================================================

    /// <summary>
    /// Hook for derived combat types (e.g. WeaponCombat) to reset/prepare
    /// attack-specific state right when a new attack is committed.
    /// </summary>
    protected virtual void OnAttackCommitted(AttackData attack)
    {
    }

    /// <summary>
    /// Hook for derived combat types to immediately close any active
    /// animation-event-driven attack state (for example weapon tip hitboxes)
    /// when an attack is interrupted or expires unexpectedly.
    /// </summary>
    protected virtual void ForceEndActiveAttackAnimationEventState()
    {
    }
    
    /// <summary>Returns true and consumes momentum if the attack's cost is met, or if the attack doesn't require momentum.</summary>
    bool TryConsumeMomentum(bool consumesMomentum, float cost)
    {
        if (!consumesMomentum || battleMomentum == null) return true;
        if (battleMomentum.Momentum < cost) return false;
        battleMomentum.ConsumeMomentum(cost);
        return true;
    }

    void DoAttack(AttackData attack, Color visualColor)
    {
        if (!TryConsumeMomentum(attack.consumesMomentum, attack.momentumCost)) return;

        // Attacking while free-looking snaps the camera back to tracking the locked target.
        playerController?.threatSystem?.ExitFreeLook();

        // New committed attack clears the previous interrupt-suppression window.
        suppressHitboxActivationsUntilNextCommit = false;
        currentAttackStartTime = Time.time;
        ResetChargeState(resetInputOrigin: false);
        forceChargeForCurrentAttack = forceChargeForNextAttack;
        forceChargeForNextAttack = false;
        // Set cooldown (when you can attack again) and lock duration (when you can move again)
        nextAttackTime = Time.time + attack.cooldown;
        currentAttackEndTime = Time.time + attack.lockDuration;
        currentStartUpLength = attack.startUpLength;
        currentStartUpSpeed = attack.startUpSpeed;
        currentRecoveryLength = attack.recoveryLength;
        currentRecoverySpeed = attack.recoverySpeed;
        currentAttackStateName = !string.IsNullOrEmpty(attack.animationTrigger) ? attack.animationTrigger : null;
        currentAttackData = attack;
        isAttacking = true;
        OnAttackCommitted(attack);
        
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

        // Play attack animation: hard-cut to frame 0 so hit reactions or recovery
        // animations never bleed into the new attack clip.
        if (animator != null && !string.IsNullOrEmpty(attack.animationTrigger))
        {
            if (DebugSettings.Instance != null && DebugSettings.Instance.logAttackTiming)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[AttackStart] trigger='{attack.animationTrigger}' startUp=({attack.startUpLength:F2},{attack.startUpSpeed:F2}) recovery=({attack.recoveryLength:F2},{attack.recoverySpeed:F2}) | fromStateHash={state.shortNameHash} fromNT={state.normalizedTime:F3}");
            }
            else
                Debug.Log($"Playing animation trigger: '{attack.animationTrigger}'");
            animator.speed = 1f;
            animator.Play(attack.animationTrigger, 0, 0f);
        }

        PlayAttackCues(attack, AttackSfxTriggerType.OnAttackStart, 0, useLegacyFallback: true);
        
        GameObject startVfxPrefab = attack.attackStartVfxPrefab != null ? attack.attackStartVfxPrefab : attackStartVfxPrefab;
        if (startVfxPrefab != null)
        {
            Transform origin = hitOrigin != null ? hitOrigin : transform;
            Vector3 pos = origin.position + attack.attackStartVfxPositionOffset;
            Quaternion rot = transform.rotation * Quaternion.Euler(attack.attackStartVfxRotationOffset);
            var go = Instantiate(startVfxPrefab, pos, rot);
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
        
        // Instantly face the best threat in the current stick direction at commit time (unlocked only).
        // This makes mid-combo redirects crisp: point stick at a different enemy and attack,
        // you snap to face them on frame 0 of the animation rather than slowly rotating during it.
        // Hard lock-on skips this — the tracking window handles it already.
        if (threatSystem != null && !threatSystem.IsLockedOn)
        {
            // Suck-to-target attacks snap to the suck target unconditionally (no cone restriction)
            // so a dash-left → attack still faces the enemy on commit.
            Transform snapTarget = (attack.suckToTarget)
                ? GetSuckTarget()
                : GetBestThreatInFront();
            if (snapTarget != null)
            {
                Vector3 toSnap = snapTarget.position - transform.position;
                toSnap.y = 0f;
                if (toSnap.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(toSnap.normalized, Vector3.up);
            }
        }

        // After the facing snap, realign lungeDirection so suck-to-target lunges
        // travel toward the enemy rather than continuing in the dash direction.
        if (attack.suckToTarget && lungePending)
            lungeDirection = transform.forward;

        // Save rotation NOW (after snap, before torso tweak) so the end-of-attack revert
        // only undoes the small torso adjustment, not the full facing snap.
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
        
        // Hitbox activation is handled by WeaponCombat + WeaponTipHitbox via BeginHitbox/EndHitbox animation events.
    }

    // ========================================================================
    // HITBOX HELPERS
    // ========================================================================

    void PlayAttackSfxClip(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || sfxSource == null) return;
        float minPitch = Mathf.Min(sfxPitchMin, sfxPitchMax);
        float maxPitch = Mathf.Max(sfxPitchMin, sfxPitchMax);
        sfxSource.pitch = Random.Range(minPitch, maxPitch);
        sfxSource.PlayOneShot(clip, Mathf.Max(0f, volumeScale));
    }

    void PlayAttackStartSfxClip(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || attackStartSfxSource == null) return;
        if (attackStartSfxFadeCoroutine != null)
        {
            StopCoroutine(attackStartSfxFadeCoroutine);
            attackStartSfxFadeCoroutine = null;
        }
        float minPitch = Mathf.Min(sfxPitchMin, sfxPitchMax);
        float maxPitch = Mathf.Max(sfxPitchMin, sfxPitchMax);
        attackStartSfxSource.pitch = Random.Range(minPitch, maxPitch);
        attackStartSfxSource.clip = clip;
        attackStartSfxTargetVolume = Mathf.Max(0f, volumeScale);
        attackStartSfxSource.volume = attackStartSfxTargetVolume;
        attackStartSfxSource.Play();
    }

    void StopAttackStartSfx()
    {
        if (attackStartSfxFadeCoroutine != null)
        {
            StopCoroutine(attackStartSfxFadeCoroutine);
            attackStartSfxFadeCoroutine = null;
        }
        if (attackStartSfxSource != null && attackStartSfxSource.isPlaying)
            attackStartSfxSource.Stop();
    }

    void FadeOutAttackStartSfx()
    {
        if (attackStartSfxSource == null || !attackStartSfxSource.isPlaying)
            return;
        if (attackStartSfxFadeCoroutine != null)
            StopCoroutine(attackStartSfxFadeCoroutine);
        float duration = Mathf.Max(0f, chargeReleaseSfxFadeOutDuration);
        if (duration <= 0f)
        {
            StopAttackStartSfx();
            return;
        }
        attackStartSfxFadeCoroutine = StartCoroutine(FadeOutAttackStartSfxRoutine(duration));
    }

    IEnumerator FadeOutAttackStartSfxRoutine(float duration)
    {
        float startVolume = attackStartSfxSource != null ? attackStartSfxSource.volume : 0f;
        float elapsed = 0f;
        while (attackStartSfxSource != null && attackStartSfxSource.isPlaying && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            attackStartSfxSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }
        if (attackStartSfxSource != null)
        {
            attackStartSfxSource.Stop();
            attackStartSfxSource.volume = attackStartSfxTargetVolume;
        }
        attackStartSfxFadeCoroutine = null;
    }

    void StartChargeLoopSfx(AttackData attack)
    {
        if (chargeLoopSfxSource == null) return;

        AudioClip loopClip = attack != null && attack.chargeLoopSfx != null
            ? attack.chargeLoopSfx
            : chargeLoopSfx;
        if (loopClip == null) return;

        float loopPitch = attack != null ? attack.chargeLoopSfxPitch : chargeLoopSfxPitch;
        float loopVolume = attack != null ? attack.chargeLoopSfxVolume : chargeLoopSfxVolume;
        if (chargeLoopSfxFadeCoroutine != null)
        {
            StopCoroutine(chargeLoopSfxFadeCoroutine);
            chargeLoopSfxFadeCoroutine = null;
        }
        chargeLoopSfxSource.pitch = Mathf.Max(0.01f, loopPitch);
        chargeLoopSfxSource.clip = loopClip;
        chargeLoopSfxTargetVolume = Mathf.Max(0f, loopVolume);
        chargeLoopSfxSource.volume = chargeLoopSfxTargetVolume;
        if (!chargeLoopSfxSource.isPlaying)
            chargeLoopSfxSource.Play();
    }

    void StopChargeLoopSfxImmediate()
    {
        if (chargeLoopSfxFadeCoroutine != null)
        {
            StopCoroutine(chargeLoopSfxFadeCoroutine);
            chargeLoopSfxFadeCoroutine = null;
        }
        if (chargeLoopSfxSource != null && chargeLoopSfxSource.isPlaying)
            chargeLoopSfxSource.Stop();
    }

    void FadeOutChargeLoopSfx()
    {
        if (chargeLoopSfxSource == null || !chargeLoopSfxSource.isPlaying)
            return;
        if (chargeLoopSfxFadeCoroutine != null)
            StopCoroutine(chargeLoopSfxFadeCoroutine);
        float duration = Mathf.Max(0f, chargeLoopSfxFadeOutDuration);
        if (duration <= 0f)
        {
            StopChargeLoopSfxImmediate();
            return;
        }
        chargeLoopSfxFadeCoroutine = StartCoroutine(FadeOutChargeLoopSfxRoutine(duration));
    }

    IEnumerator FadeOutChargeLoopSfxRoutine(float duration)
    {
        float startVolume = chargeLoopSfxSource != null ? chargeLoopSfxSource.volume : 0f;
        float elapsed = 0f;
        while (chargeLoopSfxSource != null && chargeLoopSfxSource.isPlaying && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            chargeLoopSfxSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }
        if (chargeLoopSfxSource != null)
        {
            chargeLoopSfxSource.Stop();
            chargeLoopSfxSource.volume = chargeLoopSfxTargetVolume;
        }
        chargeLoopSfxFadeCoroutine = null;
    }

    void PlayAttackCues(AttackData attack, AttackSfxTriggerType trigger, int eventId, bool useLegacyFallback)
    {
        if (attack == null) return;

        bool hasCueList = attack.sfxCues != null && attack.sfxCues.Count > 0;
        if (hasCueList)
        {
            for (int i = 0; i < attack.sfxCues.Count; i++)
            {
                AttackSfxCue cue = attack.sfxCues[i];
                if (cue == null || cue.trigger != trigger) continue;
                if (trigger == AttackSfxTriggerType.OnAnimEvent && cue.eventId != eventId) continue;

                AudioClip chosenClip = null;
                if (cue.clips != null && cue.clips.Length > 0)
                    chosenClip = cue.clips[Random.Range(0, cue.clips.Length)];
                if (chosenClip == null) continue;

                if (trigger == AttackSfxTriggerType.OnAttackStart)
                {
                    StopAttackStartSfx();
                    PlayAttackStartSfxClip(chosenClip, cue.volume);
                    return;
                }

                PlayAttackSfxClip(chosenClip, cue.volume);
            }
            return;
        }

        if (!useLegacyFallback) return;
        if (trigger == AttackSfxTriggerType.OnAttackStart)
        {
            StopAttackStartSfx();
            PlayAttackStartSfxClip(attack.attackStartSfx);
        }
        else if (trigger == AttackSfxTriggerType.OnHitConfirm)
            PlayAttackSfxClip(attack.hitConnectSfx);
    }

    /// <summary>
    /// Shared hit-confirm effects (VFX, SFX, hit-stop, threat registration).
    /// Used by alternate hitbox systems (e.g. WeaponTipHitbox) that apply damage outside ExecuteHitbox().
    /// </summary>
    protected void NotifyHitConfirmedFromExternalHitbox(AttackData attack, Transform targetTransform, Vector3 hitPoint)
    {
        // Guard: if the caller did not provide move data, we cannot resolve VFX/SFX/hit stop settings.
        if (attack == null) return;

        // Tell lock-on/threat logic that this target was just interacted with (recent hits raise priority).
        if (targetTransform != null && threatSystem != null)
            threatSystem.RegisterInteraction(targetTransform);

        OnHitConfirmed?.Invoke(attack, chargeReleaseDamageScale);

        // Target-side hit stop:
        // Freeze only the target Animator (not whole game time) for attack.hitStopDuration seconds.
        if (targetTransform != null && attack.hitStopDuration > 0f)
        {
            // We freeze the first Animator in the target hierarchy (standard for character rigs).
            Animator targetAnim = targetTransform.GetComponentInChildren<Animator>();
            // Avoid double-adding the same animator if multiple colliders report the same hit target.
            if (targetAnim != null && !IsAnimatorFrozen(targetAnim))
            {
                // Save original speed so UpdateHitStop() can restore exactly after freeze window.
                frozenAnimators.Add(new FrozenAnimator { animator = targetAnim, originalSpeed = targetAnim.speed });
                targetAnim.speed = 0f;
            }
        }

        // Spawn hit-connect VFX at the supplied hit point (from the external hitbox system).
        GameObject connectVfxPrefab = attack.hitConnectVfxPrefab != null ? attack.hitConnectVfxPrefab : hitConnectVfxPrefab;
        if (connectVfxPrefab != null)
        {
            // Build a forward vector from attacker to hit point so effects face impact direction.
            Vector3 from = transform.position;
            Vector3 toHit = hitPoint - from;
            // Fallback to current facing if hit point is effectively at attacker origin.
            Quaternion rot = toHit.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toHit)
                : transform.rotation;
            // Apply per-move art offset so each attack can tweak VFX rotation in data.
            rot = rot * Quaternion.Euler(attack.hitConnectVfxRotationOffset);
            // Apply per-move positional offset for effects that need to sit slightly off contact point.
            var go = Instantiate(connectVfxPrefab, hitPoint + attack.hitConnectVfxPositionOffset, rot);
            // Ensure all child particle systems begin playing immediately.
            PlayVfx(go);
        }

        // Play hit-confirm audio cues (new cue list first, legacy fallback clip second).
        PlayAttackCues(attack, AttackSfxTriggerType.OnHitConfirm, 0, useLegacyFallback: true);

        // Attacker-side hit stop:
        // Freeze attacker animator, too, to create impact feel on both sides of contact.
        if (attack.hitStopDuration > 0f)
        {
            // Global timestamp used by UpdateHitStop() to restore all frozen animators.
            hitStopEndTime = Time.time + attack.hitStopDuration;

            // Freeze attacker animator once; reuse the same frozenAnimators restore pipeline.
            if (animator != null && !IsAnimatorFrozen(animator))
            {
                frozenAnimators.Add(new FrozenAnimator { animator = animator, originalSpeed = animator.speed });
                animator.speed = 0f;
            }

            // Optional gamepad feedback aligned to the same hit stop duration.
            if (Gamepad.current != null)
                StartCoroutine(RumbleForSeconds(attack.hitStopDuration));
        }
    }

    public void OnAttackSfxEvent(int eventId)
    {
        PlayAttackCues(currentAttackData, AttackSfxTriggerType.OnAnimEvent, eventId, useLegacyFallback: false);
    }

    public void OnAttackSfxEvent()
    {
        OnAttackSfxEvent(0);
    }

    // AnimationEvent can pass float/string depending on clip setup; normalize to int id.
    public void OnAttackSfxEvent(float eventId)
    {
        OnAttackSfxEvent(Mathf.RoundToInt(eventId));
    }

    public void OnAttackSfxEvent(string eventId)
    {
        int parsed;
        OnAttackSfxEvent(int.TryParse(eventId, out parsed) ? parsed : 0);
    }

    // Alias for naming variants often typed in clips.
    public void OnAttackSFXEvent()
    {
        OnAttackSfxEvent(0);
    }

    public void OnAttackSFXEvent(int eventId)
    {
        OnAttackSfxEvent(eventId);
    }
    
    static void PlayVfx(GameObject instance)
    {
        if (instance == null) return;
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play();
    }
    
    /// <summary>
    /// Check if the scheduled throw grab hitbox is ready to fire.
    /// </summary>
    void UpdatePendingHitbox()
    {
        if (suppressHitboxActivationsUntilNextCommit)
        {
            pendingThrowHitbox = false;
            return;
        }

        if (pendingThrowHitbox && Time.time >= throwHitboxTriggerTime)
        {
            ExecuteThrowHitbox();
            pendingThrowHitbox = false;
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
        if (IsAnimatorFrozen(animator)) return;
        UpdateChargeState();
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!string.IsNullOrEmpty(currentAttackStateName) && !state.IsName(currentAttackStateName))
        {
            animator.speed = 1f;
            return;
        }
        bool useStartUp = currentStartUpLength > 0f && currentStartUpSpeed < 1f;
        bool useRecovery = currentRecoveryLength > 0f && currentRecoverySpeed < 1f;
        float nt = state.normalizedTime;
        float targetSpeed = 1f;
        if (nt < 1f)
        {
            if (useStartUp && nt < currentStartUpLength)
                targetSpeed = currentStartUpSpeed;
            else if (useRecovery && nt >= (1f - currentRecoveryLength))
                targetSpeed = currentRecoverySpeed;
        }

        if (isChargingAttack)
            targetSpeed = Mathf.Min(targetSpeed, chargeAnimatorSpeed);
        else if (chargeReleaseBoostEndTime > Time.time)
            targetSpeed = Mathf.Max(targetSpeed, chargeReleaseBoostSpeed);

        animator.speed = targetSpeed;
    }

    void ResetChargeState(bool resetInputOrigin = true)
    {
        StopChargeRumble();
        StopChargeLoopSfxImmediate();
        chargeWindowOpen = false;
        isChargingAttack = false;
        chargeStartTime = 0f;
        currentChargeDuration = 0f;
        chargeReleaseBoostEndTime = 0f;
        chargeReleaseBoostSpeed = 1f;
        chargeReleaseDamageScale = 1f;
        chargeReleaseKnockbackScale = 1f;
        chargeReleaseLaunchScale = 1f;
        forceChargeForCurrentAttack = false;
        if (resetInputOrigin)
        {
            currentAttackStartedFromLightInput = false;
            currentAttackStartedFromHeavyInput = false;
            currentAttackStartedFromRbXInput = false;
        }
    }

    bool CanCurrentAttackCharge()
    {
        if (!enableWeaponCharge) return false;
        if (currentAttackData == null) return false;
        if (chargeHeavyOnly && !currentAttackStartedFromHeavyInput) return false;
        return true;
    }

    bool IsChargeInputStillHeld()
    {
        bool lightHeld =
            (Keyboard.current != null && Keyboard.current.jKey.isPressed) ||
            (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed);

        bool heavyHeld =
            (Keyboard.current != null && Keyboard.current.kKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.ReadValue() > heavyTriggerHeldThreshold);

        if (currentAttackStartedFromHeavyInput) return heavyHeld;
        if (currentAttackStartedFromLightInput) return lightHeld;
        if (currentAttackStartedFromRbXInput)   // Chord: charge while either button is still held.
            return (Gamepad.current != null && (Gamepad.current.rightShoulder.isPressed || Gamepad.current.buttonWest.isPressed));
        return false;
    }

    void StartChargeIfInputHeld()
    {
        if (!CanCurrentAttackCharge()) return;
        if (!forceChargeForCurrentAttack && !IsChargeInputStillHeld()) return;
        isChargingAttack = true;
        chargeStartTime = Time.time;
        currentChargeDuration = 0f;
        StartChargeLoopSfx(currentAttackData);
    }

    void StopCharge()
    {
        if (!isChargingAttack) return;
        currentChargeDuration = Mathf.Max(0f, Time.time - chargeStartTime);
        isChargingAttack = false;
        // Keep all delayed timings aligned with the charge-extended animation timeline.
        if (currentChargeDuration > 0f)
        {
            if (lungePending)
            {
                lungeTriggerTime += currentChargeDuration;
                lungeEndTime     += currentChargeDuration;
            }
            // Combo cancel window: opened at commit time relative to the animation; shift it
            // so it stays in the correct position after the charge-extended animation plays out.
            // Without this the window expires mid-hold and the next chain hit becomes unreachable.
            if (lightComboCount > 0)
            {
                comboWindowStart += currentChargeDuration;
                comboWindowEnd   += currentChargeDuration;
            }
            // Attack tracking window: tracks the active frames of the swing; those frames arrive
            // later when charge slows the animation, so extend the window to match.
            if (trackingEndTime > 0f)
                trackingEndTime += currentChargeDuration;
        }
        // Charge hold slows animation and consumes extra real time; extend cooldown so
        // next attack timing stays aligned with the delayed move completion.
        nextAttackTime += currentChargeDuration;
        currentAttackEndTime += currentChargeDuration;
        StopChargeRumble();
        FadeOutChargeLoopSfx();
        FadeOutAttackStartSfx();
        float normalizedCharge = maxChargeTime > 0f
            ? Mathf.Clamp01(currentChargeDuration / maxChargeTime)
            : 1f;
        if (chargeReleaseBoostDuration > 0f && normalizedCharge > 0f)
        {
            float minRelease = Mathf.Max(1f, Mathf.Min(chargeReleaseMinSpeed, chargeReleaseMaxSpeed));
            chargeReleaseBoostSpeed = Mathf.Lerp(minRelease, chargeReleaseMaxSpeed, normalizedCharge);
            chargeReleaseBoostEndTime = Time.time + chargeReleaseBoostDuration;
        }
        else
        {
            chargeReleaseBoostSpeed = 1f;
            chargeReleaseBoostEndTime = 0f;
        }
        chargeReleaseDamageScale = Mathf.Lerp(1f, chargeDamageMultiplier, normalizedCharge);
        chargeReleaseKnockbackScale = Mathf.Lerp(1f, chargeKnockbackMultiplier, normalizedCharge);
        chargeReleaseLaunchScale = Mathf.Lerp(1f, chargeLaunchMultiplier, normalizedCharge);
        // Scale lunge distance by charge amount too, so charged releases travel further.
        if (lungePending && currentLungeDistance > 0f)
            currentLungeDistance *= chargeReleaseKnockbackScale;
        // TODO: Use currentChargeDuration / maxChargeTime to increase sword size while charging/releasing.
    }

    void UpdateChargeState()
    {
        if (!chargeWindowOpen) return;

        // If input begins being held during the window (not only exactly on start event),
        // treat it as a valid charge start.
        if (!isChargingAttack)
            StartChargeIfInputHeld();
        if (!isChargingAttack) return;

        UpdateChargeRumble();

        bool released = !IsChargeInputStillHeld();
        bool reachedMax = maxChargeTime > 0f && Time.time >= (chargeStartTime + maxChargeTime);
        if (released || reachedMax)
            StopCharge();
    }

    void UpdateChargeRumble()
    {
        if (!enableChargeRumble || !isChargingAttack)
        {
            StopChargeRumble();
            return;
        }

        var gamepad = Gamepad.current;
        if (gamepad == null)
        {
            chargeRumbleActive = false;
            return;
        }

        float normalizedCharge = maxChargeTime > 0f
            ? Mathf.Clamp01((Time.time - chargeStartTime) / maxChargeTime)
            : 1f;
        float curvedCharge = Mathf.Pow(normalizedCharge, Mathf.Max(1f, chargeRumbleCurveExponent));

        float low = Mathf.Lerp(chargeRumbleMinLow, chargeRumbleMaxLow, curvedCharge);
        float high = Mathf.Lerp(chargeRumbleMinHigh, chargeRumbleMaxHigh, curvedCharge);
        gamepad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high));
        chargeRumbleActive = true;
    }

    void StopChargeRumble()
    {
        if (!chargeRumbleActive) return;
        var gamepad = Gamepad.current;
        if (gamepad != null)
            gamepad.SetMotorSpeeds(0f, 0f);
        chargeRumbleActive = false;
    }

    // Animation Event: place at the frame where hold-to-charge should begin.
    public void OnChargeWindowStart()
    {
        chargeWindowOpen = true;
        StartChargeIfInputHeld();
    }

    // Animation Event: place at the frame where charge window should end (optional safety).
    public void OnChargeWindowEnd()
    {
        chargeWindowOpen = false;
        StopCharge();
    }

    // Alias for naming variant.
    public void OnAttackChargeWindowStart()
    {
        OnChargeWindowStart();
    }

    // Alias for naming variant.
    public void OnAttackChargeWindowEnd()
    {
        OnChargeWindowEnd();
    }

    // Animation Event: place on the throw animation at the frame where the "loaded" hold begins.
    // If the player is still holding the throw button, the animation freezes and knockback scales up.
    public void OnThrowChargeWindowStart()
    {
        throwChargeWindowOpen = true;
        StartThrowChargeIfInputHeld();
    }

    // Animation Event: optional safety — closes the charge window. Place at the windup frame
    // where charging should no longer be possible. If the player is still holding, releases now.
    public void OnThrowChargeWindowEnd()
    {
        if (isChargingThrow) StopThrowCharge();
        throwChargeWindowOpen = false;
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
