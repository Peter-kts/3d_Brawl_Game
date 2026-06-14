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

        Vector3 toPlayer = ai.player.position - ai.transform.position;
        toPlayer.y = 0f;

        float dist = toPlayer.magnitude;
        if (dist < 0.1f) return;

        float flankAngle    = FlankCoordinator.GetFlankAngle(ai);
        Vector3 awayDir     = -(toPlayer / dist);
        Vector3 flankOffset = Quaternion.AngleAxis(flankAngle, Vector3.up) * awayDir * FlankBuffer;
        Vector3 flankTarget = ai.player.position + flankOffset;

        Vector3 primaryDir = flankTarget - ai.transform.position;
        primaryDir.y = 0f;

        if (primaryDir.magnitude < 0.1f) return;
        primaryDir.Normalize();

        // Blend separation steering into move direction so enemies naturally spread apart.
        Vector3 separation = Vector3.ClampMagnitude(ai.GetSeparationSteering(), 1f);
        Vector3 moveDir = (primaryDir + separation).normalized;

        ai.CC.Move(moveDir * ai.moveSpeed * Time.deltaTime);
        ai.RotateTowardWithDelay(primaryDir); // still face toward the flank target, not the separation offset
        ai.TargetAnimSpeed = 1f;
    }
}
