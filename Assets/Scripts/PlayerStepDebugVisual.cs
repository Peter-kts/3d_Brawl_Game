/*
 * PlayerStepDebugVisual.cs - Debug cylinders showing step cycle (which foot is pushing).
 * Add to same GameObject as PlayerController. Reads step state and DebugSettings.
 */

using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class PlayerStepDebugVisual : MonoBehaviour
{
    [Tooltip("Show step indicators")]
    public bool showStepVisuals = true;
    [Tooltip("Size of step indicators")]
    public float stepVisualSize = 0.15f;
    [Tooltip("Horizontal offset to the right of character for step markers")]
    public float stepVisualOffset = 1.5f;
    public Color stepActiveColor = new Color(0f, 1f, 0.5f, 0.8f);
    public Color stepInactiveColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);

    private PlayerController player;
    private GameObject step1;
    private GameObject step2;
    private MeshRenderer renderer1;
    private MeshRenderer renderer2;

    void Start()
    {
        player = GetComponent<PlayerController>();
        step1 = DebugDrawHelper.CreatePrimitive(PrimitiveType.Cylinder, "StepVisual1", true);
        step1.transform.SetParent(transform);
        renderer1 = step1.GetComponent<MeshRenderer>();
        step2 = DebugDrawHelper.CreatePrimitive(PrimitiveType.Cylinder, "StepVisual2", true);
        step2.transform.SetParent(transform);
        renderer2 = step2.GetComponent<MeshRenderer>();
        step1.SetActive(false);
        step2.SetActive(false);
    }

    void LateUpdate()
    {
        if (step1 == null || step2 == null || player == null) return;
        bool shouldShow = showStepVisuals && player.enableStepSync;
        step1.SetActive(shouldShow);
        step2.SetActive(shouldShow);
        if (!shouldShow) return;

        float normalizedTime = player.StepCycleNormalizedTime;
        bool step1Active = normalizedTime >= player.step1StartTime && normalizedTime < player.step1EndTime;
        bool step2Active = normalizedTime >= player.step2StartTime && normalizedTime < player.step2EndTime;

        Vector3 step1Pos = transform.position + transform.right * stepVisualOffset + Vector3.up * 0.5f;
        Vector3 step2Pos = transform.position + transform.right * stepVisualOffset + Vector3.up * 0.3f;
        step1.transform.position = step1Pos;
        step2.transform.position = step2Pos;

        Vector3 scale = new Vector3(stepVisualSize, 0.01f, stepVisualSize);
        step1.transform.localScale = scale;
        step2.transform.localScale = scale;

        renderer1.material.color = step1Active ? stepActiveColor : stepInactiveColor;
        renderer2.material.color = step2Active ? stepActiveColor : stepInactiveColor;
    }

    void OnDestroy()
    {
        if (step1 != null) Destroy(step1);
        if (step2 != null) Destroy(step2);
    }
}
