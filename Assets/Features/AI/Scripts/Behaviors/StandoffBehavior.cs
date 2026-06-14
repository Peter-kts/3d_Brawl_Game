/*
 * ============================================================================
 * STANDOFFBEHAVIOR.CS - Circle the target, occasionally attack
 * ============================================================================
 *
 * STANDOFF PATTERN:
 * -----------------
 *
 * Common in action games (Batman Arkham, Devil May Cry, etc.):
 *   - Enemies don't all rush in at once
 *   - They circle the player, creating tension
 *   - Occasionally one enemy breaks from the circle to attack
 *   - This gives the player time to react and creates visual drama
 *
 * CIRCLING LOGIC:
 * ---------------
 *
 * The enemy moves along a TANGENT to the circle around the player:
 *
 *          tangent -->
 *          --------
 *         /        \
 *   Enemy *    P    *   (P = Player at center)
 *         \        /
 *          --------
 *
 * To maintain the correct distance, we blend tangent movement with
 * a radial correction (push toward/away from player).
 *
 * SUB-STATES:
 * -----------
 *
 *   Circling --> PreAttack --> Attacking --> Circling
 *   Circling/Attacking --> (reaction fires) --> BackingOff or Interrupting --> Circling
 *
 *   - Circling:     move tangentially, face target, maintain radius
 *   - PreAttack:    brief pause/telegraph so the player can react
 *   - Attacking:    EnemyCombat fires the attack, enemy is locked in place
 *   - BackingOff:   step away until player's attack ends
 *   - Interrupting: rush in to punish player during their attack animation
 *
 * REACTION SYSTEM:
 * ----------------
 *
 * Separate from the sub-state machine. When the player charges an attack
 * aimed at the enemy and the enemy is within range, a reaction timer starts.
 * The enemy continues its current behavior while the timer counts down
 * (this is the "thinking" window — purely internal, debug-only visible).
 * When the timer expires the enemy commits: BackingOff or Interrupting.
 *
 * ============================================================================
 */

using UnityEngine;

public class StandoffBehavior : EnemyBehavior
{
    // Internal sub-states — Reading is NOT a state, it's a parallel timer
    private enum SubState { Circling, ClosingIn, PreAttack, Attacking, Retreating, BackingOff, Interrupting }

    private SubState state = SubState.Circling;

    /*
     * circleDirection: 1 = clockwise, -1 = counter-clockwise (viewed from above)
     *
     * Randomized on enter and periodically reversed for variety.
     * Different enemies will circle in different directions,
     * making group encounters look more dynamic.
     */
    private float circleDirection = 1f;
    private bool circleDirectionInitialized;
    private float directionChangeTimer;
    private float attackTimer;
    private float preAttackTimer;
    private float interruptRushTimer;
    private float closeInTimer; // safety cap on ClosingIn — abort if we can't reach attack range in time // safety cap on Interrupting — abort if we can't close the gap in time

    // ---- Reaction system (parallel to sub-states) ----
    // reactionTimer >= 0 means a reaction is in progress (debug shows "Reading").
    // reactionTriggered prevents the same attack from firing the reaction twice.
    private float reactionTimer = -1f;
    private bool reactionTriggered;

    public StandoffBehavior(SimpleEnemyAI ai) : base(ai) { }

    /// <summary>
    /// Returns the current sub-state name for debug visualization.
    /// "Reading" overlays when a reaction timer is active regardless of actual sub-state,
    /// so the debug shows the enemy is "thinking" while still moving normally.
    /// </summary>
    public override string CurrentStateName
    {
        get
        {
            // Reaction timer is running — enemy is mid-decision; overlay "Reading" regardless of sub-state
            if (reactionTimer >= 0f) return "Reading";
            return state switch
            {
                SubState.Circling     => "Circling",
                SubState.ClosingIn    => "ClosingIn",
                SubState.PreAttack    => "PreAttack",
                SubState.Attacking    => "Attacking",
                SubState.Retreating   => "Retreating",
                SubState.BackingOff   => "BackingOff",
                SubState.Interrupting => "Interrupting",
                _                    => "Standoff"
            };
        }
    }

