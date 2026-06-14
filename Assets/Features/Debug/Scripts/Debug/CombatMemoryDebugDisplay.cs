/*
 * ============================================================================
 * COMBATMEMORYDEBUGDISPLAY.CS - Corner GUI for PlayerCombatMemory visualization
 * ============================================================================
 *
 * WHAT IT SHOWS:
 * --------------
 * Top-left corner panel with two sections:
 *
 *   COMBAT MEMORY
 *   ─────────────────────────────
 *   ► LIGHT   MID    0.2s ago    ← newest entry, arrow + full brightness
 *     LIGHT   MID    1.4s ago
 *     HEAVY   HIGH   2.1s ago
 *     LIGHT   LOW    3.5s ago
 *     MEDIUM  MID    5.0s ago    ← oldest entry, dimmer
 *   ─────────────────────────────
 *   ENEMIES  (2 watching)
 *   ● Enemy_01        IN RANGE   ← green dot = reading from memory
 *   ○ Enemy_02        14.2m      ← gray dot = out of range
 *
 * Additionally, a small "WATCHING" label is drawn above each enemy that is
 * actively within the awareness radius and targeting this player, so you can
 * see at a glance which enemies share the memory in the 3D view.
 *
 * SETUP:
 * ------
 * Add this component to the same GameObject as PlayerCombatMemory.
 * Toggle via DebugSettings.Instance.showCombatMemory.
 *
 * ============================================================================
 */

using UnityEngine;

[RequireComponent(typeof(PlayerCombatMemory))]
public class CombatMemoryDebugDisplay : MonoBehaviour
{
    // ========================================================================
    // CONFIG
    // ========================================================================

    [Header("Panel")]
    [Tooltip("Pixel offset from the top-left corner of the screen.")]
    public Vector2 panelOffset = new Vector2(10f, 10f);

    [Tooltip("Width of the memory panel in pixels.")]
    public float panelWidth = 230f;

    [Header("World Labels")]
    [Tooltip("Height above an enemy's feet to draw the WATCHING label.")]
    public float watchingLabelHeight = 3.2f;

    // ========================================================================
    // COLOR PALETTE
    // ========================================================================

    /* Heaviness colors — match the attack hitbox colors in DebugSettings */
    private static readonly Color ColorLight  = new Color(0.3f, 1.0f, 1.0f, 1f); // cyan
    private static readonly Color ColorMedium = new Color(0.9f, 0.9f, 0.9f, 1f); // near-white
    private static readonly Color ColorHeavy  = new Color(1.0f, 0.55f, 0.1f, 1f); // orange

    /* Height colors */
    private static readonly Color ColorLow  = new Color(1.0f, 0.85f, 0.3f, 1f); // yellow
    private static readonly Color ColorMid  = new Color(0.8f, 0.8f, 0.8f, 1f); // gray
    private static readonly Color ColorHigh = new Color(0.6f, 0.85f, 1.0f, 1f); // sky blue

    /* UI chrome */
    private static readonly Color ColorPanel      = new Color(0f,   0f,   0f,   0.72f);
    private static readonly Color ColorHeader     = new Color(0.9f, 0.9f, 0.9f, 1f);
    private static readonly Color ColorDivider    = new Color(0.4f, 0.4f, 0.4f, 0.8f);
    private static readonly Color ColorWatching   = new Color(0.2f, 1.0f, 0.4f, 1f);   // green
    private static readonly Color ColorOutOfRange = new Color(0.5f, 0.5f, 0.5f, 1f);   // gray
    private static readonly Color ColorNewest     = new Color(1.0f, 1.0f, 1.0f, 1f);
    private static readonly Color ColorOldest     = new Color(0.6f, 0.6f, 0.6f, 1f);

    // ========================================================================
    // CACHED REFERENCES
    // ========================================================================

    private PlayerCombatMemory memory;
    private Camera             mainCam;

    /* Enemy list is refreshed periodically — scene-wide object scan is expensive. */
    private SimpleEnemyAI[] cachedEnemies = new SimpleEnemyAI[0];
    private float           nextEnemyScanTime = 0f;
    private const float     EnemyScanInterval = 2f;

