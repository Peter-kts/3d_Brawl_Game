using UnityEngine;

/// <summary>
/// Visual-reaction state machine (animation-only).
/// Keeps short-lived animation priority decisions separate from gameplay stun timers.
/// </summary>
public class EnemyReactionStateMachine
{
    private float knockbackEntryHoldUntil;

    /// <summary>
    /// Requests temporary visual priority for knockback-stun entry so normal hit
    /// reactions do not immediately overwrite it in the same stun window.
    /// </summary>
    public void RequestKnockbackEntry(float now, float graceSeconds)
    {
        knockbackEntryHoldUntil = Mathf.Max(knockbackEntryHoldUntil, now + Mathf.Max(0f, graceSeconds));
    }

    /// <summary>
    /// True while within the knockback-entry grace window or while the configured
    /// knockback-entry state is currently playing.
    /// </summary>
    public bool IsKnockbackEntryActive(float now, Animator animator, int layerIndex, string stateName)
    {
        if (now < knockbackEntryHoldUntil)
            return true;

        if (animator == null || string.IsNullOrEmpty(stateName))
            return false;
        if (layerIndex < 0 || layerIndex >= animator.layerCount)
            return false;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(layerIndex);
        return info.IsName(stateName) || info.IsName(animator.GetLayerName(layerIndex) + "." + stateName);
    }
}
