using UnityEngine;

/// <summary>
/// Place on the same GameObject as the Animator that plays the throw clip.
/// Forwards throw animation events to EnemyCombat on the same GameObject.
/// </summary>
[RequireComponent(typeof(EnemyCombat))]
public class ThrowAnimationEventForwarder : MonoBehaviour
{
    EnemyCombat combat;
    bool warnedMissingThrowHandlers;

    void Awake()
    {
        combat = GetComponent<EnemyCombat>();
        if (combat == null)
            Debug.LogWarning("ThrowAnimationEventForwarder could not find EnemyCombat on the same GameObject.", this);
    }

    /// <summary>Call at the frame you want to detach the victim from the grab socket (before OnThrowRelease).</summary>
    public void OnThrowUnparent()
    {
        WarnMissingThrowHandlers(nameof(OnThrowUnparent));
    }
    public void OnThrowVictimRootMotion(int enabled)
    {
        WarnMissingThrowHandlers(nameof(OnThrowVictimRootMotion));
    }
    public void OnThrowVictimRootMotionOn()
    {
        WarnMissingThrowHandlers(nameof(OnThrowVictimRootMotionOn));
    }
    public void OnThrowVictimRootMotionOff()
    {
        WarnMissingThrowHandlers(nameof(OnThrowVictimRootMotionOff));
    }
    public void OnThrowPlayerRootMotion(int enabled)
    {
        WarnMissingThrowHandlers(nameof(OnThrowPlayerRootMotion));
    }
    public void OnThrowPlayerRootMotionOn()
    {
        WarnMissingThrowHandlers(nameof(OnThrowPlayerRootMotionOn));
    }
    public void OnThrowPlayerRootMotionOff()
    {
        WarnMissingThrowHandlers(nameof(OnThrowPlayerRootMotionOff));
    }
    public void OnThrowRelease()
    {
        WarnMissingThrowHandlers(nameof(OnThrowRelease));
    }
    public void OnThrowRelease(int releaseProfileIndex)
    {
        WarnMissingThrowHandlers(nameof(OnThrowRelease));
    }
    public void OnThrowDamage(int profileIndex)
    {
        WarnMissingThrowHandlers(nameof(OnThrowDamage));
    }

    void WarnMissingThrowHandlers(string eventName)
    {
        if (combat == null || warnedMissingThrowHandlers) return;
        warnedMissingThrowHandlers = true;
        Debug.LogWarning($"ThrowAnimationEventForwarder received '{eventName}', but EnemyCombat has no throw event handlers. Event ignored.", this);
    }
}
