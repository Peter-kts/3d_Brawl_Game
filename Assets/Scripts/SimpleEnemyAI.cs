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
    
    [Tooltip("Base duration of the hit animation clip (seconds). Used to scale animation to match hitstun.")]
    public float baseHitAnimDuration = 0.4f;
    
    [Tooltip("Optional: multiple hit reaction state names. If set, one is chosen at random (never the same twice in a row). Leave empty to use hitStateName only.")]
    public string[] hitStateNames;
    
    [Tooltip("Animator trigger name for death animation")]
    public string deathTriggerParameter = "Death";
    
    [Tooltip("Animator parameter name for airborne state (bool)")]
    public string airborneParameter = "IsAirborne";

    // ========================================================================
    // PRIVATE REFERENCES
    // ========================================================================
    
    private CharacterController cc;   // For collision-aware movement
    private EnemyHealth health;       // Reference to our health component
    private Vector3 velocity;         // Tracks vertical velocity for gravity
    
    // Animation state
    private float currentAnimSpeed;   // Smoothed animation speed value
    private float targetAnimSpeed;    // Target speed this frame (set by movement)
    private int lastHitStateIndex = -1;  // Last played index in hitStateNames (-1 when using single hitStateName)
    
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
    
    /// <summary>Enemy combat component (for attack execution). May be null if not present.</summary>
    public EnemyCombat EnemyCombat { get; private set; }
    
    /// <summary>
    /// Target animation speed this frame. Set by behaviors:
    /// 0 = idle, 0.5 = strafing/circling, 1 = full run.
    /// Smoothed by UpdateAnimator() for natural transitions.
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
        // While dying: only apply gravity (so they fall), skip everything else
        if (health != null && health.IsDying)
        {
            ApplyGravity();
            return;
        }
        
        // Reset animation target each frame (movement will set it if moving)
        targetAnimSpeed = 0f;
        
        HandleMovement();
        ApplyGravity();
        UpdateAnimator();
    }
    
    // ========================================================================
    // ANIMATION
    // ========================================================================
    
    /*
     * UpdateAnimator:
     * Smoothly blends the animation speed parameter for natural transitions.
     * 
     * Uses the same approach as PlayerController:
     *   - Exponential lerp for framerate-independent smoothing
     *   - Snap to zero when close to prevent floating-point drift
     */
    void UpdateAnimator()
    {
        if (animator == null) return;
        
        // Smooth the animation speed for natural transitions
        currentAnimSpeed = Mathf.Lerp(
            currentAnimSpeed, 
            targetAnimSpeed, 
            1f - Mathf.Exp(-animationDamping * Time.deltaTime)
        );
        
        // Snap to 0 when very close (prevents floating-point drift)
        if (currentAnimSpeed < 0.001f && targetAnimSpeed == 0f)
        {
            currentAnimSpeed = 0f;
        }
        
        // Set animator parameters
        animator.SetFloat(speedParameter, currentAnimSpeed);
        
        // Set airborne state (for floating/falling animation when launched)
        if (health != null)
        {
            animator.SetBool(airborneParameter, health.IsAirborne);
        }
    }
    
    /// <summary>
    /// Triggers the hit reaction animation. Called by EnemyHealth when taking damage.
    /// Uses a trigger (not a bool) so it fires once and doesn't restart every frame.
    /// </summary>
    /// <param name="hitstun">Duration of the hitstun - animation speed is scaled to match this.</param>
    public void TriggerHitAnimation(float hitstun)
    {
        // #region agent log
        try { var tn = (gameObject?.name ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); System.IO.File.AppendAllText(@"c:\Users\peter\3dbrawlerlearn\3dbrawlerlearn\.cursor\debug.log", "{\"location\":\"SimpleEnemyAI.cs:TriggerHitAnimation\",\"message\":\"TriggerHitAnimation\",\"data\":{\"target\":\"" + tn + "\"},\"timestamp\":" + (long)(UnityEngine.Time.realtimeSinceStartup * 1000) + ",\"hypothesisId\":\"H1\"}\n"); } catch { }
        // #endregion
        if (animator != null)
        {
            /*
             * Scale animation speed to match hitstun duration:
             * 
             * If baseHitAnimDuration = 0.4s and hitstun = 0.2s:
             *   speedMultiplier = 0.4 / 0.2 = 2.0 (plays 2x faster)
             * 
             * If baseHitAnimDuration = 0.4s and hitstun = 0.8s:
             *   speedMultiplier = 0.4 / 0.8 = 0.5 (plays at half speed)
             * 
             * This ensures the animation finishes exactly when hitstun ends.
             */
            float speedMultiplier = baseHitAnimDuration / Mathf.Max(hitstun, 0.01f);
            animator.SetFloat(hitSpeedParameter, speedMultiplier);
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
                // #region agent log
                try { var sn = (stateToPlay ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); System.IO.File.AppendAllText(@"c:\Users\peter\3dbrawlerlearn\3dbrawlerlearn\.cursor\debug.log", "{\"location\":\"SimpleEnemyAI.cs:TriggerHitAnimation\",\"message\":\"hitState\",\"data\":{\"useArray\":true,\"arrayLen\":" + hitStateNames.Length + ",\"chosenIndex\":" + chosenIndex + ",\"stateToPlay\":\"" + sn + "\"},\"timestamp\":" + (long)(UnityEngine.Time.realtimeSinceStartup * 1000) + ",\"hypothesisId\":\"H3\"}\n"); } catch { }
                // #endregion
            }
            else
            {
                lastHitStateIndex = -1;
                stateToPlay = hitStateName;
                // #region agent log
                try { var sn = (stateToPlay ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); System.IO.File.AppendAllText(@"c:\Users\peter\3dbrawlerlearn\3dbrawlerlearn\.cursor\debug.log", "{\"location\":\"SimpleEnemyAI.cs:TriggerHitAnimation\",\"message\":\"hitState\",\"data\":{\"useArray\":false,\"stateToPlay\":\"" + sn + "\"},\"timestamp\":" + (long)(UnityEngine.Time.realtimeSinceStartup * 1000) + ",\"hypothesisId\":\"H3\"}\n"); } catch { }
                // #endregion
            }
            animator.Play(stateToPlay, hitAnimationLayer, 0f);
            animator.Update(0f);
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
    /// Add this as an event on your death animation clip in Unity.
    /// </summary>
    public void OnDeathAnimationComplete()
    {
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
        
        /*
         * Check if we're stunned (recently hit)
         * 
         * This is a GLOBAL check that pauses ALL behaviors:
         *   - Enemy gets hit
         *   - EnemyHealth sets stunUntil
         *   - AI pauses (no behavior executes)
         *   - After stun wears off, current behavior resumes
         */
        if (health != null && health.IsStunned) return;

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
