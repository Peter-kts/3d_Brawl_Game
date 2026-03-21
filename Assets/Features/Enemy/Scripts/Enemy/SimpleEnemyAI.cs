/*
 * ============================================================================
 * SIMPLEENEMYAI.CS - Enemy AI with behavior system
 * ============================================================================
 * 
 * BEHAVIOR SYSTEM:
 * ----------------
 * 
 * This script manages enemy AI through a BEHAVIOR DELEGATION pattern:
 *   - Each behavior (chase, standoff, etc.) is a separate class
 *   - This script holds the current behavior and calls Execute() each frame
 *   - Transition logic here decides when to switch behaviors
 * 
 * CURRENT BEHAVIORS:
 * ------------------
 * 
 * 1. CHASE (ChaseBehavior):
 *    - Move toward the player
 *    - Transitions to Standoff when close enough
 * 
 * 2. STANDOFF (StandoffBehavior):
 *    - Circle the player at a preferred distance
 *    - Occasionally attack (via EnemyCombat)
 *    - Transitions back to Chase if player gets too far
 * 
 * TRANSITIONS (managed here, with hysteresis to prevent flickering):
 *    Chase -> Standoff: distance <= standoffEnterRange
 *    Standoff -> Chase: distance > standoffExitRange
 * 
 * COLLISION HANDLING:
 * -------------------
 * 
 * This script uses CharacterController for movement (same as player).
 * CharacterController provides:
 *   - Collision detection with other CharacterControllers
 *   - Collision with static geometry (walls, floors)
 *   - No need for Rigidbody physics
 *   - Built-in slope and step handling
 *
 * STATE PRIORITY (CurrentState / CanAct):
 * --------------------------------------
 * Dying > Airborne > Crashed > Prone > Stunned > GettingUp > Normal.
 * Only Normal allows CanAct (chase/standoff/attack). All others block HandleMovement.
 *
 * UPDATE ORDER (each frame):
 * --------------------------
 * ApplyGravity -> AirborneSequence.Update -> UpdateAnimator -> [if !dying] HandleMovement.
 * 
 * ============================================================================
 */

using System.Collections.Generic;
using UnityEngine;

/*
 * [RequireComponent]:
 * 
 * This attribute tells Unity: "This script NEEDS a CharacterController"
 * When you add this script to a GameObject, Unity auto-adds CharacterController
 * Prevents null reference errors from missing dependencies
 */
[RequireComponent(typeof(CharacterController))]
public class SimpleEnemyAI : MonoBehaviour
{
    // ========================================================================
    // SERIALIZED FIELDS (tweak in Inspector; many have Tooltips for details)
    // ========================================================================
    
    // --- Movement: who we chase and how we move/rotate ---
    [Header("Movement")]
    [Tooltip("Reference to the player's Transform. Auto-finds by 'Player' tag if not assigned.")]
    public Transform player;
    
    [Tooltip("How fast the enemy moves (units per second). Slower than player so they can escape.")]
    public float moveSpeed = 3.5f;
    
    [Tooltip("Rotation speed in degrees per second")]
    public float rotationSpeed = 540f;

    [Tooltip("Randomized delay before starting to rotate toward the player (seconds).")]
    public float faceTurnDelayMin = 0.06f;

    [Tooltip("Max delay before rotating toward the player (seconds). Keep >= Min.")]
    public float faceTurnDelayMax = 0.14f;
    
    // --- Personality: drives all combat/standoff behavior settings ---
    [Header("Personality")]
    [Tooltip("ScriptableObject defining this enemy's combat style and reaction behavior. Create via Assets > Create > Enemy > Personality. Leave null to use hardcoded Normal defaults.")]
    public EnemyPersonality personality;

    // --- Separation: prevent enemies from stacking on top of each other ---
    [Header("Separation")]
    [Tooltip("Radius within which this enemy pushes away from other enemies.")]
    public float separationRadius = 2f;
    [Tooltip("Strength of the separation push (units/sec).")]
    public float separationForce = 3f;

    // --- Physics: gravity (CharacterController has no built-in gravity) ---
    [Header("Physics")]
    [Tooltip("Gravity applied to the enemy (should match player's gravity)")]
    public float gravity = -20f;
    
    // --- Animation: Animator ref, shared config asset, and per-enemy tuning values ---
    [Header("Animation")]
    [Tooltip("Animator for locomotion. Auto-finds on this object or children if not set.")]
    public Animator animator;

    [Tooltip("ScriptableObject with all animator parameter/state names. Shared across all enemies using the same controller. Create via Assets > Create > Enemy > Animation Config.")]
    public EnemyAnimationConfig animationConfig;

    [Tooltip("How quickly the animation speed blends (higher = snappier)")]
    public float animationDamping = 10f;

    [Tooltip("Yaw turn speed while in knockback-stun (deg/sec). Faces knockback direction so entry/loop orientation matches movement.")]
    public float knockbackStunFacingTurnSpeed = 900f;
    [Tooltip("If enabled, knockback-stun faces opposite the knockback movement direction (useful for clips authored to move backward).")]
    public bool invertKnockbackStunFacing;

    [Tooltip("Fallback base duration (seconds) when auto-detect fails. Hit state length is auto-fetched from the Animator and cached; use this if a state has no motion or to override.")]
    public float baseHitAnimDuration = 0.4f;

    /*
     * LIFTOFF / LOOP / CRASH SYSTEM:
     * 
     * Splits a SINGLE animation clip into three phases so the airborne
     * spinning portion can loop for as long as the enemy stays in the air:
     * 
     *   [--- Liftoff ---][--- Loop (repeats) ---][--- Crash ---]
     *   0.0         liftoffEnd/loopStart    loopEnd/crashStart    1.0
     * 
     * How it works:
     *   1. Enemy gets hit with makesAirborne = true
     *   2. IsAirborne goes true → Liftoff plays (hit reaction, launch up)
     *   3. When normalizedTime >= liftoffEnd → Loop starts (spinning in air)
     *   4. Loop portion repeats: when normalizedTime >= loopEnd, jump back to loopStart
     *   5. IsAirborne goes false → Crash plays (slam to ground)
     *   6. When normalizedTime >= crashEnd → phase done, triggers get-up
     * 
     * Configure the normalized time boundaries in the Inspector by scrubbing
     * through your animation clip to find where each phase starts/ends.
     * Leave airborneStateName EMPTY to disable airborne crash handling.
     */
    [Header("Airborne Animation (Liftoff / Loop / Crash)")]
    [Tooltip("Settings for splitting a single airborne animation into liftoff, loop, and crash phases. Leave airborneStateName empty to disable.")]
    public AirborneAnimationSettings airborneAnimation = new AirborneAnimationSettings();

