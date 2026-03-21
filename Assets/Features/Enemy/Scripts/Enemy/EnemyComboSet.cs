using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class EnemyMoveEntry
{
    [Tooltip("Friendly label for this enemy move.")]
    public string moveId = "Move";

    [Tooltip("Move properties shared with player attacks.")]
    public AttackData attack = new AttackData();

    [Header("Selection Rules")]
    [Tooltip("Move can be selected only when distance to player is >= this value.")]
    public float minDistance = 0f;

    [Tooltip("Move can be selected only when distance to player is <= this value.")]
    public float maxDistance = 100f;

    [Tooltip("Higher values are selected first when multiple moves match.")]
    public int priority = 0;
}

[CreateAssetMenu(fileName = "EnemyComboSet", menuName = "Combat/Enemy Combo Set", order = 1)]
public class EnemyComboSet : ScriptableObject
{
    [Tooltip("Enemy moves evaluated by EnemyCombat.TrySelectAttack.")]
    public List<EnemyMoveEntry> moves = new List<EnemyMoveEntry>();
}
