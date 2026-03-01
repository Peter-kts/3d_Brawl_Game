/*
 * CombatHitboxDebugVisual.cs - Debug sphere for player attack hitbox (active + preview).
 * Add to same GameObject as Combat. Reads GetHitboxDebugState() and DebugSettings.
 */

using UnityEngine;

[RequireComponent(typeof(Combat))]
public class CombatHitboxDebugVisual : MonoBehaviour
{
    private Combat combat;
    private GameObject sphere;
    private MeshRenderer sphereRenderer;

    void Start()
    {
        combat = GetComponent<Combat>();
        sphere = DebugDrawHelper.CreatePrimitive(PrimitiveType.Sphere, "HitboxVisual", true);
        sphereRenderer = sphere.GetComponent<MeshRenderer>();
        sphere.SetActive(false);
    }

    void LateUpdate()
    {
        if (sphere == null || combat == null) return;
        DebugSettings debug = DebugSettings.Instance;
        bool showHitboxes = debug.ShouldShow(debug.showAttackHitboxes);
        bool showPreview = debug.ShouldShow(debug.showHitboxPreview);
        if (!showHitboxes && !showPreview)
        {
            sphere.SetActive(false);
            return;
        }

        var state = combat.GetHitboxDebugState();
        if (state.showActive)
        {
            sphere.SetActive(true);
            sphere.transform.position = state.center;
            sphere.transform.localScale = Vector3.one * state.radius * 2f;
            float pulse = Mathf.PingPong(Time.time * 8f, 0.3f) + 0.4f;
            Color c = state.color;
            c.a *= pulse;
            sphereRenderer.material.color = c;
        }
        else if (state.showPreview)
        {
            sphere.SetActive(true);
            sphere.transform.position = state.center;
            sphere.transform.localScale = Vector3.one * state.radius * 2f;
            sphereRenderer.material.color = state.color;
        }
        else
        {
            sphere.SetActive(false);
        }
    }

    void OnDestroy()
    {
        if (sphere != null) Destroy(sphere);
    }
}
