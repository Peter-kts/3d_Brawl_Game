using UnityEngine;

/// <summary>
/// Standalone prone state: plays the prone animation, runs the prone timer, and fires onProneEnded when done.
/// Owned by SimpleEnemyAI. Entered by AirborneSequence's onCrashLanded callback (crash landing) or directly by
/// any future knockdown path. When the timer expires it calls onProneEnded so SimpleEnemyAI can start get-up.
/// </summary>
public class EnemyProneSystem
{
    /// <summary>True while the enemy is lying prone (crash landed, before get-up starts).</summary>
    public bool IsInProne { get; private set; }

    readonly Transform ownerTransform;
    readonly Animator animator;
    readonly string proneStateName;
    readonly int proneLayer;
    readonly float proneTransitionDuration;
    readonly EnemyHealth health;
    readonly System.Action onProneEnded;

    float proneUntil;
    float _nextProneDurationOverride = -1f;

    /// <param name="ownerTransform">Enemy root transform whose world yaw is used for prone facing.</param>
    /// <param name="animator">Animator to play the prone state on.</param>
    /// <param name="proneStateName">Animator state name for the prone loop. Empty = no prone animation (animator stays on crash pose).</param>
    /// <param name="proneLayer">Animator layer index for the prone state.</param>
    /// <param name="proneTransitionDuration">Seconds to crossfade into prone. 0 = immediate Play().</param>
    /// <param name="health">Used for GetAirborneSpeedMultiplier when scaling prone duration.</param>
    /// <param name="onProneEnded">Callback fired when the prone timer expires; SimpleEnemyAI uses this to start get-up.</param>
    public EnemyProneSystem(Transform ownerTransform, Animator animator, string proneStateName, int proneLayer, float proneTransitionDuration, EnemyHealth health, System.Action onProneEnded)
    {
        this.ownerTransform = ownerTransform;
        this.animator = animator;
        this.proneStateName = proneStateName;
        this.proneLayer = proneLayer;
        this.proneTransitionDuration = Mathf.Max(0f, proneTransitionDuration);
        this.health = health;
        this.onProneEnded = onProneEnded;
    }

    /// <summary>
    /// Override the prone duration for the next Enter() call. A value &lt;= 0 is ignored; the default will be used.
    /// Call before TakeHit when a throw will launch the enemy airborne so the override is ready when the crash lands.
    /// </summary>
    public void OverrideNextProneDuration(float duration)
    {
        if (duration > 0f) _nextProneDurationOverride = duration;
    }

    /// <summary>
    /// Enter prone state. Plays the prone animation and starts the prone timer.
    /// Called via AirborneSequence's onCrashLanded callback (passing groundedDuration) or directly.
    /// </summary>
    /// <param name="defaultDuration">Default prone seconds; overridden by OverrideNextProneDuration if set.</param>
    public void Enter(float defaultDuration, bool invertFacing = false)
    {
        float dur = (_nextProneDurationOverride > 0f) ? _nextProneDurationOverride : defaultDuration;
        _nextProneDurationOverride = -1f;
        IsInProne = true;
        float speedMult = (health != null) ? health.GetAirborneSpeedMultiplier() : 1f;
        proneUntil = Time.time + dur / speedMult;

        // Match throw-style prone facing: keep upright and use the current baked/root yaw.
        if (ownerTransform != null)
        {
            Quaternion sourceRotation = animator != null ? animator.rootRotation : ownerTransform.rotation;
            float yaw = sourceRotation.eulerAngles.y + (invertFacing ? 180f : 0f);
            ownerTransform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        if (animator != null)
        {
            animator.speed = 1f;
            if (!string.IsNullOrEmpty(proneStateName))
            {
                if (proneTransitionDuration > 0f)
                    animator.CrossFadeInFixedTime(proneStateName, proneTransitionDuration, proneLayer, 0f);
                else
                    animator.Play(proneStateName, proneLayer, 0f);
            }
        }
    }

    /// <summary>
    /// Call once per frame from SimpleEnemyAI.Update. Fires onProneEnded when the timer expires.
    /// </summary>
    public void Update()
    {
        if (!IsInProne) return;
        if (Time.time >= proneUntil)
        {
            IsInProne = false;
            onProneEnded?.Invoke();
        }
    }
}
