/*
 * ============================================================================
 * AIRBORNESEQUENCE.CS - Liftoff / Loop / Crash airborne animation controller
 * ============================================================================
 *
 * PURPOSE
 * -------
 * This class owns the Liftoff → Loop → Crash phases for an enemy. It drives
 * the Animator through those phases and when the crash segment ends it fires
 * onCrashLanded(groundedDuration) so the caller (SimpleEnemyAI) can hand off
 * to EnemyProneSystem (prone) or handle death. Get-up and prone are NOT owned
 * here; they live in EnemyProneSystem and SimpleEnemyAI respectively.
 *
 * HOW IT FITS WITH OTHER COMPONENTS
 * ---------------------------------
 * - SimpleEnemyAI: Creates this sequence in Awake, calls Update(health) each
 *   frame, and reads CurrentPhase / InCrash / GetAirborneForAnimator(health)
 *   to drive the Animator's Speed, IsAirborne, and Stun parameters.
 * - EnemyProneSystem: Receives the onCrashLanded callback and starts prone.
 * - EnemyHealth: Provides IsAirborne (timer) and IsDying. When crash finishes
 *   (dying path), this class calls health.OnAirborneSequenceComplete() and
 *   health.OnAirborneSequenceEnded(). Non-dying path delegates to caller.
 * - Animator: We play one state (settings.airborneStateName) on one layer,
 *   jumping to normalized times (liftoffStart, loopStart, crashStart).
 *
 * PHASE FLOW
 * ----------
 * None → (IsAirborne rises) → Liftoff → (normalizedTime >= liftoffEnd) → Loop
 * Loop → (each time normalizedTime >= loopEnd, jump back to loopStart)
 * Loop → (IsAirborne falls) → Crash → (normalizedTime >= crashEnd) → None
 *   Crash end (dying)    → health.OnAirborneSequenceComplete/Ended (death)
 *   Crash end (not dying) → onCrashLanded(groundedDuration) → EnemyProneSystem
 *
 * ============================================================================
 */

using UnityEngine;

/// <summary>
/// Encapsulates the Liftoff / Loop / Crash airborne animation phases. When crash ends,
/// fires onCrashLanded(groundedDuration) so SimpleEnemyAI can hand off to EnemyProneSystem.
/// Owned by SimpleEnemyAI.
/// </summary>
public class AirborneSequence
{
    // ------------------------------------------------------------------------
    // Phase enum and public state
    // ------------------------------------------------------------------------

    /// <summary>
    /// Current segment of the airborne animation.
    /// None = not in airborne sequence; Liftoff = launch up; Loop = spinning in air; Crash = landing.
    /// </summary>
    public enum Phase { None, Liftoff, Loop, Crash }

    /// <summary>
    /// Which phase we are in. Set internally in Update and on IsAirborne edge transitions.
    /// SimpleEnemyAI uses this to set Animator Speed and to compute CurrentState.
    /// </summary>
    public Phase CurrentPhase { get; private set; } = Phase.None;

    /// <summary>
    /// True when CurrentPhase == Crash (enemy has landed, playing crash animation, cannot act).
    /// SimpleEnemyAI.CurrentState returns Crashed and CanAct is false until crash ends.
    /// </summary>
    public bool InCrash => CurrentPhase == Phase.Crash;

    // ------------------------------------------------------------------------
    // Injected dependencies
    // ------------------------------------------------------------------------

    /// <summary>Animator we drive during airborne phases.</summary>
    readonly Animator animator;

    /// <summary>Normalized-time boundaries and state name/layer/crossfade settings.</summary>
    readonly AirborneAnimationSettings settings;

    /// <summary>Animator float parameter name for hit/get-up speed scale; set to 1f on liftoff.</summary>
    readonly string hitSpeedParameter;

    /// <summary>Enemy health component. We read IsAirborne and IsDying; on death we call the health complete/ended methods.</summary>
    readonly EnemyHealth health;

    /// <summary>
    /// Seconds the enemy should lie prone after crash. Passed as the argument to onCrashLanded
    /// so EnemyProneSystem can use it as the default duration.
    /// </summary>
    readonly float groundedDuration;

    /// <summary>
    /// Called when the crash segment ends and the enemy is not dying.
    /// Argument is groundedDuration; EnemyProneSystem.Enter(groundedDuration) is the typical handler.
    /// </summary>
    readonly System.Action<float> onCrashLanded;

    // ------------------------------------------------------------------------
    // Internal state
    // ------------------------------------------------------------------------

    /// <summary>Previous frame's health.IsAirborne for rising/falling edge detection.</summary>
    bool wasAirborne;

    /// <summary>Cached hash of settings.airborneStateName for per-frame state comparison.</summary>
    int cachedStateNameHash;

    // ------------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------------

