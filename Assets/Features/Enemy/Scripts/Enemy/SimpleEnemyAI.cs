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
 * FILE LAYOUT (partial class):
 * ----------------------------
 * SimpleEnemyAI.cs           - fields, properties, Awake/Update (this file)
 * SimpleEnemyAI.Animation.cs - per-frame animator driving + knockback-stun facing
 * SimpleEnemyAI.Reactions.cs - hit/thrown/get-up/wall-bounce/death triggers
 * SimpleEnemyAI.Movement.cs  - behavior tick/transitions, steering, rotation, gravity
 * SimpleEnemyAI.Targeting.cs - team-based retargeting
 * 
 * ============================================================================
 */

using UnityEngine;

/*
 * [RequireComponent]:
 * 
 * This attribute tells Unity: "This script NEEDS a CharacterController"
 * When you add this script to a GameObject, Unity auto-adds CharacterController
 * Prevents null reference errors from missing dependencies
 */
[RequireComponent(typeof(CharacterController))]
public partial class SimpleEnemyAI : MonoBehaviour
{
    // ========================================================================
    // SERIALIZED FIELDS (tweak in Inspector; many have Tooltips for details)
    // ========================================================================
    
    // --- Movement: who we chase and how we move/rotate ---
    [Header("Movement")]
    [Tooltip("Reference to the player's Transform. Auto-finds by 'Player' tag if not assigned.")]
    public Transform player;

    [Tooltip("Which team this AI will hunt. Leave as Player for standard enemies. Set to Enemy for an allied AI that should attack enemies.")]
    public Team targetTeam = Team.Player;

    [Tooltip("Priority weight for the player when scoring targets. 0.5 = player treated as 2x closer (strongly preferred). 1.0 = pure distance. Set higher to make allies more competitive.")]
    [Range(0f, 2f)]
    public float playerPriorityMultiplier = 0.5f;
    
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
    
    private AirborneSequence airborneSequence;  // Liftoff → Loop → Crash; fires onCrashLanded when crash ends; created in Awake
    private EnemyProneSystem proneSystem;       // Prone timer and animation; created in Awake; entered via AirborneSequence.onCrashLanded
    private EnemyReactionStateMachine reactionStateMachine; // Visual-only reaction machine for temporary animation priority (currently knockback-entry)

    private EnemyStunMeter stunMeter;           // Optional; when present, IsStandingStunned blocks behavior and drives animator

    private EnemyStateMachine stateMachine;     // Central behavioral state machine — all Chase↔Standoff transitions go through stateMachine.Transition()
    private ChaseBehavior chaseBehavior;        // Pre-allocated; reused every time the enemy enters the chase state
    private StandoffBehavior standoffBehavior;  // Pre-allocated; reused every time the enemy enters the standoff state
    
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
    // Personality uses higher = higher priority; fallback keeps legacy semantics from playerPriorityMultiplier
    // where lower meant higher priority (e.g. 0.5 ~= strongly prefer player).
    public float PlayerTargetPriorityMultiplier => personality != null
        ? personality.playerTargetPriorityMultiplier
        : (playerPriorityMultiplier > 0f ? 1f / playerPriorityMultiplier : 0f);
    public float AllyTargetPriorityMultiplier   => personality != null ? personality.allyTargetPriorityMultiplier   : 1f;

    /// <summary>Enemy combat component (for attack execution). May be null if not present.</summary>
    public EnemyCombat EnemyCombat { get; private set; }

    /// <summary>Player's controller (for dodge detection). May be null if player not set.</summary>
    public PlayerController PlayerController { get; private set; }

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

        if (player == null)
            player = FindNearestHostileTarget();

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
        UpdateTarget();
        // Default: no movement. Behaviors set TargetAnimSpeed inside HandleMovement (chase=1, standoff=0.5 or 0).
        targetAnimSpeed = 0f;
        HandleMovement();
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