    public override void Enter()
    {
        state = SubState.Circling;
        reactionTimer = -1f;
        reactionTriggered = false;

        // Only randomize circle direction once — prevents re-rolling every time we re-enter standoff from the hysteresis band
        if (!circleDirectionInitialized)
        {
            circleDirection = Random.value > 0.5f ? 1f : -1f;
            circleDirectionInitialized = true;
        }

        ResetDirectionTimer();
        ResetAttackTimer();
    }

    public override void Execute()
    {
        if (ai.player == null) return;

        // Reaction system is independent of sub-states — runs every frame on top of normal behavior
        TryTriggerReaction();
        TickReaction();

        switch (state)
        {
            case SubState.Circling:     ExecuteCircling();     break;
            case SubState.ClosingIn:    ExecuteClosingIn();    break;
            case SubState.PreAttack:    ExecutePreAttack();    break;
            case SubState.Attacking:    ExecuteAttacking();    break;
            case SubState.Retreating:   ExecuteRetreating();   break;
            case SubState.BackingOff:   ExecuteBackingOff();   break;
            case SubState.Interrupting: ExecuteInterrupting(); break;
        }
    }

    public override void Exit()
    {
        state = SubState.Circling;
        reactionTimer = -1f;
        reactionTriggered = false;
    }

    // ========================================================================
    // REACTION SYSTEM (parallel to sub-states)
    // ========================================================================

    /*
     * TryTriggerReaction — called every frame before the sub-state runs.
     *
     * Fires once per player attack when: the attack is aimed at us, we're in range,
     * and a personality chance roll passes.
     *
     * reactionTriggered is a latch — it stays true until the player stops attacking
     * so the same swing can't restart the timer mid-execution.
     */
    void TryTriggerReaction()
    {
        var pc = ai.PlayerController;
        if (pc == null || pc.combat == null) return;

        if (!pc.combat.IsAttacking)
        {
            // Player stopped attacking — clear the latch so the next attack can be reacted to
            reactionTriggered = false;
            return;
        }

        // Already processing a reaction for this attack — don't start a second timer
        if (reactionTriggered) return;

        // Already committed to a response — let it play out before reacting again
        if (state == SubState.BackingOff || state == SubState.Interrupting) return;

        // Don't react while mid-attack — we're the aggressor right now, not the defender
        if (state == SubState.Attacking) return;

        // Check that the player is actually swinging AT us (dot product: player forward vs direction to enemy)
        Vector3 toPlayer   = ai.player.position - ai.transform.position;
        toPlayer.y = 0f;
        float dist         = toPlayer.magnitude;
        Vector3 dirToPlayer = toPlayer / Mathf.Max(dist, 0.001f);
        Vector3 toEnemy    = -dirToPlayer; // from player toward us
        float facingDot    = Vector3.Dot(pc.transform.forward, toEnemy);

        // Attack isn't aimed at us or we're too far away to care — ignore it
        if (facingDot < ai.BackOffFacingThreshold || dist >= ai.StandoffRadius + 1.5f) return;

        // Lock the latch now regardless of the roll — we've "seen" this attack
        reactionTriggered = true;

        // Personality chance: not every enemy reacts every time (makes them feel less robotic)
        var p = ai.personality;
        float reactionChance = p != null ? p.reactionChance : 0.8f;
        if (Random.value >= reactionChance) return; // decided to ignore this one

        // Start the thinking timer — enemy continues doing whatever it's doing until this fires
        float minDelay = p != null ? p.reactionTimeMin : 0.1f;
        float maxDelay = p != null ? p.reactionTimeMax : 0.25f;
        reactionTimer = Random.Range(minDelay, maxDelay);
    }

