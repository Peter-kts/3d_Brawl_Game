/*
 * ============================================================================
 * ENEMYBEHAVIOR.CS - Abstract base class for enemy AI behaviors
 * ============================================================================
 * 
 * BEHAVIOR PATTERN:
 * -----------------
 * 
 * Each behavior encapsulates a specific AI pattern (chase, standoff, etc.).
 * SimpleEnemyAI holds a reference to the current behavior and calls Execute()
 * every frame, delegating the actual movement/decision logic.
 * 
 * This is a lightweight version of the STATE PATTERN:
 *   - Each behavior is a "state" with its own logic
 *   - SimpleEnemyAI manages transitions between behaviors
 *   - Enter()/Exit() hooks allow setup/cleanup on transitions
 * 
 * WHY SEPARATE CLASSES?
 * ---------------------
 * 
 * Instead of a giant switch statement in SimpleEnemyAI:
 *   - Each behavior is self-contained and easy to understand
 *   - New behaviors can be added without modifying existing ones
 *   - Behaviors can have their own internal sub-states (e.g., StandoffBehavior
 *     has Circling, PreAttack, Attacking phases)
 * 
 * ============================================================================
 */

using UnityEngine;

/// <summary>
/// Abstract base class for enemy behaviors.
/// Subclasses implement Execute() to define per-frame AI logic.
/// </summary>
public abstract class EnemyBehavior
{
    /*
     * Reference to the owning SimpleEnemyAI.
     * 
     * Behaviors access the enemy's components through this reference:
     *   - ai.transform (position, rotation)
     *   - ai.player (target to chase/circle)
     *   - ai.CC (CharacterController for movement)
     *   - ai.moveSpeed, ai.rotationSpeed (tuning values)
     *   - ai.TargetAnimSpeed (drives animation blending)
     */
    protected SimpleEnemyAI ai;

    public EnemyBehavior(SimpleEnemyAI ai)
    {
        this.ai = ai;
    }

    /// <summary>
    /// Called once when this behavior becomes the active behavior.
    /// Use for initialization (reset timers, pick random values, etc.)
    /// </summary>
    public virtual void Enter() { }

    /// <summary>
    /// Called every frame while this behavior is active.
    /// This is where the actual AI logic lives (movement, decisions, etc.)
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// Called once when switching away from this behavior.
    /// Use for cleanup if needed.
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// Display name of the current state for debug visualization.
    /// Override in subclasses to expose internal sub-states.
    /// </summary>
    public virtual string CurrentStateName => "Unknown";
}