    /*
     * GET-UP SYSTEM:
     * After the crash animation finishes (Liftoff/Loop/Crash crash phase),
     * the enemy plays a get-up animation. The enemy stays stunned for
     * getUpDuration so they can't act while getting off the ground.
     */
    [Header("Prone (ground knockdown)")]
    [Tooltip("Seconds the enemy lies prone before get-up starts when entering prone from airborne crash (and as default fallback for other prone entries). 0 = get-up starts immediately.")]
    public float groundedDuration = 1f;
    [Tooltip("Seconds to blend into the prone animation (0 = snap immediately).")]
    public float proneTransitionDuration = 0.08f;
    [Tooltip("Prone variant used specifically when wall-bounce resolves into prone.")]
    public ProneVariant wallBounceProneVariant = ProneVariant.Default;
    [Tooltip("If enabled, wall-bounce -> prone applies a 180-degree facing flip when entering prone.")]
    public bool invertWallBounceProneFacing = false;

    [Header("Get Up (after prone)")]
    [Tooltip("Duration the enemy is stunned while getting up (and length the get-up animation is scaled to).")]
    public float getUpDuration = 1.5f;
    [Tooltip("Base duration of get-up clip (seconds). Speed is scaled so animation matches getUpDuration. Ignored if 0.")]
    public float baseGetUpAnimDuration = 1.2f;

    // ========================================================================
    // PRIVATE REFERENCES (set in Awake; used every frame or on events)
    // ========================================================================
    
    private CharacterController cc;   // Movement + collision; required by [RequireComponent]
    private EnemyHealth health;       // HP, stun, airborne, dying; we read flags and get callbacks
    private Vector3 velocity;         // Vertical only; used in ApplyGravity then cc.Move(velocity * dt)
    
    // --- Animation: behaviors set target, we smooth and push to Animator ---
    /// <summary>Smoothed value sent to the Animator each frame. Lerps toward targetAnimSpeed so transitions aren't instant.</summary>
    private float currentAnimSpeed;
    /// <summary>Desired speed for this frame: 0 = idle, 0.5 = circling/strafe, 1 = full run. Set by current behavior in HandleMovement; reset to 0 at start of FixedUpdate.</summary>
    private float targetAnimSpeed;
    private int lastHitStateIndex = -1;  // When using hitStateNames[], avoid playing same state twice in a row
    private Dictionary<string, float> cachedHitStateDurations;  // Clip length per hit state name; avoids GetCurrentAnimatorStateInfo every hit
    private AirborneSequence airborneSequence;  // Liftoff → Loop → Crash; fires onCrashLanded when crash ends; created in Awake
    private EnemyProneSystem proneSystem;       // Prone timer and animation; created in Awake; entered via AirborneSequence.onCrashLanded
    private EnemyReactionStateMachine reactionStateMachine; // Visual-only reaction machine for temporary animation priority (currently knockback-entry)

    private EnemyStunMeter stunMeter;           // Optional; when present, IsStandingStunned blocks behavior and drives animator

    private EnemyStateMachine stateMachine;     // Central behavioral state machine — all Chase↔Standoff transitions go through stateMachine.Transition()
    private ChaseBehavior chaseBehavior;        // Pre-allocated; reused every time the enemy enters the chase state
    private StandoffBehavior standoffBehavior;  // Pre-allocated; reused every time the enemy enters the standoff state
    private string currentReactionDebug = "None"; // Debug-only visual reaction state (None/WallBounce/KnockbackEntry)
    // Wall-bounce latch:
    // - Set true when OnWallBounce fires
    // - While true, stun animator params are forced off so wall-bounce owns presentation
    // - Cleared by re-hit or wall-bounce completion callback
    private bool wallBounceStunLatched;
    // After wall-bounce, keep standing/knockback stun params suppressed until a new hit arrives.
    // This prevents old standing-stun timer state from re-activating those params at wall-bounce end.
    private bool suppressStandingStunParamsUntilNextHit;
    private bool wasInKnockbackStun;              // Tracks entry into knockback-stun mode for direction seeding
    private Vector3 lastKnockbackFacingDir = Vector3.forward; // Last valid horizontal knockback direction used for stable facing as velocity decays
    private float faceTurnDelayTimer;
    private bool isFaceTurnReady = true;
    private Vector3 lastFaceDirection = Vector3.forward;
    
    // ========================================================================
    // PUBLIC PROPERTIES (accessed by behaviors and debug visuals)
    // ========================================================================
    
    /// <summary>Current behavior state name for debug visualization (Chase, Circling, PreAttack, Attacking).</summary>
    public string CurrentBehaviorStateName => stateMachine?.CurrentBehavior?.CurrentStateName ?? "Unknown";
    
    /// <summary>CharacterController for collision-aware movement.</summary>
    public CharacterController CC => cc;
    
    /// <summary>Enemy health component (for stun/airborne checks).</summary>
    public EnemyHealth Health => health;

    /// <summary>Airborne sequence (Liftoff/Loop/Crash). Use AirborneSequence.NotifyCrashFinished(isDying) when crash completes externally (e.g. PATH A).</summary>
    public AirborneSequence AirborneSequence => airborneSequence;

    /// <summary>Prone system (lying on ground after crash). Call ProneSystem.OverrideNextProneDuration() before a throw to set the prone length.</summary>
    public EnemyProneSystem ProneSystem => proneSystem;

