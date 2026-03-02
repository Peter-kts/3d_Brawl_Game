using UnityEngine;

/// <summary>
/// Optional helper for clips played on child animators.
/// Forwards attack SFX animation events to parent combat components.
/// </summary>
public class AttackAnimationEventForwarder : MonoBehaviour
{
    public void OnAttackSfxEvent(int eventId)
    {
        Combat combat = GetComponentInParent<Combat>();
        if (combat != null)
        {
            combat.OnAttackSfxEvent(eventId);
            return;
        }

        EnemyCombat enemyCombat = GetComponentInParent<EnemyCombat>();
        if (enemyCombat != null)
            enemyCombat.OnAttackSfxEvent(eventId);
    }

    public void OnAttackSfxEvent()
    {
        OnAttackSfxEvent(0);
    }
}
