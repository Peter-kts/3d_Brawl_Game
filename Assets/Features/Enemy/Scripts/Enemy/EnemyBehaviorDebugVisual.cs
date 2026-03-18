/*
 * EnemyBehaviorDebugVisual.cs - Debug cube + text label above enemy showing current AI behavior state.
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
    public Color backingOffColor = new Color(0f, 0.6f, 0.4f, 0.8f);
    public Color readingColor = new Color(1f, 0.7f, 0f, 0.9f);
    public Color interruptingColor = new Color(1f, 0.3f, 0f, 0.9f);

    private SimpleEnemyAI ai;
    private GameObject indicator;
    private MeshRenderer indicatorRenderer;

    // Label drawn in OnGUI
    private string currentLabel = "";
    private Color currentLabelColor = Color.white;
    private Camera mainCam;

    void Start()
    {
        ai = GetComponent<SimpleEnemyAI>();
        mainCam = Camera.main;
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
        if (!shouldShow) { currentLabel = ""; return; }

        indicator.transform.position = transform.position + Vector3.up * height;
        indicator.transform.Rotate(Vector3.up, 90f * Time.deltaTime);

        string stateName = ai.CurrentBehaviorStateName;
        Color targetColor;
        float pulseSpeed = 0f;
        switch (stateName)
        {
            case "Chase":        targetColor = chaseColor;        currentLabel = "CHASE";       currentLabelColor = chaseColor;        break;
            case "Circling":     targetColor = circlingColor;     currentLabel = "CIRCLING";    currentLabelColor = circlingColor;     break;
            case "PreAttack":    targetColor = preAttackColor;    currentLabel = "PRE-ATTACK";  currentLabelColor = preAttackColor;    pulseSpeed = 10f; break;
            case "Attacking":    targetColor = attackingColor;    currentLabel = "ATTACKING";   currentLabelColor = attackingColor;    pulseSpeed = 14f; break;
            case "BackingOff":   targetColor = backingOffColor;   currentLabel = "BACKING OFF"; currentLabelColor = backingOffColor;   pulseSpeed = 5f;  break;
            case "Reading":      targetColor = readingColor;      currentLabel = "READING...";  currentLabelColor = readingColor;      pulseSpeed = 7f;  break;
            case "Interrupting": targetColor = interruptingColor; currentLabel = "INTERRUPT!";  currentLabelColor = interruptingColor; pulseSpeed = 12f; break;
            default:             targetColor = Color.gray;        currentLabel = stateName;     currentLabelColor = Color.gray;        break;
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

    void OnGUI()
    {
        if (string.IsNullOrEmpty(currentLabel)) return;
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        Vector3 worldPos = transform.position + Vector3.up * (height + 0.35f);
        Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);
        if (screenPos.z <= 0f) return; // behind camera

        // Flip Y — GUI origin is top-left, screen origin is bottom-left
        float guiY = Screen.height - screenPos.y;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        // Shadow
        style.normal.textColor = Color.black;
        GUI.Label(new Rect(screenPos.x - 51f, guiY - 11f, 102f, 22f), currentLabel, style);

        // Foreground
        style.normal.textColor = currentLabelColor;
        GUI.Label(new Rect(screenPos.x - 50f, guiY - 10f, 100f, 20f), currentLabel, style);
    }

    void OnDestroy()
    {
        if (indicator != null) Destroy(indicator);
    }
}