    /// <summary>
    /// Build the sequence. Call from SimpleEnemyAI.Awake after animator and health are assigned.
    /// </summary>
    /// <param name="animator">Animator to drive (airborne state playback).</param>
    /// <param name="settings">Phase boundaries and state name; Update no-ops if !settings.IsConfigured.</param>
    /// <param name="hitSpeedParameter">Float parameter name for hit speed; set to 1f when liftoff starts.</param>
    /// <param name="health">Used for IsAirborne/IsDying and for complete/ended calls on death.</param>
    /// <param name="groundedDuration">Seconds passed to onCrashLanded as the default prone duration.</param>
    /// <param name="onCrashLanded">Called when crash ends (not dying); argument is groundedDuration.</param>
    public AirborneSequence(
        Animator animator,
        AirborneAnimationSettings settings,
        string hitSpeedParameter,
        EnemyHealth health,
        float groundedDuration,
        System.Action<float> onCrashLanded)
    {
        this.animator = animator;
        this.settings = settings;
        this.hitSpeedParameter = hitSpeedParameter;
        this.health = health;
        this.groundedDuration = groundedDuration;
        this.onCrashLanded = onCrashLanded;
    }

    // ------------------------------------------------------------------------
    // Crash-finished notification
    // ------------------------------------------------------------------------

    /// <summary>
    /// Called when the crash phase has finished. If dying, completes death via health callbacks.
    /// If not dying, fires onCrashLanded(groundedDuration) so the caller starts prone.
    /// Can also be invoked externally (e.g. EnemyHealth PATH A).
    /// </summary>
    /// <param name="isDying">True if killed by the airborne attack; complete death instead of prone.</param>
    public void NotifyCrashFinished(bool isDying)
    {
        if (health == null) return;
        if (isDying)
        {
            health.OnAirborneSequenceComplete();
            health.OnAirborneSequenceEnded();
        }
        else
        {
            onCrashLanded?.Invoke(groundedDuration);
        }
    }

    // ------------------------------------------------------------------------
    // Animator query
    // ------------------------------------------------------------------------

    /// <summary>
    /// Value SimpleEnemyAI should write to the Animator's IsAirborne bool.
    /// True when health says airborne or we are in any active phase (Liftoff/Loop/Crash),
    /// keeping the Animator in the airborne state until we fully exit the sequence.
    /// </summary>
    public bool GetAirborneForAnimator(EnemyHealth health)
    {
        return health != null && (health.IsAirborne || (settings.IsConfigured && CurrentPhase != Phase.None));
    }

    /// <summary>
    /// Immediately cancels the airborne sequence without triggering crash-landed or death callbacks.
    /// Used when the enemy is grabbed mid-air so the throw system takes full control.
    /// </summary>
    public void ForceCancel()
    {
        CurrentPhase = Phase.None;
        wasAirborne = false;
        cachedStateNameHash = 0;
    }

    // ------------------------------------------------------------------------
    // Per-frame update
    // ------------------------------------------------------------------------

    /// <summary>
    /// Call once per frame from SimpleEnemyAI.Update. Advances the phase machine, plays the
    /// airborne state, jumps between segments, and calls NotifyCrashFinished when crash ends.
    /// No-ops if animator or health is null, or if settings.IsConfigured is false.
    /// </summary>
    public void Update(EnemyHealth health)
    {
        if (animator == null || health == null) return;
        if (!settings.IsConfigured) return;

        bool isAirborne = health.IsAirborne;

        // Rising edge: IsAirborne just became true → start Liftoff.
        if (isAirborne && !wasAirborne)
        {
            CurrentPhase = Phase.Liftoff;
            float speedMult = health.GetAirborneSpeedMultiplier();
            animator.speed = 1f * speedMult;
            if (!string.IsNullOrEmpty(hitSpeedParameter))
                animator.SetFloat(hitSpeedParameter, 1f);

            if (settings.crossfadeDuration > 0f)
                animator.CrossFadeInFixedTime(settings.airborneStateName, settings.crossfadeDuration, settings.airborneAnimationLayer, settings.liftoffStart);
            else
                animator.Play(settings.airborneStateName, settings.airborneAnimationLayer, settings.liftoffStart);
        }

        // Falling edge: IsAirborne just became false while in a phase → start Crash.
        if (!isAirborne && wasAirborne && CurrentPhase != Phase.None)
        {
            CurrentPhase = Phase.Crash;
            animator.Play(settings.airborneStateName, settings.airborneAnimationLayer, settings.crashStart);
        }

        wasAirborne = isAirborne;

        if (CurrentPhase == Phase.None) return;

        // Only use normalizedTime when actually in the airborne state (avoid wrong timing during transitions).
        if (cachedStateNameHash == 0)
            cachedStateNameHash = Animator.StringToHash(settings.airborneStateName);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(settings.airborneAnimationLayer);
        if (stateInfo.shortNameHash != cachedStateNameHash)
            return;
        float normalizedTime = stateInfo.normalizedTime;

        switch (CurrentPhase)
        {
            case Phase.Liftoff:
                if (normalizedTime >= settings.liftoffEnd)
                {
                    CurrentPhase = Phase.Loop;
                    animator.Play(settings.airborneStateName, settings.airborneAnimationLayer, settings.loopStart);
                }
                break;
            case Phase.Loop:
                if (normalizedTime >= settings.loopEnd)
                    animator.Play(settings.airborneStateName, settings.airborneAnimationLayer, settings.loopStart);
                break;
            case Phase.Crash:
                if (normalizedTime >= settings.crashEnd)
                {
                    cachedStateNameHash = 0;
                    CurrentPhase = Phase.None;
                    NotifyCrashFinished(health.IsDying);
                }
                break;
        }
    }
}
