/*
 * ============================================================================
 * PLAYERCOMBATMEMORY.CS - Shared pseudo-memory of the player's recent attacks
 * ============================================================================
 *
 * PURPOSE:
 * --------
 * Stores the last N attacks the player committed as a circular buffer.
 * Any enemy within awarenessRadius that is targeting this player can query
 * this memory to make smarter blocking and reaction decisions.
 *
 * This represents the collective "knowledge" of nearby enemies — they've all
 * been watching the same player and share what they've observed.
 *
 * ARCHITECTURE:
 * -------------
 *   Combat.cs         → RecordMove()  (writes on every committed attack)
 *   SimpleEnemyAI     → GetRecentMoves() + IsEnemyWatching()  (reads during reaction)
 *   CombatMemoryDebugDisplay → reads for visualization
 *
 * MULTIPLAYER DESIGN:
 * -------------------
 * This is a per-player component (not a singleton) so it naturally scales to
 * multiplayer. Each player has their own memory. Enemies check which player
 * they are targeting and read that player's memory specifically.
 *
 * ============================================================================
 */

using UnityEngine;

public class PlayerCombatMemory : MonoBehaviour
{
    // ========================================================================
    // CONFIG
    // ========================================================================

    [Header("Memory")]
    [Tooltip("Number of recent player attacks to remember. The oldest entry is overwritten once full.")]
    public int capacity = 5;

    [Header("Awareness")]
    [Tooltip("Enemies must be within this radius AND targeting this player to read from the shared memory.")]
    public float awarenessRadius = 15f;

    // ========================================================================
    // MOVE RECORD
    // ========================================================================

    /* Each entry records the essential observable facts about one committed attack. */
    public struct MoveRecord
    {
        public AttackHeaviness heaviness; // Light / Medium / Heavy
        public AttackHeight    height;    // Low / Mid / High
        public float           timestamp; // Time.time when the attack was committed
    }

    // ========================================================================
    // CIRCULAR BUFFER STATE
    // ========================================================================

    /*
     * Fixed-size circular buffer:
     *   _head  = next write position (advances after each write, wraps at capacity)
     *   _count = number of valid entries in the buffer (0..capacity)
     *
     * Oldest entry is naturally overwritten once the buffer is full, so memory
     * always reflects the most recent 'capacity' attacks without any shifting.
     */
    private MoveRecord[] _buffer;
    private int _head  = 0;
    private int _count = 0;

    /// <summary>Number of valid records currently stored (0 to capacity).</summary>
    public int Count => _count;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        _buffer = new MoveRecord[capacity];
    }

    // ========================================================================
    // WRITE — called by Combat.cs on every committed attack
    // ========================================================================

    /// <summary>
    /// Record a newly committed player attack.
    /// Overwrites the oldest entry once the buffer is full.
    /// </summary>
    public void RecordMove(AttackData attack)
    {
        if (attack == null) return;

        _buffer[_head] = new MoveRecord
        {
            heaviness = attack.heaviness,
            height    = attack.height,
            timestamp = Time.time
        };

        _head  = (_head + 1) % capacity;          // advance write head, wrap around
        _count = Mathf.Min(_count + 1, capacity);  // cap at capacity
    }

    // ========================================================================
    // READ — called by enemy AI during reaction decisions
    // ========================================================================

    /// <summary>
    /// Returns a snapshot of all stored records ordered newest → oldest.
    /// Length equals Count (may be less than capacity if buffer isn't full yet).
    /// </summary>
    public MoveRecord[] GetRecentMoves()
    {
        MoveRecord[] result = new MoveRecord[_count];
        for (int i = 0; i < _count; i++)
        {
            /* Walk backward from the current write head. The slot just before head
             * is the most recent write; each step further back is one attack older. */
            int idx = ((_head - 1 - i) % capacity + capacity) % capacity;
            result[i] = _buffer[idx];
        }
        return result;
    }

    // ========================================================================
    // RANGE + TARGET CHECK
    // ========================================================================

    /// <summary>
    /// True if the given enemy is within the awareness radius AND currently
    /// targeting this player. Both conditions must hold to be "watching."
    /// </summary>
    public bool IsEnemyWatching(SimpleEnemyAI enemy)
    {
        if (enemy == null) return false;

        /* Dead enemies can't watch. */
        if (enemy.Health != null && enemy.Health.IsDying) return false;

        /* Must be targeting this player specifically — future-proofs for multiplayer
         * where different enemies may be chasing different players. */
        if (enemy.player != transform) return false;

        return Vector3.Distance(transform.position, enemy.transform.position) <= awarenessRadius;
    }

    // ========================================================================
    // SCENE GIZMO
    // ========================================================================

    void OnDrawGizmosSelected()
    {
        /* Draw the awareness radius in the scene view as a faint filled sphere
         * so you can see which enemies are inside the "watching" zone. */
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.12f);
        Gizmos.DrawSphere(transform.position, awarenessRadius);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.55f);
        Gizmos.DrawWireSphere(transform.position, awarenessRadius);
    }
}
