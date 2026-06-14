/// <summary>
/// Which faction an entity belongs to. Controls who deals damage to whom.
/// </summary>
public enum Team { Player, Enemy, Ally }

/// <summary>
/// Utility for team-based damage gating.
/// </summary>
public static class TeamUtil
{
    /// <summary>
    /// Returns true when the two teams should deal damage to each other.
    /// Player and Ally are on the same side — neither damages the other.
    /// Enemy is hostile to both Player and Ally.
    /// </summary>
    public static bool AreHostile(Team a, Team b)
    {
        if (a == b) return false;
        if ((a == Team.Player && b == Team.Ally) || (a == Team.Ally && b == Team.Player)) return false;
        return true;
    }
}
