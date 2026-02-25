using UnityEngine;

/// <summary>
/// Debug: draw text above this enemy showing world position, local position (root), and mesh (Animator) world position.
/// Add to the enemy root (e.g. Enemy_01). Disable when not needed.
/// </summary>
public class EnemyMeshParentDebugVisual : MonoBehaviour
{
    [Tooltip("Show mesh/parent position labels above the enemy")]
    public bool show = true;
    [Tooltip("Height above root for the label")]
    public float height = 2.5f;

    private Animator _anim;
    private GUIStyle _labelStyle;

    void Awake()
    {
        _anim = GetComponentInChildren<Animator>();
    }

    void OnGUI()
    {
        if (!show || _anim == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 worldPos = transform.position;
        Vector3 localPos = transform.localPosition;
        Vector3 meshPos = _anim.transform.position;
        Vector3 labelWorld = worldPos + Vector3.up * height;
        Vector3 screen = cam.WorldToScreenPoint(labelWorld);
        screen.y = Screen.height - screen.y;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 12;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.alignment = TextAnchor.MiddleCenter;
        }

        string worldStr = $"World:  X={worldPos.x,8:F3}  Y={worldPos.y,8:F3}  Z={worldPos.z,8:F3}";
        string localStr = $"Local:  X={localPos.x,8:F3}  Y={localPos.y,8:F3}  Z={localPos.z,8:F3}";
        string meshStr = $"Mesh:   X={meshPos.x,8:F3}  Y={meshPos.y,8:F3}  Z={meshPos.z,8:F3}";
        float lineH = 18f;
        Rect r = new Rect(screen.x - 150f, screen.y - lineH, 300f, lineH * 3f);
        GUI.Label(r, worldStr + "\n" + localStr + "\n" + meshStr, _labelStyle);
    }
}
