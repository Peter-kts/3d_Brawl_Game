using UnityEngine;

/// <summary>
/// Optional helper for clips played on child animators.
/// Forwards attack SFX animation events to parent combat components.
/// </summary>
public class AttackAnimationEventForwarder : MonoBehaviour
{
    void ForwardBeginHitbox(int id)
    {
        WeaponCombat weaponCombat = GetComponentInParent<WeaponCombat>();
        if (weaponCombat != null)
            weaponCombat.BeginHitbox(id);
    }

    void ForwardEndHitbox(int id)
    {
        WeaponCombat weaponCombat = GetComponentInParent<WeaponCombat>();
        if (weaponCombat != null)
            weaponCombat.EndHitbox(id);
    }

    void ForwardChargeWindowStart()
    {
        Combat combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnChargeWindowStart();
    }

    void ForwardChargeWindowEnd()
    {
        Combat combat = GetComponentInParent<Combat>();
        if (combat != null)
            combat.OnChargeWindowEnd();
    }

    void ForwardAttackSfxEvent(int eventId)
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

    public void OnAttackSfxEvent(int eventId)
    {
        ForwardAttackSfxEvent(eventId);
    }

    public void OnAttackSfxEvent()
    {
        OnAttackSfxEvent(0);
    }

    public void OnAttackSfxEvent(float eventId)
    {
        OnAttackSfxEvent(Mathf.RoundToInt(eventId));
    }

    public void OnAttackSfxEvent(string eventId)
    {
        int parsed;
        OnAttackSfxEvent(int.TryParse(eventId, out parsed) ? parsed : 0);
    }

    // Alias for naming variants often typed in clips.
    public void OnAttackSFXEvent()
    {
        OnAttackSfxEvent(0);
    }

    public void OnAttackSFXEvent(int eventId)
    {
        ForwardAttackSfxEvent(eventId);
    }

    public void OnChargeWindowStart()
    {
        ForwardChargeWindowStart();
    }

    public void OnChargeWindowEnd()
    {
        ForwardChargeWindowEnd();
    }

    public void OnAttackChargeWindowStart()
    {
        ForwardChargeWindowStart();
    }

    public void OnAttackChargeWindowEnd()
    {
        ForwardChargeWindowEnd();
    }

    public void OnBeginHitbox(int id)
    {
        ForwardBeginHitbox(id);
    }

    public void OnEndHitbox(int id)
    {
        ForwardEndHitbox(id);
    }

    public void OnBeginHitbox()
    {
        OnBeginHitbox(0);
    }

    public void OnEndHitbox()
    {
        OnEndHitbox(0);
    }
}
