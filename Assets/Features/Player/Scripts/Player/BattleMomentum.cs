/*
 * BattleMomentum — player flow-state resource.
 *
 * Fills by landing attacks (charged hits add more). Drains when the player takes
 * damage. Decays in two phases: slow decay during a grace window that resets
 * each time a hit lands, then fast decay once the grace window expires.
 *
 * Attach to the same GameObject as PlayerHealth and Combat/WeaponCombat.
 */

using UnityEngine;

public class BattleMomentum : MonoBehaviour
{
    // ========================================================================
    // SETTINGS
    // ========================================================================

    [Header("Momentum")]
    [Tooltip("Maximum momentum value.")]
    public float maxMomentum = 100f;

    [Tooltip("Momentum gained per hit landed (uncharged).")]
    public float momentumPerHit = 10f;

    [Tooltip("Multiplier applied to momentumPerHit for a fully charged hit. Lerped by charge scale.")]
    public float chargedHitMultiplier = 2.5f;

    [Header("Damage Drain")]
    [Tooltip("Flat momentum lost each time the player takes real (unblocked) damage.")]
    public float momentumDrainOnDamage = 25f;

    [Header("Decay — Two Phase")]
    [Tooltip("Seconds of slow decay after the last hit lands. Resets on each hit.")]
    public float graceDuration = 2.5f;

    [Tooltip("Momentum lost per second during the grace window.")]
    public float slowDecayRate = 3f;

    [Tooltip("Momentum lost per second after the grace window expires.")]
    public float fastDecayRate = 20f;

    [Header("Debug Bar")]
    [Tooltip("World-space height above the player origin to draw the bar.")]
    public float debugWorldHeight = 2.8f;

    public float barWidth  = 90f;
    public float barHeight = 7f;

    public Color barBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
    public Color barFillColor       = new Color(0.2f,  0.6f,  1f,   0.9f);  // blue
    public Color barHighlightColor  = new Color(0.4f,  0.9f,  1f,   0.95f); // bright cyan at 75 %+

    // ========================================================================
    // STATE
    // ========================================================================

    private float momentum;
    private float graceTimer;

    // ========================================================================
    // PUBLIC API
    // ========================================================================

    public float Momentum           => momentum;
    public float NormalizedMomentum => maxMomentum > 0f ? momentum / maxMomentum : 0f;

    /// <summary>Spend the given amount of momentum. Clamps to zero. Does not check affordability — caller must do that.</summary>
    public void ConsumeMomentum(float amount)
    {
        momentum = Mathf.Max(momentum - amount, 0f);
    }

    // ========================================================================
    // CACHED REFERENCES & TEXTURES
    // ========================================================================

    private PlayerHealth playerHealth;
    private Combat       combat;
    private Texture2D    bgTex;
    private Texture2D    fillTex;
    private bool         fillTexIsHighlight; // tracks which colour is baked into fillTex to avoid redundant Apply() calls

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        bgTex   = new Texture2D(1, 1);
        fillTex = new Texture2D(1, 1);
        bgTex.SetPixel(0, 0, barBackgroundColor);
        bgTex.Apply();
        fillTex.SetPixel(0, 0, barFillColor);
        fillTex.Apply();
    }

    void Start()
    {
        playerHealth = GetComponent<PlayerHealth>();
        if (playerHealth != null)
            playerHealth.OnDamageTaken += HandleDamageTaken;
        else
            Debug.LogWarning("[BattleMomentum] No PlayerHealth found on this GameObject.", this);

        // WeaponCombat inherits Combat, so GetComponentInChildren covers both.
        combat = GetComponentInChildren<Combat>(true);
        if (combat == null) combat = GetComponent<Combat>();
        if (combat != null)
            combat.OnHitConfirmed += HandleHitConfirmed;
        else
            Debug.LogWarning("[BattleMomentum] No Combat/WeaponCombat found on this GameObject or its children.", this);
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnDamageTaken -= HandleDamageTaken;
        if (combat       != null) combat.OnHitConfirmed      -= HandleHitConfirmed;
    }

    void Update()
    {
        if (momentum <= 0f) return;

        if (graceTimer > 0f)
        {
            graceTimer -= Time.deltaTime;
            momentum   -= slowDecayRate * Time.deltaTime;
        }
        else
        {
            momentum -= fastDecayRate * Time.deltaTime;
        }

        momentum = Mathf.Max(momentum, 0f);
    }

    // ========================================================================
    // EVENT CALLBACKS
    // ========================================================================

    void HandleHitConfirmed(AttackData attack, float chargeScale)
    {
        // chargeScale is 1.0 when uncharged and rises with charge level.
        // Map the extra-above-1 portion to the multiplier range.
        float chargeBonus = Mathf.Lerp(1f, chargedHitMultiplier, chargeScale - 1f);
        momentum   = Mathf.Min(momentum + momentumPerHit * chargeBonus, maxMomentum);
        graceTimer = graceDuration; // reset grace on every successful hit
    }

    void HandleDamageTaken(int damage)
    {
        momentum = Mathf.Max(momentum - momentumDrainOnDamage, 0f);
        // Grace timer is intentionally left alone — decay continues at its current rate.
    }

    // ========================================================================
    // DEBUG BAR
    // ========================================================================

    void OnGUI()
    {
        if (DebugSettings.Instance == null) return;
        if (!DebugSettings.Instance.ShouldShow(DebugSettings.Instance.showBattleMomentum)) return;
        if (Camera.main == null) return;

        Vector3 worldPos  = transform.position + Vector3.up * debugWorldHeight;
        Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
        if (screenPos.z < 0f) return; // Behind camera

        float screenY = Screen.height - screenPos.y;
        float x       = screenPos.x - barWidth * 0.5f;
        float fill    = NormalizedMomentum;

        // Background
        GUI.DrawTexture(new Rect(x, screenY, barWidth, barHeight), bgTex);

        // Fill — swap to highlight colour above 75 %
        if (fill > 0f)
        {
            bool wantHighlight = fill >= 0.75f;
            if (wantHighlight != fillTexIsHighlight)
            {
                fillTex.SetPixel(0, 0, wantHighlight ? barHighlightColor : barFillColor);
                fillTex.Apply();
                fillTexIsHighlight = wantHighlight;
            }
            GUI.DrawTexture(new Rect(x, screenY, barWidth * fill, barHeight), fillTex);
        }
    }
}