    /*
     * TickReaction — counts down the reaction timer each frame.
     *
     * Normal behavior continues uninterrupted while this ticks.
     * When it hits zero the enemy commits to either BackingOff or Interrupting,
     * overriding whatever sub-state they were in.
     *
     * If the player stops attacking before the timer fires, the reaction is
     * silently cancelled — the enemy missed their window to react.
     */
    void TickReaction()
    {
        // No active reaction — nothing to do
        if (reactionTimer < 0f) return;

        var pc = ai.PlayerController;

        // Player stopped attacking before we decided — cancel the reaction silently
        if (pc == null || pc.combat == null || !pc.combat.IsAttacking)
        {
            reactionTimer = -1f;
            return;
        }

        reactionTimer -= Time.deltaTime;
        // Still thinking — let normal behavior continue
        if (reactionTimer > 0f) return;

        // Timer expired — make the decision and commit
        reactionTimer = -1f;

        Vector3 toPlayer = ai.player.position - ai.transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        var p = ai.personality;
        float interruptChance   = p != null ? p.interruptChance   : 0.4f;
        float interruptMaxRange = p != null ? p.interruptMaxRange : 3.5f;

        // Only attempt to interrupt if close enough — too far and we'd never reach in time
        bool inInterruptRange = dist <= interruptMaxRange;
        // Personality roll: aggressive enemies interrupt more often, cautious ones back off more
        bool rollSucceeds = Random.value < interruptChance;

        if (inInterruptRange && rollSucceeds)
        {
            interruptRushTimer = 2f; // safety cap — if we don't reach in 2 seconds, abort
            state = SubState.Interrupting;
        }
        else
        {
            // Either out of range or roll failed — retreat instead
            state = SubState.BackingOff;
        }
    }

    // ========================================================================
    // CIRCLING
    // ========================================================================

void ExecuteCircling()
    {
        var pc = ai.PlayerController;
        if (pc != null)
        {
            if (pc.RecentlyAttacked(ai.OpportunityWindow) && attackTimer > ai.OpportunityAttackDelay)
                attackTimer = ai.OpportunityAttackDelay;
        }

        DoCirclingMovement();

        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            if (ai.EnemyCombat != null)
            {
                Vector3 toPlayer = ai.player != null ? ai.player.position - ai.transform.position : Vector3.zero;
                toPlayer.y = 0f;

                AttackData dummy;
                if (ai.EnemyCombat.TrySelectAttack(toPlayer.magnitude, out dummy))
                {
                    // Already in range — telegraph immediately
                    state = SubState.PreAttack;
                    preAttackTimer = ai.AttackTelegraphDuration;
                }
                else
                {
                    // Out of range — close in first
                    closeInTimer = 3f;
                    state = SubState.ClosingIn;
                }
            }
            else
            {
                ResetAttackTimer();
            }
        }
    }

// ========================================================================
    // CLOSING IN (move into attack range before telegraphing)
    // ========================================================================

    void ExecuteClosingIn()
    {
        // Safety cap — abort if we can't reach attack range in time
        closeInTimer -= Time.deltaTime;
        if (closeInTimer <= 0f)
        {
            state = SubState.Circling;
            ResetAttackTimer();
            return;
        }

        Vector3 toPlayer    = ai.player.position - ai.transform.position;
        toPlayer.y          = 0f;
        float dist          = toPlayer.magnitude;
        Vector3 dirToPlayer = toPlayer.normalized;

        // Light separation blend so enemies don't stack while both closing in
        Vector3 separation = Vector3.ClampMagnitude(ai.GetSeparationSteering(), 0.5f);
        Vector3 moveDir    = (dirToPlayer + separation).normalized;

        ai.CC.Move(moveDir * ai.moveSpeed * Time.deltaTime);
        ai.RotateTowardWithDelay(dirToPlayer);
        ai.TargetAnimSpeed = 1f;

        // Transition to PreAttack once we're in range of any attack
        AttackData dummy;
        if (ai.EnemyCombat != null && ai.EnemyCombat.TrySelectAttack(dist, out dummy))
        {
            state = SubState.PreAttack;
            preAttackTimer = ai.AttackTelegraphDuration;
        }
    }


    // Shared movement used by both Circling and Interrupting approach phase.
    // Handles tangent orbit, radial correction, rotation, animation speed, and direction-flip timer.
    // The attack timer is NOT ticked here — callers handle that separately.
