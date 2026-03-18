/*
 * ============================================================================
 * CHASEBEHAVIOR.CS - Move toward the target, flanking when multiple enemies chase
 * ============================================================================
 *
 * This behavior is extracted from SimpleEnemyAI's original HandleMovement().
 * The enemy moves toward the player and rotates to face them each frame.
 *
 * FLANKING:
 *   When multiple enemies are in Chase state simultaneously, FlankCoordinator
 *   assigns each one a different approach angle so they spread to different
 *   sides of the player instead of all funneling in from the same direction.
 *   A single chasing enemy gets angle 0 — identical to the old behaviour.
 *
 * TRANSITIONS:
 *   - Chase -> Standoff: when distance to player <= standoffEnterRange
 *     (evaluated by SimpleEnemyAI.EvaluateBehaviorTransition(), not this class)
 *
 * ============================================================================
 */

using UnityEngine;

public class ChaseBehavior : EnemyBehavior
{
    // How far from the player the flank target is placed.
    // Small enough that the enemy still closes to melee range, large enough to
    // visibly curve their approach when multiple enemies are chasing.
    const float FlankBuffer = 1.5f;

    public ChaseBehavior(SimpleEnemyAI ai) : base(ai) { }

    public override string CurrentStateName => "Chase";

    public override void Enter()
    {
        // Register with the coordinator so slot angles are (re)assigned for
        // all enemies currently chasing the same player.
        FlankCoordinator.Register(ai, ai.player);
    }

    public override void Exit()
    {
        // Release our slot — remaining chasers will spread back out.
        FlankCoordinator.Unregister(ai, ai.player);
    }

    public override void Execute()
    {
        if (ai.player == null) return;

        // --------------------------------------------------------------------
        // Calculate direction to player (XZ only — ignore height)
        // --------------------------------------------------------------------

        Vector3 toPlayer = ai.player.position - ai.transform.position;
        toPlayer.y = 0f; // flatten to ground plane so slopes don't affect movement direction

        float dist = toPlayer.magnitude;
        if (dist < 0.1f) return; // already overlapping the player, nothing to chase

        // --------------------------------------------------------------------
        // Compute flank target — a point near the player offset by our
        // assigned approach angle (0° when alone, spread when grouped).
        // --------------------------------------------------------------------

        /*
         * awayDir points from the player back toward the enemy's current side.
         * Rotating it by the assigned flank angle picks a different point
         * around the player that the enemy steers toward.
         *
         *   flankTarget = player + Rotate(awayDir, flankAngle) * FlankBuffer
         *
         * With flankAngle = 0: target sits directly between player and enemy
         *   → same as the old straight-in approach.
         * With flankAngle = ±90°: target is to the player's side
         *   → enemy curves around to approach from a different angle.
         *
         * Because FlankBuffer is small relative to chase distances, enemies
         * still close in quickly — the curve is subtle but clearly visible.
         */
        float flankAngle    = FlankCoordinator.GetFlankAngle(ai);
        Vector3 awayDir     = -(toPlayer / dist);  // normalised, points player→enemy
        Vector3 flankOffset = Quaternion.AngleAxis(flankAngle, Vector3.up) * awayDir * FlankBuffer;
        Vector3 flankTarget = ai.player.position + flankOffset;

        // --------------------------------------------------------------------
        // Move toward flank target
        // --------------------------------------------------------------------

        Vector3 moveDir = flankTarget - ai.transform.position;
        moveDir.y = 0f;

        if (moveDir.magnitude < 0.1f) return;

        /*
         * Normalize manually — moveDir.magnitude is already computed above.
         * CharacterController.Move():
         *   - Respects collisions with walls and other CharacterControllers
         *   - Slides along surfaces automatically
         *   - Does NOT apply gravity — handled separately in SimpleEnemyAI
         */
        Vector3 dir = moveDir.normalized;
        ai.CC.Move(dir * ai.moveSpeed * Time.deltaTime);

        // --------------------------------------------------------------------
        // Face movement direction
        // --------------------------------------------------------------------

        /*
         * RotateTowardWithDelay smoothly turns the enemy toward dir each frame.
         * Keeps facing logic consistent with Standoff — no duplication needed.
         */
        ai.RotateTowardWithDelay(dir);

        // Set walking speed; actual animator parameter is smoothed in SimpleEnemyAI.UpdateAnimator
        ai.TargetAnimSpeed = 1f;
    }
}
