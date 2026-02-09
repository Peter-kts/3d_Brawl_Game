/*
 * CombatHitboxGizmos.cs - Editor Gizmos for Combat hitbox ranges (light/heavy).
 * Add to same GameObject as Combat. Only draws when object is selected in editor.
 */

using UnityEngine;

[RequireComponent(typeof(Combat))]
public class CombatHitboxGizmos : MonoBehaviour
{
    private Combat combat;

    void Awake()
    {
        combat = GetComponent<Combat>();
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (combat == null) combat = GetComponent<Combat>();
        if (combat == null) return;

        // Use reflection or public API to get forwardJab/heavyAttack - Combat doesn't expose them.
        // We need Transform, hitOrigin, forwardJab.range, forwardJab.hitboxOffset, forwardJab.hitboxRadius,
        // heavyAttack same. So we need Combat to expose preview positions for Gizmos, or we use SerializedObject.
        // Simplest: Combat exposes two methods GetLightHitboxCenterAndRadius and GetHeavyHitboxCenterAndRadius
        // or a single GetGizmoHitboxRanges that returns (Vector3 centerL, float radiusL, Vector3 centerH, float radiusH).
        // I'll add a small API to Combat for editor gizmos: GetEditorHitboxCenters(out Vector3 lightCenter, out float lightRadius, out Vector3 heavyCenter, out float heavyRadius)
        var (centerL, radiusL, centerH, radiusH) = combat.GetEditorHitboxCenters();
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(centerL, radiusL);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(centerH, radiusH);
    }
#endif
}