void DoCirclingMovement()
    {
        Vector3 toPlayer    = ai.player.position - ai.transform.position;
        toPlayer.y          = 0f;
        float dist          = toPlayer.magnitude;
        Vector3 dirToPlayer = toPlayer / Mathf.Max(dist, 0.001f);

        Vector3 tangent = Vector3.Cross(Vector3.up, dirToPlayer) * circleDirection;

        float radiusError        = dist - ai.StandoffRadius;
        Vector3 radialCorrection = dirToPlayer * Mathf.Sign(radiusError);
        float radialWeight       = Mathf.Clamp01(Mathf.Abs(radiusError) / ai.StandoffRadius);
        Vector3 primaryDir       = Vector3.Lerp(tangent, radialCorrection, radialWeight).normalized;

        // Blend separation steering so enemies orbit without stacking on each other.
        Vector3 separation = Vector3.ClampMagnitude(ai.GetSeparationSteering(), 1f);
        Vector3 moveDir    = (primaryDir + separation).normalized;

        ai.CC.Move(moveDir * ai.CircleSpeed * Time.deltaTime);

        ai.RotateTowardWithDelay(dirToPlayer); // always face the player regardless of moveDir
        ai.TargetAnimSpeed = 0.5f;

        directionChangeTimer -= Time.deltaTime;
        if (directionChangeTimer <= 0f)
        {
            circleDirection *= -1f;
            ResetDirectionTimer();
        }
    }

    // ========================================================================
    // PRE-ATTACK (TELEGRAPH)
    // ========================================================================

    /*
     * Brief pause before the actual attack fires.
     *
     * This is a TELEGRAPH — a visual tell the player can learn to react to.
     * The enemy stops moving and snaps to face the player so the incoming
     * attack direction is clearly readable. In a full game this would play
     * a wind-up animation (raising weapon, shifting weight, etc.).
     */
    void ExecutePreAttack()
    {
        if (ai.player != null)
        {
            Vector3 toPlayer = ai.player.position - ai.transform.position;
            toPlayer.y = 0f;
            // Faster than normal while telegraphing, but still uses turn reaction delay.
            ai.RotateTowardWithDelay(toPlayer, 2f);
        }

        // Stop moving during the telegraph — standing still makes the pause feel intentional
        ai.TargetAnimSpeed = 0f;

        preAttackTimer -= Time.deltaTime;
        if (preAttackTimer <= 0f)
        {
            // Choose which attack to use based on distance and player state
            Vector3 toPlayer = ai.player != null ? ai.player.position - ai.transform.position : Vector3.zero;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;

            AttackData selectedAttack;
            if (ai.EnemyCombat.TrySelectAttack(dist, out selectedAttack))
                ai.EnemyCombat.DoAttack(selectedAttack);

            state = SubState.Attacking;
        }
    }

    // ========================================================================
    // ATTACKING
    // ========================================================================

    /*
     * Waits for the attack animation lock to expire (lockDuration from AttackData).
     * Enemy holds position during the swing — moving during an attack would make
     * the hitbox teleport and feel disconnected from the animation.
     * Once EnemyCombat.IsAttacking goes false, return to circling.
     */
void ExecuteAttacking()
    {
        ai.TargetAnimSpeed = 0f;

        if (!ai.EnemyCombat.IsAttacking)
            state = SubState.Retreating; // back up to standoff distance before circling again
    }

