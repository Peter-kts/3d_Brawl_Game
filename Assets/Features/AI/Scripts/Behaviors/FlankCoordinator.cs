/*
 * ============================================================================
 * FLANKCOORDINATOR.CS - Spread chasing enemies to different approach angles
 * ============================================================================
 *
 * PROBLEM:
 * When multiple enemies are in Chase state they all beeline straight at the
 * player, stack together, and arrive from the same direction. This looks
 * dumb and creates cheap pile-ons rather than interesting multi-enemy tension.
 *
 * SOLUTION:
 * Assign each chasing enemy an angular offset so they approach from different
 * sides. Enemy 1 might curve from the left, Enemy 2 from the right, Enemy 3
 * from directly behind — all still closing in, just from spread positions.
 *
 * HOW IT WORKS:
 * 1. When an enemy enters Chase it calls Register(). This adds it to the
 *    registry for its target player and triggers a ReassignSlots() pass.
 *
 * 2. ReassignSlots() divides 360° by the number of active chasers and
 *    greedily assigns each enemy the slot closest to its CURRENT world angle
 *    relative to the player. This avoids sending an enemy that's already on
 *    the right to approach from the left — it picks the nearest available slot.
 *
 * 3. Each frame ChaseBehavior calls GetFlankAngle() to get its current offset,
 *    then moves toward a point near the player offset by that angle.
 *
 * 4. When an enemy exits Chase (enters Standoff, dies, etc.) it calls
 *    Unregister(). Slots are reassigned so the remaining enemies spread back
 *    out cleanly.
 *
 * SLOT ANGLES (centred at 0°, evenly spaced):
 *   1 chaser  →  0°
 *   2 chasers → –90°, +90°
 *   3 chasers → –120°, 0°, +120°
 *   4 chasers → –135°, –45°, +45°, +135°
 *
 * This is a static class — no MonoBehaviour, no scene setup required.
 * ============================================================================
 */

using System.Collections.Generic;
using UnityEngine;

public static class FlankCoordinator
{
    // All enemies currently in Chase state, grouped by their target player
    static readonly Dictionary<Transform, List<SimpleEnemyAI>> registry = new();

    // The angle offset (in degrees, world Y-axis) each enemy has been assigned
    static readonly Dictionary<SimpleEnemyAI, float> assignedAngle = new();

    // ========================================================================
    // PUBLIC API
    // ========================================================================

    /// <summary>
    /// Call when an enemy enters Chase state. Assigns a flank slot.
    /// </summary>
    public static void Register(SimpleEnemyAI enemy, Transform player)
    {
        if (enemy == null || player == null) return;

        if (!registry.ContainsKey(player))
            registry[player] = new List<SimpleEnemyAI>();

        if (!registry[player].Contains(enemy))
            registry[player].Add(enemy);

        ReassignSlots(player);
    }

    /// <summary>
    /// Call when an enemy exits Chase state (enters Standoff, dies, etc.).
    /// </summary>
    public static void Unregister(SimpleEnemyAI enemy, Transform player)
    {
        if (enemy == null) return;

        assignedAngle.Remove(enemy);

        if (player != null && registry.TryGetValue(player, out var list))
        {
            list.Remove(enemy);
            if (list.Count == 0)
                registry.Remove(player);
            else
                ReassignSlots(player); // spread remaining enemies back out
        }
    }

    /// <summary>
    /// Returns the angle offset (degrees) this enemy should use when computing
    /// its flank approach target. Returns 0 if the enemy is unregistered
    /// (single-enemy case — straight approach, no change in behaviour).
    /// </summary>
    public static float GetFlankAngle(SimpleEnemyAI enemy)
    {
        if (enemy == null) return 0f;
        return assignedAngle.TryGetValue(enemy, out float angle) ? angle : 0f;
    }

    // ========================================================================
    // SLOT ASSIGNMENT
    // ========================================================================

    /*
     * ReassignSlots — called every time the chaser list changes.
     *
     * Generates evenly-spaced target angles for all chasers, then greedily
     * matches each enemy to the slot it is ALREADY closest to. This minimises
     * unnecessary lateral movement when enemies register mid-chase.
     *
     * The greedy pass:
     *   For each slot (sorted by most-natural fit), assign the closest unmatched enemy.
     *   Avoids the classic "everyone crosses each other" assignment bug.
     */
    static void ReassignSlots(Transform player)
    {
        if (!registry.TryGetValue(player, out var chasers) || chasers.Count == 0) return;

        int n = chasers.Count;

        // Build target angle list (evenly spaced, centred at 0°)
        float[] targetAngles = new float[n];
        float step = 360f / n;
        float start = -(step * (n - 1)) / 2f; // centre the spread around 0°
        for (int i = 0; i < n; i++)
            targetAngles[i] = start + step * i;

        if (n == 1)
        {
            // Single chaser — straight approach, no offset needed
            assignedAngle[chasers[0]] = 0f;
            return;
        }

        // Compute each enemy's current world angle relative to the player
        float[] currentAngles = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (chasers[i] == null) { currentAngles[i] = 0f; continue; }
            Vector3 toEnemy = chasers[i].transform.position - player.position;
            toEnemy.y = 0f;
            // Angle from world +Z axis, clockwise
            currentAngles[i] = Mathf.Atan2(toEnemy.x, toEnemy.z) * Mathf.Rad2Deg;
        }

        // Greedy best-fit: for each enemy pick the unoccupied slot with least angular delta
        bool[] slotTaken = new bool[n];
        bool[] enemyAssigned = new bool[n];

        for (int pass = 0; pass < n; pass++)
        {
            float bestDelta = float.MaxValue;
            int bestEnemy = -1, bestSlot = -1;

            for (int e = 0; e < n; e++)
            {
                if (enemyAssigned[e]) continue;
                for (int s = 0; s < n; s++)
                {
                    if (slotTaken[s]) continue;
                    float delta = Mathf.Abs(Mathf.DeltaAngle(currentAngles[e], targetAngles[s]));
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestEnemy = e;
                        bestSlot = s;
                    }
                }
            }

            if (bestEnemy < 0) break;

            slotTaken[bestSlot]       = true;
            enemyAssigned[bestEnemy]  = true;

            // The assigned angle is the TARGET slot angle minus the enemy's current world angle.
            // ChaseBehavior uses this as a rotation offset applied to the approach direction.
            float offset = Mathf.DeltaAngle(currentAngles[bestEnemy], targetAngles[bestSlot]);
            assignedAngle[chasers[bestEnemy]] = offset;
        }
    }
}
