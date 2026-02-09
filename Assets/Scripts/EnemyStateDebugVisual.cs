/*
 * EnemyStateDebugVisual.cs - Debug sphere above enemy showing state (stunned/airborne/normal).
 * Add to same GameObject as EnemyHealth. Reads IsStunned, IsAirborne and DebugSettings.
 */

using UnityEngine;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyStateDebugVisual : MonoBehaviour
{
    [Tooltip("Height above enemy for the indicator")]
    public float height = 2.2f;
    [Tooltip("Size of the sphere")]
    public float size = 0.3f;
    public Color stunnedColor = new Color(1f, 0.3f, 0f, 0.8f);
    public Color airborneColor = new Color(0.5f, 0f, 1f, 0.8f);
    public Color normalColor = new Color(0f, 1f, 0f, 0.5f);

    private EnemyHealth health;
    private GameObject indicator;
    private MeshRenderer indicatorRenderer;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        indicator = DebugDrawHelper.CreatePrimitive(PrimitiveType.Sphere, "StateIndicator", true);
        indicatorRenderer = indicator.GetComponent<MeshRenderer>();
        indicator.transform.localScale = Vector3.one * size;
    }

    void LateUpdate()
    {
        if (indicator == null || health == null) return;
        DebugSettings debug = DebugSettings.Instance;
        bool shouldShow = debug.ShouldShow(debug.showEnemyStateIndicator);
        indicator.SetActive(shouldShow);
        if (!shouldShow) return;

        indicator.transform.position = transform.position + Vector3.up * height;

        Color targetColor;
        float pulseSpeed = 0f;
        if (health.IsAirborne)
        {
            targetColor = airborneColor;
            pulseSpeed = 12f;
        }
        else if (health.IsStunned)
        {
            targetColor = stunnedColor;
            pulseSpeed = 8f;
        }
        else
        {
            targetColor = normalColor;
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
