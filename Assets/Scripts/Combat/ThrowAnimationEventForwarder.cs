using UnityEngine;

/// <summary>
/// Place on the same GameObject as the Animator that plays the throw clip.
/// Forwards throw animation events to Combat on a parent (so the event has a receiver).
/// </summary>
public class ThrowAnimationEventForwarder : MonoBehaviour
{
    /// <summary>Call at the frame you want to detach the victim from the grab socket (before OnThrowRelease).</summary>
    public void OnThrowUnparent()
    {
        var combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnThrowUnparent();
    }
    public void OnThrowRelease()
    {
        var combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnThrowRelease();
    }
    public void OnThrowRelease(int releaseProfileIndex)
    {
        var combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnThrowRelease(releaseProfileIndex);
    }
    public void OnThrowDamage(int profileIndex)
    {
        var combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnThrowDamage(profileIndex);
    }
}
