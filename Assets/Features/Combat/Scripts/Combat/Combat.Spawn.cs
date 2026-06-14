/*
 * Per-attack object spawning — driven by animation events.
 * Add an OnSpawnObject animation event to the attack clip at the exact frame you want the
 * object to appear. Because it fires from the animator, it respects charge slowdown and
 * release speed boost automatically.
 */

using UnityEngine;

public partial class Combat
{
    /// <summary>
    /// Animation event. Place on the attack clip at the frame the object should spawn.
    /// Uses the player's position and rotation at that moment (post-lunge, post-arc).
    /// </summary>
    public void OnSpawnObject()
    {
        if (currentAttackData == null || !currentAttackData.spawnObject) return;
        ExecuteAttackSpawn(currentAttackData, transform.position, transform.rotation);
    }

    void ExecuteAttackSpawn(AttackData data, Vector3 originPos, Quaternion originRot)
    {
        if (data == null || data.spawnPrefab == null) return;

        Vector3 worldOffset = data.spawnInLocalSpace
            ? originRot * data.spawnOffset
            : data.spawnOffset;

        Vector3 spawnPos    = originPos + worldOffset;
        Quaternion spawnRot = data.inheritPlayerRotation ? originRot : Quaternion.identity;
        Transform parent    = data.parentToPlayer ? transform : null;

        Instantiate(data.spawnPrefab, spawnPos, spawnRot, parent);
    }

    // No pending state needed — animation events are self-timed.
    void ClearAttackSpawn() { }
}
