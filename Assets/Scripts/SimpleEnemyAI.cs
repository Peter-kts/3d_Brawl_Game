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
    // SERIALIZED FIELDS
    // ========================================================================
    
    [Header("Movement")]
    [Tooltip("Reference to the player's Transform. Auto-finds by 'Player' tag if not assigned.")]
    public Transform player;
    
    [Tooltip("How fast the enemy moves (units per second). Slower than player so they can escape.")]
    public float moveSpeed = 3.5f;
    
    [Tooltip("Rotation speed in degrees per second")]
    public float rotationSpeed = 540f;
    
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
    
    [Header("Behavior - Attack")]
    [Tooltip("Minimum time between enemy attacks from standoff")]
    public float attackIntervalMin = 1.5f;
    
    [Tooltip("Maximum time between enemy attacks from standoff")]
    public float attackIntervalMax = 4f;
    
    [Tooltip("Brief pause before attacking (telegraph for player to react)")]
    public float attackTelegraphDuration = 0.2f;

    [Header("Attack - Dodge Punish")]
    [Tooltip("Use dodge-punish attack when player dodged within this many seconds")]
    public float dodgePunishWindow = 0.6f;
    [Tooltip("Use dodge-punish attack only when distance to player is at least this")]
    public float dodgePunishDistMin = 2.5f;
    [Tooltip("Use dodge-punish attack only when distance to player is at most this")]
    public float dodgePunishDistMax = 6f;
    
    [Header("Physics")]
    [Tooltip("Gravity applied to the enemy (should match player's gravity)")]
    public float gravity = -20f;
    
    [Header("Animation")]
    [Tooltip("Animator for locomotion. Auto-finds on this object or children if not set.")]
    public Animator animator;
    
    [Tooltip("Animator parameter name for movement speed (0 = idle, 1 = full speed)")]
    public string speedParameter = "Speed";
    
    [Tooltip("How quickly the animation speed blends (higher = snappier)")]
    public float animationDamping = 10f;
    
    [Tooltip("Animator state name for the hit reaction animation (used with animator.Play to force-snap pose before hit stop)")]
    public string hitStateName = "Stunned";
    
    [Tooltip("Animator layer index for the hit reaction animation (0 = Base Layer, 1 = Stun layer, etc.)")]
    public int hitAnimationLayer = 1;
    
    [Tooltip("Animator parameter name for hit animation speed multiplier")]
    public string hitSpeedParameter = "HitSpeed";
    
    [Tooltip("Fallback base duration (seconds) when auto-detect fails. Hit state length is auto-fetched from the Animator and cached; use this if a state has no motion or to override.")]
    public float baseHitAnimDuration = 0.4f;
    
    [Tooltip("Optional: multiple hit reaction state names. If set, one is chosen at random (never the same twice in a row). Leave empty to use hitStateName only.")]
    public string[] hitStateNames;
    
    [Tooltip("Animator trigger name for death animation")]
    public string deathTriggerParameter = "Death";
    
    [Tooltip("Animator parameter name for airborne state (bool)")]
    public string airborneParameter = "IsAirborne";

    [Tooltip("Animator bool for stun (hitstun or get-up). Drive Stunned state entry/exit from this in the controller, not Speed. Must match parameter name in Animator (e.g. 'IsStunned 0' in enemy.controller).")]
    public string stunParameter = "IsStunned 0";

    /*
     * SIMPLE CRASH (non-Liftoff/Loop/Crash path):
     * 
     * These fields provide a simpler alternative when you DON'T want the full
     * three-phase airborne animation system. When IsAirborne ends:
     *   - Force-play the crash state (e.g. a separate landing clip)
     *   - Wait crashStateDuration seconds for it to finish
     *   - Then trigger get-up
     * 
     * This is SKIPPED when airborneAnimation.IsConfigured is true,
     * because the Liftoff/Loop/Crash system handles crash internally.
     */
    [Tooltip("Optional: animator state name for crash/land when NOT using Liftoff/Loop/Crash. When airborne ends, this state is forced so the looping section can be interrupted even before one full loop. Leave empty to rely on animator transitions only. Default 'New State' matches enemy.controller Stun layer crash state.")]
    public string airborneCrashStateName = "Crash";
    [Tooltip("Animator layer index for airborne crash state (1 = Stun layer if using default enemy setup).")]
    public int airborneCrashLayer = 1;
    [Tooltip("When NOT using Liftoff/Loop/Crash: time playing crash state (seconds). Get-up delay is added so total crash phase = this + getUpDelay.")]
    public float crashStateDuration = 0.5f;

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
     * 
     * Leave airborneStateName EMPTY to disable this system entirely and
     * fall back to the simple crash path above.
     */
    [Header("Airborne Animation (Liftoff / Loop / Crash)")]
    [Tooltip("Settings for splitting a single airborne animation into liftoff, loop, and crash phases. Leave airborneStateName empty to disable.")]
    public AirborneAnimationSettings airborneAnimation = new AirborneAnimationSettings();

    /*
     * GET-UP SYSTEM:
     * 
     * After the crash animation finishes (either from the simple crash path
     * or the Liftoff/Loop/Crash system), the enemy plays a get-up animation.
     * 
     * Timeline:
     *   [Crash ends] → [getUpDelay pause] → [get-up animation plays] → [enemy resumes AI]
     * 
     * The enemy stays stunned for the full getUpDuration so they can't
     * act while still getting off the ground.
     */
    [Header("Get Up (after airborne crash)")]
    [Tooltip("Added to crash phase (PATH A) so character holds crash/land pose this long before get-up. Get-up animation starts as soon as crash phase ends.")]
    public float getUpDelay = 0.5f;
    [Tooltip("Duration the enemy is stunned while getting up (and length the get-up animation is scaled to).")]
    public float getUpDuration = 1.5f;
    [Tooltip("Animator state name for get-up animation (stunned while getting up). Leave empty to skip get-up animation.")]
    public string getUpStateName = "GetUp";
    [Tooltip("Animator layer index for get-up state (e.g. 1 = Stun layer).")]
    public int getUpLayer = 1;
    [Tooltip("Base duration of get-up clip (seconds). Speed is scaled so animation matches getUpDuration. Ignored if 0.")]
    public float baseGetUpAnimDuration = 1.2f;

    // ========================================================================
    // PRIVATE REFERENCES
    // ========================================================================
    
    private CharacterController cc;   // For collision-aware movement
    private EnemyHealth health;       // Reference to our health component
    private Vector3 velocity;         // Tracks vertical velocity for gravity
    
    // Animation state (Speed parameter driven by behaviors, smoothed here)
    /// <summary>Smoothed value sent to the Animator each frame. Lerps toward targetAnimSpeed so transitions aren't instant.</summary>
    private float currentAnimSpeed;
    /// <summary>Desired speed for this frame: 0 = idle, 0.5 = circling/strafe, 1 = full run. Set by current behavior in HandleMovement; reset to 0 at start of FixedUpdate.</summary>
    private float targetAnimSpeed;
    private int lastHitStateIndex = -1;  // Last played index in hitStateNames (-1 when using single hitStateName)
    private Dictionary<string, float> cachedHitStateDurations;  // Auto-fetched from Animator per state name
    private int cachedAirborneStateNameHash;  // Hash of airborne state name for fast per-frame check (0 = not set)

    /*
     * Airborne animation phase state machine:
     * 
     * AirbornePhase tracks which portion of the animation is currently playing:
     *   None    = not in airborne animation (normal behavior)
     *   Liftoff = playing the initial hit/launch portion of the clip
     *   Loop    = repeating the mid-air spinning portion
     *   Crash   = playing the landing/slam portion after IsAirborne ended
     * 
     * wasAirborne detects rising/falling edges of health.IsAirborne:
     *   - Rising edge  (false → true):  start Liftoff phase
     *   - Falling edge (true → false):  start Crash phase
     * 
     * wasAirborne is updated only in UpdateAnimator() so both paths use the same edge detection.
     */
    private enum AirbornePhase { None, Liftoff, Loop, Crash }
    private AirbornePhase airbornePhase = AirbornePhase.None;
    private bool wasAirborne;  // Updated only in UpdateAnimator(); used for airborne rising/falling edge in both paths
    private float animatorSpeedBeforeGetUpFreeze = 1f;      // Saved animator speed before get-up delay freeze, restored when get-up starts

    // Behavior system
    private EnemyBehavior currentBehavior;
    private ChaseBehavior chaseBehavior;
    private StandoffBehavior standoffBehavior;
    
    // ========================================================================
    // PUBLIC PROPERTIES (accessed by behaviors and debug visuals)
    // ========================================================================
    
    /// <summary>Current behavior state name for debug visualization (Chase, Circling, PreAttack, Attacking).</summary>
    public string CurrentBehaviorStateName => currentBehavior?.CurrentStateName ?? "Unknown";
    
    /// <summary>CharacterController for collision-aware movement.</summary>
    public CharacterController CC => cc;
    
    /// <summary>Enemy health component (for stun/airborne checks).</summary>
    public EnemyHealth Health => health;

    /// <summary>
    /// Current logical state (Dying &gt; Airborne &gt; Crashed &gt; Stunned &gt; GettingUp &gt; Normal).
    /// Single source of truth so callers don't duplicate the priority order. Combines EnemyHealth timers and airborne phase.
    /// </summary>
    public EnemyState CurrentState
    {
        get
        {
            if (health == null) return EnemyState.Normal;
            if (health.IsDying) return EnemyState.Dying;
            if (health.IsAirborne || (airborneAnimation.IsConfigured && airbornePhase != AirbornePhase.None && airbornePhase != AirbornePhase.Crash))
                return EnemyState.Airborne;
            if (InAirborneCrash) return EnemyState.Crashed;
            if (health.IsStunned) return EnemyState.Stunned;
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
        if (animator == null) animator = GetComponentInChildren<Animator>();
        
        // Find EnemyCombat component (optional - enemies work without it, just can't attack)
        EnemyCombat = GetComponent<EnemyCombat>();
        
        // Create behaviors and start in Chase
        chaseBehavior = new ChaseBehavior(this);
        standoffBehavior = new StandoffBehavior(this);
        currentBehavior = chaseBehavior;
        currentBehavior.Enter();
    }

    void Update()
    {
        ApplyGravity();
        UpdateAirborneAnimation();
        UpdateAnimator();
        // While dying (including airborne-as-death): skip movement/behavior
        if (health != null && health.IsDying) return;
        // Default: no movement. Behaviors set TargetAnimSpeed inside HandleMovement (chase=1, standoff=0.5 or 0).
        targetAnimSpeed = 0f;
        HandleMovement();
    }
    
    // ========================================================================
    // AIRBORNE ANIMATION (Liftoff → Loop → Crash)
    // ========================================================================
    
    /*
     * UpdateAirborneAnimation:
     * Drives a three-phase animation from a single clip when the enemy is
     * launched airborne. Configured via the public airborneAnimation settings.
     * 
     * Phases:
     *   1. Liftoff: plays once from liftoffStart to liftoffEnd
     *   2. Loop:    repeats between loopStart and loopEnd while IsAirborne
     *   3. Crash:   plays once from crashStart when IsAirborne ends
     * 
     * If airborneAnimation is not configured (empty state name), the system
     * falls back to the existing IsAirborne bool parameter on the Animator.
     */
    void UpdateAirborneAnimation()
    {
        // Only runs when the Liftoff/Loop/Crash system is configured
        if (animator == null || health == null) return;
        if (!airborneAnimation.IsConfigured) return;
        
        bool isAirborne = health.IsAirborne;
        
        /*
         * RISING EDGE: IsAirborne just turned true (enemy was just launched).
         * 
         * Start the Liftoff phase by playing the animator state from liftoffStart.
         * Uses crossfade if configured, so the transition from the hit reaction
         * into the airborne animation is smooth rather than a hard cut.
         */
        if (isAirborne && !wasAirborne)
        {
            airbornePhase = AirbornePhase.Liftoff;
            // No speed scaling for airborne: play at 1x so liftoff/loop/crash use natural timing.
            if (!string.IsNullOrEmpty(hitSpeedParameter))
                animator.SetFloat(hitSpeedParameter, 1f);
            
            if (airborneAnimation.crossfadeDuration > 0f)
            {
                animator.CrossFadeInFixedTime(
                    airborneAnimation.airborneStateName,
                    airborneAnimation.crossfadeDuration,
                    airborneAnimation.airborneAnimationLayer,
                    airborneAnimation.liftoffStart);
            }
            else
            {
                animator.Play(
                    airborneAnimation.airborneStateName,
                    airborneAnimation.airborneAnimationLayer,
                    airborneAnimation.liftoffStart);
            }
        }
        
        /*
         * FALLING EDGE: IsAirborne just turned false (airborne duration expired).
         * 
         * Start the Crash phase by jumping to crashStart in the same animation.
         * This is a hard Play() (no crossfade) because the loop and crash are
         * adjacent portions of the same clip — a blend would look wrong.
         */
        if (!isAirborne && wasAirborne && airbornePhase != AirbornePhase.None)
        {
            airbornePhase = AirbornePhase.Crash;
            animator.Play(
                airborneAnimation.airborneStateName,
                airborneAnimation.airborneAnimationLayer,
                airborneAnimation.crashStart);
        }
        
        // Nothing to do if we're not in any airborne phase
        if (airbornePhase == AirbornePhase.None) return;
        
        /*
         * PER-FRAME PHASE LOGIC:
         * 
         * Read the current normalizedTime (0-1) from the Animator to decide
         * when to transition between phases or loop back. Only use it when we're
         * actually in the airborne state (avoids wrong timing if layer is in transition).
         * Use shortNameHash for the check to avoid string comparison every frame.
         */
        if (cachedAirborneStateNameHash == 0)
            cachedAirborneStateNameHash = Animator.StringToHash(airborneAnimation.airborneStateName);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(airborneAnimation.airborneAnimationLayer);
        if (stateInfo.shortNameHash != cachedAirborneStateNameHash)
            return;
        float normalizedTime = stateInfo.normalizedTime;
        
        switch (airbornePhase)
        {
            case AirbornePhase.Liftoff:
                /*
                 * Liftoff plays once. When the clip reaches liftoffEnd,
                 * jump to the loop portion by playing from loopStart.
                 */
                if (normalizedTime >= airborneAnimation.liftoffEnd)
                {
                    airbornePhase = AirbornePhase.Loop;
                    animator.Play(
                        airborneAnimation.airborneStateName,
                        airborneAnimation.airborneAnimationLayer,
                        airborneAnimation.loopStart);
                }
                break;
                
            case AirbornePhase.Loop:
                /*
                 * Loop repeats indefinitely while IsAirborne is true.
                 * Each frame, if the clip has reached loopEnd, jump back
                 * to loopStart. This creates a seamless spin cycle.
                 * 
                 * The loop exits when IsAirborne goes false (falling edge
                 * above sets phase to Crash).
                 */
                if (normalizedTime >= airborneAnimation.loopEnd)
                {
                    animator.Play(
                        airborneAnimation.airborneStateName,
                        airborneAnimation.airborneAnimationLayer,
                        airborneAnimation.loopStart);
                }
                break;
                
            case AirbornePhase.Crash:
                /*
                 * Crash plays once to completion. When it finishes: clear the phase (so GetAirborneForAnimator goes false
                 * and the Animator can leave the airborne state), then use the same "crash just ended" handler as PATH A
                 * so get-up or death completion is handled in one place.
                 */
                if (normalizedTime >= airborneAnimation.crashEnd)
                {
                    airbornePhase = AirbornePhase.None;
                    cachedAirborneStateNameHash = 0;  // Invalidate so state name changes are picked up next time
                    if (health != null)
                        OnAirborneCrashFinished(health.IsDying);
                }
                break;
        }
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
        float t = 1f - Mathf.Exp(-animationDamping * Time.deltaTime);
        currentAnimSpeed = Mathf.Lerp(currentAnimSpeed, targetAnimSpeed, t);

        // Avoid drift: when we're aiming for 0 and very close, clamp to exactly 0.
        if (currentAnimSpeed < 0.001f && targetAnimSpeed == 0f)
            currentAnimSpeed = 0f;

        // Send to Animator. During airborne (liftoff/loop/crash) use 1 so the spin animation isn't slowed by locomotion Speed.
        float speedToApply = (airbornePhase != AirbornePhase.None) ? 1f : currentAnimSpeed;
        animator.SetFloat(speedParameter, speedToApply);
        
        /*
         * AIRBORNE BOOL PARAMETER (Animator "IsAirborne"):
         * - PATH A (simple crash): Bool follows health.IsAirborne. When it goes false we force-play the crash state and
         *   call health.StartCrashPhase(crashStateDuration). EnemyHealth then calls us back via OnCrashPhaseComplete when
         *   crashUntil expires, so we don't need a separate timer here.
         * - PATH B (Liftoff/Loop/Crash): Bool must stay true until airbornePhase is None, so the Animator stays in the
         *   airborne state through the Crash phase. health.IsAirborne is already false by then; GetAirborneForAnimator()
         *   keeps the bool true via the (airbornePhase != AirbornePhase.None) term.
         */
        if (health != null)
        {
            bool isAirborne = health.IsAirborne;
            
            // PATH A only: falling edge of IsAirborne (airborne duration expired). Force-play the crash/land state on the
            // Stun layer and start the crash phase in health; health will call OnCrashPhaseComplete() when crashStateDuration elapses.
            if (wasAirborne && !isAirborne && !string.IsNullOrEmpty(airborneCrashStateName) && !airborneAnimation.IsConfigured)
            {
                animator.Play(airborneCrashStateName, airborneCrashLayer, 0f);
                health.StartCrashPhase(crashStateDuration + getUpDelay);
            }
            
            animator.SetBool(airborneParameter, GetAirborneForAnimator());
            // Single place for edge detection: both PATH A and PATH B use this for rising/falling edge of IsAirborne.
            wasAirborne = isAirborne;

            // Stun: drive from game state. Keep false during Liftoff/Loop/Crash so the crash animation isn't interrupted by a transition to Stunned.
            if (!string.IsNullOrEmpty(stunParameter))
                animator.SetBool(stunParameter, (airbornePhase == AirbornePhase.None && !health.IsAirborne && health.IsStunned) || health.IsGettingUp);

            // Reset HitSpeed to 1 when not in a hit/get-up/airborne flow so regular stuns always get a clean scale next time.
            if (airbornePhase == AirbornePhase.None && !health.IsStunned && !health.IsGettingUp && !string.IsNullOrEmpty(hitSpeedParameter))
                animator.SetFloat(hitSpeedParameter, 1f);
        }
    }

    /// <summary>
    /// Value to drive the Animator's IsAirborne bool. True when: (1) health says we're airborne, or (2) we're in
    /// Liftoff/Loop/Crash and still playing the airborne clip (including Crash phase, so we don't transition away early).
    /// </summary>
    bool GetAirborneForAnimator()
    {
        return health != null && (health.IsAirborne || (airborneAnimation.IsConfigured && airbornePhase != AirbornePhase.None));
    }

    /// <summary>
    /// True when the enemy is in the "crashed" state (landed from airborne, playing crash/land animation, cannot act).
    /// Unifies PATH A (health.IsCrashed, from StartCrashPhase) and PATH B (airbornePhase == Crash) for behavior blocking.
    /// </summary>
    bool InAirborneCrash => (airbornePhase == AirbornePhase.Crash) || (health != null && health.IsCrashed);

    /// <summary>
    /// Triggers the hit reaction animation. Called by EnemyHealth when taking damage.
    /// Plays the chosen state (hitStateName or random from hitStateNames) and scales speed to match hitstun.
    /// The Stun layer must NOT have an Any State transition for the stun bool, or that transition would override this and always show one state.
    /// </summary>
    /// <param name="hitstun">Duration of the hitstun; animation is scaled to match.</param>
    public void TriggerHitAnimation(float hitstun)
    {
        if (animator != null)
        {
            string stateToPlay;
            if (hitStateNames != null && hitStateNames.Length > 0)
            {
                int chosenIndex;
                do
                {
                    chosenIndex = Random.Range(0, hitStateNames.Length);
                }
                while (hitStateNames.Length >= 2 && chosenIndex == lastHitStateIndex);
                lastHitStateIndex = chosenIndex;
                stateToPlay = hitStateNames[chosenIndex];
            }
            else
            {
                lastHitStateIndex = -1;
                stateToPlay = hitStateName;
            }

            // Play at 1x first so we can read the state length (when not cached), then scale to match hitstun.
            if (!string.IsNullOrEmpty(hitSpeedParameter))
                animator.SetFloat(hitSpeedParameter, 1f);
            animator.Play(stateToPlay, hitAnimationLayer, 0f);
            animator.Update(0f);

            // Use cached duration when available to avoid GetCurrentAnimatorStateInfo every hit.
            float baseDuration = baseHitAnimDuration;
            if (cachedHitStateDurations != null && cachedHitStateDurations.TryGetValue(stateToPlay, out float cached))
                baseDuration = cached;
            else
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(hitAnimationLayer);
                if (stateInfo.IsName(stateToPlay) && stateInfo.length > 0f)
                {
                    if (cachedHitStateDurations == null)
                        cachedHitStateDurations = new Dictionary<string, float>();
                    cachedHitStateDurations[stateToPlay] = stateInfo.length;
                    baseDuration = stateInfo.length;
                }
            }

            if (!string.IsNullOrEmpty(hitSpeedParameter) && baseDuration > 0f)
            {
                float speed = (hitstun > 0.001f) ? (baseDuration / hitstun) : 1f;
                animator.SetFloat(hitSpeedParameter, speed);
            }
            else if (!string.IsNullOrEmpty(hitSpeedParameter))
                animator.SetFloat(hitSpeedParameter, 1f);
        }
    }
    
    /// <summary>
    /// Freezes the animator on the current frame (e.g. last frame of crash). Called by EnemyHealth when get-up delay starts.
    /// Unfreeze happens when TriggerGetUpAnimation is called.
    /// </summary>
    public void FreezeAnimatorForGetUpDelay()
    {
        if (animator != null)
        {
            animatorSpeedBeforeGetUpFreeze = animator.speed;
            animator.speed = 0f;
        }
    }

    /// <summary>
    /// Triggers the get-up animation after landing from airborne. Called from OnAirborneCrashFinished when crash phase ends.
    /// </summary>
    public void TriggerGetUpAnimation(float duration)
    {
        if (animator == null || string.IsNullOrEmpty(getUpStateName)) return;
        animator.speed = animatorSpeedBeforeGetUpFreeze;
        if (!string.IsNullOrEmpty(hitSpeedParameter))
            animator.SetFloat(hitSpeedParameter, 1f);
        animator.Play(getUpStateName, getUpLayer, 0f);
    }

    /// <summary>
    /// Called by EnemyHealth when the crash phase ends (PATH A only: crashUntil just expired after StartCrashPhase).
    /// This is the single entry point for "airborne crash just finished" from the health side; we delegate to
    /// OnAirborneCrashFinished so both PATH A (here) and PATH B (from UpdateAirborneAnimation when Crash phase hits crashEnd) use the same logic.
    /// </summary>
    /// <param name="isDying">True if the enemy was killed by the airborne attack; we complete death instead of get-up.</param>
    public void OnCrashPhaseComplete(bool isDying)
    {
        OnAirborneCrashFinished(isDying);
    }

    /// <summary>
    /// Shared handler for "airborne crash sequence just finished": either start get-up (and trigger get-up anim immediately)
    /// or complete death if the enemy was killed by the launch. Called from OnCrashPhaseComplete (PATH A) and from
    /// UpdateAirborneAnimation when normalizedTime >= crashEnd (PATH B).
    /// </summary>
    void OnAirborneCrashFinished(bool isDying)
    {
        if (isDying)
            health.OnAirborneSequenceComplete();
        else
        {
            health.StartGetUp(0f, getUpDuration);
            TriggerGetUpAnimation(getUpDuration);
        }
    }

    /// <summary>
    /// Triggers the death animation. Called by EnemyHealth when HP reaches 0.
    /// The actual disable happens when OnDeathAnimationComplete() is called via Animation Event.
    /// </summary>
    public void TriggerDeathAnimation()
    {
        if (animator != null)
        {
            animator.SetTrigger(deathTriggerParameter);
        }
    }
    
    /// <summary>
    /// Called by Animation Event at the end of the death animation.
    /// Disables logic but keeps the mesh visible (corpse stays).
    /// </summary>
    public void OnDeathAnimationComplete()
    {
        if (health != null)
            health.CompleteDeath();
        else
            gameObject.SetActive(false);
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
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            
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
        currentBehavior?.Exit();
        currentBehavior = newBehavior;
        currentBehavior?.Enter();
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
        // Check if we're airborne (launched by an attack)
        bool isAirborne = health != null && health.IsAirborne;
        
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