// ========================================================================
    // RETREATING (back up to standoff radius after attacking)
    // ========================================================================

    void ExecuteRetreating()
    {
        Vector3 toPlayer    = ai.player.position - ai.transform.position;
        toPlayer.y          = 0f;
        float dist          = toPlayer.magnitude;
        Vector3 dirToPlayer = toPlayer.normalized;

        ai.CC.Move(-dirToPlayer * ai.BackOffSpeed * Time.deltaTime);
        ai.RotateTowardWithDelay(dirToPlayer); // keep facing the player while backing away
        ai.TargetAnimSpeed = 0.5f;

        if (dist >= ai.StandoffRadius)
        {
            state = SubState.Circling;
            ResetAttackTimer();
        }
    }


    // ========================================================================
    // BACKING OFF (reaction decision: step away while player attacks)
    // ========================================================================

    /*
     * The reaction timer fired and the enemy decided to disengage.
     * Steps backward (away from player) until the player's attack ends,
     * then resumes circling with a flipped orbit direction.
     *
     * Flipping direction after a retreat feels natural — like the enemy
     * repositioned themselves while backing away.
     */
    void ExecuteBackingOff()
    {
        Vector3 toPlayer    = ai.player.position - ai.transform.position;
        toPlayer.y          = 0f;
        Vector3 dirToPlayer = toPlayer.normalized;

        ai.CC.Move(-dirToPlayer * ai.BackOffSpeed * Time.deltaTime); // move away from player

        // Keep facing the player during the retreat so we don't lose sight of the threat
        ai.RotateTowardWithDelay(dirToPlayer);

        ai.TargetAnimSpeed = 0.5f;

        var pc = ai.PlayerController;
        // Player's attack finished — they can act again; time for us to reposition and circle
        if (pc == null || pc.combat == null || !pc.combat.IsAttacking)
        {
            circleDirection *= -1f; // flip orbit so repositioning looks deliberate, not a loop
            state = SubState.Circling;
        }
    }

    // ========================================================================
    // INTERRUPTING (reaction decision: rush in during player's attack)
    // ========================================================================

    /*
     * The reaction timer fired and the enemy decided to go aggressive.
     * Rushes toward the player at interruptRushSpeed. Once close enough,
     * decides whether to actually commit to attacking based on trade willingness:
     *
     *   Aggressive: commits regardless — willing to eat a hit to land one
     *   Cautious:   only commits when player's lock is nearly expired (safe punish)
     *   Normal:     probability roll weighted by personality
     *
     * If the player's attack ends before the enemy gets in range, the window
     * is missed and the enemy returns to circling — rewarding fast combos.
     */
    void ExecuteInterrupting()
    {
        var pc = ai.PlayerController;
        var p  = ai.personality;

        // Player stopped attacking — we missed the window, resume circling normally
        if (pc == null || pc.combat == null || !pc.combat.IsAttacking)
        {
            state = SubState.Circling;
            ResetAttackTimer(); // fresh timer so we don't immediately attack again after missing
            return;
        }

        // Safety cap: if we've been chasing for 2 seconds and still can't reach, give up
        interruptRushTimer -= Time.deltaTime;
        if (interruptRushTimer <= 0f)
        {
            // Took too long to close — switch to backing off so we don't stand there looking confused
            state = SubState.BackingOff;
            return;
        }

        // Rush toward the player at the personality-defined interrupt speed
        Vector3 toPlayer = ai.player.position - ai.transform.position;
        toPlayer.y       = 0f;
        float dist       = toPlayer.magnitude;
        Vector3 dir      = toPlayer.normalized;

        float rushSpeed  = p != null ? p.interruptRushSpeed : 5f;
        ai.CC.Move(dir * rushSpeed * Time.deltaTime);

        // Lock rotation toward target while rushing — don't let the animation drift sideways
        ai.RotateTowardWithDelay(dir);

        // Full run speed animation while closing the gap
        ai.TargetAnimSpeed = 1f;

        // Not in attack range yet — keep closing
        AttackData rangeCheckAttack;
        if (ai.EnemyCombat == null || !ai.EnemyCombat.TrySelectAttack(dist, out rangeCheckAttack)) return;

        // In range — now decide whether to actually fire the attack
        float lockRemaining = pc.AttackLockTimeRemaining; // how long is left on the player's attack lock
        float tradeWill     = p != null ? p.tradeWillingness    : 0.5f;
        float safeThresh    = p != null ? p.safeWindowThreshold : 0.3f;

        // "Safe" means the player's attack is basically over — no risk of trading
        bool isSafe    = lockRemaining <= safeThresh;
        // "WillTrade" means the personality rolled to commit even if it's not safe (aggressive behavior)
        bool willTrade = Random.value < tradeWill;

        if (isSafe || willTrade)
        {
            // Commit: go straight into PreAttack with a shortened/zero telegraph (no time to wind up mid-rush)
            preAttackTimer = p != null ? p.interruptAttackDelay : 0.05f;
            state = SubState.PreAttack;
        }
        else
        {
            // Not willing to eat the hit and it's not safe yet — abort and back off
            state = SubState.BackingOff;
        }
    }

    // ========================================================================
    // TIMER HELPERS
    // ========================================================================

    void ResetDirectionTimer()
    {
        directionChangeTimer = Random.Range(ai.DirChangeIntervalMin, ai.DirChangeIntervalMax);
    }

    void ResetAttackTimer()
    {
        attackTimer = Random.Range(ai.AttackIntervalMin, ai.AttackIntervalMax);
    }
}
