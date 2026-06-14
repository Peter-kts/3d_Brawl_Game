using UnityEngine;

/// <summary>
/// Config for the player's object-spawn moveset action.
/// SpawnOffset is relative to the player's position at cast time — (0,0,0) = player center.
/// Assign on ComboSet.spawnAction and set Enable to activate.
/// </summary>
[System.Serializable]
public class SpawnActionData
{
    [Header("Enable")]
    [Tooltip("Check to activate the spawn action. Uncheck to disable without losing values.")]
    public bool enable = false;

    [Header("Prefab")]
    [Tooltip("The object to instantiate when the action fires.")]
    public GameObject prefab;

    [Header("Spawn Transform")]
    [Tooltip("Offset from the player's position at cast time. (0,0,0) = player center.\n" +
             "When 'Spawn In Local Space' is enabled: X = right, Y = up, Z = forward (relative to player facing).\n" +
             "When disabled: offset is applied in world space (X = world right, Z = world forward).")]
    public Vector3 spawnOffset = Vector3.zero;

    [Tooltip("If true, the offset axes rotate with the player (Z points where they face). " +
             "If false, the offset is in world space.")]
    public bool spawnInLocalSpace = true;

    [Tooltip("If true, the spawned object takes the player's current rotation. " +
             "If false, it spawns with identity rotation (facing world forward).")]
    public bool inheritPlayerRotation = true;

    [Tooltip("If true, the spawned object is parented to the player and moves with them. " +
             "If false, it exists independently in the scene.")]
    public bool parentToPlayer = false;

    [Header("Timing")]
    [Tooltip("Animator trigger name to fire when the action commits (leave blank to skip).")]
    public string animationTrigger = "";

    [Tooltip("How long the player is locked (can't move or re-attack) after committing. " +
             "This is the full animation window for the cast.")]
    [Min(0f)]
    public float lockDuration = 0.5f;

    [Tooltip("Normalized time within lockDuration when the object actually spawns.\n" +
             "0 = immediately on commit, 0.5 = halfway through the lock, 1 = at the very end.")]
    [Range(0f, 1f)]
    public float spawnDelay = 0f;

    [Tooltip("Seconds before this action can be used again.")]
    [Min(0f)]
    public float cooldown = 1f;

    [Header("Requirements")]
    [Tooltip("If true, the player must be in combat mode (LT/RMB held) to use this action.")]
    public bool requiresCombatMode = false;
}
