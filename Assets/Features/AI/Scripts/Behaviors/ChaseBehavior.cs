/*
 * ============================================================================
 * CHASEBEHAVIOR.CS - Move toward the target
 * ============================================================================
 * 
 * This behavior is extracted from SimpleEnemyAI's original HandleMovement().
 * The enemy moves directly toward the player and rotates to face them.
 * 
 * TRANSITIONS:
 *   - Chase -> Standoff: when distance to player <= standoffEnterRange
 *     (managed by SimpleEnemyAI, not this class)
 * 
 * ============================================================================
 */

using UnityEngine;

public class ChaseBehavior : EnemyBehavior
{
    public ChaseBehavior(SimpleEnemyAI ai) : base(ai) { }

    public override string CurrentStateName => "Chase";

    public override void Execute()
    {
        if (ai.player == null) return;

        // --------------------------------------------------------------------
        // Calculate direction to player
        // --------------------------------------------------------------------

        Vector3 toPlayer = ai.player.position - ai.transform.position;
        toPlayer.y = 0f;  // Ignore vertical difference (stay on ground)

        float dist = toPlayer.magnitude;

        // Only move if we have some distance to cover
        if (dist > 0.1f)
        {
            /*
             * Normalize direction (length 1) for consistent speed.
             * Mathf.Max prevents division by zero.
             */
            Vector3 dir = toPlayer / Mathf.Max(dist, 0.001f);

            /*
             * CharacterController.Move():
             *   - Respects collisions with other CharacterControllers
             *   - Collides with walls and obstacles
             *   - Slides along surfaces
             */
            Vector3 movement = dir * ai.moveSpeed * Time.deltaTime;
            ai.CC.Move(movement);

            /*
             * Rotate to face movement direction:
             * RotateTowards smoothly turns toward target rotation
             */
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                ai.transform.rotation = Quaternion.RotateTowards(
                    ai.transform.rotation,
                    targetRot,
                    ai.rotationSpeed * Time.deltaTime
                );
            }

            // Set animation to walking (smoothed by SimpleEnemyAI.UpdateAnimator)
            ai.TargetAnimSpeed = 1f;
        }
    }
}
