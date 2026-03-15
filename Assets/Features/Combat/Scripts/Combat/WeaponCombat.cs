using UnityEngine;

/// <summary>
/// Bootstrap weapon combat component.
/// Inherits current Combat behavior so you can start wiring a separate component
/// without duplicating the full Combat partial files yet.
/// </summary>
public class WeaponCombat : Combat
{
    [System.Serializable]
    public struct HitboxSlot
    {
        [Tooltip("Animation-event ID used by BeginHitbox(int)/EndHitbox(int).")]
        public int id;
        [Tooltip("Hitbox to activate when this ID is requested.")]
        public WeaponTipHitbox hitbox;
    }

    [Header("Weapon Tip Hitbox")]
    [Tooltip("Tip hitbox component enabled only during active weapon frames.")]
    public WeaponTipHitbox weaponTipHitbox;
    [Tooltip("Auto-find WeaponTipHitbox in children if not assigned.")]
    public bool autoFindWeaponTipHitbox = true;
    [Tooltip("Optional additional hitboxes addressable by ID (e.g. 1=left hand, 2=foot).")]
    public HitboxSlot[] hitboxSlots;

    // Animation Event hook: call at first active weapon frame.
    public void BeginWeaponTipActiveFrames()
    {
        BeginHitbox(0);
    }

    // Animation Event hook: call at last active weapon frame.
    public void EndWeaponTipActiveFrames()
    {
        EndHitbox(0);
    }

    // Animation Event hook: call at first active frame for the provided hitbox ID.
    public void BeginHitbox(int id)
    {
        WeaponTipHitbox hitbox = GetHitboxById(id);
        if (hitbox == null) return;

        // Damage-interrupt fallback from base Combat: ignore any late hitbox events
        // until a brand-new attack is committed.
        if (IsHitboxActivationSuppressed) return;

        // Use only the currently committed attack from base Combat.
        // If combat was interrupted and events still fire, do not synthesize a fallback move.
        AttackData attack = CurrentAttackData;
        if (!IsAttacking || attack == null || attack.hitboxType != AttackHitboxType.WeaponStrike) return;

        hitbox.HitConfirmed -= OnWeaponTipHitConfirmed;
        hitbox.HitConfirmed += OnWeaponTipHitConfirmed;
        hitbox.BeginActiveFrames(transform, attack, ChargeReleaseDamageScale, ChargeReleaseKnockbackScale);
    }

    // Animation Event hook: call at last active frame for the provided hitbox ID.
    public void EndHitbox(int id)
    {
        WeaponTipHitbox hitbox = GetHitboxById(id);
        StopHitbox(hitbox);
    }

    // Optional safety event: force-close every configured hitbox.
    public void EndAllHitboxes()
    {
        StopAllConfiguredHitboxes();
    }

    // Safety: always disable active tip collider if component gets disabled.
    void OnDisable()
    {
        StopAllConfiguredHitboxes();
    }

    void EnsureWeaponTipHitbox()
    {
        if (weaponTipHitbox != null || !autoFindWeaponTipHitbox) return;
        weaponTipHitbox = GetComponentInChildren<WeaponTipHitbox>(true);
    }

    WeaponTipHitbox GetHitboxById(int id)
    {
        if (hitboxSlots != null)
        {
            for (int i = 0; i < hitboxSlots.Length; i++)
            {
                if (hitboxSlots[i].id == id && hitboxSlots[i].hitbox != null)
                    return hitboxSlots[i].hitbox;
            }
        }

        if (id != 0) return null;
        EnsureWeaponTipHitbox();
        return weaponTipHitbox;
    }

    void StopHitbox(WeaponTipHitbox hitbox)
    {
        if (hitbox == null) return;
        hitbox.HitConfirmed -= OnWeaponTipHitConfirmed;
        hitbox.EndActiveFrames();
    }

    void StopAllConfiguredHitboxes()
    {
        EnsureWeaponTipHitbox();
        StopHitbox(weaponTipHitbox);

        if (hitboxSlots == null) return;
        for (int i = 0; i < hitboxSlots.Length; i++)
        {
            WeaponTipHitbox hitbox = hitboxSlots[i].hitbox;
            if (hitbox == null || hitbox == weaponTipHitbox) continue;
            StopHitbox(hitbox);
        }
    }

    void OnWeaponTipHitConfirmed(AttackData attack, Transform targetTransform, Vector3 hitPoint)
    {
        NotifyHitConfirmedFromExternalHitbox(attack, targetTransform, hitPoint);
    }

    protected override void OnAttackCommitted(AttackData attack)
    {
        // Combo cancels can skip prior EndWeaponTipActiveFrames events.
        // Hard-reset stale active window/hit cache whenever a new attack begins.
        StopAllConfiguredHitboxes();
    }
}
