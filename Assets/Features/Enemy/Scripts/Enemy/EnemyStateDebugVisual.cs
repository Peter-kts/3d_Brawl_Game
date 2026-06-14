/*
 * EnemyStateDebugVisual.cs - Debug sphere above enemy showing state with label (Dying/Airborne/Crashed/Stunned/GettingUp/Normal).
 * Add to same GameObject as EnemyHealth. Uses SimpleEnemyAI.CurrentState when present (single source of truth), else falls back to health flags.
 */

using UnityEngine;

[RequireComponent(typeof(EnemyHealth))]
public class EnemyStateDebugVisual : MonoBehaviour
{
    [Tooltip("Height above enemy for the indicator")]
    public float height = 2.2f;
    [Tooltip("Size of the sphere")]
    public float size = 0.3f;
    public Color dyingColor = new Color(0.5f, 0f, 0f, 0.9f);
    public Color airborneColor = new Color(0.5f, 0f, 1f, 0.8f);
    public Color crashedColor = new Color(0.9f, 0.4f, 0f, 0.8f);
    public Color stunnedColor = new Color(1f, 0.3f, 0f, 0.8f);
    public Color standingStunnedColor = new Color(1f, 0.95f, 0f, 0.9f);
    public Color gettingUpColor = new Color(1f, 0.6f, 0f, 0.8f);
    public Color normalColor = new Color(0f, 1f, 0f, 0.5f);

    private EnemyHealth health;
    private SimpleEnemyAI enemyAI;
    private EnemyStunMeter stunMeter;
    private GameObject indicator;
    private MeshRenderer indicatorRenderer;
    private string currentStateText = "Normal";
    private string currentReactionText = "None";
    private string currentStunTimerText = "HS:0.00s SS:0.00s";
    private Color currentStateColor = Color.green;
    private Color currentTimerColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    private GUIStyle labelStyle;
    private bool labelStyleReady;
    private Texture2D labelBgTexture;

    void Start()
    {
        health = GetComponent<EnemyHealth>();
        enemyAI = GetComponent<SimpleEnemyAI>();
        stunMeter = GetComponent<EnemyStunMeter>();
        indicator = DebugDrawHelper.CreatePrimitive(PrimitiveType.Sphere, "StateIndicator", true);
        indicatorRenderer = indicator.GetComponent<MeshRenderer>();
        indicator.transform.localScale = Vector3.one * size;
    }

    void LateUpdate()
    {
        if (indicator == null || health == null) return;
        DebugSettings debug = DebugSettings.Instance;
        bool shouldShow = debug.ShouldShowEnemyHealthStats(debug.showEnemyStateIndicator);
        indicator.SetActive(shouldShow);
        if (!shouldShow) return;

        indicator.transform.position = transform.position + Vector3.up * height;

        EnemyState state = enemyAI != null ? enemyAI.CurrentState : GetStateFromHealth();
        currentReactionText = enemyAI != null ? enemyAI.CurrentReactionDebug : "None";
        float hitstunRemaining = health != null ? health.HitstunRemaining : 0f;
        float standingRemaining = stunMeter != null ? stunMeter.RemainingStandingStunTime : 0f;
        currentStunTimerText = $"HS:{hitstunRemaining:0.00}s SS:{standingRemaining:0.00}s";
        if (standingRemaining > 0f)
            currentTimerColor = new Color(1f, 0.65f, 0.15f, 1f);   // orange: standing stun active
        else if (hitstunRemaining > 0f)
            currentTimerColor = new Color(1f, 0.35f, 0.35f, 1f);   // red: hitstun only
        else
            currentTimerColor = new Color(0.85f, 0.85f, 0.85f, 1f); // neutral: no stun
        Color targetColor;
        float pulseSpeed = 0f;
        switch (state)
        {
            case EnemyState.Dying:
                currentStateText = "Dying";
                targetColor = dyingColor;
                pulseSpeed = 6f;
                break;
            case EnemyState.Airborne:
                currentStateText = "Airborne";
                targetColor = airborneColor;
                pulseSpeed = 12f;
                break;
            case EnemyState.Crashed:
                currentStateText = "Crashed";
                targetColor = crashedColor;
                pulseSpeed = 6f;
                break;
            case EnemyState.Prone:
                currentStateText = "Prone";
                targetColor = crashedColor;
                pulseSpeed = 6f;
                break;
            case EnemyState.Hitstunned:
                currentStateText = "Hitstunned";
                targetColor = stunnedColor;
                pulseSpeed = 8f;
                break;
            case EnemyState.Stunned:
                currentStateText = "Stunned";
                targetColor = standingStunnedColor;
                pulseSpeed = 4f;
                break;
            case EnemyState.GettingUp:
                currentStateText = "GettingUp";
                targetColor = gettingUpColor;
                pulseSpeed = 6f;
                break;
            default:
                currentStateText = "Normal";
                targetColor = normalColor;
                break;
        }
        currentStateColor = targetColor;

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

    /// <summary>Fallback when SimpleEnemyAI is not present; uses same priority order from health flags only (Crashed not represented without AI).</summary>
    EnemyState GetStateFromHealth()
    {
        if (health == null) return EnemyState.Normal;
        if (health.IsDying) return EnemyState.Dying;
        if (health.IsAirborne) return EnemyState.Airborne;
        if (health.IsHitstunned) return EnemyState.Hitstunned;
        if (health.IsGettingUp) return EnemyState.GettingUp;
        return EnemyState.Normal;
    }

    void OnGUI()
    {
        if (health == null || indicator == null) return;
        DebugSettings debug = DebugSettings.Instance;
        if (!debug.ShouldShowEnemyHealthStats(debug.showEnemyStateIndicator)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        if (!labelStyleReady)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Bold;
            labelStyle.alignment = TextAnchor.MiddleCenter;
            labelStyle.normal.textColor = Color.white;
            labelStyle.padding = new RectOffset(4, 4, 2, 2);
            labelBgTexture = new Texture2D(1, 1);
            labelBgTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.75f));
            labelBgTexture.Apply();
            labelStyle.normal.background = labelBgTexture;
            labelStyleReady = true;
        }

        Vector3 worldPos = transform.position + Vector3.up * height;
        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
        float y = Screen.height - screenPos.y;
        float w = 170f;
        float h = 54f;
        Rect bgRect = new Rect(screenPos.x - w * 0.5f, y - h * 0.5f, w, h);
        labelStyle.normal.textColor = currentStateColor;
        string stateAndReactionText = currentStateText + "\nRx: " + currentReactionText;
        GUI.Label(bgRect, stateAndReactionText, labelStyle);
        Color prevColor = GUI.color;
        GUI.color = currentTimerColor;
        Rect timerRect = new Rect(bgRect.x, bgRect.y + 34f, bgRect.width, 16f);
        GUI.Label(timerRect, currentStunTimerText, labelStyle);
        GUI.color = prevColor;
    }

    void OnDestroy()
    {
        if (indicator != null) Destroy(indicator);
        if (labelBgTexture != null) Destroy(labelBgTexture);
    }
}
