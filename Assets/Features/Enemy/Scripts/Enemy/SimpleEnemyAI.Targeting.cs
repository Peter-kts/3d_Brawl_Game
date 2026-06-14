/*
 * SimpleEnemyAI.Targeting.cs — team-based retargeting.
 *
 * Lives in a partial of SimpleEnemyAI so it shares player, health, targetTeam, etc.
 * Periodically re-scores all living EntityHealth instances hostile to this AI's team
 * and points `player` at the best one. Mostly relevant for non-player teams (allies).
 */

using UnityEngine;

public partial class SimpleEnemyAI
{
    private float retargetTimer; // Throttles FindObjectsOfType when retargeting (non-player teams only)

    // ========================================================================
    // TEAM TARGETING (retarget when target dies — used by non-player teams)
    // ========================================================================

    /// <summary>
    /// Periodically checks if the current target is still alive and finds a new one if not.
    /// No-ops when targetTeam == Player (the player doesn't die normally).
    /// </summary>
    void UpdateTarget()
    {
        retargetTimer -= Time.deltaTime;
        if (retargetTimer > 0f) return;
        retargetTimer = 0.5f;

        Transform best = FindNearestHostileTarget();
        if (best != null)
            player = best;
    }

    /// <summary>
    /// Finds the nearest living EntityHealth that is hostile to this AI's team.
    /// </summary>
    Transform FindNearestHostileTarget()
    {
        if (health == null) return null;

        EntityHealth[] all = FindObjectsOfType<EntityHealth>();
        Transform best = null;
        float bestScore = float.MaxValue;

        foreach (EntityHealth e in all)
        {
            if (e == (EntityHealth)health) continue;
            if (e.CurrentHp <= 0) continue;
            if (!TeamUtil.AreHostile(health.team, e.team)) continue;

            float dist = Vector3.Distance(transform.position, e.transform.position);
            float priorityMultiplier = 1f;
            if (e.team == Team.Player) priorityMultiplier = PlayerTargetPriorityMultiplier;
            else if (e.team == Team.Ally) priorityMultiplier = AllyTargetPriorityMultiplier;
            float score = dist / Mathf.Max(0.0001f, priorityMultiplier);

            if (score < bestScore)
            {
                bestScore = score;
                best = e.transform;
            }
        }
        return best;
    }
}
