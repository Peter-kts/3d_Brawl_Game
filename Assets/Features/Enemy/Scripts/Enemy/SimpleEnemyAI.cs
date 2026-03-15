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
    
    // --- Standoff: when to switch to circling and how to circle ---
    [Header("Behavior - Standoff")]
    [Tooltip("Distance at which enemy stops chasing and begins circling the target")]
    public float standoffEnterRange = 4f;
    
    [Tooltip("Distance at which enemy stops circling and resumes chasing (should be > standoffEnterRange to prevent flickering)")]
    public float standoffExitRange = 6f;
    
    [Tooltip("Preferred distance to maintain while circling the target")]
    public float standoffRadius = 3f;
    
    [Tooltip("Movement speed while circling (typically slower than chase speed)")]
    public float circleSpeed = 2.5f;
    
    [Tooltip("Minimum time between direction changes while circling")]
    public float directionChangeIntervalMin = 1.5f;
    
    [Tooltip("Maximum time between direction changes while circling")]
    public float directionChangeIntervalMax = 4f;
    
    // --- Attack from standoff: timing and telegraph ---
    [Header("Behavior - Attack")]
    [Tooltip("Minimum time between enemy attacks from standoff")]
    public float attackIntervalMin = 1.5f;
    
    [Tooltip("Maximum time between enemy attacks from standoff")]
    public float attackIntervalMax = 4f;
    
    [Tooltip("Brief pause before attacking (telegraph for player to react)")]
    public float attackTelegraphDuration = 0.2f;

    // --- Dodge punish: special attack when player just dodged (range/time window) ---
    [Header("Attack - Dodge Punish")]
    [Tooltip("Use dodge-punish attack when player dodged within this many seconds")]
    public float dodgePunishWindow = 0.6f;
    [Tooltip("Use dodge-punish attack only when distance to player is at least this")]
    public float dodgePunishDistMin = 2.5f;
    [Tooltip("Use dodge-punish attack only when distance to player is at most this")]
    public float dodgePunishDistMax = 6f;
    
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
    [Header("Prone (after crash landing)")]
    [Tooltip("Seconds the enemy lies prone on the floor after crash before get-up starts. 0 = get-up starts immediately.")]
    public float groundedDuration = 1f;
    [Tooltip("Seconds to blend into the prone animation (0 = snap immediately).")]
    public float proneTransitionDuration = 0.08f;

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

    private EnemyBehavior currentBehavior;      // Active behavior — Execute() called every frame; switched by distance (chase↔standoff)
    private ChaseBehavior chaseBehavior;        // Pre-allocated; reused every time the enemy enters the chase state
    private StandoffBehavior standoffBehavior;  // Pre-allocated; reused every time the enemy enters the standoff state
    
    // ========================================================================
    // PUBLIC PROPERTIES (accessed by behaviors and debug visuals)
    // ========================================================================
    
    /// <summary>Current behavior state name for debug visualization (Chase, Circling, PreAttack, Attacking).</summary>
    public string CurrentBehaviorStateName => currentBehavior?.CurrentStateName ?? "Unknown";
    
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
            if (health.IsStunned) return EnemyState.Stunned;
            // Get-up after crash
            if (health.IsGettingUp) return EnemyState.GettingUp;
            return EnemyState.Normal;
        }
    }

    /// <summary>True when the enemy can run behavior (chase, standoff, attack). False when health is missing or during stun, airborne, crash, get-up, or dying.</summary>
    public bool CanAct => health != null && CurrentState == EnemyState.Normal;
    
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
        
        // Create behaviors and start in Chase (transitions to Standoff when in range)
        chaseBehavior = new ChaseBehavior(this);
        standoffBehavior = new StandoffBehavior(this);
        currentBehavior = chaseBehavior;
        currentBehavior.Enter();  // Let Chase initialize if it has entry logic

        if (animationConfig == null)
            Debug.LogError($"SimpleEnemyAI on '{name}': animationConfig is not assigned. Create an EnemyAnimationConfig asset (Assets > Create > Enemy > Animation Config) and assign it.", this);

        // Prone system: owns the prone timer/animation; fires TriggerGetUpSequence when timer expires.
        // Created before airborneSequence so the lambda below can close over it.
        string proneState    = animationConfig != null ? animationConfig.proneStateName : "";
        int    proneLayerIdx = animationConfig != null ? animationConfig.proneLayer     : 1;
        proneSystem = new EnemyProneSystem(transform, animator, proneState, proneLayerIdx, proneTransitionDuration, health, TriggerGetUpSequence);

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
                animator.SetBool(animationConfig.stunLayerParameter, (!inAirbornePhase && !health.IsAirborne && health.IsStunned) || inProne || health.IsGettingUp);

            // When not in stun/airborne/prone/get-up, ensure hit layer plays at 1x (TriggerHitAnimation sets it when hit)
            if (!inAirbornePhase && !health.IsStunned && !inProne && !health.IsGettingUp && !string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
                animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
        }
    }

    /// <summary>
    /// Triggers the hit reaction animation. Called by EnemyHealth when taking damage.
    /// Prefers height-specific states (Hit_High / Hit_Mid / Hit_Low), then falls back to hitStateName or random hitStateNames.
    /// The Stun layer must NOT have an Any State transition for the stun bool, or that transition would override this and always show one state.
    /// </summary>
    /// <param name="hitstun">Duration of the hitstun; animation is scaled to match.</param>
    public void TriggerHitAnimation(float hitstun, AttackHeight height)
    {
        if (animator == null || animationConfig == null) return;

        // If the enemy is prone, use the prone-specific hit state (if configured and exists in the controller).
        bool currentlyProne = proneSystem != null && proneSystem.IsInProne;
        if (currentlyProne
            && !string.IsNullOrEmpty(animationConfig.proneHitStateName)
            && AnimatorHasStateOnLayer(animator, animationConfig.hitAnimationLayer, animationConfig.proneHitStateName))
        {
            animator.Play(animationConfig.proneHitStateName, animationConfig.hitAnimationLayer, 0f);
            return;
        }

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
        // --------------------------------------------------------------------
        // STEP 1: Validate state - can we act?
        // --------------------------------------------------------------------
        
        // No player reference? Can't do anything
        if (player == null) return;
        if (!cc.enabled) return;
        
        // Block all behavior when not in Normal state (stun, airborne, crash, get-up, dying)
        if (!CanAct) return;

        // --------------------------------------------------------------------
        // STEP 2: Check behavior transitions (distance-based)
        // --------------------------------------------------------------------
        
        /*
         * Transition logic with HYSTERESIS:
         * 
         * standoffEnterRange < standoffExitRange prevents rapid flickering:
         *   - Chase -> Standoff at 4 units
         *   - Standoff -> Chase at 6 units
         *   - Between 4-6 units: stays in current behavior
         * 
         * Don't transition mid-attack (enemy should finish attacking first)
         */
        bool midAttack = EnemyCombat != null && EnemyCombat.IsAttacking;
        
        if (!midAttack)
        {
            // Horizontal distance only (ignore height difference)
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            // Enter standoff when close enough; leave when far enough (hysteresis band between enter/exit)
            if (currentBehavior == chaseBehavior && dist <= standoffEnterRange)
            {
                SetBehavior(standoffBehavior);
            }
            else if (currentBehavior == standoffBehavior && dist > standoffExitRange)
            {
                SetBehavior(chaseBehavior);
            }
        }

        // --------------------------------------------------------------------
        // STEP 3: Execute current behavior
        // --------------------------------------------------------------------
        
        /*
         * The current behavior handles all movement/decision logic:
         *   - ChaseBehavior: move toward player, rotate to face them
         *   - StandoffBehavior: circle player, occasionally attack
         */
        currentBehavior?.Execute();
    }
    
    /// <summary>
    /// Switch to a new behavior. Calls Exit() on old and Enter() on new.
    /// </summary>
    public void SetBehavior(EnemyBehavior newBehavior)
    {
        if (currentBehavior == newBehavior) return;
        currentBehavior?.Exit();   // Cleanup (e.g. reset timers)
        currentBehavior = newBehavior;
        currentBehavior?.Enter(); // Initialize (e.g. pick next attack time)
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
 *   └── Pauses all behaviors when stunned (EnemyHealth.IsStunned)
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