    /// <summary>
    /// Current logical state (Dying &gt; Airborne &gt; Crashed &gt; Prone &gt; Stunned &gt; GettingUp &gt; Normal).
    /// Single source of truth so callers don't duplicate the priority order. Combines EnemyHealth timers and airborne phase.
    /// </summary>
    public EnemyState CurrentState
    {
        get
        {
            if (health == null) return EnemyState.Normal;
            // Highest priority: death in progress (including death during airborne)
            if (health.IsDying) return EnemyState.Dying;
            // In the air: either Health says so, or we're in Liftoff/Loop (not yet Crash)
            if (health.IsAirborne || (airborneAnimation.IsConfigured && airborneSequence != null && airborneSequence.CurrentPhase != AirborneSequence.Phase.None && airborneSequence.CurrentPhase != AirborneSequence.Phase.Crash))
                return EnemyState.Airborne;
            // Slammed to ground, crash anim playing
            if (airborneSequence != null && airborneSequence.InCrash) return EnemyState.Crashed;
            // Lying prone after crash, playing prone animation until get-up starts
            if (proneSystem != null && proneSystem.IsInProne) return EnemyState.Prone;
            // Hitstun (non-airborne)
            if (health.IsHitstunned) return EnemyState.Hitstunned;
            // Stun meter filled — stunned state (lower priority than hitstun so flinches still show)
            if (stunMeter != null && stunMeter.IsStunned) return EnemyState.Stunned;
            // Get-up after crash
            if (health.IsGettingUp) return EnemyState.GettingUp;
            return EnemyState.Normal;
        }
    }

    /// <summary>True when the enemy can run behavior (chase, standoff, attack). False when health is missing or during stun, airborne, crash, get-up, or dying.</summary>
    public bool CanAct => health != null && CurrentState == EnemyState.Normal;

    // Personality helpers — read from the personality asset with safe fallbacks matching old defaults.
    public float StandoffEnterRange         => personality != null ? personality.standoffEnterRange         : 4f;
    public float StandoffExitRange          => personality != null ? personality.standoffExitRange          : 6f;
    public float StandoffRadius             => personality != null ? personality.standoffRadius             : 3f;
    public float CircleSpeed                => personality != null ? personality.circleSpeed                : 2.5f;
    public float DirChangeIntervalMin       => personality != null ? personality.directionChangeIntervalMin : 1.5f;
    public float DirChangeIntervalMax       => personality != null ? personality.directionChangeIntervalMax : 4f;
    public float AttackIntervalMin          => personality != null ? personality.attackIntervalMin          : 1.5f;
    public float AttackIntervalMax          => personality != null ? personality.attackIntervalMax          : 4f;
    public float AttackTelegraphDuration    => personality != null ? personality.attackTelegraphDuration    : 0.2f;
    public float BackOffSpeed               => personality != null ? personality.backOffSpeed               : 2f;
    public float BackOffFacingThreshold     => personality != null ? personality.backOffFacingThreshold     : 0.4f;
    public float OpportunityWindow          => personality != null ? personality.opportunityWindow          : 0.5f;
    public float OpportunityAttackDelay     => personality != null ? personality.opportunityAttackDelay     : 0.15f;

    /// <summary>Enemy combat component (for attack execution). May be null if not present.</summary>
    public EnemyCombat EnemyCombat { get; private set; }

    /// <summary>Player's controller (for dodge detection). May be null if player not set.</summary>
    public PlayerController PlayerController { get; private set; }
    
    /// <summary>
    /// Target animation speed this frame. Set by behaviors (ChaseBehavior = 1, StandoffBehavior = 0.5 or 0).
    /// Values: 0 = idle, 0.5 = strafing/circling, 1 = full run.
    /// UpdateAnimator() lerps currentAnimSpeed toward this and passes it to the Animator's Speed parameter;
    /// the Animator Controller uses that parameter in transition conditions (e.g. Speed &gt; 0.1 to leave Idle).
    /// </summary>
    public float TargetAnimSpeed { get => targetAnimSpeed; set => targetAnimSpeed = value; }
    public string CurrentReactionDebug => currentReactionDebug;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    void Awake()
    {
        /*
         * GetComponent<T>() searches THIS GameObject for a component of type T
         * CharacterController is required by [RequireComponent] so it will exist
         */
        cc = GetComponent<CharacterController>();
        health = GetComponent<EnemyHealth>();
        stunMeter = GetComponent<EnemyStunMeter>();
        
        /*
         * Auto-find player if not assigned:
         * 
         * GameObject.FindGameObjectWithTag():
         *   - Searches ALL active GameObjects for matching tag
         *   - Returns the first one found (null if none)
         *   - Relatively slow - don't call every frame!
         * 
         * Tags must be defined in Edit > Project Settings > Tags and Layers
         * Then assigned to GameObjects in the Inspector
         * 
         * Here we call it once in Awake, which is fine
         */
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player != null)
            PlayerController = player.GetComponent<PlayerController>();

        // Auto-find animator on this object or children (e.g., on the visual model)
        if (animator == null) animator = GetComponent<Animator>();
        // if (animator == null) animator = GetComponentInChildren<Animator>();
        
        // Find EnemyCombat component (optional - enemies work without it, just can't attack)
        EnemyCombat = GetComponent<EnemyCombat>();
        
        // Create behaviors and wire up the central state machine (always starts in Chase)
        chaseBehavior    = new ChaseBehavior(this);
        standoffBehavior = new StandoffBehavior(this);
        stateMachine     = new EnemyStateMachine();
        stateMachine.Initialize(chaseBehavior);
        reactionStateMachine = new EnemyReactionStateMachine();

        if (animationConfig == null)
            Debug.LogError($"SimpleEnemyAI on '{name}': animationConfig is not assigned. Create an EnemyAnimationConfig asset (Assets > Create > Enemy > Animation Config) and assign it.", this);

