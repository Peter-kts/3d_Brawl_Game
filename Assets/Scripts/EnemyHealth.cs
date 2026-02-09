/*
 * ============================================================================
 * ENEMYHEALTH.CS - Handles enemy HP, damage, knockback, and death
 * ============================================================================
 * 
 * ENTITY HEALTH PATTERNS:
 * -----------------------
 * 
 * Every damageable entity in games typically needs:
 *   - Current HP and max HP
 *   - A way to receive damage (TakeHit, TakeDamage, etc.)
 *   - Death handling (destroy, disable, ragdoll, etc.)
 *   - Often: invincibility frames, damage resistance, healing
 * 
 * This script also handles:
 *   - Knockback physics (getting pushed when hit)
 *   - Hit stun (brief pause in AI behavior when hit)
 * 
 * DESIGN PATTERN - COMPONENT-BASED:
 * ----------------------------------
 * 
 * Notice how EnemyHealth handles ONLY health-related logic.
 * Movement is in SimpleEnemyAI. Visual feedback would be separate.
 * 
 * This is Unity's component philosophy:
 *   - Each component does ONE thing well
 *   - Combine components to build complex behavior
 *   - Easy to mix-and-match, reuse, debug
 * 
 * COLLISION-AWARE KNOCKBACK:
 * --------------------------
 * 
 * This script uses CharacterController.Move() for knockback.
 * This means:
 *   - Enemies won't get knocked through walls
 *   - Enemies won't overlap when knocked into each other
 *   - Knockback respects all collision rules
 * 
 * ============================================================================
 */

using UnityEngine;

/*
 * EnemyHealth implements IDamageable:
 * This means it promises to have TakeHit(), IsStunned, CurrentHp, MaxHp
 * 
 * Any attack system can damage this through the IDamageable interface
 */
public class EnemyHealth : MonoBehaviour, IDamageable
{
    // ========================================================================
    // SERIALIZED FIELDS
    // ========================================================================
    
    [Header("Health")]
    [Tooltip("Starting/maximum health points. 60 HP with 10 damage = 6 hits to kill.")]
    public int maxHp = 60;
    
    [Header("Hit Reaction")]
    [Tooltip("How quickly knockback velocity decays. Higher = stops faster (heavy enemy), Lower = slides further (light enemy).")]
    public float knockbackFriction = 12f;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================
    
    private int hp;                       // Current health points
    private Vector3 kbVel;                // Current knockback velocity (being applied over time)
    private float stunUntil;              // Time.time when stun ends
    private float airborneUntil;          // Time.time when airborne state ends
    private float hitStopEndTime;          // Time.time when hitstop ends (0 = not in hitstop); position frozen until then
    private CharacterController cc;       // For collision-aware knockback movement
    private SimpleEnemyAI enemyAI;        // Reference to AI for triggering hit animations
    private bool isDying;                 // True once death animation starts (prevents further hits)

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    void Awake()
    {
        hp = maxHp;
        
        /*
         * Get CharacterController for collision-aware knockback
         * 
         * The CharacterController is added by SimpleEnemyAI's [RequireComponent]
         * We use it here to apply knockback with proper collision detection
         * 
         * If there's no CharacterController, we fall back to direct transform movement
         */
        cc = GetComponent<CharacterController>();
        enemyAI = GetComponent<SimpleEnemyAI>();
    }

    void Update()
    {
        // Always apply knockback (even while dying - lets corpse get pushed around)
        ApplyKnockback();
        
        // Skip other processing while death animation plays
        if (isDying) return;
        
        ApplyAirbornePhysics();
    }
    
    // ========================================================================
    // AIRBORNE PHYSICS
    // ========================================================================
    
    /*
     * ApplyAirbornePhysics:
     * Handles the airborne state - when an enemy is launched into the air
     * 
     * While airborne:
     *   - Enemy floats (reduced/no gravity from SimpleEnemyAI)
     *   - Can be "juggled" with additional attacks
     *   - Knockback velocity decays slower (stays in air longer)
     * 
     * The actual gravity suspension is checked in SimpleEnemyAI.ApplyGravity()
     * This method handles the gradual descent when airborne time expires
     */
    void ApplyAirbornePhysics()
    {
        // Nothing special needed here - the IsAirborne property is checked by SimpleEnemyAI
        // The knockback velocity (including upward component) handles the actual movement
    }
    
