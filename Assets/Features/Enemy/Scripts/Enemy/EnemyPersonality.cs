using UnityEngine;

/// <summary>
/// Data asset describing how an enemy personality reacts to the player's attacks.
/// Assign to SimpleEnemyAI.personality. Create via Assets > Create > Enemy > Personality.
///
/// PERSONALITIES:
/// - Aggressive: short reaction time, high interrupt chance, willing to trade hits
/// - Normal:     balanced — sometimes backs off, sometimes interrupts if in range
/// - Cautious:   longer delay, prefers back off, only interrupts near the end of player lock
///
/// All values have null-safe fallbacks in StandoffBehavior, so leaving this unassigned
/// uses hardcoded "Normal" defaults and never crashes.
/// </summary>
[CreateAssetMenu(fileName = "EnemyPersonality", menuName = "Enemy/Personality", order = 2)]
public class EnemyPersonality : ScriptableObject
{
    // =========================================================================
    // STANDOFF — how close to get and how to circle
    // =========================================================================

    [Header("Spacing")]
    [Tooltip("Radius within which this enemy steers away from other enemies. Separation is blended into movement direction — no teleport push.")]
    public float separationRadius = 2f;

    [Header("Target Priority")]
    [Tooltip("Priority weight for Player targets. Higher = more likely to be selected.")]
    [Range(0f, 2f)]
    public float playerTargetPriorityMultiplier = 2f;

    [Tooltip("Priority weight for Ally targets. Higher = more likely to be selected.")]
    [Range(0f, 2f)]
    public float allyTargetPriorityMultiplier = 1f;

    [Header("Standoff - Range")]
    [Tooltip("Distance at which enemy stops chasing and begins circling the target.")]
    public float standoffEnterRange = 4f;

    [Tooltip("Distance at which enemy stops circling and resumes chasing. Should be > standoffEnterRange to prevent flickering.")]
    public float standoffExitRange = 6f;

    [Tooltip("Preferred distance to maintain while circling the target.")]
    public float standoffRadius = 3f;

    [Tooltip("Movement speed while circling (typically slower than chase speed).")]
    public float circleSpeed = 2.5f;

    [Tooltip("Minimum time between orbit direction changes while circling.")]
    public float directionChangeIntervalMin = 1.5f;

    [Tooltip("Maximum time between orbit direction changes while circling.")]
    public float directionChangeIntervalMax = 4f;

    // =========================================================================
    // ATTACK TIMING — when to attack from standoff
    // =========================================================================

    [Header("Attack Timing")]
    [Tooltip("Minimum time between attacks from standoff.")]
    public float attackIntervalMin = 1.5f;

    [Tooltip("Maximum time between attacks from standoff.")]
    public float attackIntervalMax = 4f;

    [Tooltip("Brief pause before attacking (telegraph so player can react).")]
    public float attackTelegraphDuration = 0.2f;

    // =========================================================================
    // BACK OFF — retreat when player attacks
    // =========================================================================

    [Header("Back Off")]
    [Tooltip("Speed the enemy moves backward when reacting to a player attack.")]
    public float backOffSpeed = 2f;

    [Tooltip("Minimum dot product between player forward and direction to enemy for back-off to trigger. 0.4 ≈ within ~66° of facing.")]
    [Range(0f, 1f)]
    public float backOffFacingThreshold = 0.4f;

    // =========================================================================
    // OPPORTUNITY ATTACK — punish player recovery
    // =========================================================================

    [Header("Opportunity Attack")]
    [Tooltip("Seconds after the player's attack lock ends during which the enemy will immediately punish.")]
    public float opportunityWindow = 0.5f;

    [Tooltip("How long the enemy waits before punishing during the player's recovery.")]
    public float opportunityAttackDelay = 0.15f;

    // =========================================================================
    // REACTION TIME — how quickly the enemy "sees" the player's attack starting
    // =========================================================================

    [Header("Reaction Time")]
    [Tooltip("Minimum seconds before the enemy reacts to a player attack. Lower = sharper reads.")]
    [Range(0f, 0.5f)]
    public float reactionTimeMin = 0.12f;

    [Tooltip("Maximum seconds before the enemy reacts. The actual delay is random between min and max, giving human-like variance.")]
    [Range(0f, 0.6f)]
    public float reactionTimeMax = 0.25f;

    [Tooltip("Probability (0–1) that the enemy reacts at all to a given player attack. At 0.8 the enemy misses ~1 in 5 attacks — feels natural, not robotic.")]
    [Range(0f, 1f)]
    public float reactionChance = 0.8f;

    // =========================================================================
    // DECISION — interrupt vs back off
    // =========================================================================

    [Header("Decision - Interrupt vs Back Off")]
    [Tooltip("Base probability (0–1) to choose Interrupt over BackOff after the reaction delay. Modified by distance: if the enemy is outside interruptMaxRange it always backs off regardless of this value.")]
    [Range(0f, 1f)]
    public float interruptChance = 0.5f;

    [Tooltip("Maximum distance at which the enemy will attempt to interrupt. Beyond this it always backs off instead.")]
    public float interruptMaxRange = 3.5f;

    // =========================================================================
    // INTERRUPT — trade willingness
    // =========================================================================

    [Header("Interrupt - Trade Willingness")]
    [Tooltip("How willing the enemy is to trade hits during an interrupt attempt. " +
             "0 = never trades (only commits when player lock is nearly expired), " +
             "1 = always commits the moment it reaches attack range regardless of timing.")]
    [Range(0f, 1f)]
    public float tradeWillingness = 0.5f;

    [Tooltip("Seconds remaining in the player's attack lock that the enemy considers 'safe' to interrupt. " +
             "If the lock has less than this time left, even a cautious enemy will commit. " +
             "Ignored when tradeWillingness is high.")]
    [Range(0f, 0.8f)]
    public float safeWindowThreshold = 0.3f;

    // =========================================================================
    // INTERRUPT — execution
    // =========================================================================

    [Header("Interrupt - Execution")]
    [Tooltip("Movement speed while rushing toward the player to interrupt (units/sec). Higher than circleSpeed so the enemy can close distance mid-attack.")]
    public float interruptRushSpeed = 5f;

    [Tooltip("Seconds between reaching attack range and firing the attack. 0 = immediate (aggressive). " +
             "Small value (0.1) = still visible telegraph so it doesn't feel unfair.")]
    [Range(0f, 0.4f)]
    public float interruptAttackDelay = 0.05f;

}
