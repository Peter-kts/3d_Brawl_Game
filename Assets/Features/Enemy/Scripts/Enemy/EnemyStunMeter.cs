/*
 * ============================================================================
 * ENEMYSTUNMETER.CS - Hidden stun buildup that triggers a standing stun state
 * ============================================================================
 *
 * STANDING STUN SYSTEM:
 * ---------------------
 *
 * Each player hit adds stunBuildup (from AttackData) to the enemy's hidden
 * stun meter (0–1). When the meter reaches 1, the enemy enters standing stun:
 *   - AI behavior and attacks are blocked (CanAct returns false via EnemyState)
 *   - SimpleEnemyAI reads CurrentPhase to play the entry then loop animation
 *   - After standingStunDuration seconds the meter resets and the enemy recovers
 *
 * ANIMATION PHASES:
 * -----------------
 * Standing stun has two phases:
 *
 *   Entry  →  Loop
 *
 *   Entry: one-shot animation that plays when stun first triggers (stagger in).
 *          Duration set by standingStunEntryDuration — should match the clip.
 *   Loop:  looping idle that holds until the stun timer expires.
 *
 * If the stun expires before the entry animation finishes, the phase returns
 * to None immediately. SimpleEnemyAI detects this and never triggers the loop,
 * so no animation glitch occurs. The Animator exits via the IsStandingStunned
 * bool going false — set up an Any State or normal transition for cleanup.
 *
 * TWO-PHASE DECAY:
 * ----------------
 * After each hit the grace timer resets and the meter drains at slowDecayRate.
 * Once the grace period expires without a hit, decay switches to fastDecayRate.
 * This rewards sustained pressure — back off and buildup drains quickly.
 *
 * STUN VALUE SOURCE:
 * ------------------
 * AttackData.stunBuildup is a flat per-attack value for now.
 * TODO: replace with per-move ComboSet stun values when that system is ready.
 *
 * SETUP:
 * ------
 * Add this component to the same GameObject as EnemyHealth. Optional — enemies
 * without it simply never enter standing stun.
 *
 * In EnemyAnimationConfig set standingStunEntryStateName / standingStunLoopStateName
 * and standingStunLayer. SimpleEnemyAI calls animator.Play() on phase transitions.
 * Also add the standingStunParameter Bool to the Animator Controller — SimpleEnemyAI
 * sets it true/false so you can drive exit transitions from it.
 *
 * DEBUG:
 * ------
 * A yellow bar is drawn above the enemy when DebugSettings.showEnemyStunMeter
 * is enabled. The bar turns orange while standing stun is active.
 *
 * ============================================================================
 */

using UnityEngine;

public class EnemyStunMeter : MonoBehaviour
{
    // ========================================================================
    // PHASE ENUM
    // ========================================================================

    /// <summary>Current animation phase of the standing stun state.</summary>
    public enum StunPhase
    {
        /// <summary>Not in standing stun; meter is draining or empty.</summary>
        None,
        /// <summary>Standing stun just triggered; entry animation is playing.</summary>
        Entry,
        /// <summary>Entry finished and stun is still active; loop animation is playing.</summary>
        Loop
    }

    // ========================================================================
    // CONFIGURATION
    // ========================================================================

    [Header("Standing Stun")]
    [Tooltip("How long (seconds) the enemy is locked in standing stun once the meter fills.")]
    public float standingStunDuration = 2f;

    [Tooltip("Duration of the entry animation (seconds). Should match the clip length. " +
             "Once this elapses and stun is still active, the loop phase begins.")]
    public float standingStunEntryDuration = 0.6f;

    [Tooltip("Knockback force required for the triggering hit to be treated as a knockback stun (plays KnockbackStunEntry/Loop). Based on attack.knockback.")]
    public float knockbackStunThreshold = 8f;

    [Header("Decay — Two Phase")]
    [Tooltip("Seconds after the last hit during which the meter drains slowly (grace period). Rewards sustained pressure.")]
    public float graceDuration = 1.5f;

