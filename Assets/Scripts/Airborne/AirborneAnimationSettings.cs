using UnityEngine;

/// <summary>
/// Configures how an airborne animation is split into three phases:
/// Liftoff (play once), Loop (repeat while airborne), Crash (play once on landing).
/// All times are normalized (0-1) within a single Animator state.
/// Assign on the entity that RECEIVES the hit (SimpleEnemyAI, PlayerHealth, etc.).
/// </summary>
[System.Serializable]
public class AirborneAnimationSettings
{
    [Header("Animator State")]
    [Tooltip("Animator state name containing the full airborne animation (hit → launch → spin → crash)")]
    public string airborneStateName = "";

    [Tooltip("Animator layer index for the airborne state. Use 1 for the default enemy controller (Airborne state is on the Stun layer).")]
    public int airborneAnimationLayer = 1;

    [Tooltip("Crossfade blend duration when entering the airborne state (seconds). 0 = instant snap.")]
    public float crossfadeDuration = 0.05f;

    [Header("Liftoff Phase")]
    [Tooltip("Normalized time (0-1) where the liftoff portion begins (usually 0)")]
    [Range(0f, 1f)]
    public float liftoffStart = 0f;

    [Tooltip("Normalized time (0-1) where the liftoff portion ends and the loop begins")]
    [Range(0f, 1f)]
    public float liftoffEnd = 0.2f;

    [Header("Airborne Loop Phase")]
    [Tooltip("Normalized time (0-1) where the looping portion begins")]
    [Range(0f, 1f)]
    public float loopStart = 0.2f;

    [Tooltip("Normalized time (0-1) where the looping portion ends")]
    [Range(0f, 1f)]
    public float loopEnd = 0.6f;

    [Header("Crash Phase")]
    [Tooltip("Normalized time (0-1) where the crash/landing portion begins (usually equals loopEnd)")]
    [Range(0f, 1f)]
    public float crashStart = 0.6f;

    [Tooltip("Normalized time (0-1) where the crash/landing portion ends (usually 1)")]
    [Range(0f, 1f)]
    public float crashEnd = 1f;

    /// <summary>Returns true if a valid airborne state name has been assigned.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(airborneStateName);
}