    // ========================================================================
    // KNOCKBACK PHYSICS
    // ========================================================================
    
    void ApplyKnockback()
    {
        // During hitstop: freeze position and do not decay kbVel; knockback resumes when hitstop ends
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime)
            return;
        if (hitStopEndTime > 0f && Time.time >= hitStopEndTime)
            hitStopEndTime = 0f;
        
        /*
         * Knockback implementation:
         * 
         * When hit, kbVel is set to a high value (e.g., (3, 0, 2))
         * Each frame, we:
         *   1. Move by kbVel * deltaTime
         *   2. Decay kbVel toward zero
         * 
         * This creates smooth "slide back" effect, not instant teleport
         */
        if (kbVel.sqrMagnitude > 0.0001f)
        {
            /*
             * Movement with collision detection:
             * 
             * If we have a CharacterController, use Move() for collision-aware movement
             * This prevents enemies from being knocked through walls or into each other
             * 
             * If no CharacterController, fall back to direct transform movement
             */
            Vector3 movement = kbVel * Time.deltaTime;
            
            if (cc != null)
            {
                // CharacterController.Move() respects collisions
                cc.Move(movement);
            }
            else
            {
                // Fallback: direct transform movement (no collision)
                transform.position += movement;
            }
            
            /*
             * Exponential decay for knockback:
             * 
             * Lerp toward zero with exponential factor
             * 
             * Mathf.Exp(-friction * deltaTime):
             *   - High friction = fast decay
             *   - Creates natural "friction" feel
             *   - Framerate-independent
             * 
             * 1 - Exp(...) is the lerp factor:
             *   - 0 = no change
             *   - 1 = instant snap
             *   - Values between = smooth transition
             */
            kbVel = Vector3.Lerp(kbVel, Vector3.zero, 1f - Mathf.Exp(-knockbackFriction * Time.deltaTime));
        }
    }

    // ========================================================================
    // PUBLIC INTERFACE
    // ========================================================================
    
    /*
     * IsStunned property:
     * 
     * Other scripts (like SimpleEnemyAI) check this to know if enemy can act
     * 
     * Expression-bodied property with time comparison:
     *   - Time.time is current game time
     *   - stunUntil is when stun expires
     *   - If current time < stun end time, still stunned
     */
    public bool IsStunned => Time.time < stunUntil;
    
    /*
     * IsAirborne property:
     * 
     * True when the enemy has been launched into the air by an attack
     * 
     * While airborne:
     *   - SimpleEnemyAI suspends gravity (enemy floats)
     *   - Enemy can be "juggled" with additional attacks
     *   - Creates dramatic combo opportunities
     */
    public bool IsAirborne => Time.time < airborneUntil;
    
    /*
     * IsDying property:
     * 
     * True once HP reaches 0 and death animation starts.
     * Used by other systems to disable mechanics while death animation plays.
     */
    public bool IsDying => isDying;
    
    /*
     * CurrentHp / MaxHp properties:
     * 
     * Read-only access to health values for UI or other systems
     */
    public int CurrentHp => hp;
    public int MaxHp => maxHp;

    /*
     * TakeHit: The "damage interface" for this enemy (implements IDamageable)
     * 
     * Parameters:
     *   - damage: How much HP to lose
     *   - knockback: Velocity to add (direction × magnitude)
     *   - hitstun: How long the enemy is stunned (set by the ATTACKER, not the enemy)
     *   - airborneDuration: How long the enemy stays airborne (0 = not launched)
     * 
     * This is called by Combat.cs when an attack connects
     * 
     * The hitstun and airborne are controlled by the attack, not the enemy:
     *   - Light attacks: Short stun, no airborne
     *   - Heavy attacks: Long stun, launches enemy into air
     *   - Launcher attacks: Can juggle enemies for combos
     */
    public void TakeHit(int damage, Vector3 knockback, float hitstun, float airborneDuration, float hitStopDuration = 0f)
    {
        // #region agent log
        try { var tn = (gameObject?.name ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); System.IO.File.AppendAllText(@"c:\Users\peter\3dbrawlerlearn\3dbrawlerlearn\.cursor\debug.log", "{\"location\":\"EnemyHealth.cs:TakeHit\",\"message\":\"TakeHit\",\"data\":{\"target\":\"" + tn + "\"},\"timestamp\":" + (long)(UnityEngine.Time.realtimeSinceStartup * 1000) + ",\"hypothesisId\":\"H1\"}\n"); } catch { }
        // #endregion
        // Ignore hits if already dying (death animation playing)
        if (isDying) return;
        
        // --------------------------------------------------------------------
        // STEP 1: Apply damage
        // --------------------------------------------------------------------
        hp -= damage;
        
        // --------------------------------------------------------------------
        // STEP 2: Apply knockback (stored; movement frozen during hitstop, resumes after)
        // --------------------------------------------------------------------
        
        /*
         * Using += instead of = allows knockback stacking
         * 
         * If hit twice quickly, velocities add up
         * Creates "juggle" effects in fighting games
         * 
         * Alternative: kbVel = knockback (overwrite, no stacking)
         */
        kbVel += knockback;
        if (hitStopDuration > 0f)
            hitStopEndTime = Time.time + hitStopDuration;
        
        // --------------------------------------------------------------------
        // STEP 3: Apply hit stun (duration from the attack)
        // --------------------------------------------------------------------
        
        /*
         * Set stun expiration time:
         * 
         * The 'hitstun' parameter comes from the attack (Combat.cs)
         * This allows different attacks to cause different stun durations
         * 
         * Using Mathf.Max ensures we don't shorten an existing longer stun
         * Example: If stunned for 0.5s and hit with 0.1s stun, stay stunned for 0.5s
         */
        stunUntil = Mathf.Max(stunUntil, Time.time + hitstun);
        
        // --------------------------------------------------------------------
        // STEP 4: Trigger hit animation
        // --------------------------------------------------------------------
        
        /*
         * Fire the hit animation trigger (fires once, doesn't repeat)
         * This is better than a bool because:
         *   - Trigger fires once and auto-resets
         *   - Bool would restart the animation every frame while true
         * 
         * We pass hitstun so the animation speed can be scaled to match.
         */
        if (enemyAI != null)
        {
            enemyAI.TriggerHitAnimation(hitstun);
        }
        
        // --------------------------------------------------------------------
        // STEP 5: Apply airborne state (if attack launches)
        // --------------------------------------------------------------------
        
        /*
         * Set airborne expiration time:
         * 
         * If airborneDuration > 0, the attack launches this enemy into the air
         * While airborne:
         *   - SimpleEnemyAI suspends normal gravity
         *   - Enemy floats based on knockback velocity
         *   - Additional attacks can "juggle" the enemy
         * 
         * Using Mathf.Max ensures we don't shorten existing airborne time
         */
        if (airborneDuration > 0f)
        {
            airborneUntil = Mathf.Max(airborneUntil, Time.time + airborneDuration);
        }

        // --------------------------------------------------------------------
        // STEP 6: Check for death
        // --------------------------------------------------------------------
        
        if (hp <= 0)
        {
            /*
             * Death animation system:
             * 
             * 1. Set isDying flag to prevent further hits
             * 2. Trigger death animation via SimpleEnemyAI
             * 3. Animation Event calls OnDeathAnimationComplete() when done
             * 4. That method disables the GameObject
             * 
             * This allows the death animation to play fully before the enemy disappears.
             */
            isDying = true;
            
            if (enemyAI != null)
            {
                enemyAI.TriggerDeathAnimation();
            }
            else
            {
                // Fallback if no AI component - just disable immediately
                gameObject.SetActive(false);
            }
            
            /*
             * TODO for a full game:
             * - Spawn particles/effects
             * - Drop loot
             * - Update kill counters
             * - Play death sound
             */
        }
    }
}
