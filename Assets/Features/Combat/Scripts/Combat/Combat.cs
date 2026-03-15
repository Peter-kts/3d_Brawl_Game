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
    private AudioSource attackStartSfxSource;
    private Coroutine attackStartSfxFadeCoroutine;
    private float attackStartSfxTargetVolume = 1f;
    private AudioSource chargeLoopSfxSource;
    private Coroutine chargeLoopSfxFadeCoroutine;
    private float chargeLoopSfxTargetVolume = 1f;
    
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
    [Tooltip("How long light input must be held before release uses charged light move.")]
    [Min(0f)]
    public float lightHoldChargeThreshold = 0.1f;
    private bool lightPressArmed;
    private float lightPressStartTime;
    private bool resolvedLightAttackUseCharged;
    private bool rbXPressArmed;
    private bool forceChargeForNextAttack;
    private bool forceChargeForCurrentAttack;
    
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
    private AttackData currentAttackData;
    public AttackData CurrentAttackData => currentAttackData;
    
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
    private bool currentAttackStartedFromLightInput;
    private bool currentAttackStartedFromHeavyInput;

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
    private bool chargeWindowOpen;
    private bool isChargingAttack;
    public bool IsChargingAttack => isChargingAttack;
    private float chargeStartTime;
    private float currentChargeDuration;
    private float chargeReleaseBoostEndTime;
    private float chargeReleaseBoostSpeed = 1f;
    private bool chargeRumbleActive;
    private float chargeReleaseDamageScale = 1f;
    private float chargeReleaseKnockbackScale = 1f;
    public float ChargeReleaseDamageScale => chargeReleaseDamageScale;
    public float ChargeReleaseKnockbackScale => chargeReleaseKnockbackScale;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    void Awake()
    {
        if (playerController == null) playerController = GetComponent<PlayerController>();
        if (threatSystem == null) threatSystem = GetComponent<LockOnSystem>();

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
            ApplyThrowDamage(_deferThrowDamageProfileIndex);  // Mid-throw damage only — prone handled by OnThrowRelease
            _deferThrowDamageToLateUpdate = false;              // Consume deferred flag
            _deferThrowDamageProfileIndex = -1;                 // Reset profile index
            throwDamageAppliedThisFrame = true;                 // Mark so CompleteThrowRelease doesn't double-apply
        }
        // Throw release was deferred (from OnThrowRelease or from Update timer) so we run after Animator has applied root motion this frame
        if (!_deferThrowReleaseToLateUpdate || currentThrowVictim == null || !IsAnyThrowEnabled()) return;  // Skip if not deferred or invalid
        _deferThrowReleaseToLateUpdate = false;  // Consume deferred release flag
        Transform vt = (currentThrowVictim as Component)?.transform;  // Get victim transform for release
        if (vt == null) { currentThrowVictim = null; return; }  // Bail if victim destroyed
        ReleaseThrowVictimFromSocket();          // Bake victim, stop pseudo-parent follow, CC/rb, ClearKnockback, reapply
        BakePlayerThrowRootMotionAndRestore();   // Save player root motion state and restore pre-throw pose
        CompleteThrowRelease(vt, _deferThrowReleaseProfileIndex, throwDamageAppliedThisFrame, applyReleaseEffects: true);  // Apply release forces/effects and cleanup
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
        UpdatePendingHitbox();
        UpdateHitStop();
        UpdateAttackStartUpSpeed();
        
        RevertTorsoRotationIfExpired();

        // 4) Read and process attack inputs (light / heavy / throw with gating and dispatch).
        ReadAttackInputs(out bool lightTriggered, out bool heavyPressed, out bool throwPressed, out bool rbXReleased, out bool lightHeld, out bool heavyHeld);
        TryProcessAttackInputs(lightTriggered, heavyPressed, throwPressed, rbXReleased, lightHeld, heavyHeld);
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
            if ((currentStartUpLength > 0f || currentRecoveryLength > 0f) && animator != null && !frozenAnimators.Any(f => f.animator == animator)) // Restore normal animator speed if startup/recovery speed scaling was in use.
                animator.speed = 1f;
            isAttacking = false;
            hitboxPending = false;      // Cancel any delayed normal-attack hitbox.
            pendingThrowHitbox = false; // Cancel any delayed throw grab hitbox.
            currentAttackData = null;   // Clear current move context (used by SFX/events/debug).
            ResetChargeState();
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
        if (damageableForStun == null || !damageableForStun.IsStunned || (!isAttacking && !hitboxPending)) return; // Interrupt combat flow only if stunned mid-attack.

        // Player stunned (e.g. hit during throw): release victim without damage/get-up, then clear state
        if (currentThrowVictim != null) // If a throw victim is attached, release safely without applying throw end effects.
            ForceThrowReleaseFallback(applyReleaseEffects: false);
        else
            ClearThrowState();

        // Fully abort the attack so no delayed hitbox, lunge, or tracking continues.
        // Also restore any temporary animator speed modifications (startup/recovery/charge/hitstop).
        RestoreAnimatorSpeedStateAfterDamageOrStun();
        isAttacking = false;
        hitboxPending = false;
        pendingThrowHitbox = false;
        lungePending = false;
        trackingEndTime = 0f;
        lightComboCount = 0;
        hasAppliedTorsoRotation = false;
        currentAttackData = null;
        ResetChargeState();
    }

    /// <summary>
    /// Immediate external interrupt path used when damage lands during charge.
    /// Forces attack/charge state to unwind this frame so stun can take over instantly.
    /// </summary>
    public void InterruptAttackAndChargeForStun()
    {
        if (!isAttacking && !hitboxPending && !isChargingAttack) return;
        if (currentThrowVictim != null)
            ForceThrowReleaseFallback(applyReleaseEffects: false);
        RestoreAnimatorSpeedStateAfterDamageOrStun();
        isAttacking = false;
        hitboxPending = false;
        pendingThrowHitbox = false;
        lungePending = false;
        trackingEndTime = 0f;
        lightComboCount = 0;
        hasAppliedTorsoRotation = false;
        currentAttackData = null;
        ResetChargeState();
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

        // Simultaneous gamepad shortcut: arm when both are held and one was just pressed, fire on release.
        if (!rbXPressArmed && ((rbPressedThisFrame && xHeldNow) || (xPressedThisFrame && rbHeldNow)))
            rbXPressArmed = true;
        rbXReleased = false;
        if (rbXPressArmed && (rbReleasedThisFrame || xReleasedThisFrame))
        {
            rbXReleased = true;
            rbXPressArmed = false;
        }
        if (!rbHeldNow && !xHeldNow && !rbXReleased)
            rbXPressArmed = false;
        if (rbXPressArmed || rbXReleased)
            lightPressArmed = false;

        throwPressed =
            (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame) ||
            ((Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame) && !rbHeldNow && !rbXPressArmed);

        lightHeld =
            (Keyboard.current != null && Keyboard.current.jKey.isPressed) ||
            (Mouse.current != null && Mouse.current.leftButton.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed);

        heavyHeld =
            (Keyboard.current != null && Keyboard.current.kKey.isPressed) ||
            (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed);
    }

    /// <summary>Gate checks (stun, comboSet, cooldown) then dispatch throw / light / heavy.</summary>
    void TryProcessAttackInputs(bool lightAttackInput, bool heavyAttackInput, bool throwInput, bool rbXAttackInput, bool lightHeld, bool heavyHeld)
    {
        bool canAttack = playerController != null;
        var damageable = GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.IsStunned)
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
            // Block all normal attack inputs while throw flow is active.
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
            forceChargeForNextAttack = false;
            DoAttack(comboSet.rbXAttack, Color.magenta);
            return;
        }

        bool canThrow = threatSystem != null && threatSystem.HasSoftTarget;
        if (throwInput && canThrow && !isAttacking && Time.time >= nextThrowTime && IsAnyThrowEnabled())
        {
            DoThrow();
            return;
        }

        bool inCancelWindow = (lightComboCount == 1) &&
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
            // Starting a new combo - check if holding forward
            bool holdingForward = playerController != null && GetRawStickInput().y > 0.3f;
            isNeutralCombo = !holdingForward;
            Debug.Log($"[Combat] holdingForward: {holdingForward}");
            
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
            // Second jab - read raw input directly (CombatStickInput is stale during attacks)
            bool holdingForwardNow = GetRawStickInput().y > 0.3f;
            AttackData jab2 = holdingForwardNow
                ? (useCharged ? comboSet.forwardJab2 : comboSet.forwardJab2Normal)
                : (useCharged ? comboSet.neutralJab2 : comboSet.neutralJab2Normal);
            Color jab2Color = holdingForwardNow ? Color.cyan : Color.yellow;
            forceChargeForNextAttack = useCharged;
            DoAttack(jab2, jab2Color);
            
            // Combo finished - must wait full cooldown now
            lightComboCount = 0;
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
    
    void DoAttack(AttackData attack, Color visualColor)
    {
        currentAttackStartTime = Time.time;
        ResetChargeState(resetInputOrigin: false);
        forceChargeForCurrentAttack = forceChargeForNextAttack;
        forceChargeForNextAttack = false;
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
        
        // Play attack animation with a near-instant fixed-time crossfade (~1-2 frames at 60fps).
        // If attack animations feel delayed or hitbox timing drifts, revert to: animator.Play(attack.animationTrigger, 0, 0f);
        if (animator != null && !string.IsNullOrEmpty(attack.animationTrigger))
        {
            if (DebugSettings.Instance != null && DebugSettings.Instance.logAttackTiming)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                Debug.Log($"[AttackStart] trigger='{attack.animationTrigger}' hitboxDelay={attack.hitboxDelay:F3} startUp=({attack.startUpLength:F2},{attack.startUpSpeed:F2}) recovery=({attack.recoveryLength:F2},{attack.recoverySpeed:F2}) | fromStateHash={state.shortNameHash} fromNT={state.normalizedTime:F3}");
            }
            else
                Debug.Log($"Playing animation trigger: '{attack.animationTrigger}'");
            animator.CrossFadeInFixedTime(attack.animationTrigger, 0.033f, 0, 0f);
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
        
        if (attack.hitboxType == AttackHitboxType.WeaponStrike)
        {
            // Weapon strikes are handled exclusively by WeaponCombat + WeaponTipHitbox
            // via BeginWeaponTipActiveFrames/EndWeaponTipActiveFrames animation events.
            hitboxPending = false;
            pendingAttackData = null;
        }
        else if (attack.hitboxDelay > 0f)
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

        // Target-side hit stop:
        // Freeze only the target Animator (not whole game time) for attack.hitStopDuration seconds.
        if (targetTransform != null && attack.hitStopDuration > 0f)
        {
            // We freeze the first Animator in the target hierarchy (standard for character rigs).
            Animator targetAnim = targetTransform.GetComponentInChildren<Animator>();
            // Avoid double-adding the same animator if multiple colliders report the same hit target.
            if (targetAnim != null && !frozenAnimators.Any(f => f.animator == targetAnim))
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
            if (animator != null && !frozenAnimators.Any(f => f.animator == animator))
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
    /// Returns light and heavy hitbox center/radius for editor Gizmos.
    /// </summary>
    public (Vector3 lightCenter, float lightRadius, Vector3 heavyCenter, float heavyRadius) GetEditorHitboxCenters()
    {
        if (comboSet == null) return (transform.position, 0f, transform.position, 0f);
        Transform origin = hitOrigin != null ? hitOrigin : transform;
        AttackData light = comboSet.forwardJabNormal;
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
            state.center = CalculateHitboxCenter(comboSet.forwardJabNormal);
            state.radius = comboSet.forwardJabNormal.hitboxRadius;
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
            
            Vector3 knockbackVector = ((horizontalDir * attack.knockback) + (Vector3.up * attack.knockbackUp)) * chargeReleaseKnockbackScale;

            // Apply damage
            float airborne = attack.makesAirborne ? attack.airborneDuration : 0f;
            damageable.TakeHit(
                Mathf.RoundToInt(attack.damage * chargeReleaseDamageScale),
                knockbackVector,
                attack.hitstun,
                airborne,
                attack.hitStopDuration,
                attack.heaviness,
                attack.height
            );
            
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
        
        GameObject connectVfxPrefab = attack.hitConnectVfxPrefab != null ? attack.hitConnectVfxPrefab : hitConnectVfxPrefab;
        if (didHit && connectVfxPrefab != null)
        {
            Quaternion rot = (center - transform.position).sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(center - transform.position)
                : transform.rotation;
            rot = rot * Quaternion.Euler(attack.hitConnectVfxRotationOffset);
            var go = Instantiate(connectVfxPrefab, center + attack.hitConnectVfxPositionOffset, rot);
            PlayVfx(go);
        }

        if (didHit)
            PlayAttackCues(attack, AttackSfxTriggerType.OnHitConfirm, 0, useLegacyFallback: true);
        
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
        forceChargeForCurrentAttack = false;
        if (resetInputOrigin)
        {
            currentAttackStartedFromLightInput = false;
            currentAttackStartedFromHeavyInput = false;
        }
    }

    bool CanCurrentAttackCharge()
    {
        if (!enableWeaponCharge) return false;
        if (currentAttackData == null) return false;
        if (currentAttackData.hitboxType != AttackHitboxType.WeaponStrike) return false;
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
    
    IEnumerator RumbleForSeconds(float duration)
    {
        var gamepad = Gamepad.current;
        if (gamepad == null) yield break;
        gamepad.SetMotorSpeeds(0.25f, 0.5f);
        yield return new WaitForSecondsRealtime(duration);
        gamepad.SetMotorSpeeds(0f, 0f);
    }

}
