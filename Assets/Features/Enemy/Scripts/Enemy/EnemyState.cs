/*
 * EnemyState.cs - Explicit state enum for enemy "recovery from hit" flow.
 *
 * Single source of truth for "what state is the enemy in?" so callers don't
 * have to know the priority order of IsDying, IsAirborne, IsCrashed, etc.
 * Computed from EnemyHealth timers + SimpleEnemyAI airborne phase in one place (SimpleEnemyAI.CurrentState).
 */

/// <summary>Logical state of an enemy for hit recovery and AI blocking. Priority: Dying &gt; Airborne &gt; Crashed &gt; Grounded &gt; Hitstunned &gt; Stunned &gt; GettingUp &gt; Normal.</summary>
public enum EnemyState
{
    /// <summary>Default: not in any hit reaction; AI runs (chase, standoff, attack).</summary>
    Normal,

    /// <summary>Hitstun from a hit; playing hit reaction, cannot act.</summary>
    Hitstunned,

    /// <summary>Stun meter filled; stunned state active, cannot act. Animation may be standard stun or knockback-stun flavor.</summary>
    Stunned,
    /// <summary>Legacy alias for Stunned to preserve compatibility with existing references.</summary>
    StandingStunned = Stunned,

    /// <summary>Launched by an attack; floating, can be juggled, cannot act.</summary>
    Airborne,

    /// <summary>Landed from airborne; playing crash/land animation, cannot act.</summary>
    Crashed,

    /// <summary>Crash finished; lying prone on floor before get-up, cannot act.</summary>
    Prone,

    /// <summary>Crash finished; playing get-up (or holding crash pose), cannot act.</summary>
    GettingUp,

    /// <summary>HP &lt;= 0; death or airborne-as-death sequence playing.</summary>
    Dying
}
