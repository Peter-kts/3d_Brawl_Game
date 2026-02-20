/*
 * ============================================================================
 * AIRBORNESEQUENCE.CS - Liftoff / Loop / Crash airborne animation controller
 * ============================================================================
 *
 * PURPOSE
 * -------
 * This class owns the full airborne→crash→get-up flow for an enemy. It splits
 * a single Animator state into three phases (Liftoff, Loop, Crash), drives the
 * Animator, and when the crash segment ends it either starts get-up or
 * completes death. SimpleEnemyAI creates one instance and delegates all
 * airborne logic here.
 *
 * HOW IT FITS WITH OTHER COMPONENTS
 * ---------------------------------
 * - SimpleEnemyAI: Creates this sequence in Awake, calls Update(health) each
 *   frame, and reads CurrentPhase / InCrash / GetAirborneForAnimator(health)
 *   to drive the Animator's Speed, IsAirborne, and Stun parameters. Exposes
 *   this instance via AirborneSequence so EnemyHealth (PATH A) can call
 *   NotifyCrashFinished(isDying).
 * - EnemyHealth: Provides IsAirborne (timer) and IsDying. When crash finishes,
 *   this class calls health.StartGetUp(getUpDuration) or
 *   health.OnAirborneSequenceComplete(). EnemyHealth does not call this class
 *   unless PATH A is used (then enemyAI.AirborneSequence.NotifyCrashFinished).
 * - Animator: We play one state (settings.airborneStateName) on one layer,
 *   jumping to normalized times (liftoffStart, loopStart, crashStart). We set
 *   animator.speed and hitSpeedParameter so the clip plays at 1x during
 *   airborne. Get-up is a separate state (getUpStateName) on getUpLayer.
 *
 * PHASE FLOW
 * ----------
 * None → (IsAirborne rises) → Liftoff → (normalizedTime >= liftoffEnd) → Loop
 * Loop → (each time normalizedTime >= loopEnd, jump back to loopStart)
 * Loop → (IsAirborne falls) → Crash → (normalizedTime >= crashEnd) → Grounded (or None if dying)
 * Grounded → (after groundedDuration) → None + NotifyCrashFinished (get-up). TODO: Replace animator freeze with a real grounded animation state.
 * When leaving Crash (dying) we call NotifyCrashFinished(health.IsDying) immediately.
 *
 * ============================================================================
 */

using UnityEngine;

/// <summary>
/// Encapsulates the Liftoff / Loop / Crash airborne animation and the
/// crash-finished flow (get-up or complete death). Owned by SimpleEnemyAI.
/// </summary>
public class AirborneSequence
{
    // ------------------------------------------------------------------------
    // Phase enum and public state (read by SimpleEnemyAI for CurrentState, Speed, Stun)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Current segment of the airborne animation.
    /// None = not in airborne; Liftoff = launch; Loop = spinning in air; Crash = landing; Grounded = lying on floor before get-up.
    /// SimpleEnemyAI uses this to set Animator Speed and to compute CurrentState (Airborne / Crashed / Grounded).
    /// </summary>
    public enum Phase { None, Liftoff, Loop, Crash, Grounded }

    /// <summary>
    /// Which phase we are in. Set internally in Update and when entering Crash from the falling edge of IsAirborne.
    /// Effect: SimpleEnemyAI.CurrentState returns Airborne when phase is Liftoff or Loop, Crashed when phase is Crash;
    /// UpdateAnimator uses this to force Speed=1 and to drive the IsAirborne and Stun animator parameters.
    /// </summary>
    public Phase CurrentPhase { get; private set; } = Phase.None;

    /// <summary>
    /// True when CurrentPhase == Crash (enemy has landed, playing crash animation, cannot act).
    /// Effect: SimpleEnemyAI.CurrentState returns Crashed and CanAct is false until crash ends.
    /// </summary>
    public bool InCrash => CurrentPhase == Phase.Crash;

    /// <summary>
    /// True when CurrentPhase == Grounded (enemy lying on floor after crash, waiting for groundedDuration before get-up).
    /// Effect: SimpleEnemyAI.CurrentState returns Grounded; animator is frozen on crash pose until get-up starts.
    /// </summary>
    public bool InGrounded => CurrentPhase == Phase.Grounded;

    // ------------------------------------------------------------------------
    // Injected dependencies (set in constructor; used in Update and NotifyCrashFinished)
    // ------------------------------------------------------------------------

    /// <summary>Animator we drive. We Play/CrossFade the airborne state and the get-up state on their respective layers. Effect: all airborne and get-up animation playback.</summary>
    readonly Animator animator;

    /// <summary>Normalized-time boundaries (liftoff/loop/crash start/end) and state name/layer/crossfade. Effect: when Update runs we only do work if settings.IsConfigured; phase transitions use these times.</summary>
    readonly AirborneAnimationSettings settings;

    /// <summary>Animator float parameter name for hit/get-up speed scale. We set it to 1f when entering Liftoff and when triggering get-up so those clips play at default speed. Effect: avoids slowed airborne/get-up if something else had scaled hit speed.</summary>
    readonly string hitSpeedParameter;

