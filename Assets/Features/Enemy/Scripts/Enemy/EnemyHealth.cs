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

    [Tooltip("Default airborne duration for crash relaunch when the hitting move has no launch (makesAirborne false).")]
    public float crashRelaunchAirborneDurationDefault = 0.5f;

    [Tooltip("Crash relaunch: knockback Y is scaled by this (e.g. 0.5 = 50% of original), so they go further not higher.")]
    [Range(0f, 1f)]
    public float crashRelaunchKnockbackYScale = 0.25f;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================
    
    private int hp;                       // Current health points
    private Vector3 kbVel;                // Current knockback velocity (being applied over time)
    private float stunUntil;              // Time.time when stun ends
    private float airborneUntil;          // Time.time when airborne state ends
    private float hitStopEndTime;          // Time.time when hitstop ends (0 = not in hitstop); position frozen until then
    private Vector3 pendingKnockback;
    private float pendingAirborneDuration;
    private float pendingLaunchApplyTime;  // When hitstop ends, apply knockback/launch so we "cut to midair"
    private CharacterController cc;       // For collision-aware knockback movement
    private SimpleEnemyAI enemyAI;        // Reference to AI for triggering hit animations
    private bool isDying;                 // True once death animation starts (prevents further hits)
    private float getUpUntil;             // Time.time when get-up stun ends (0 = not getting up)
    private bool crashHitAlreadyUsed;    // True after taking the one allowed hit while in crash
    private float airborneSpeedMultiplier = 1f;  // 1.4f during crash-relaunch airborne so animation and timers match

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
        
        UpdateGetUpOnCrash();

        // Clear crash-one-hit flag when not in crash so next crash allows one hit again
        if (enemyAI != null && enemyAI.AirborneSequence != null && !enemyAI.AirborneSequence.InCrash)
            crashHitAlreadyUsed = false;
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

    /// <summary>
    /// Clear get-up timer when expired. Get-up is started (and get-up animation triggered) by SimpleEnemyAI when the crash phase finishes.
    /// </summary>
    void UpdateGetUpOnCrash()
    {
        if (getUpUntil > 0f && Time.time >= getUpUntil)
            getUpUntil = 0f;
    }

    /// <summary>
    /// Start the get-up sequence. Called by SimpleEnemyAI when the airborne crash phase has finished.
    /// Caller triggers the get-up animation immediately after this.
    /// </summary>
    /// <param name="duration">How long the get-up stun lasts (passed from SimpleEnemyAI.getUpDuration).</param>
    public void StartGetUp(float duration)
    {
        // Already in get-up sequence; avoid playing get-up twice
        if (getUpUntil > 0f && Time.time < getUpUntil)
            return;
        float scaled = GetAirborneSpeedMultiplier() > 1f ? duration / GetAirborneSpeedMultiplier() : duration;
        getUpUntil = Time.time + scaled;
    }

    /// <summary>
    /// Called by SimpleEnemyAI when the airborne sequence (liftoff/loop/crash) finishes.
    /// If the enemy was killed by an airborne attack, this completes the death (disables logic, keeps mesh visible).
    /// </summary>
    public void OnAirborneSequenceComplete()
    {
        if (isDying)
            CompleteDeath();
    }

    /// <summary>
    /// Disables AI, combat, and movement so the corpse stays visible; does not disable the GameObject/mesh.
    /// </summary>
    public void CompleteDeath()
    {
        if (enemyAI != null) enemyAI.enabled = false;
        var combat = GetComponent<EnemyCombat>();
        if (combat != null) combat.enabled = false;
        if (cc != null) cc.enabled = false;
        enabled = false;
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
        // When hitstop ends, apply delayed launch so we "cut to midair"
        if (pendingLaunchApplyTime > 0f && Time.time >= pendingLaunchApplyTime)
        {
            kbVel += pendingKnockback;
            airborneUntil = Mathf.Max(airborneUntil, Time.time + pendingAirborneDuration);
            pendingLaunchApplyTime = 0f;
        }

        // Only move/decay if knockback is meaningful (sqrMagnitude avoids sqrt; 0.01^2 = 0.0001)
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
            // Only move when CC is enabled (e.g. skip while thrown — throw system disables CC and moves the root)
            if (cc != null && cc.enabled)
            {
                cc.Move(movement);
            }
            else if (cc == null)
            {
                transform.position += movement;
            }
            // else: CC exists but disabled — don't call Move (avoids "Move called on inactive controller")
            
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
     * IsGettingUp property:
     * True when the enemy has just landed from airborne and is in the get-up stun (playing get-up animation).
     * During this time the AI does not act - they are stunned until the timer expires.
     */
    public bool IsGettingUp => getUpUntil > 0f && Time.time < getUpUntil;

    /*
     * CurrentHp / MaxHp properties:
     * 
     * Read-only access to health values for UI or other systems
     */
    public int CurrentHp => hp;
    public int MaxHp => maxHp;

    /// <summary>Speed multiplier for airborne animation (1f normal, 1.4f during crash relaunch). Cleared when sequence ends.</summary>
    public float GetAirborneSpeedMultiplier() => airborneSpeedMultiplier;

    /// <summary>Called when the airborne sequence (liftoff/loop/crash) ends so the next airborne is 1x again.</summary>
    public void OnAirborneSequenceEnded()
    {
        airborneSpeedMultiplier = 1f;
    }

    /// <summary>Clear current knockback velocity and pending knockback. Use when releasing a throw victim so leftover velocity doesn't move them after we bake position.</summary>
    public void ClearKnockback()
    {
        kbVel = Vector3.zero;
        pendingKnockback = Vector3.zero;
        hitStopEndTime = 0f;
    }

    /// <summary>
    /// Start throw-victim state: stun for duration and play the thrown animation (no damage/knockback here; applied at throw end by Combat).
    /// </summary>
    public void StartThrowVictim(float durationSeconds, string thrownStateName)
    {
        if (isDying) return;
        stunUntil = Mathf.Max(stunUntil, Time.time + durationSeconds);
        if (enemyAI != null)
            enemyAI.TriggerThrownAnimation(durationSeconds, thrownStateName);
    }

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
    public void TakeHit(
        int damage,
        Vector3 knockback,
        float hitstun,
        float airborneDuration,
        float hitStopDuration = 0f,
        AttackHeaviness heaviness = AttackHeaviness.Medium,
        AttackHeight height = AttackHeight.Mid
    )
    {
        // #region agent log
        try { var tn = (gameObject?.name ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); System.IO.File.AppendAllText(@"c:\Users\peter\3dbrawlerlearn\3dbrawlerlearn\.cursor\debug.log", "{\"location\":\"EnemyHealth.cs:TakeHit\",\"message\":\"TakeHit\",\"data\":{\"target\":\"" + tn + "\"},\"timestamp\":" + (long)(UnityEngine.Time.realtimeSinceStartup * 1000) + ",\"hypothesisId\":\"H1\"}\n"); } catch { }
        // #endregion
        // Ignore hits if already dying (death animation playing)
        if (isDying) return;

        // Crash mode: allow one hit; that hit gets 1.5x knockback, re-launch, and 1.4x airborne animation/timers
        bool inCrash = enemyAI != null && enemyAI.AirborneSequence != null && enemyAI.AirborneSequence.InCrash;
        bool inGrounded = enemyAI != null && enemyAI.AirborneSequence != null && enemyAI.AirborneSequence.InGrounded;
        if (inCrash && crashHitAlreadyUsed)
            return;
        if (inCrash && !crashHitAlreadyUsed)
        {
            crashHitAlreadyUsed = true;
            airborneSpeedMultiplier = 1.4f;
            // 1.5x total; Y scaled down so knockback goes further, not higher
            knockback.y *= crashRelaunchKnockbackYScale;
            knockback *= 1.5f;
            airborneDuration = (airborneDuration > 0f ? airborneDuration : crashRelaunchAirborneDurationDefault) / airborneSpeedMultiplier;
        }
        
        // --------------------------------------------------------------------
        // STEP 1: Apply damage
        // --------------------------------------------------------------------
        hp -= damage;
        ScreenShake.RequestShake();

        // When grounded (on floor after crash): take damage only — no knockback, airborne, hitstop, stun, or hit animation
        if (inGrounded)
        {
            if (hp <= 0)
            {
                isDying = true;
                if (enemyAI != null)
                    enemyAI.TriggerDeathAnimation();
                else
                    CompleteDeath();
            }
            return;
        }
        
        // --------------------------------------------------------------------
        // STEP 2: Apply knockback (stored; movement frozen during hitstop, resumes after)
        // --------------------------------------------------------------------
        
        if (airborneDuration > 0f)
        {
            pendingKnockback = knockback;
            pendingAirborneDuration = airborneDuration;
        }
        else
        {
            kbVel += knockback;
        }
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
        if (airborneDuration > 0f)
            pendingLaunchApplyTime = (hitStopDuration > 0f) ? (Time.time + hitStopDuration) : Time.time;  // launch when hit stop ends
        
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
            enemyAI.TriggerHitAnimation(hitstun, height);
        }
        
        // --------------------------------------------------------------------
        // STEP 5: Apply airborne state (if attack launches) — delayed until hitstop ends (applied in ApplyKnockback)
        // --------------------------------------------------------------------

        // --------------------------------------------------------------------
        // STEP 6: Check for death
        // --------------------------------------------------------------------
        
        if (hp <= 0)
        {
            isDying = true;
            /*
             * If killed by an airborne attack, the airborne animation (liftoff/loop/crash)
             * is used as the death — don't play a separate death animation. SimpleEnemyAI
             * will call OnAirborneSequenceComplete() when the airborne sequence ends.
             */
            if (airborneDuration > 0f)
            {
                if (enemyAI == null)
                    CompleteDeath();
            }
            else
            {
                if (enemyAI != null)
                    enemyAI.TriggerDeathAnimation();
                else
                    CompleteDeath();
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
