/*
 * HealthDebugVisual.cs - Debug HP bar and value above any entity with IDamageable (EnemyHealth or PlayerHealth).
 * Add to same GameObject as EnemyHealth or PlayerHealth. Uses DebugSettings.showHealthIndicator.
 */

using UnityEngine;

public class HealthDebugVisual : MonoBehaviour
{
    [Tooltip("Height above entity for the health bar")]
    public float height = 2f;
    [Tooltip("Width of the bar in screen space")]
    public float barWidth = 80f;
    [Tooltip("Height of the bar in screen space")]
    public float barHeight = 8f;
    public Color barBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    public Color barFullColor = new Color(0f, 1f, 0.3f, 0.9f);
    public Color barLowColor = new Color(1f, 0.2f, 0f, 0.9f);
    [Tooltip("HP ratio below this uses barLowColor")]
    [Range(0f, 1f)]
    public float lowHpThreshold = 0.25f;

    private IDamageable damageable;
    private GUIStyle labelStyle;
    private bool labelStyleReady;
    private Texture2D labelBgTexture;
    private Texture2D barBgTexture;
    private Texture2D barFillTexture;
    private bool isEnemyHealthVisual;

    void Start()
    {
        damageable = GetComponent<EnemyHealth>() as IDamageable ?? GetComponent<PlayerHealth>() as IDamageable;
        isEnemyHealthVisual = GetComponent<EnemyHealth>() != null;
    }

    void OnGUI()
    {
        if (damageable == null) return;
        DebugSettings debug = DebugSettings.Instance;
        if (debug == null) return;
        bool shouldShow = isEnemyHealthVisual
            ? debug.ShouldShowEnemyHealthStats(debug.showHealthIndicator)
            : debug.ShouldShow(debug.showHealthIndicator);
        if (!shouldShow) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        int current = damageable.CurrentHp;
        int max = damageable.MaxHp;
        float ratio = max > 0 ? Mathf.Clamp01((float)current / max) : 0f;

        if (!labelStyleReady)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 12;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.normal.textColor = Color.white;
            labelStyle.padding = new RectOffset(4, 4, 2, 2);
            labelBgTexture = new Texture2D(1, 1);
            labelBgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            labelBgTexture.Apply();
            labelStyle.normal.background = labelBgTexture;
            barBgTexture = new Texture2D(1, 1);
            barBgTexture.SetPixel(0, 0, barBackgroundColor);
            barBgTexture.Apply();
            barFillTexture = new Texture2D(1, 1);
            barFillTexture.Apply();
            labelStyleReady = true;
        }

        Vector3 worldPos = transform.position + Vector3.up * height;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        float y = Screen.height - screenPos.y;

        // Bar: background then fill
        float barHalfW = barWidth * 0.5f;
        Rect barRect = new Rect(screenPos.x - barHalfW, y - barHeight * 0.5f - 14f, barWidth, barHeight);
        GUI.DrawTexture(barRect, barBgTexture);
        Color fillColor = ratio <= lowHpThreshold ? barLowColor : barFullColor;
        barFillTexture.SetPixel(0, 0, fillColor);
        barFillTexture.Apply();
        Rect fillRect = new Rect(barRect.x, barRect.y, barRect.width * ratio, barRect.height);
        GUI.DrawTexture(fillRect, barFillTexture);

        // Label "45/100"
        string text = current + "/" + max;
        float labelW = 56f;
        float labelH = 20f;
        Rect labelRect = new Rect(screenPos.x - labelW * 0.5f, y - labelH * 0.5f, labelW, labelH);
        GUI.Label(labelRect, text, labelStyle);
    }

    void OnDestroy()
    {
        if (labelBgTexture != null) Destroy(labelBgTexture);
        if (barBgTexture != null) Destroy(barBgTexture);
        if (barFillTexture != null) Destroy(barFillTexture);
    }
}
