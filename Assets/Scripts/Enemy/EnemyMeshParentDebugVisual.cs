using UnityEngine;

/// <summary>
/// Debug: draw text above this enemy showing mesh (Animator) position and parent (root) position.
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

        Vector3 rootPos = transform.position;
        Vector3 meshPos = _anim.transform.position;
        Vector3 labelWorld = rootPos + Vector3.up * height;
        Vector3 screen = cam.WorldToScreenPoint(labelWorld);
        screen.y = Screen.height - screen.y;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 12;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.alignment = TextAnchor.MiddleCenter;
        }

        string meshStr = $"Mesh:  X={meshPos.x,8:F3}  Y={meshPos.y,8:F3}  Z={meshPos.z,8:F3}";
        string parentStr = $"Parent: X={rootPos.x,8:F3}  Y={rootPos.y,8:F3}  Z={rootPos.z,8:F3}";
        float lineH = 18f;
        Rect r = new Rect(screen.x - 150f, screen.y - lineH, 300f, lineH * 2f);
        GUI.Label(r, meshStr + "\n" + parentStr, _labelStyle);
    }
}