    [Tooltip("Drain rate (per second) during the grace period — slow so continued pressure keeps the meter high.")]
    public float slowDecayRate = 0.05f;

    [Tooltip("Drain rate (per second) after the grace period expires — fast so the window closes if the player backs off.")]
    public float fastDecayRate = 0.5f;

    [Header("Debug Bar")]
    [Tooltip("World-space height above the enemy for the debug stun bar.")]
    public float debugHeight = 2.55f;

    [Tooltip("Width of the stun bar in screen space pixels.")]
    public float barWidth = 80f;

    [Tooltip("Height of the stun bar in screen space pixels.")]
    public float barHeight = 6f;

    public Color barBackgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.9f);
    [Tooltip("Bar fill color while the meter is building up.")]
    public Color barFillColor = new Color(1f, 0.85f, 0f, 0.9f);
    [Tooltip("Bar fill color while standing stun is active.")]
    public Color barActiveColor = new Color(1f, 0.3f, 0f, 0.95f);

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private float stunMeter;            // Current buildup (0–1)
    private float graceTimer;           // Counts down after last hit; slow decay while > 0
    private float standingStunTimer;    // Counts down while in standing stun; 0 = not active
    private float entryTimer;           // Counts up after stun triggers; switches to Loop when >= entryDuration
    private StunPhase currentPhase = StunPhase.None;
    private bool triggerWasHeavy;       // True if the hit that filled the meter was AttackHeaviness.Heavy

    private Texture2D barBgTex;
    private Texture2D barFillTex;

    // ========================================================================
    // PUBLIC INTERFACE
    // ========================================================================

    /// <summary>True while the enemy is locked in standing stun (meter was full).</summary>
    public bool IsStandingStunned => standingStunTimer > 0f;
    /// <summary>Gameplay-state alias: enemy is in the stunned state (same underlying timer as IsStandingStunned).</summary>
    public bool IsStunned => IsStandingStunned;

    /// <summary>Current animation phase. SimpleEnemyAI watches this to call animator.Play() on transitions.</summary>
    public StunPhase CurrentPhase => currentPhase;

    /// <summary>True if the hit that filled the meter was AttackHeaviness.Heavy. SimpleEnemyAI uses this to pick the correct entry animation.</summary>
    public bool TriggerWasHeavy => triggerWasHeavy;

    /// <summary>Current meter value (0–1). Use for debug or UI only.</summary>
    public float StunMeterValue => stunMeter;
    /// <summary>Remaining standing-stun time in seconds.</summary>
    public float RemainingStandingStunTime => Mathf.Max(0f, standingStunTimer);

    /// <summary>
    /// Call when the enemy is hit while already standing stunned.
    /// If the knockback is heavy enough, resets to Entry phase with TriggerWasHeavy = true
    /// so SimpleEnemyAI can play the KnockbackStunEntry animation again.
    /// Returns true when the re-trigger happened.
    /// </summary>
    public bool TryRetriggerAsKnockback(float knockbackMagnitude)
    {
        if (!IsStandingStunned) return false;
        if (knockbackMagnitude < knockbackStunThreshold) return false;
        // Reset entry phase so the knockback stun entry animation plays from the top
        entryTimer     = 0f;
        triggerWasHeavy = true;
        currentPhase   = StunPhase.Entry;
        return true;
    }

    /// <summary>
    /// Ensures a minimum remaining standing-stun time (seconds) if standing stun is currently active.
    /// Used by wall-bounce to extend the underlying standing-stun state without affecting hitstun.
    /// </summary>
    public void ExtendStandingStunMinRemaining(float minRemainingSeconds)
    {
        if (!IsStandingStunned) return;
        if (minRemainingSeconds <= 0f) return;
        standingStunTimer = Mathf.Max(standingStunTimer, minRemainingSeconds);
    }

    /// <summary>
    /// Force standing stun for at least <paramref name="durationSeconds"/>.
    /// Used by throw-victim flow so throw lock is represented as standing stun, not hitstun.
    /// </summary>
    public void ForceStandingStun(float durationSeconds, bool asKnockback = false)
    {
        if (durationSeconds <= 0f) return;
        standingStunTimer = Mathf.Max(standingStunTimer, durationSeconds);
        stunMeter = 1f;
        currentPhase = StunPhase.Entry;
        entryTimer = 0f;
        triggerWasHeavy = asKnockback;
        graceTimer = 0f;
    }

    /// <summary>
    /// Immediately end any active standing stun and reset the meter.
    /// Used when the throw releases so lingering stun doesn't re-trigger thrown animations.
    /// </summary>
    public void ClearStandingStun()
    {
        standingStunTimer = 0f;
        stunMeter = 0f;
        graceTimer = 0f;
        entryTimer = 0f;
        triggerWasHeavy = false;
        currentPhase = StunPhase.None;
    }

    /// <summary>
    /// Add stun buildup from a hit. Resets the grace timer so slow decay restarts.
    /// Ignored while already standing stunned. Knockback force is recorded if the hit fills the meter.
    /// </summary>
    public void AddStun(float amount, float knockback = 0f)
    {
        if (amount <= 0f || IsStandingStunned) return;

        stunMeter += amount;
        graceTimer = graceDuration;

        if (stunMeter >= 1f)
        {
            stunMeter = 1f;
            standingStunTimer = standingStunDuration;
            entryTimer = 0f;
            triggerWasHeavy = knockback >= knockbackStunThreshold;
            currentPhase = StunPhase.Entry;
        }
    }

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Update()
    {
        if (IsStandingStunned)
        {
            standingStunTimer -= Time.deltaTime;

            if (standingStunTimer <= 0f)
            {
                // Stun expired — reset everything regardless of which phase we were in
                standingStunTimer = 0f;
                stunMeter = 0f;
                graceTimer = 0f;
                entryTimer = 0f;
                triggerWasHeavy = false;
                currentPhase = StunPhase.None;
                return;
            }

            // Advance entry timer; switch to Loop once entry animation is done
            if (currentPhase == StunPhase.Entry)
            {
                entryTimer += Time.deltaTime;
                if (entryTimer >= standingStunEntryDuration)
                    currentPhase = StunPhase.Loop;
            }

            return;
        }

        // Not standing stunned — apply two-phase decay
        if (stunMeter <= 0f) return;

        if (graceTimer > 0f)
        {
            graceTimer -= Time.deltaTime;
            stunMeter -= slowDecayRate * Time.deltaTime;
        }
        else
        {
            stunMeter -= fastDecayRate * Time.deltaTime;
        }

        stunMeter = Mathf.Max(0f, stunMeter);
    }

    // ========================================================================
    // DEBUG DISPLAY
    // ========================================================================

    void OnGUI()
    {
        DebugSettings debug = DebugSettings.Instance;
        if (debug == null || !debug.ShouldShowEnemyHealthStats(debug.showEnemyStunMeter)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(transform.position + Vector3.up * debugHeight);
        if (screenPos.z < 0f) return;

        float y = Screen.height - screenPos.y;

        if (barBgTex == null)
        {
            barBgTex = new Texture2D(1, 1);
            barBgTex.SetPixel(0, 0, barBackgroundColor);
            barBgTex.Apply();
        }

        if (barFillTex == null)
        {
            barFillTex = new Texture2D(1, 1);
            barFillTex.Apply();
        }

        float halfW = barWidth * 0.5f;
        Rect bgRect = new Rect(screenPos.x - halfW, y - barHeight * 0.5f, barWidth, barHeight);
        GUI.DrawTexture(bgRect, barBgTex);

        Color fill = IsStandingStunned ? barActiveColor : barFillColor;
        barFillTex.SetPixel(0, 0, fill);
        barFillTex.Apply();
        Rect fillRect = new Rect(bgRect.x, bgRect.y, bgRect.width * stunMeter, bgRect.height);
        GUI.DrawTexture(fillRect, barFillTex);
    }

    void OnDestroy()
    {
        if (barBgTex != null) Destroy(barBgTex);
        if (barFillTex != null) Destroy(barFillTex);
    }
}
