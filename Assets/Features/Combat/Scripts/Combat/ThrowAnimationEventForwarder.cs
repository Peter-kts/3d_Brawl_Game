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

    /// <summary>Enemy-side relay for OnThrowAttach (no-arg). Warns if no handler exists.</summary>
    public void OnThrowAttach()
    {
        WarnMissingThrowHandlers(nameof(OnThrowAttach));
    }
    /// <summary>Enemy-side relay for OnThrowAttachSocket(int socketIndex). Warns if no handler exists.</summary>
    public void OnThrowAttachSocket(int socketIndex)
    {
        WarnMissingThrowHandlers(nameof(OnThrowAttachSocket));
    }

    /// <summary>
    /// Enemy-side relay for OnThrowVictimNudge. Finds the Combat holding this victim and
    /// forwards the nudge so events authored on victim clips reach the player throw system.
    /// </summary>
    public void OnThrowVictimNudge(Object nudgeAsset)
    {
        Combat c = FindHoldingCombat();
        if (c != null) c.OnThrowVictimNudge(nudgeAsset);
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
    public void OnThrowDamage()
    {
        WarnMissingThrowHandlers(nameof(OnThrowDamage));
    }

    public void OnThrowHitStop(int index)
    {
        Combat c = FindHoldingCombat();
        if (c != null) c.OnThrowHitStop(index);
    }

    Combat FindHoldingCombat()
    {
        EnemyHealth myHealth = GetComponentInParent<EnemyHealth>();
        if (myHealth == null) return null;
        Combat[] combats = FindObjectsOfType<Combat>();
        for (int i = 0; i < combats.Length; i++)
        {
            if (combats[i] != null && combats[i].IsHoldingThrowVictim(myHealth))
                return combats[i];
        }
        return null;
    }

    void WarnMissingThrowHandlers(string eventName)
    {
        if (combat == null || warnedMissingThrowHandlers) return;
        warnedMissingThrowHandlers = true;
        Debug.LogWarning($"ThrowAnimationEventForwarder received '{eventName}', but EnemyCombat has no throw event handlers. Event ignored.", this);
    }
}
