using UnityEngine;
using System.Collections.Generic;
using System;

/// <summary>
/// Weapon hitbox component. Attach to the weapon GameObject alongside a BoxCollider.
/// The box collider's shape, size, and position define the exact hit volume each frame.
/// Uses Physics.OverlapBox polling instead of OnTriggerEnter — no Rigidbody required,
/// with swept sub-sampling between frames to reduce misses on fast swings.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class WeaponTipHitbox : MonoBehaviour
{
    public event Action<AttackData, Transform, Vector3> HitConfirmed;
    [Tooltip("Physics layers considered valid hit targets for this hitbox overlap.")]
    public LayerMask hitLayers = ~0;

    private BoxCollider boxCollider;
    private Transform attackerRoot;
    private Animator attackerAnimator;
    private AttackData currentAttack;
    private bool active;
    private float damageMultiplier = 1f;
    private float knockbackMultiplier = 1f;
    private readonly HashSet<Component> alreadyHit = new HashSet<Component>();
    private static readonly Collider[] overlapBuffer = new Collider[64]; // reused — avoids per-frame allocation
    private Vector3 previousCenter;
    private Quaternion previousRotation;
    private bool hasPreviousSample;
    private const float SweepStepDistance = 0.2f;
    private const float SweepStepAngle = 12f;
    private const int MaxSweepSteps = 6;

    void Awake()
    {
        boxCollider = GetComponent<BoxCollider>();
    }

    public void BeginActiveFrames(Transform attacker, AttackData attack, float damageScale = 1f, float knockbackScale = 1f)
    {
        attackerRoot = attacker;
        attackerAnimator = attacker != null ? attacker.GetComponentInChildren<Animator>() : null;
        currentAttack = attack;
        damageMultiplier = damageScale;
        knockbackMultiplier = knockbackScale;
        // Do NOT clear alreadyHit here — it persists for the full attack duration so
        // an enemy hit in one active window cannot be hit again in a later window of
        // the same attack. ResetHitCache() is called by WeaponCombat.OnAttackCommitted()
        // when a brand-new attack starts.
        active = (boxCollider != null && attack != null);
        hasPreviousSample = false;
        if (active)
            HitOverlapBox();
    }

    public void EndActiveFrames()
    {
        // Capture one last sample before closing the window to avoid dropping
        // the final pose when EndHitbox is fired during animator evaluation.
        if (active && currentAttack != null)
            HitOverlapBox();
        active = false;
        currentAttack = null;
        hasPreviousSample = false;
        // Do NOT clear alreadyHit here — a multi-window attack calls EndActiveFrames
        // between windows and we need the hit cache to persist across those windows.
    }

    void LateUpdate()
    {
        if (!active || currentAttack == null) return;
        if (attackerAnimator != null && Mathf.Approximately(attackerAnimator.speed, 0f)) return;

        // Poll after animator updates so the sampled volume matches this frame's final pose.
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
        Vector3 center      = transform.TransformPoint(boxCollider.center);
        Quaternion rotation = transform.rotation;
        Vector3 halfExtents = GetHalfExtents();

        if (hasPreviousSample)
        {
            float centerDistance = Vector3.Distance(previousCenter, center);
            float rotationDelta = Quaternion.Angle(previousRotation, rotation);
            int sweepSteps = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Max(centerDistance / SweepStepDistance, rotationDelta / SweepStepAngle)),
                1,
                MaxSweepSteps
            );

            for (int step = 1; step <= sweepSteps; step++)
            {
                float t = step / (float)sweepSteps;
                Vector3 sampleCenter = Vector3.Lerp(previousCenter, center, t);
                Quaternion sampleRotation = Quaternion.Slerp(previousRotation, rotation, t);
                ProcessOverlapSample(sampleCenter, halfExtents, sampleRotation);
            }
        }
        else
        {
            ProcessOverlapSample(center, halfExtents, rotation);
        }

        previousCenter = center;
        previousRotation = rotation;
        hasPreviousSample = true;
    }

    Vector3 GetHalfExtents()
    {
        Vector3 s = transform.lossyScale;
        return new Vector3(
            Mathf.Abs(boxCollider.size.x * 0.5f * s.x),
            Mathf.Abs(boxCollider.size.y * 0.5f * s.y),
            Mathf.Abs(boxCollider.size.z * 0.5f * s.z));
    }

    void ProcessOverlapSample(Vector3 center, Vector3 halfExtents, Quaternion rotation)
    {
        EntityHealth attackerHealth = attackerRoot != null ? attackerRoot.GetComponentInParent<EntityHealth>() : null;
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, rotation, hitLayers.value, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider overlapCollider = overlapBuffer[i];
            if (overlapCollider == null) continue;

            IDamageable damageable = overlapCollider.GetComponentInParent<IDamageable>();
            Component target = damageable as Component;
            if (target == null) continue;
            if (attackerRoot != null && target.transform.root == attackerRoot.root) continue;
            
            EntityHealth targetHealth = overlapCollider.GetComponentInParent<EntityHealth>();
            if (attackerHealth != null && targetHealth != null && !TeamUtil.AreHostile(attackerHealth.team, targetHealth.team))
                continue;
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

        if (currentAttack.makesAirborne)
        {
            var targetAI = target.GetComponentInParent<SimpleEnemyAI>();
            if (targetAI != null && targetAI.ProneSystem != null)
                targetAI.ProneSystem.OverrideNextProneVariant(currentAttack.proneVariant);
        }

        damageable.TakeHit(
            Mathf.RoundToInt(currentAttack.damage * damageMultiplier),
            knockbackVector,
            currentAttack.hitstun,
            airborne,
            currentAttack.hitStopDuration,
            currentAttack.heaviness,
            currentAttack.height,
            attackerRoot?.gameObject
        );

        Vector3 hitPoint = target.GetComponent<Collider>()?.ClosestPoint(transform.position) ?? target.transform.position;
        HitConfirmed?.Invoke(currentAttack, target.transform, hitPoint);
    }
}
