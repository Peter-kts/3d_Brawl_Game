using UnityEngine;

/// <summary>
/// Authored as a ScriptableObject and referenced by an OnThrowVictimNudge animation event.
/// Smoothly nudges the throw victim by a local-space offset relative to the active grab socket.
/// Multiple simultaneous nudges accumulate additively — each independently lerps from zero to
/// its own target, so layering several events on the same clip is safe.
/// Duration is scaled by the player animator speed so charge slowdown, startup, and recovery
/// mechanics automatically stretch or compress the nudge to stay in sync with the throw animation.
/// </summary>
[CreateAssetMenu(fileName = "ThrowVictimNudge", menuName = "Combat/Throw Victim Nudge", order = 1)]
public class ThrowVictimNudge : ScriptableObject
{
    [Tooltip("Target offset in the grab socket's local space.\n" +
             "X = right of socket, Y = up, Z = forward.\n" +
             "The victim interpolates from zero toward this value over Duration seconds, " +
             "then holds it for the remainder of the throw.")]
    public Vector3 offset;

    [Tooltip("Seconds to interpolate from zero to the target offset (at 1x animator speed).\n" +
             "Automatically scales with throw charge, startup, and recovery speed.")]
    [Min(0f)]
    public float duration = 0.2f;
}
