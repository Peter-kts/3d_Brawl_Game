/*
 * EnemyFacingDebugVisual.cs - Debug line showing enemy facing direction.
 * Add to same GameObject as SimpleEnemyAI. Reads DebugSettings for visibility.
 */

using UnityEngine;

[RequireComponent(typeof(Transform))]
public class EnemyFacingDebugVisual : MonoBehaviour
{
    [Tooltip("Length of the direction line")]
    public float lineLength = 1.5f;
    [Tooltip("Width of the direction line")]
    public float lineWidth = 0.06f;
    [Tooltip("Color of the line (overridden by DebugSettings if desired)")]
    public Color lineColor = Color.red;

    private LineRenderer line;

    void Start()
    {
        line = DebugDrawHelper.CreateLine(transform, lineWidth, lineWidth * 0.3f);
        line.startColor = lineColor;
        line.endColor = lineColor;
    }

    void LateUpdate()
    {
        if (line == null) return;
        var health = GetComponent<EnemyHealth>();
        if (health != null && health.IsDying)
        {
            line.enabled = false;
            return;
        }
        DebugSettings debug = DebugSettings.Instance;
        bool shouldShow = debug.ShouldShow(debug.showEnemyFacing);
        line.enabled = shouldShow;
        if (!shouldShow) return;

        Vector3 start = transform.position + Vector3.up * 1f;
        Vector3 end = start + transform.forward * lineLength;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startColor = lineColor;
        line.endColor = lineColor;
    }

    void OnDestroy()
    {
        if (line != null && line.gameObject != null)
            Destroy(line.gameObject);
    }
}