    /* Pre-allocated GUIStyles — created once in OnGUI on first call. */
    private GUIStyle styleNormal;
    private GUIStyle styleBold;
    private GUIStyle styleShadow;
    private bool     stylesInitialized;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        memory = GetComponent<PlayerCombatMemory>();
    }

    // ========================================================================
    // ENEMY SCAN
    // ========================================================================

    /* Refresh the enemy list on a timer to handle spawns/deaths cheaply. */
    void RefreshEnemiesIfNeeded()
    {
        if (Time.time < nextEnemyScanTime) return;
        cachedEnemies    = Object.FindObjectsByType<SimpleEnemyAI>(FindObjectsSortMode.None);
        nextEnemyScanTime = Time.time + EnemyScanInterval;
    }

    // ========================================================================
    // STYLE INIT
    // ========================================================================

    void EnsureStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;

        styleNormal = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Normal,
            alignment = TextAnchor.MiddleLeft,
            wordWrap  = false
        };

        styleBold = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap  = false
        };

        styleShadow = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap  = false
        };
    }

    // ========================================================================
    // ON GUI
    // ========================================================================

    void OnGUI()
    {
        if (memory == null) return;

        DebugSettings debug = DebugSettings.Instance;
        if (!debug.ShouldShow(debug.showCombatMemory)) return;

        EnsureStyles();
        RefreshEnemiesIfNeeded();

        if (mainCam == null) mainCam = Camera.main;

        DrawCornerPanel();
        DrawWorldWatchingLabels();
    }

    // ========================================================================
    // CORNER PANEL
    // ========================================================================

    void DrawCornerPanel()
    {
        const float lineH   = 18f;
        const float padX    = 8f;
        const float padY    = 6f;
        const float divH    = 1f;

        PlayerCombatMemory.MoveRecord[] moves = memory.GetRecentMoves();

        /* Count enemies to determine panel height before drawing. */
        int enemyCount = cachedEnemies.Length;

        /* Panel height: header + divider + up to capacity rows + divider + enemy section. */
        float contentRows = Mathf.Max(moves.Length, 1);             // at least one row for "—"
        float panelH = padY                                          // top pad
                     + lineH                                         // "COMBAT MEMORY" header
                     + padY * 0.5f + divH + padY * 0.5f             // divider
                     + contentRows * lineH                           // move rows
                     + padY * 0.5f + divH + padY * 0.5f             // divider
                     + lineH                                         // "ENEMIES" header row
                     + enemyCount * lineH                            // enemy rows
                     + padY;                                         // bottom pad

        Rect panelRect = new Rect(panelOffset.x, panelOffset.y, panelWidth, panelH);

        /* Dark background */
        GUI.color = ColorPanel;
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x  = panelRect.x + padX;
        float y  = panelRect.y + padY;
        float contentW = panelWidth - padX * 2f;

        /* ── Header ────────────────────────────────────────────── */
        styleBold.normal.textColor = ColorHeader;
        GUI.Label(new Rect(x, y, contentW, lineH), "COMBAT MEMORY", styleBold);
        y += lineH;

        DrawHorizontalLine(panelRect.x, y + padY * 0.5f, panelWidth, ColorDivider);
        y += padY * 0.5f + divH + padY * 0.5f;

        /* ── Move rows ─────────────────────────────────────────── */
        if (moves.Length == 0)
        {
            styleNormal.normal.textColor = ColorOutOfRange;
            GUI.Label(new Rect(x, y, contentW, lineH), "  — no moves recorded —", styleNormal);
            y += lineH;
        }
        else
        {
            for (int i = 0; i < moves.Length; i++)
            {
                /* Newest entry (i=0) is brightest; oldest is dimmer. */
                float ageFraction  = moves.Length > 1 ? (float)i / (moves.Length - 1) : 0f;
                float brightness   = Mathf.Lerp(1f, 0.5f, ageFraction);

                bool  isNewest    = i == 0;
                float rowX        = x;

                /* Arrow on newest entry */
                styleBold.normal.textColor = Color.Lerp(ColorNewest, ColorOldest, ageFraction);
                GUI.Label(new Rect(rowX, y, 14f, lineH), isNewest ? "►" : " ", styleBold);
                rowX += 14f;

                /* Heaviness label */
                Color hCol  = HeavinessColor(moves[i].heaviness);
                hCol.a     *= brightness;
                styleNormal.normal.textColor = hCol;
                GUI.Label(new Rect(rowX, y, 62f, lineH), HeavinessLabel(moves[i].heaviness), styleNormal);
                rowX += 62f;

                /* Height label */
                Color htCol = HeightColor(moves[i].height);
                htCol.a    *= brightness;
                styleNormal.normal.textColor = htCol;
                GUI.Label(new Rect(rowX, y, 46f, lineH), HeightLabel(moves[i].height), styleNormal);
                rowX += 46f;

                /* Age label */
                float age    = Time.time - moves[i].timestamp;
                string ageStr = age < 60f ? $"{age:F1}s" : "old";
                Color ageCol = ColorOutOfRange;
                ageCol.a    *= brightness;
                styleNormal.normal.textColor = ageCol;
                GUI.Label(new Rect(rowX, y, 50f, lineH), ageStr, styleNormal);

                y += lineH;
            }
        }

        DrawHorizontalLine(panelRect.x, y + padY * 0.5f, panelWidth, ColorDivider);
        y += padY * 0.5f + divH + padY * 0.5f;

        /* ── Enemies section ───────────────────────────────────── */
        int watchingCount = 0;
        foreach (var e in cachedEnemies)
            if (e != null && memory.IsEnemyWatching(e)) watchingCount++;

        styleBold.normal.textColor = ColorHeader;
        GUI.Label(new Rect(x, y, contentW, lineH), $"ENEMIES  ({watchingCount} watching)", styleBold);
        y += lineH;

        foreach (var enemy in cachedEnemies)
        {
            if (enemy == null) continue;

            bool watching = memory.IsEnemyWatching(enemy);
            float dist    = Vector3.Distance(transform.position, enemy.transform.position);

            /* Dot indicator */
            styleNormal.normal.textColor = watching ? ColorWatching : ColorOutOfRange;
            GUI.Label(new Rect(x, y, 14f, lineH), watching ? "●" : "○", styleNormal);

            /* Enemy name */
            styleNormal.normal.textColor = watching ? ColorWatching : ColorOutOfRange;
            string name   = enemy.gameObject.name;
            /* Truncate long names to keep the panel tidy */
            if (name.Length > 14) name = name.Substring(0, 13) + "…";
            GUI.Label(new Rect(x + 14f, y, 110f, lineH), name, styleNormal);

            /* Status / distance */
            string status = watching ? "IN RANGE" : $"{dist:F1}m";
            styleNormal.normal.textColor = watching ? ColorWatching : ColorOutOfRange;
            GUI.Label(new Rect(x + 130f, y, 90f, lineH), status, styleNormal);

            y += lineH;
        }
    }

    // ========================================================================
    // WORLD LABELS — "WATCHING" above each in-range enemy
    // ========================================================================

    void DrawWorldWatchingLabels()
    {
        if (mainCam == null) return;

        foreach (var enemy in cachedEnemies)
        {
            if (enemy == null) continue;
            if (!memory.IsEnemyWatching(enemy)) continue;

            Vector3 worldPos  = enemy.transform.position + Vector3.up * watchingLabelHeight;
            Vector3 screenPos = mainCam.WorldToScreenPoint(worldPos);
            if (screenPos.z <= 0f) continue; // behind camera

            float guiY = Screen.height - screenPos.y;

            /* Shadow */
            styleShadow.normal.textColor = Color.black;
            GUI.Label(new Rect(screenPos.x - 51f, guiY - 11f, 102f, 20f), "WATCHING", styleShadow);

            /* Foreground */
            styleShadow.normal.textColor = ColorWatching;
            GUI.Label(new Rect(screenPos.x - 50f, guiY - 10f, 100f, 20f), "WATCHING", styleShadow);
        }
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    void DrawHorizontalLine(float x, float y, float width, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, width, 1f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    static Color HeavinessColor(AttackHeaviness h) => h switch
    {
        AttackHeaviness.Light  => ColorLight,
        AttackHeaviness.Heavy  => ColorHeavy,
        _                      => ColorMedium
    };

    static string HeavinessLabel(AttackHeaviness h) => h switch
    {
        AttackHeaviness.Light  => "LIGHT",
        AttackHeaviness.Medium => "MEDIUM",
        AttackHeaviness.Heavy  => "HEAVY",
        _                      => h.ToString().ToUpper()
    };

    static Color HeightColor(AttackHeight h) => h switch
    {
        AttackHeight.Low  => ColorLow,
        AttackHeight.High => ColorHigh,
        _                 => ColorMid
    };

    static string HeightLabel(AttackHeight h) => h switch
    {
        AttackHeight.Low  => "LOW",
        AttackHeight.Mid  => "MID",
        AttackHeight.High => "HIGH",
        _                 => h.ToString().ToUpper()
    };
}
