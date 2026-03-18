using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Weapon hitbox component. Attach to the weapon GameObject alongside a BoxCollider.
/// The box collider's shape, size, and position define the exact hit volume each frame.
/// Uses Physics.OverlapBox polling instead of OnTriggerEnter — no Rigidbody required,
/// immune to tunneling on fast swings.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WeaponTipHitbox : MonoBehaviour
{
    public event Action<AttackData, Transform, Vector3> HitConfirmed;

    private BoxCollider boxCollider;
    private Transform attackerRoot;
    private AttackData currentAttack;
    private bool active;
    private float damageMultiplier = 1f;
    private float knockbackMultiplier = 1f;
    private readonly HashSet<Component> alreadyHit = new HashSet<Component>();
    private static readonly Collider[] overlapBuffer = new Collider[32]; // reused — avoids per-frame allocation

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider>();
    }

    public void BeginActiveFrames(Transform attacker, AttackData attack, float damageScale = 1f, float knockbackScale = 1f)
    {
        attackerRoot = attacker;
        currentAttack = attack;
        damageMultiplier = damageScale;
        knockbackMultiplier = knockbackScale;
        // Do NOT clear alreadyHit here — it persists for the full attack duration so
        // an enemy hit in one active window cannot be hit again in a later window of
        // the same attack. ResetHitCache() is called by WeaponCombat.OnAttackCommitted()
        // when a brand-new attack starts.
        active = (boxCollider != null && attack != null);
    }

    public void EndActiveFrames()
    {
        active = false;
        currentAttack = null;
        // Do NOT clear alreadyHit here — a multi-window attack calls EndActiveFrames
        // between windows and we need the hit cache to persist across those windows.
    }

    void Update()
    {
        if (!active || currentAttack == null) return;

        // Poll the box collider's exact shape every frame.
        // Polling every frame means no tunneling regardless of swing speed.
        // alreadyHit deduplicates so each enemy is only hit once per attack.
        HitOverlapBox();
    }

    /// <summary>
    /// Clear the per-attack hit cache. Call this when a new attack is committed,
    /// not at the start/end of each active hitbox window.
    /// </summary>
    public void ResetHitCache()
    {
        alreadyHit.Clear();
    }

    void HitOverlapBox()
    {
        // Use the box collider's world-space center, half extents, and rotation
        // so the hit volume exactly matches what's visible in the Inspector.
        Vector3 center      = transform.TransformPoint(boxCollider.center);
        Vector3 s = transform.lossyScale;
        Vector3 halfExtents = new Vector3(
            Mathf.Abs(boxCollider.size.x * 0.5f * s.x),
            Mathf.Abs(boxCollider.size.y * 0.5f * s.y),
            Mathf.Abs(boxCollider.size.z * 0.5f * s.z));

        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            IDamageable damageable = overlapBuffer[i].GetComponentInParent<IDamageable>();
            Component target = damageable as Component;
            if (target == null) continue;
            if (attackerRoot != null && target.transform.root == attackerRoot.root) continue;
            if (alreadyHit.Contains(target)) continue;
            HitSingleTarget(damageable, target);
        }
    }

    void OnDrawGizmos()
    {
        if (boxCollider == null) boxCollider = GetComponent<BoxCollider>();
        if (boxCollider == null) return;
        Gizmos.color = active ? new Color(1f, 0.2f, 0.2f, 0.5f) : new Color(0.2f, 1f, 0.2f, 0.2f);
        Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(boxCollider.center), transform.rotation, Vector3.one);
        Vector3 s = transform.lossyScale;
        Vector3 size = new Vector3(
            Mathf.Abs(boxCollider.size.x * s.x),
            Mathf.Abs(boxCollider.size.y * s.y),
            Mathf.Abs(boxCollider.size.z * s.z));
        Gizmos.DrawWireCube(Vector3.zero, size);
    }

    // Applies damage, knockback, stun buildup, and fires HitConfirmed for one target.
    void HitSingleTarget(IDamageable damageable, Component target)
    {
        alreadyHit.Add(target);

        Vector3 from = attackerRoot != null ? attackerRoot.position : transform.position;
        Vector3 dir  = target.transform.position - from;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            dir = attackerRoot != null ? attackerRoot.forward : transform.forward;
        dir.Normalize();

        Vector3 knockbackVector = ((dir * currentAttack.knockback) + (Vector3.up * currentAttack.knockbackUp)) * knockbackMultiplier;
        float airborne = currentAttack.makesAirborne ? currentAttack.airborneDuration : 0f;

        // Apply stun buildup before TakeHit so TriggerHitAnimation can see IsStandingStunned
        var targetStunMeter = target.GetComponent<EnemyStunMeter>();
        if (targetStunMeter != null)
            targetStunMeter.AddStun(currentAttack.stunBuildup, currentAttack.knockback * knockbackMultiplier);

        damageable.TakeHit(
            Mathf.RoundToInt(currentAttack.damage * damageMultiplier),
            knockbackVector,
            currentAttack.hitstun,
            airborne,
            currentAttack.hitStopDuration,
            currentAttack.heaviness,
            currentAttack.height
        );

        Vector3 hitPoint = target.GetComponent<Collider>()?.ClosestPoint(transform.position) ?? target.transform.position;
        HitConfirmed?.Invoke(currentAttack, target.transform, hitPoint);
    }
}
