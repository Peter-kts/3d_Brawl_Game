using UnityEngine;

/// <summary>
/// Place on the same GameObject as the Animator that plays the throw clip.
/// Forwards OnThrowRelease animation events to Combat on a parent (so the event has a receiver).
/// </summary>
public class ThrowAnimationEventForwarder : MonoBehaviour
{
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
    public void OnThrowDamage()
    {
        var combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnThrowDamage();
    }
    public void OnThrowDamage(int profileIndex)
    {
        var combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnThrowDamage(profileIndex);
    }
}
