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

    /// <summary>Immediately cancel prone without triggering the get-up callback. Used when grabbed mid-prone.</summary>
    public void ForceCancel() { IsInProne = false; }

    readonly Transform ownerTransform;
    readonly Animator animator;
    readonly string proneStateName;
    readonly string proneFaceDownStateName;
    readonly int proneLayer;
    readonly float proneTransitionDuration;
    readonly EnemyHealth health;
    readonly System.Action onProneEnded;

    float proneUntil;
    float _nextProneDurationOverride = -1f;
    ProneVariant _nextProneVariantOverride = ProneVariant.Default;
    bool _hasNextProneVariantOverride;
    Quaternion lockedProneRotation;

    /// <param name="ownerTransform">Enemy root transform whose world yaw is used for prone facing.</param>
    /// <param name="animator">Animator to play the prone state on.</param>
    /// <param name="proneStateName">Animator state name for the prone loop. Empty = no prone animation (animator stays on crash pose).</param>
    /// <param name="proneLayer">Animator layer index for the prone state.</param>
    /// <param name="proneTransitionDuration">Seconds to crossfade into prone. 0 = immediate Play().</param>
    /// <param name="health">Used for GetAirborneSpeedMultiplier when scaling prone duration.</param>
    /// <param name="onProneEnded">Callback fired when the prone timer expires; SimpleEnemyAI uses this to start get-up.</param>
    public EnemyProneSystem(Transform ownerTransform, Animator animator, string proneStateName, string proneFaceDownStateName, int proneLayer, float proneTransitionDuration, EnemyHealth health, System.Action onProneEnded)
    {
        this.ownerTransform = ownerTransform;
        this.animator = animator;
        this.proneStateName = proneStateName;
        this.proneFaceDownStateName = proneFaceDownStateName;
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
    /// Override the prone animation variant for the next Enter() call.
    /// </summary>
    public void OverrideNextProneVariant(ProneVariant proneVariant)
    {
        _nextProneVariantOverride = proneVariant;
        _hasNextProneVariantOverride = true;
    }

    /// <summary>
    /// Enter prone state. Plays the prone animation and starts the prone timer.
    /// Called via AirborneSequence's onCrashLanded callback (passing groundedDuration) or directly.
    /// </summary>
    /// <param name="defaultDuration">Default prone seconds; overridden by OverrideNextProneDuration if set.</param>
    public void Enter(float defaultDuration, bool invertFacing = false, ProneVariant proneVariant = ProneVariant.Default, bool preserveCurrentFacing = false)
    {
        float dur = (_nextProneDurationOverride > 0f) ? _nextProneDurationOverride : defaultDuration;
        _nextProneDurationOverride = -1f;
        ProneVariant variantToUse = _hasNextProneVariantOverride ? _nextProneVariantOverride : proneVariant;
        _hasNextProneVariantOverride = false;
        _nextProneVariantOverride = ProneVariant.Default;
        IsInProne = true;
        float speedMult = (health != null) ? health.GetAirborneSpeedMultiplier() : 1f;
        proneUntil = Time.time + dur / speedMult;

        // Keep upright and choose yaw source:
        // - preserveCurrentFacing = true: use current transform yaw (wall-bounce handoff)
        // - false: use animator root yaw when available (throw/root-motion handoff)
        if (ownerTransform != null)
        {
            // Choose yaw source:
            // - preserveCurrentFacing: keep the current transform yaw (used by wall-bounce handoff)
            // - otherwise: use animator root yaw when available (used by throw/root-motion handoff)
            Quaternion sourceRotation = preserveCurrentFacing
                ? ownerTransform.rotation
                : (animator != null ? animator.rootRotation : ownerTransform.rotation);
            // Optional 180 flip lets callers invert prone orientation without changing source selection.
            float yaw = sourceRotation.eulerAngles.y + (invertFacing ? 180f : 0f);
            // Apply upright-only rotation (yaw), discarding pitch/roll to keep prone stable on flat ground.
            lockedProneRotation = Quaternion.Euler(0f, yaw, 0f);
            ownerTransform.rotation = lockedProneRotation;
        }

        if (animator != null)
        {
            animator.speed = 1f;
            string stateToPlay = proneStateName;
            if (variantToUse == ProneVariant.FaceDown && !string.IsNullOrEmpty(proneFaceDownStateName))
                stateToPlay = proneFaceDownStateName;
            if (!string.IsNullOrEmpty(stateToPlay))
            {
                if (proneTransitionDuration > 0f)
                    animator.CrossFadeInFixedTime(stateToPlay, proneTransitionDuration, proneLayer, 0f);
                else
                    animator.Play(stateToPlay, proneLayer, 0f);
            }
        }
    }

    /// <summary>
    /// Call once per frame from SimpleEnemyAI.Update. Fires onProneEnded when the timer expires.
    /// </summary>
    public void Update()
    {
        if (!IsInProne) return;

        // Prone owns facing while active; keep yaw locked so root motion or other systems
        // cannot rotate the enemy away from the chosen prone orientation.
        if (ownerTransform != null)
            ownerTransform.rotation = lockedProneRotation;

        if (Time.time >= proneUntil)
        {
            IsInProne = false;
            onProneEnded?.Invoke();
        }
    }
}
