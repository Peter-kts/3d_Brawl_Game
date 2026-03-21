/*
 * ============================================================================
 * DEBUGSETTINGS.CS - Centralized debug visualization control
 * ============================================================================
 * 
 * PURPOSE:
 * --------
 * 
 * Single place to enable/disable ALL debug visualizations in the game.
 * Instead of hunting through multiple scripts, toggle everything here.
 * 
 * USAGE:
 * ------
 * 
 * 1. Add this script to a GameObject in your scene (e.g., "DebugManager")
 * 2. Toggle settings in the Inspector
 * 3. Other scripts check DebugSettings.Instance.showX
 * 
 * This is a SINGLETON - only one instance exists, accessible from anywhere.
 * 
 * ============================================================================
 */

using UnityEngine;

public class DebugSettings : MonoBehaviour
{
    // ========================================================================
    // SINGLETON PATTERN
    // ========================================================================
    
    private static DebugSettings _instance;
    
    /// <summary>
    /// Global access to debug settings. Creates instance if needed.
    /// </summary>
    public static DebugSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<DebugSettings>();
                
                // If no instance exists in scene, create one
                if (_instance == null)
                {
                    GameObject go = new GameObject("DebugSettings");
                    _instance = go.AddComponent<DebugSettings>();
                }
            }
            return _instance;
        }
    }
    
    void Awake()
    {
        // Enforce singleton
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ========================================================================
    // MASTER TOGGLE
    // ========================================================================
    
    [Header("Master Control")]
    [Tooltip("Master toggle - disables ALL debug visualizations when off")]
    public bool enableAllDebug = true;

    // ========================================================================
    // PLAYER VISUALIZATION
    // ========================================================================
    
    [Header("Player")]
    [Tooltip("Show facing direction line (blue = free roam, red = combat mode)")]
    public bool showPlayerFacing = true;
    
    [Tooltip("Color of facing line in Free Roam mode")]
    public Color freeRoamFacingColor = Color.blue;
    
    [Tooltip("Color of facing line in Combat Mode")]
    public Color combatModeFacingColor = Color.red;

    // ========================================================================
    // COMBAT VISUALIZATION
    // ========================================================================
    
    [Header("Combat")]
    [Tooltip("Show attack hitbox spheres")]
    public bool showAttackHitboxes = true;
    
    [Tooltip("Show hitbox preview when in Combat Mode (where attack will land)")]
    public bool showHitboxPreview = true;
    
    [Tooltip("Color of light attack hitbox")]
    public Color lightAttackColor = new Color(0f, 1f, 1f, 0.5f);
    
    [Tooltip("Color of heavy attack hitbox")]
    public Color heavyAttackColor = new Color(1f, 0.5f, 0f, 0.5f);

    [Tooltip("Log attack start and hitbox fire timing to Console (for debugging hitbox vs animation desync)")]
    public bool logAttackTiming = false;

    // ========================================================================
    // THREAT SYSTEM VISUALIZATION
    // ========================================================================
    
    [Header("Threat System")]
    [Tooltip("Show the focus cone (where soft auto-facing works)")]
    public bool showFocusCone = true;
    
    [Tooltip("Show the snap cone (used for combat mode entry snap)")]
    public bool showSnapCone = true;
    
    [Tooltip("Show lines to tracked threats")]
    public bool showThreatLines = true;
    
    [Tooltip("Show threat priority indicators above enemies")]
    public bool showThreatIndicators = true;
    
    [Tooltip("Color of the focus cone")]
    public Color focusConeColor = new Color(1f, 1f, 0f, 0.15f);
    
    [Tooltip("Color of the snap cone")]
    public Color snapConeColor = new Color(1f, 0.7f, 0f, 0.12f);
    
    [Tooltip("Color of threat connection lines")]
    public Color threatLineColor = new Color(1f, 0.5f, 0f, 0.5f);

    // ========================================================================
    // ENEMY VISUALIZATION
    // ========================================================================
    
    [Header("Enemies")]
    [Tooltip("Show enemy facing direction")]
    public bool showEnemyFacing = true;
    
    [Tooltip("Show enemy state indicator (stunned/airborne/normal)")]
    public bool showEnemyStateIndicator = true;
    
    [Tooltip("Show enemy behavior indicator (chase/circling/preattack/attacking)")]
    public bool showEnemyBehaviorIndicator = true;

    // ========================================================================
    // HEALTH VISUALIZATION
    // ========================================================================

    [Header("Health")]
    [Tooltip("Show HP bar and value above player and enemies (requires HealthDebugVisual on same GameObject as EnemyHealth/PlayerHealth)")]
    public bool showHealthIndicator = true;

    [Tooltip("Show stun meter bar above enemies (requires EnemyStunMeter on same GameObject as EnemyHealth)")]
    public bool showEnemyStunMeter = true;

    [Tooltip("Show battle momentum bar above the player (requires BattleMomentum on same GameObject as PlayerHealth)")]
    public bool showBattleMomentum = true;

    // ========================================================================
    // HELPER METHODS
    // ========================================================================
    
    /// <summary>
    /// Check if a specific visualization should be shown.
    /// Respects master toggle.
    /// </summary>
    public bool ShouldShow(bool specificToggle)
    {
        return enableAllDebug && specificToggle;
    }
    
    /// <summary>
    /// Disable all visualizations at once
    /// </summary>
    public void DisableAll()
    {
        enableAllDebug = false;
    }
    
    /// <summary>
    /// Enable all visualizations at once
    /// </summary>
    public void EnableAll()
    {
        enableAllDebug = true;
        showPlayerFacing = true;
        showAttackHitboxes = true;
        showHitboxPreview = true;
        showFocusCone = true;
        showThreatLines = true;
        showThreatIndicators = true;
        showEnemyFacing = true;
        showEnemyStateIndicator = true;
        showEnemyBehaviorIndicator = true;
        showHealthIndicator = true;
        showEnemyStunMeter = true;
    }
}
