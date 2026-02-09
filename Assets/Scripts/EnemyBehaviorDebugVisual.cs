/*
 * EnemyBehaviorDebugVisual.cs - Debug cube above enemy showing current AI behavior state.
 * Add to same GameObject as SimpleEnemyAI. Reads CurrentBehaviorStateName and DebugSettings.
 */

using UnityEngine;

[RequireComponent(typeof(SimpleEnemyAI))]
public class EnemyBehaviorDebugVisual : MonoBehaviour
{
    [Tooltip("Height above enemy for the indicator")]
    public float height = 2.7f;
    [Tooltip("Size of the cube")]
    public float size = 0.25f;
    public Color chaseColor = new Color(0.2f, 0.5f, 1f, 0.8f);
    public Color circlingColor = new Color(0f, 1f, 1f, 0.8f);
    public Color preAttackColor = new Color(1f, 1f, 0f, 0.8f);
    public Color attackingColor = new Color(1f, 0f, 0f, 0.8f);

    private SimpleEnemyAI ai;
    private GameObject indicator;
    private MeshRenderer indicatorRenderer;

    void Start()
    {
        ai = GetComponent<SimpleEnemyAI>();
        indicator = DebugDrawHelper.CreatePrimitive(PrimitiveType.Cube, "BehaviorIndicator", true);
        indicatorRenderer = indicator.GetComponent<MeshRenderer>();
        indicator.transform.localScale = Vector3.one * size;
    }

    void LateUpdate()
    {
        if (indicator == null || ai == null) return;
        if (ai.Health != null && ai.Health.IsDying)
        {
            indicator.SetActive(false);
            return;
        }
        DebugSettings debug = DebugSettings.Instance;
        bool shouldShow = debug.ShouldShow(debug.showEnemyBehaviorIndicator);
        indicator.SetActive(shouldShow);
        if (!shouldShow) return;

        indicator.transform.position = transform.position + Vector3.up * height;
        indicator.transform.Rotate(Vector3.up, 90f * Time.deltaTime);

        string stateName = ai.CurrentBehaviorStateName;
        Color targetColor;
        float pulseSpeed = 0f;
        switch (stateName)
        {
            case "Chase": targetColor = chaseColor; break;
            case "Circling": targetColor = circlingColor; break;
            case "PreAttack": targetColor = preAttackColor; pulseSpeed = 10f; break;
            case "Attacking": targetColor = attackingColor; pulseSpeed = 14f; break;
            default: targetColor = Color.gray; break;
        }

        if (pulseSpeed > 0f)
        {
            float pulse = Mathf.PingPong(Time.time * pulseSpeed, 0.4f) + 0.6f;
            targetColor.a *= pulse;
            float scaleMultiplier = 1f + (pulse - 0.8f) * 0.5f;
            indicator.transform.localScale = Vector3.one * size * scaleMultiplier;
        }
        else
        {
            indicator.transform.localScale = Vector3.one * size;
        }
        indicatorRenderer.material.color = targetColor;
    }

    void OnDestroy()
    {
        if (indicator != null) Destroy(indicator);
    }
}