    /// <summary>Enemy health component. We read IsAirborne and IsDying; we call StartGetUp(getUpDuration) or OnAirborneSequenceComplete() when crash finishes. Effect: ties airborne timing to health and drives get-up/death.</summary>
    readonly EnemyHealth health;

    /// <summary>Duration (seconds) the enemy is in get-up stun. Passed to health.StartGetUp(getUpDuration). Effect: EnemyHealth.getUpUntil = Time.time + getUpDuration so IsGettingUp is true for that long.</summary>
    readonly float getUpDuration;

    /// <summary>Animator state name for the get-up animation. We Play(this, getUpLayer, 0f) when crash finishes and not dying. Effect: plays the get-up state on the get-up layer.</summary>
    readonly string getUpStateName;

    /// <summary>Animator layer index for the get-up state (e.g. 1 = Stun layer). Effect: get-up animation plays on this layer.</summary>
    readonly int getUpLayer;

    /// <summary>Seconds the enemy lies on the floor (crash pose held) after crash segment ends before get-up is started. 0 = no delay.</summary>
    readonly float groundedDuration;

    // ------------------------------------------------------------------------
    // Internal state (edge detection and cache)
    // ------------------------------------------------------------------------

    /// <summary>Previous frame's health.IsAirborne. Used to detect rising edge (start Liftoff) and falling edge (start Crash). Updated at end of Update after reading current IsAirborne.</summary>
    bool wasAirborne;

    /// <summary>Time.time when grounded phase ends; get-up starts after this. Set when entering Phase.Grounded.</summary>
    float groundedUntil;

    /// <summary>Cached hash of settings.airborneStateName for fast per-frame comparison with animator state. Set when we need it; cleared when we leave Crash so a changed state name is picked up next time.</summary>
    int cachedStateNameHash;

    // ------------------------------------------------------------------------
    // Constructor
    // ------------------------------------------------------------------------

    /// <summary>
    /// Build the sequence. Call from SimpleEnemyAI.Awake after animator and health are assigned.
    /// </summary>
    /// <param name="animator">Animator to drive (airborne and get-up states).</param>
    /// <param name="settings">Phase boundaries and state name; Update no-ops if !settings.IsConfigured.</param>
    /// <param name="hitSpeedParameter">Float parameter name for hit speed; we set to 1f for airborne and get-up.</param>
    /// <param name="health">Used for IsAirborne/IsDying and for StartGetUp / OnAirborneSequenceComplete when crash ends.</param>
    /// <param name="getUpDuration">Duration passed to health.StartGetUp when crash ends (not dying).</param>
    /// <param name="getUpStateName">State name to play for get-up; empty skips get-up animation.</param>
    /// <param name="getUpLayer">Layer index for get-up state.</param>
    /// <param name="groundedDuration">Seconds to lie on floor after crash before get-up; 0 = no delay.</param>
    public AirborneSequence(
        Animator animator,
        AirborneAnimationSettings settings,
        string hitSpeedParameter,
        EnemyHealth health,
        float getUpDuration,
        string getUpStateName,
        int getUpLayer,
        float groundedDuration)
    {
        this.animator = animator;
        this.settings = settings;
        this.hitSpeedParameter = hitSpeedParameter;
        this.health = health;
        this.getUpDuration = getUpDuration;
        this.getUpStateName = getUpStateName;
        this.getUpLayer = getUpLayer;
        this.groundedDuration = groundedDuration;
    }