        // Prone system: owns the prone timer/animation; fires TriggerGetUpSequence when timer expires.
        // Created before airborneSequence so the lambda below can close over it.
        string proneState         = animationConfig != null ? animationConfig.proneStateName         : "";
        string proneFaceDownState = animationConfig != null ? animationConfig.proneFaceDownStateName : "";
        int    proneLayerIdx      = animationConfig != null ? animationConfig.proneLayer              : 1;
        proneSystem = new EnemyProneSystem(transform, animator, proneState, proneFaceDownState, proneLayerIdx, proneTransitionDuration, health, TriggerGetUpSequence);

        // Airborne sequence: Liftoff → Loop → Crash; when crash ends fires onCrashLanded → proneSystem.Enter(dur).
        string hitSpeedParam = animationConfig != null ? animationConfig.hitSpeedParameter : "";
        airborneSequence = new AirborneSequence(animator, airborneAnimation, hitSpeedParam, health, groundedDuration, (dur) => proneSystem.Enter(dur));
    }

    /*
     * Update order: gravity first (so we're grounded for logic), then airborne sequence (phase advances),
     * then animator (so visuals match state), then early-out if dying, then movement/behavior.
     * targetAnimSpeed is reset to 0 here; behaviors set it inside HandleMovement when they run.
     */
    void Update()
    {
        ApplyGravity();
        if (airborneSequence != null) airborneSequence.Update(health);
        if (proneSystem != null) proneSystem.Update();
        UpdateKnockbackStunFacing();
        UpdateAnimator();
        // While dying (including airborne-as-death): skip movement/behavior
        if (health != null && health.IsDying) return;
        // Default: no movement. Behaviors set TargetAnimSpeed inside HandleMovement (chase=1, standoff=0.5 or 0).
        targetAnimSpeed = 0f;
        HandleMovement();
    }
    
    // ========================================================================
    // ANIMATION
    // ========================================================================
    
    /*
     * UpdateAnimator - Drives the Animator's "Speed" parameter from AI behavior.
     *
     * WHAT THIS DOES:
     * Behaviors set TargetAnimSpeed each frame (0 = idle, 0.5 = circle, 1 = run). We don't pass that
     * value straight to the Animator; we smooth it into currentAnimSpeed and send that. The Animator
     * Controller (e.g. enemy.controller) uses the Speed parameter in transition conditions (e.g.
     * "Speed > 0.1" to leave Idle, "Speed < 0.1" to go to Idle). So this code only updates a parameter;
     * the actual state machine and transitions are defined in the Animator Controller asset.
     *
     * SMOOTHING:
     * Exponential lerp (1 - Exp(-k*dt)) gives framerate-independent decay toward target. Without it,
     * Speed would snap from 1 to 0 when the enemy stops, causing a visible pop. Snapping to 0 when
     * both current and target are near zero avoids floating-point drift (Speed never quite reaching 0).
     */
    void UpdateAnimator()
    {
        if (animator == null) return;

        // Lerp current toward target: smooth, framerate-independent (same formula as PlayerController).
        float t = PlayerController.ExponentialBlendFactor(animationDamping);
        currentAnimSpeed = Mathf.Lerp(currentAnimSpeed, targetAnimSpeed, t);

        // Avoid drift: when we're aiming for 0 and very close, clamp to exactly 0.
        if (currentAnimSpeed < 0.001f && targetAnimSpeed == 0f)
            currentAnimSpeed = 0f;

        if (animationConfig == null) return;

        // Visual arbitration overview:
        // 1) Wall-bounce uses a latch (not a timer) while bounce owns presentation.
        // 2) Knockback-entry uses a short priority window from reactionStateMachine.
        // 3) Debug text reports which visual channel currently has priority.
        bool wallBounceActive = wallBounceStunLatched;
        bool knockbackEntryActive = reactionStateMachine != null &&
                                    reactionStateMachine.IsKnockbackEntryActive(
                                        Time.time,
                                        animator,
                                        animationConfig.standingStunLayer,
                                        animationConfig.knockbackStunEntryStateName);
        currentReactionDebug = wallBounceActive ? "WallBounce" : (knockbackEntryActive ? "KnockbackEntry" : "None");
        if (!string.IsNullOrEmpty(animationConfig.wallBounceActiveParameter))
            animator.SetBool(animationConfig.wallBounceActiveParameter, wallBounceActive);

        // Send to Animator. During prone/airborne, zero locomotion speed so enemy doesn't walk.
        bool inAirbornePhase = airborneSequence != null && airborneSequence.CurrentPhase != AirborneSequence.Phase.None;
        bool inProne = proneSystem != null && proneSystem.IsInProne;
        float speedToApply = (inProne || inAirbornePhase) ? 0f : currentAnimSpeed;
        animator.SetFloat(animationConfig.speedParameter, speedToApply);

        // Airborne + crash: play at 1x (or speed-multiplied during crash relaunch). Prone plays at 1x via its own state.
        if (CurrentState == EnemyState.Airborne || CurrentState == EnemyState.Crashed)
            animator.speed = 1f * (health != null ? health.GetAirborneSpeedMultiplier() : 1f);

        // Drive Animator booleans and reset hit speed when not in special states
        if (health != null)
        {
            // IsAirborne: from AirborneSequence when configured, else from Health
            animator.SetBool(animationConfig.airborneParameter, airborneSequence != null ? airborneSequence.GetAirborneForAnimator(health) : health.IsAirborne);

            // StunLayerActive: hitstun (and not airborne), prone, or get-up all activate the Stun animator layer
            if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                animator.SetBool(animationConfig.stunLayerParameter, ((!inAirbornePhase && !health.IsAirborne && health.IsHitstunned) || inProne || health.IsGettingUp) && !wallBounceActive);

            // When not in stun/airborne/prone/get-up, ensure hit layer plays at 1x (TriggerHitAnimation sets it when hit)
            if (!inAirbornePhase && !health.IsHitstunned && !inProne && !health.IsGettingUp && !string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
                animator.SetFloat(animationConfig.hitSpeedParameter, 1f);

            // StandingStunned: stun meter filled — both paths (normal and knockback) are fully parameter-driven
            if (stunMeter != null)
            {
                bool canShowStandingStunParams = !wallBounceActive && !suppressStandingStunParamsUntilNextHit;
                if (!string.IsNullOrEmpty(animationConfig.standingStunParameter))
                    animator.SetBool(animationConfig.standingStunParameter, canShowStandingStunParams && stunMeter.IsStandingStunned);
                if (!string.IsNullOrEmpty(animationConfig.knockbackStunParameter))
                    animator.SetBool(animationConfig.knockbackStunParameter, canShowStandingStunParams && stunMeter.TriggerWasHeavy && stunMeter.IsStandingStunned);
            }
        }
    }

    // Keep knockback-stun entry/loop oriented toward movement direction so the reaction reads consistently
    // from all impact angles (including reflected wall bounces).
    void UpdateKnockbackStunFacing()
    {
        if (health == null) return;
        // Rotation ownership rule:
        // while prone, EnemyProneSystem locks yaw every frame to the chosen prone orientation.
        // If knockback-facing also ran here, the two systems would fight and cause one-frame snaps.
        if (proneSystem != null && proneSystem.IsInProne)
        {
            wasInKnockbackStun = false;
            return;
        }

        bool inKnockbackStun = !wallBounceStunLatched
            && stunMeter != null
            && stunMeter.IsStandingStunned
            && stunMeter.TriggerWasHeavy;

        if (!inKnockbackStun)
        {
            wasInKnockbackStun = false;
            return;
        }

        Vector3 horizontalVel = health.KnockbackVelocity;
        horizontalVel.y = 0f;

        if (horizontalVel.sqrMagnitude > 0.0004f)
            lastKnockbackFacingDir = horizontalVel.normalized;
        else if (!wasInKnockbackStun)
        {
            Vector3 fallback = transform.forward;
            fallback.y = 0f;
            if (fallback.sqrMagnitude > 0.0001f)
                lastKnockbackFacingDir = fallback.normalized;
        }

        if (lastKnockbackFacingDir.sqrMagnitude > 0.0001f)
        {
            Vector3 facingDir = invertKnockbackStunFacing ? -lastKnockbackFacingDir : lastKnockbackFacingDir;
            Quaternion targetRot = Quaternion.LookRotation(facingDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                knockbackStunFacingTurnSpeed * Time.deltaTime);
        }

        wasInKnockbackStun = true;
    }

    /// <summary>
    /// Inject a world-space knockback facing direction (used by wall-bounce reflection).
    /// Keeps knockback-stun orientation coherent when velocity direction flips on impact.
    /// </summary>
    public void SetKnockbackFacingDirection(Vector3 worldDirection, bool snap = false)
    {
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude <= 0.0001f) return;
        lastKnockbackFacingDir = worldDirection.normalized;
        wasInKnockbackStun = true;

        if (!snap) return;
        Vector3 facingDir = invertKnockbackStunFacing ? -lastKnockbackFacingDir : lastKnockbackFacingDir;
        transform.rotation = Quaternion.LookRotation(facingDir, Vector3.up);
    }

    /// <summary>
    /// Triggers the hit reaction animation. Called by EnemyHealth when taking damage.
    /// Prefers height-specific states (Hit_High / Hit_Mid / Hit_Low), then falls back to hitStateName or random hitStateNames.
    /// The Stun layer must NOT have an Any State transition for the stun bool, or that transition would override this and always show one state.
    /// </summary>
    /// <param name="hitstun">Duration of the hitstun; animation is scaled to match.</param>
    /// <param name="heavyKnockback">True when the hit's knockback magnitude exceeded the stun threshold. Routes prone hits to KnockbackStunEntry instead of the normal prone hit state.</param>
    public void TriggerHitAnimation(float hitstun, AttackHeight height, bool heavyKnockback = false)
    {
        if (animator == null || animationConfig == null) return;
        // A fresh hit is the only thing that re-arms standing/knockback stun params after wall-bounce.
        suppressStandingStunParamsUntilNextHit = false;
        if (wallBounceStunLatched)
        {
            // Re-hit always wins: any new hit reaction cancels wall-bounce latch immediately.
            wallBounceStunLatched = false;
        }

        // If already in standing stun and hit was a heavy knockback, play KnockbackStunEntry directly.
        // TryRetriggerAsKnockback (called in EnemyHealth.TakeHit) already reset the phase + TriggerWasHeavy.
        if (stunMeter != null && stunMeter.IsStandingStunned)
        {
            if (stunMeter.TriggerWasHeavy
                && !string.IsNullOrEmpty(animationConfig.knockbackStunEntryStateName))
            {
                reactionStateMachine?.RequestKnockbackEntry(Time.time, animationConfig.knockbackStunEntryPriorityGrace);
                if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                    animator.SetBool(animationConfig.stunLayerParameter, true);
                animator.Play(animationConfig.knockbackStunEntryStateName, animationConfig.standingStunLayer, 0f);
            }
            // Normal hits while stunned — no animation change, let the stun continue uninterrupted
            return;
        }

        // If the enemy is prone, route to the appropriate hit state.
        bool currentlyProne = proneSystem != null && proneSystem.IsInProne;
        if (currentlyProne)
        {
            // Heavy knockback while prone — play the knockback stun entry (same state as standing stun knockback)
            if (heavyKnockback
                && !string.IsNullOrEmpty(animationConfig.knockbackStunEntryStateName)
                && AnimatorHasStateOnLayer(animator, animationConfig.standingStunLayer, animationConfig.knockbackStunEntryStateName))
            {
                reactionStateMachine?.RequestKnockbackEntry(Time.time, animationConfig.knockbackStunEntryPriorityGrace);
                if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                    animator.SetBool(animationConfig.stunLayerParameter, true);
                animator.Play(animationConfig.knockbackStunEntryStateName, animationConfig.standingStunLayer, 0f);
                return;
            }
            // Normal hit while prone — play prone-specific hit state if configured
            if (!string.IsNullOrEmpty(animationConfig.proneHitStateName)
                && AnimatorHasStateOnLayer(animator, animationConfig.hitAnimationLayer, animationConfig.proneHitStateName))
            {
                animator.Play(animationConfig.proneHitStateName, animationConfig.hitAnimationLayer, 0f);
                return;
            }
        }

        // Keep knockback-stun entry visual priority over normal hit reactions.
        if (reactionStateMachine != null
            && reactionStateMachine.IsKnockbackEntryActive(
                Time.time,
                animator,
                animationConfig.standingStunLayer,
                animationConfig.knockbackStunEntryStateName))
            return;

        // Prefer height-only state first (Hit_High / Hit_Mid / Hit_Low); fallback to existing random/single setup.
        string stateToPlay;
        string typedState = $"Hit_{height}";
        if (AnimatorHasStateOnLayer(animator, animationConfig.hitAnimationLayer, typedState))
        {
            stateToPlay = typedState;
            lastHitStateIndex = -1;
        }
        else if (animationConfig.hitStateNames != null && animationConfig.hitStateNames.Length > 0)
        {
            int chosenIndex;
            do
            {
                chosenIndex = Random.Range(0, animationConfig.hitStateNames.Length);
            }
            while (animationConfig.hitStateNames.Length >= 2 && chosenIndex == lastHitStateIndex);
            lastHitStateIndex = chosenIndex;
            stateToPlay = animationConfig.hitStateNames[chosenIndex];
        }
        else
        {
            lastHitStateIndex = -1;
            stateToPlay = animationConfig.hitStateName;
        }

        // Set stun bool first so the Stun layer doesn't immediately transition out of Stunned (UpdateAnimator sets it next frame; we need it true now).
        if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
            animator.SetBool(animationConfig.stunLayerParameter, true);
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
        animator.Play(stateToPlay, animationConfig.hitAnimationLayer, 0f);
        animator.Update(0f);  // One frame so GetCurrentAnimatorStateInfo below returns this state

        // Use cached duration when available to avoid GetCurrentAnimatorStateInfo every hit.
        float baseDuration = baseHitAnimDuration;
        if (cachedHitStateDurations != null && cachedHitStateDurations.TryGetValue(stateToPlay, out float cached))
            baseDuration = cached;
        else
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(animationConfig.hitAnimationLayer);
            if (stateInfo.IsName(stateToPlay) && stateInfo.length > 0f)
            {
                if (cachedHitStateDurations == null)
                    cachedHitStateDurations = new Dictionary<string, float>();
                cachedHitStateDurations[stateToPlay] = stateInfo.length;
                baseDuration = stateInfo.length;
            }
        }

        // Scale hit layer speed so clip finishes in hitstun seconds: speed = baseDuration / hitstun
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter) && baseDuration > 0f)
        {
            float speed = (hitstun > 0.001f) ? (baseDuration / hitstun) : 1f;
            animator.SetFloat(animationConfig.hitSpeedParameter, speed);
        }
        else if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
    }

    bool AnimatorHasStateOnLayer(Animator targetAnimator, int layerIndex, string stateName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(stateName)) return false;
        if (layerIndex < 0 || layerIndex >= targetAnimator.layerCount) return false;

        int shortNameHash = Animator.StringToHash(stateName);
        if (targetAnimator.HasState(layerIndex, shortNameHash)) return true;

        string fullPath = targetAnimator.GetLayerName(layerIndex) + "." + stateName;
        int fullPathHash = Animator.StringToHash(fullPath);
        return targetAnimator.HasState(layerIndex, fullPathHash);
    }

    /// <summary>
    /// Plays the thrown/grabbed state for the given duration (scaled to match). Used for synced throw.
    /// </summary>
    public void TriggerThrownAnimation(float duration, string stateName)
    {
        if (animator == null || animationConfig == null || string.IsNullOrEmpty(stateName)) return;
        animator.speed = 1.2f;
        animator.Rebind();
        animator.Update(0f);
        if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
            animator.SetBool(animationConfig.stunLayerParameter, true);
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
        animator.Play(stateName, animationConfig.hitAnimationLayer, 0f);
        animator.Update(0f);
        float baseDuration = baseHitAnimDuration;
        if (cachedHitStateDurations != null && cachedHitStateDurations.TryGetValue(stateName, out float cached))
            baseDuration = cached;
        else
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(animationConfig.hitAnimationLayer);
            if (stateInfo.IsName(stateName) && stateInfo.length > 0f)
            {
                if (cachedHitStateDurations == null)
                    cachedHitStateDurations = new Dictionary<string, float>();
                cachedHitStateDurations[stateName] = stateInfo.length;
                baseDuration = stateInfo.length;
            }
        }
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter) && baseDuration > 0f && duration > 0.001f)
            animator.SetFloat(animationConfig.hitSpeedParameter, baseDuration / duration);
        else if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
    }

    /// <summary>
    /// Called by EnemyProneSystem when the prone timer expires. Plays the get-up animation and starts the get-up stun.
    /// Also resets the airborne speed multiplier (OnAirborneSequenceEnded).
    /// </summary>
    void TriggerGetUpSequence()
    {
        if (health == null || animator == null || animationConfig == null) return;
        health.StartGetUp(getUpDuration);
        health.OnAirborneSequenceEnded();
        if (!string.IsNullOrEmpty(animationConfig.getUpStateName))
        {
            animator.speed = 1f;
            if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                animator.SetBool(animationConfig.stunLayerParameter, true);
            if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            {
                float speed = (baseGetUpAnimDuration > 0f && getUpDuration > 0.001f)
                    ? baseGetUpAnimDuration / getUpDuration
                    : 1f;
                animator.SetFloat(animationConfig.hitSpeedParameter, speed);
            }
            animator.Play(animationConfig.getUpStateName, animationConfig.getUpLayer, 0f);
        }
    }

    /// <summary>
    /// Start get-up after a throw release (no launch). Plays get-up state and sets get-up stun so the enemy stands up.
    /// </summary>
    public void TriggerGetUpFromThrow()
    {
        if (health == null || animator == null || animationConfig == null || string.IsNullOrEmpty(animationConfig.getUpStateName)) return;
        health.StartGetUp(getUpDuration);
        if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
            animator.SetBool(animationConfig.stunLayerParameter, true);
        animator.Play(animationConfig.getUpStateName, animationConfig.getUpLayer, 0f);
        if (baseGetUpAnimDuration > 0f && getUpDuration > 0.001f && !string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, baseGetUpAnimDuration / getUpDuration);
    }
    
    /// <summary>
    /// Plays the wall bounce animation when the enemy hits a wall at high velocity during knockback.
    /// Called by EnemyHealth.OnWallBounce(). kbVel is already reflected before this fires.
    /// </summary>
    public void TriggerWallBounce()
    {
        if (animator == null || animationConfig == null) return;
        if (string.IsNullOrEmpty(animationConfig.wallBounceTriggerParameter)) return;
        wallBounceStunLatched = true;
        suppressStandingStunParamsUntilNextHit = true;
        if (!string.IsNullOrEmpty(animationConfig.wallBounceActiveParameter))
            animator.SetBool(animationConfig.wallBounceActiveParameter, true);
        animator.SetTrigger(animationConfig.wallBounceTriggerParameter);
    }

    /// <summary>
    /// Called at the end of the WallBounce animation (Animation Event).
    /// If the enemy is still hitstunned, hand off to prone; otherwise just clear wall-bounce mode.
    /// This keeps re-hits interruptible while still allowing "wall bounce -> prone" when stun outlasts the clip.
    /// </summary>
    public void OnWallBounceAnimationComplete()
    {
        wallBounceStunLatched = false;
        if (health == null || proneSystem == null) return;
        bool hasAnyStun = health.IsHitstunned || (stunMeter != null && stunMeter.IsStunned);
        if (!hasAnyStun) return;
        if (proneSystem.IsInProne) return;
        proneSystem.Enter(
            groundedDuration,
            invertFacing: invertWallBounceProneFacing,
            proneVariant: wallBounceProneVariant,
            preserveCurrentFacing: true);
    }

    /// <summary>
    /// Triggers the death animation. Called by EnemyHealth when HP reaches 0.
    /// The actual disable/hide happens when OnDeathAnimationComplete() is invoked by an Animation Event on the death clip.
    /// </summary>
    public void TriggerDeathAnimation()
    {
        if (animator != null && animationConfig != null)
            animator.SetTrigger(animationConfig.deathTriggerParameter);
    }
    
    /// <summary>
    /// Called by Animation Event at the end of the death animation.
    /// Delegates to health.CompleteDeath() (or deactivates GameObject if no health); corpse can stay visible.
    /// </summary>
    public void OnDeathAnimationComplete()
    {
        // Death should settle into prone and stay there permanently.
        EnterPermanentProneForDeath();

        if (health != null)
            health.CompleteDeath();
        else
            gameObject.SetActive(false);
    }

    /// <summary>
    /// Enter prone with an effectively infinite duration so death remains on the floor.
    /// Safe to call multiple times.
    /// </summary>
    public void EnterPermanentProneForDeath()
    {
        if (proneSystem == null) return;
        proneSystem.Enter(float.PositiveInfinity);
    }
    
    // ========================================================================
    // MOVEMENT
    // ========================================================================
    
    void HandleMovement()
    {
        if (player == null) return;
        if (!cc.enabled) return;

        // Always push away from nearby enemies regardless of combat state — prevents stacking.
        ApplySeparation();

        // Block everything when not in Normal state (hitstun, airborne, crash, prone, get-up, dying).
        // CanAct reads CurrentState which is the priority-ordered interrupt check — one gate for all behavior.
        if (!CanAct) return;

        EvaluateBehaviorTransition(); // decide what state to be in
        stateMachine.Tick();          // run it
    }

    /*
     * ApplySeparation — push away from nearby enemies to prevent clumping.
     *
     * Uses OverlapSphere to find all colliders within separationRadius, filters to
     * other SimpleEnemyAI instances, then applies a push proportional to how close
     * they are. Closer enemies push harder (inverse falloff within the radius).
     * Runs every frame regardless of combat state so stunned enemies also spread out.
     */
    void ApplySeparation()
    {
        if (separationRadius <= 0f || separationForce <= 0f) return;

        Collider[] nearby = Physics.OverlapSphere(transform.position, separationRadius);
        Vector3 push = Vector3.zero;

        foreach (Collider col in nearby)
        {
            if (col.gameObject == gameObject) continue;
            if (col.GetComponent<SimpleEnemyAI>() == null) continue;

            Vector3 away = transform.position - col.transform.position;
            away.y = 0f;
            float dist = away.magnitude;
            if (dist < 0.001f)
            {
                // Exactly overlapping: push in a random horizontal direction to break the tie
                away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                dist = away.magnitude;
            }

            // Falloff: full force at dist=0, zero force at dist=separationRadius
            float strength = 1f - Mathf.Clamp01(dist / separationRadius);
            push += (away / dist) * strength;
        }

        if (push.sqrMagnitude > 0.0001f)
            cc.Move(push.normalized * separationForce * Time.deltaTime);
    }

    // -------------------------------------------------------------------------
    // THE CENTRAL DECISION POINT
    // -------------------------------------------------------------------------
    // All Chase <-> Standoff transitions are decided here and ONLY here.
    // To add a new behavioral state (e.g. Flee, Search), write its condition
    // below and call stateMachine.Transition(yourBehavior).
    void EvaluateBehaviorTransition()
    {
        // Hold current behavior until the attack animation finishes —
        // switching mid-swing would cut off the hit and look wrong.
        if (EnemyCombat != null && EnemyCombat.IsAttacking) return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f; // horizontal distance only; ignore height
        float dist = toPlayer.magnitude;

        // Hysteresis band prevents flickering at the boundary:
        //   Chase → Standoff at standoffEnterRange (e.g. 4 units)
        //   Standoff → Chase at standoffExitRange  (e.g. 6 units)
        //   Between 4–6: stay in whatever is already active
        if (stateMachine.CurrentBehavior == chaseBehavior && dist <= StandoffEnterRange)
            stateMachine.Transition(standoffBehavior);
        else if (stateMachine.CurrentBehavior == standoffBehavior && dist > StandoffExitRange)
            stateMachine.Transition(chaseBehavior);
    }

    /// <summary>
    /// Rotates toward the player with a short randomized "reaction" delay.
    /// Call this each frame where facing should happen.
    /// </summary>
    public void RotateTowardWithDelay(Vector3 directionToTarget, float speedMultiplier = 1f)
    {
        directionToTarget.y = 0f;
        if (directionToTarget.sqrMagnitude <= 0.001f) return;

        Vector3 normalizedDirection = directionToTarget.normalized;
        float delayMin = Mathf.Max(0f, faceTurnDelayMin);
        float delayMax = Mathf.Max(delayMin, faceTurnDelayMax);

        // If the facing target changed meaningfully, start a new short reaction delay.
        if (Vector3.Dot(lastFaceDirection, normalizedDirection) < 0.995f)
        {
            if (delayMax <= 0f)
            {
                isFaceTurnReady = true;
                faceTurnDelayTimer = 0f;
            }
            else
            {
                isFaceTurnReady = false;
                faceTurnDelayTimer = Random.Range(delayMin, delayMax);
            }
            lastFaceDirection = normalizedDirection;
        }

        if (!isFaceTurnReady)
        {
            faceTurnDelayTimer -= Time.deltaTime;
            if (faceTurnDelayTimer > 0f) return;
            isFaceTurnReady = true;
        }

        Quaternion targetRot = Quaternion.LookRotation(normalizedDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            rotationSpeed * Mathf.Max(0f, speedMultiplier) * Time.deltaTime
        );
    }
    
    // ========================================================================
    // GRAVITY
    // ========================================================================
    
    /*
     * ApplyGravity:
     * 
     * CharacterController doesn't have built-in gravity
     * We simulate it manually with basic kinematics
     * 
     * This keeps enemies grounded and prevents floating
     * 
     * AIRBORNE STATE:
     * When IsAirborne is true (enemy was launched by an attack),
     * gravity is reduced to let the enemy float longer.
     * This enables "juggle" combos where you can hit enemies in the air.
     */
    void ApplyGravity()
    {
        if (!cc.enabled) return;
        // Check if we're airborne (launched by an attack)
        bool isAirborne = health != null && health.IsAirborne;

        // On ground and not airborne: stick to floor with a small downward velocity
        if (cc.isGrounded && velocity.y < 0f && !isAirborne)
        {
            // Small negative value keeps us "pressed" to ground
            // Only reset if NOT airborne (otherwise we'd cancel the launch)
            velocity.y = -2f;
        }

        /*
         * Apply gravity:
         *
         * Normal: Full gravity pulls enemy down
         * Airborne: Reduced gravity (30%) lets enemy float for combos
         *
         * The reduced gravity while airborne creates that "floaty"
         * feeling when enemies are launched, perfect for juggle combos
         */
        float gravityMultiplier = isAirborne ? 0.3f : 1f;
        velocity.y += gravity * gravityMultiplier * Time.deltaTime;

        // CharacterController movement: vertical component only (horizontal is from behaviors via CC)
        cc.Move(velocity * Time.deltaTime);
    }
    
}

