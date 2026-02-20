/*
 * PlayerFacingDebugVisual.cs - Debug line showing player facing (blue = free roam, red = combat).
 * Add to same GameObject as PlayerController. Reads DebugSettings and IsInCombatMode.
 */

using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class PlayerFacingDebugVisual : MonoBehaviour
{
    [Tooltip("Length of the direction line")]
    public float lineLength = 2f;
    [Tooltip("Width of the direction line")]
    public float lineWidth = 0.08f;

    private PlayerController player;
    private LineRenderer line;

    void Start()
    {
        player = GetComponent<PlayerController>();
        line = DebugDrawHelper.CreateLine(transform, lineWidth, lineWidth * 0.3f);
    }

    void LateUpdate()
    {
        if (line == null || player == null) return;
        DebugSettings debug = DebugSettings.Instance;
        bool shouldShow = debug.ShouldShow(debug.showPlayerFacing);
        line.enabled = shouldShow;
        if (!shouldShow) return;

        Color color = player.IsInCombatMode ? debug.combatModeFacingColor : debug.freeRoamFacingColor;
        line.startColor = color;
        line.endColor = color;

        Vector3 start = transform.position + Vector3.up * 1f;
        Vector3 end = start + transform.forward * lineLength;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = lineWidth;
        line.endWidth = lineWidth * 0.3f;
    }

    void OnDestroy()
    {
        if (line != null && line.gameObject != null)
            Destroy(line.gameObject);
    }
}