    // ------------------------------------------------------------------------
    // Crash-finished API (called from Update when crash segment ends, or from EnemyHealth for PATH A)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Called when the crash phase has finished. Either completes death (if killed by the launch) or starts get-up and plays the get-up animation.
    /// Invoked from: (1) Update when normalizedTime >= settings.crashEnd; (2) externally e.g. EnemyHealth for PATH A via enemyAI.AirborneSequence.NotifyCrashFinished(isDying).
    /// Effect: If isDying, health.OnAirborneSequenceComplete() (disables AI/combat/movement, keeps mesh). Otherwise health.StartGetUp(getUpDuration) and TriggerGetUpAnimation().
    /// </summary>
    /// <param name="isDying">True if the enemy was killed by the airborne attack; we complete death instead of get-up.</param>
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
            health.StartGetUp(getUpDuration);
            TriggerGetUpAnimation();
            health.OnAirborneSequenceEnded();
        }
    }

    /// <summary>
    /// Plays the get-up state on the get-up layer at 1x speed. Called from NotifyCrashFinished when not dying.
    /// Effect: Animator plays getUpStateName on getUpLayer; animator.speed and hitSpeedParameter set to 1 so the clip isn't scaled.
    /// </summary>
    void TriggerGetUpAnimation()
    {
        if (animator == null || string.IsNullOrEmpty(getUpStateName)) return;
        animator.speed = 1f;
        if (!string.IsNullOrEmpty(hitSpeedParameter))
            animator.SetFloat(hitSpeedParameter, 1f);
        animator.Play(getUpStateName, getUpLayer, 0f);
    }

    // ------------------------------------------------------------------------
    // Query used by SimpleEnemyAI to drive the Animator
    // ------------------------------------------------------------------------

    /// <summary>
    /// Value SimpleEnemyAI should write to the Animator's IsAirborne bool. True when the enemy is logically "in airborne" so the Animator stays in the airborne state (including during Crash, when health.IsAirborne is already false).
    /// Effect: SimpleEnemyAI calls animator.SetBool(airborneParameter, GetAirborneForAnimator(health)); this keeps the Animator from transitioning away from the airborne state until we leave Crash.
    /// </summary>
    /// <param name="health">Used for health.IsAirborne and (with CurrentPhase) to decide if we're still in the sequence.</param>
    /// <returns>True if health says airborne or we're in Liftoff/Loop/Crash/Grounded (settings configured).</returns>
    public bool GetAirborneForAnimator(EnemyHealth health)
    {
        return health != null && (health.IsAirborne || (settings.IsConfigured && CurrentPhase != Phase.None));
    }

    // ------------------------------------------------------------------------
    // Per-frame update (phase machine and animator playback)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Call once per frame from SimpleEnemyAI.Update. Updates phase from health.IsAirborne edges and normalized time; plays the airborne state and jumps between liftoff/loop/crash segments; calls NotifyCrashFinished when the crash segment ends.
    /// No-ops if animator or health is null, or if settings.IsConfigured is false (empty airborne state name).
    /// Effect: Drives the full Liftoff→Loop→Crash flow and ties it to EnemyHealth.IsAirborne and the Animator.
    /// </summary>
    /// <param name="health">Source of IsAirborne (rising/falling edge) and IsDying (when we call NotifyCrashFinished).</param>
    public void Update(EnemyHealth health)
    {
        if (animator == null || health == null) return;
        if (!settings.IsConfigured) return;

        bool isAirborne = health.IsAirborne;

        // Rising edge: IsAirborne just became true → start Liftoff from liftoffStart (crossfade or instant).
        // Effect: CurrentPhase = Liftoff; animator plays airborne state from liftoffStart; speed and hitSpeed set to 1.
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

        // Falling edge: IsAirborne just became false while we were in a phase → start Crash from crashStart.
        // Effect: CurrentPhase = Crash; animator jumps to crashStart (no crossfade; loop and crash are adjacent).
        if (!isAirborne && wasAirborne && CurrentPhase != Phase.None)
        {
            CurrentPhase = Phase.Crash;
            animator.Play(settings.airborneStateName, settings.airborneAnimationLayer, settings.crashStart);
        }

        wasAirborne = isAirborne;

        if (CurrentPhase == Phase.None) return;

        // Grounded: lying on floor for groundedDuration; then start get-up. Animator is frozen (speed=0) by SimpleEnemyAI.
        if (CurrentPhase == Phase.Grounded)
        {
            if (Time.time >= groundedUntil)
            {
                animator.speed = 1f;
                CurrentPhase = Phase.None;
                cachedStateNameHash = 0;
                NotifyCrashFinished(false);
            }
            return;
        }

        // Only use normalizedTime when we're actually in the airborne state (avoid wrong timing during transitions).
        if (cachedStateNameHash == 0)
            cachedStateNameHash = Animator.StringToHash(settings.airborneStateName);
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(settings.airborneAnimationLayer);
        if (stateInfo.shortNameHash != cachedStateNameHash)
            return;
        float normalizedTime = stateInfo.normalizedTime;

        switch (CurrentPhase)
        {
            case Phase.Liftoff:
                // Liftoff segment done → jump to loop segment. Effect: phase = Loop; animator plays from loopStart.
                if (normalizedTime >= settings.liftoffEnd)
                {
                    CurrentPhase = Phase.Loop;
                    animator.Play(settings.airborneStateName, settings.airborneAnimationLayer, settings.loopStart);
                }
                break;
            case Phase.Loop:
                // Reached end of loop segment → jump back to loopStart (seamless repeat). Effect: keeps spinning until IsAirborne falls.
                if (normalizedTime >= settings.loopEnd)
                    animator.Play(settings.airborneStateName, settings.airborneAnimationLayer, settings.loopStart);
                break;
            case Phase.Crash:
                // Crash segment done → either start get-up/death immediately (dying) or enter Grounded and hold crash pose for groundedDuration.
                if (normalizedTime >= settings.crashEnd)
                {
                    cachedStateNameHash = 0;
                    if (health.IsDying)
                    {
                        CurrentPhase = Phase.None;
                        NotifyCrashFinished(true);
                    }
                    else
                    {
                        CurrentPhase = Phase.Grounded;
                        groundedUntil = Time.time + groundedDuration / health.GetAirborneSpeedMultiplier();
                        animator.speed = 0f;  // TODO: Replace with a real grounded animation state instead of freezing last frame
                    }
                }
                break;
        }
    }
}