/*
 * ============================================================================
 * SUMMARY: HOW ALL THESE SCRIPTS WORK TOGETHER
 * ============================================================================
 * 
 * PLAYER SYSTEMS:
 * ---------------
 * PlayerController.cs
 *   └── Reads input, moves player, applies gravity
 *   └── Uses CharacterController for collision
 *   └── Checks LockOnSystem for rotation behavior
 * 
 * LockOnSystem.cs
 *   └── Finds nearby enemies (Physics.OverlapSphere)
 *   └── Tracks current target
 *   └── Used by PlayerController (rotation) and ThirdPersonCamera (look target)
 * 
 * Combat.cs
 *   └── Reads attack input
 *   └── Creates hitbox (Physics.OverlapSphere)
 *   └── Calls IDamageable.TakeHit() on hit targets
 * 
 * CAMERA:
 * -------
 * ThirdPersonCamera.cs
 *   └── Follows player position
 *   └── Looks at player or lock-on target
 *   └── Runs in LateUpdate (after player moves)
 * 
 * ENEMY SYSTEMS:
 * --------------
 * EnemyHealth.cs
 *   └── Tracks HP, implements IDamageable
 *   └── Receives damage (TakeHit)
 *   └── Applies knockback physics
 *   └── Handles death (deactivate)
 * 
 * SimpleEnemyAI.cs (this script)
 *   └── Manages behavior system (current behavior + transitions)
 *   └── Delegates movement/decisions to current EnemyBehavior
 *   └── Pauses all behaviors when in hitstun (EnemyHealth.IsHitstunned)
 *   └── Handles animation blending and gravity
 * 
 * EnemyBehavior.cs (abstract base)
 *   └── ChaseBehavior: move toward player
 *   └── StandoffBehavior: circle player, attack from range
 * 
 * EnemyCombat.cs
 *   └── Enemy attack execution using AttackData
 *   └── Creates hitbox, deals damage via IDamageable
 *   └── Handles attack lunge and animation
 * 
 * DATA FLOW EXAMPLE - Enemy attacks player:
 * 1. SimpleEnemyAI runs StandoffBehavior (enemy is circling)
 * 2. StandoffBehavior's attack timer expires
 * 3. Transitions to PreAttack (telegraph pause)
 * 4. After telegraph, calls EnemyCombat.DoAttack()
 * 5. EnemyCombat creates hitbox sphere in front of enemy
 * 6. Physics.OverlapSphere finds player collider
 * 7. EnemyCombat calls IDamageable.TakeHit() on player
 * 8. StandoffBehavior waits for IsAttacking to end
 * 9. Resumes circling with new random attack timer
 * 
 * ============================================================================
 */
