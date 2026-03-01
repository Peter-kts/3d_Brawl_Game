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
 * 
 *   - Circling: move tangentially, face target, maintain radius
 *   - PreAttack: brief pause/telegraph so the player can react
 *   - Attacking: EnemyCombat fires the attack, enemy is locked in place
 * 
 * ============================================================================
 */

using UnityEngine;

public class StandoffBehavior : EnemyBehavior
{
    // Internal sub-states for the standoff behavior
    private enum SubState { Circling, PreAttack, Attacking }

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

    public StandoffBehavior(SimpleEnemyAI ai) : base(ai) { }

    /// <summary>
    /// Returns the current sub-state name for debug visualization.
    /// Shows both the behavior (Standoff) and the internal phase.
    /// </summary>
    public override string CurrentStateName => state switch
    {
        SubState.Circling   => "Circling",
        SubState.PreAttack  => "PreAttack",
        SubState.Attacking  => "Attacking",
        _                   => "Standoff"
    };

    public override void Enter()
    {
        state = SubState.Circling;

        // Randomly pick circle direction only the first time we enter standoff (avoids re-randomizing every time we cross the hysteresis band)
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

        switch (state)
        {
            case SubState.Circling:
                ExecuteCircling();
                break;
            case SubState.PreAttack:
                ExecutePreAttack();
                break;
            case SubState.Attacking:
                ExecuteAttacking();
                break;
        }
    }

    public override void Exit()
    {
        // Reset to circling so we start fresh next time we enter standoff
        state = SubState.Circling;
    }

    // ========================================================================
    // CIRCLING
    // ========================================================================

    void ExecuteCircling()
    {
        Vector3 toPlayer = ai.player.position - ai.transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        Vector3 dirToPlayer = toPlayer / Mathf.Max(dist, 0.001f);

        // --------------------------------------------------------------------
        // Tangent direction (perpendicular to line toward player)
        // --------------------------------------------------------------------

        /*
         * Vector3.Cross(up, dirToPlayer) gives a horizontal vector
         * perpendicular to the direction toward the player.
         * 
         * Multiplying by circleDirection (1 or -1) flips between
         * clockwise and counter-clockwise orbiting.
         */
        Vector3 tangent = Vector3.Cross(Vector3.up, dirToPlayer) * circleDirection;

        // --------------------------------------------------------------------
        // Radial correction (maintain preferred distance)
        // --------------------------------------------------------------------

        /*
         * radiusError: positive = too far, negative = too close
         * 
         * We blend the tangent movement with a push toward/away from
         * the player to stay near the preferred standoff radius.
         * 
         * The blend weight increases as we drift further from the ideal distance,
         * so at the right distance we move purely tangentially (smooth circle),
         * and when too far/close we course-correct.
         */
        float radiusError = dist - ai.standoffRadius;
        Vector3 radialCorrection = dirToPlayer * Mathf.Sign(radiusError);

        float radialWeight = Mathf.Clamp01(Mathf.Abs(radiusError) / ai.standoffRadius);
        Vector3 moveDir = Vector3.Lerp(tangent, radialCorrection, radialWeight).normalized;

        // --------------------------------------------------------------------
        // Move and rotate
        // --------------------------------------------------------------------

        Vector3 movement = moveDir * ai.circleSpeed * Time.deltaTime;
        ai.CC.Move(movement);

        // Face the player while circling (not the movement direction)
        if (dirToPlayer.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToPlayer, Vector3.up);
            ai.transform.rotation = Quaternion.RotateTowards(
                ai.transform.rotation,
                targetRot,
                ai.rotationSpeed * Time.deltaTime
            );
        }

        // Slower animation speed for strafing vs full-speed chase
        ai.TargetAnimSpeed = 0.5f;

        // --------------------------------------------------------------------
        // Timers
        // --------------------------------------------------------------------

        // Periodically reverse circle direction for variety
        directionChangeTimer -= Time.deltaTime;
        if (directionChangeTimer <= 0f)
        {
            circleDirection *= -1f;
            ResetDirectionTimer();
        }

        // Attack timer - when it expires, transition to pre-attack
        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            if (ai.EnemyCombat != null)
            {
                state = SubState.PreAttack;
                preAttackTimer = ai.attackTelegraphDuration;
            }
            else
            {
                // No combat component - just reset timer and keep circling
                ResetAttackTimer();
            }
        }
    }

    // ========================================================================
    // PRE-ATTACK (TELEGRAPH)
    // ========================================================================

    /*
     * Brief pause before the actual attack.
     * 
     * This is a TELEGRAPH: a visual cue that tells the player
     * "an attack is coming!" so they can dodge or counter.
     * 
     * The enemy stops moving and snaps to face the player precisely.
     * In a full game, you'd play a wind-up animation here.
     */
    void ExecutePreAttack()
    {
        if (ai.player != null)
        {
            Vector3 toPlayer = ai.player.position - ai.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
            {
                // Faster rotation to snap toward target before attacking
                Quaternion targetRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
                ai.transform.rotation = Quaternion.RotateTowards(
                    ai.transform.rotation,
                    targetRot,
                    ai.rotationSpeed * 2f * Time.deltaTime
                );
            }
        }

        // No movement during telegraph
        ai.TargetAnimSpeed = 0f;

        preAttackTimer -= Time.deltaTime;
        if (preAttackTimer <= 0f)
        {
            // Choose attack: dodge-punish when player recently dodged and is in range, else kick if beyond punchRangeThreshold, else punch (close)
            Vector3 toPlayer = ai.player != null ? ai.player.position - ai.transform.position : Vector3.zero;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            bool usePunish = ai.PlayerController != null
                && ai.PlayerController.RecentlyDodged(ai.dodgePunishWindow)
                && dist >= ai.dodgePunishDistMin
                && dist <= ai.dodgePunishDistMax;
            if (usePunish)
                ai.EnemyCombat.DoAttack(ai.EnemyCombat.dodgePunishAttack);
            else if (dist > ai.EnemyCombat.punchRangeThreshold)
                ai.EnemyCombat.DoAttack(ai.EnemyCombat.kickAttack);
            else
                ai.EnemyCombat.DoAttack();
            state = SubState.Attacking;
        }
    }

    // ========================================================================
    // ATTACKING
    // ========================================================================

    /*
     * Wait for the attack to finish (lock duration from AttackData).
     * The enemy stays in place while the attack plays out.
     * Once EnemyCombat.IsAttacking becomes false, resume circling.
     */
    void ExecuteAttacking()
    {
        // No movement during attack
        ai.TargetAnimSpeed = 0f;

        // Wait for attack lock to expire
        if (!ai.EnemyCombat.IsAttacking)
        {
            state = SubState.Circling;
            ResetAttackTimer();
        }
    }

    // ========================================================================
    // TIMER HELPERS
    // ========================================================================

    void ResetDirectionTimer()
    {
        directionChangeTimer = Random.Range(
            ai.directionChangeIntervalMin,
            ai.directionChangeIntervalMax
        );
    }

    void ResetAttackTimer()
    {
        attackTimer = Random.Range(ai.attackIntervalMin, ai.attackIntervalMax);
    }
}
