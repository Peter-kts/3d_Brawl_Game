using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Trigger-based weapon tip hitbox. Enable only during active attack frames.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WeaponTipHitbox : MonoBehaviour
{
    public event Action<AttackData, Transform, Vector3> HitConfirmed;

    private Collider tipCollider;
    private Transform attackerRoot;
    private AttackData currentAttack;
    private bool active;
    private float damageMultiplier = 1f;
    private float knockbackMultiplier = 1f;
    private readonly HashSet<Component> alreadyHit = new HashSet<Component>();

    void Awake()
    {
        tipCollider = GetComponent<Collider>();
        if (tipCollider != null)
        {
            tipCollider.isTrigger = true;
            tipCollider.enabled = false;
        }
    }

    public void BeginActiveFrames(Transform attacker, AttackData attack, float damageScale = 1f, float knockbackScale = 1f)
    {
        attackerRoot = attacker;
        currentAttack = attack;
        damageMultiplier = damageScale;
        knockbackMultiplier = knockbackScale;
        alreadyHit.Clear();
        active = (tipCollider != null && attack != null);
        if (tipCollider != null) tipCollider.enabled = active;
    }

    public void EndActiveFrames()
    {
        active = false;
        alreadyHit.Clear();
        currentAttack = null;
        if (tipCollider != null) tipCollider.enabled = false;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!active || currentAttack == null) return;

        IDamageable damageable = other.GetComponentInParent<IDamageable>();
        Component target = damageable as Component;
        if (target == null) return;

        if (attackerRoot != null && target.transform.root == attackerRoot.root) return;
        if (alreadyHit.Contains(target)) return;
        alreadyHit.Add(target);

        Vector3 from = attackerRoot != null ? attackerRoot.position : transform.position;
        Vector3 dir = target.transform.position - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            dir = attackerRoot != null ? attackerRoot.forward : transform.forward;
        dir.Normalize();

        Vector3 knockbackVector = ((dir * currentAttack.knockback) + (Vector3.up * currentAttack.knockbackUp)) * knockbackMultiplier;
        float airborne = currentAttack.makesAirborne ? currentAttack.airborneDuration : 0f;

        damageable.TakeHit(
            Mathf.RoundToInt(currentAttack.damage * damageMultiplier),
            knockbackVector,
            currentAttack.hitstun,
            airborne,
            currentAttack.hitStopDuration,
            currentAttack.heaviness,
            currentAttack.height
        );

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        HitConfirmed?.Invoke(currentAttack, target.transform, hitPoint);
    }
}
