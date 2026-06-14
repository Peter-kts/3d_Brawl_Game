/*
 * ============================================================================
 * ENEMYSTATEMACHINE.CS - Central behavioral state machine for enemy AI
 * ============================================================================
 *
 * Owns the active behavior (Chase / Standoff) and routes all transitions
 * through a single Transition() method. This is the one place you look to
 * understand what behavioral state the enemy is in and how it changes.
 *
 * WHAT THIS MANAGES:
 *   Behavioral states (AI decisions):    Chase, Standoff
 *
 * WHAT THIS DOES NOT MANAGE:
 *   Interrupt states (physics/health):   Hitstunned, Airborne, Crashed,
 *                                        Prone, GettingUp, Dying
 *   These are timer-driven responses to damage — not AI decisions. They
 *   live in EntityHealth / AirborneSequence / ProneSystem and are read
 *   via SimpleEnemyAI.CurrentState / CanAct, which gates Tick().
 *
 * ============================================================================
 */

public class EnemyStateMachine
{
    // Explicit version of { get; private set; } — same behavior, easier to read while learning.
    private EnemyBehavior _currentBehavior;

    public EnemyBehavior CurrentBehavior
    {
        get { return _currentBehavior; }
        private set { _currentBehavior = value; }
    }
        // public EnemyBehavior CurrentBehavior { get; private set; }

    // Called once from Awake — sets the starting behavior and runs its Enter()
    // without needing a "previous" behavior to exit first.
    public void Initialize(EnemyBehavior startingBehavior)
    {
        CurrentBehavior = startingBehavior;
        CurrentBehavior.Enter();
    }

    // THE only way behavior changes happen — exit the old state, enter the new one.
    // No-ops if the requested behavior is already active (safe to call every frame).
    public void Transition(EnemyBehavior next)
    {
        if (CurrentBehavior == next) return;
        CurrentBehavior?.Exit();   // Let outgoing state clean up (reset timers, etc.)
        CurrentBehavior = next;
        CurrentBehavior?.Enter();  // Let incoming state initialize (pick attack time, etc.)
    }

    // Run the active behavior for this frame.
    // SimpleEnemyAI only calls this when CanAct is true — interrupt states (stun, airborne)
    // are checked upstream so the behavior never has to know about them.
    public void Tick()
    {
        CurrentBehavior?.Execute();
    }
}
