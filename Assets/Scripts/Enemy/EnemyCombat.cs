/*
 * ============================================================================
 * ENEMYCOMBAT.CS - Enemy attack execution using AttackData
 * ============================================================================
 * 
 * ENEMY ATTACK SYSTEM:
 * --------------------
 * 
 * This component handles the mechanics of enemy attacks:
 *   - Hitbox creation (Physics.OverlapSphere)
 *   - Damage dealing (via IDamageable interface)
 *   - Attack animation triggering
 *   - Forward lunge during attacks
 *   - Cooldown and lock state tracking
 * 
 * It reuses the same AttackData class as the player's Combat.cs,
 * so enemy attacks are configured the same way in the Inspector:
 *   - range, damage, hitboxRadius
 *   - lockDuration, cooldown
 *   - knockback, hitstun
 *   - animation trigger, lunge settings
 * 
 * USAGE:
 * ------
 * 
 * Behaviors (like StandoffBehavior) call DoAttack() when they decide
 * to attack. They check IsAttacking to know when the attack is done.
 * 
 * This component is OPTIONAL on enemies. Without it, behaviors that
 * try to attack will gracefully skip the attack phase.
 * 
 * ============================================================================
 */

using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class EnemyCombat : MonoBehaviour
{
    // ========================================================================
    // ATTACK DATA
    // ========================================================================

    [Header("Basic Attack")]
    [Tooltip("Attack configuration - same format as player attacks in Combat.cs")]
    public AttackData basicAttack = new AttackData
    {
        range = 1.8f,
        damage = 8,
        hitboxRadius = 0.5f,
        lockDuration = 0.6f,
        cooldown = 0.5f,
        knockback = 5f,
        knockbackUp = 0f,
        hitstun = 0.2f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeDistance = 0.4f,
        lungeDuration = 0.1f,
        hitStopDuration = 0.1f
    };

    [Tooltip("Max distance to player to use punch; beyond this uses kick. Tune so enemy punches when close (e.g. 2.2) and kicks when farther.")]
    public float punchRangeThreshold = 2.2f;

    [Header("Kick Attack (out of punch range)")]
    [Tooltip("Used when the player is out of range of the basic attack. Longer range so the enemy can still connect.")]
    public AttackData kickAttack = new AttackData
    {
        range = 2.6f,
        damage = 8,
        hitboxRadius = 0.5f,
        lockDuration = 0.65f,
        cooldown = 0.5f,
        knockback = 8f,
        knockbackUp = 0f,
        hitstun = 0.2f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Kick",
        lungeDistance = 0.7f,
        lungeDuration = 0.12f,
        hitStopDuration = 0.1f
    };

    [Header("Dodge Punish Attack")]
    [Tooltip("Used when the player recently dodged and is at punish distance. E.g. longer range / different animation.")]
    public AttackData dodgePunishAttack = new AttackData
    {
        range = 2.8f,
        damage = 10,
        hitboxRadius = 0.5f,
        lockDuration = 0.7f,
        cooldown = 0.5f,
        knockback = 6f,
        knockbackUp = 0f,
        hitstun = 0.25f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeDistance = 0.8f,
        lungeDuration = 0.15f,
        hitStopDuration = 0.1f
    };

    [Header("VFX (optional)")]
    [Tooltip("Optional. Spawned when the attack animation starts (e.g. swing trail).")]
    public GameObject attackStartVfxPrefab;
    
    [Tooltip("Optional. Spawned at hitbox center when the attack connects with a target.")]
    public GameObject hitConnectVfxPrefab;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private float attackEndTime;           // When the current attack lock expires
    private AttackData currentAttack;      // Which attack is active (for hitbox/lunge)
    private Animator animator;
    private CharacterController cc;

    // Lunge state (forward movement during attack)
    private bool lungePending;
    private float lungeTriggerTime;
    private float lungeEndTime;
    private float currentLungeDistance;
    private float currentLungeDuration;
    private Vector3 lungeDirection;
    
    // Delayed hitbox state
    private bool hitboxPending;
    private float hitboxTriggerTime;
    
    // Hit stop state
    private struct FrozenAnimator
    {
        public Animator animator;
        public float originalSpeed;
    }
    private float hitStopEndTime;
    private List<FrozenAnimator> frozenAnimators = new List<FrozenAnimator>();
    
    // Start-up and recovery (play first/last portion of attack animation slower)
    private float currentStartUpLength;
    private float currentStartUpSpeed;
    private float currentRecoveryLength;
    private float currentRecoverySpeed;
    private string currentAttackStateName;

    // ========================================================================
    // PUBLIC PROPERTIES
    // ========================================================================

    /// <summary>
    /// True while the enemy is locked in an attack animation.
    /// Behaviors check this to know when the attack is done.
    /// </summary>
    public bool IsAttacking => Time.time < attackEndTime;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        var h = GetComponent<EnemyHealth>();
        if (h != null && h.IsStunned)
        {
            attackEndTime = 0f;
            hitboxPending = false;
            lungePending = false;
            return;
        }
        UpdateLunge();
        UpdatePendingHitbox();
        UpdateHitStop();
        UpdateAttackStartUpSpeed();
    }

    // ========================================================================
    // ATTACK EXECUTION
    // ========================================================================

    /// <summary>
    /// Execute the basic attack. Called by behaviors when the enemy decides to attack (default).
    /// </summary>
    public void DoAttack()
    {
        DoAttack(basicAttack);
    }

    /// <summary>
    /// Execute a specific attack. Used for dodge-punish or other conditional attacks.
    /// </summary>
    public void DoAttack(AttackData attack)
    {
        currentAttack = attack;
        attackEndTime = Time.time + attack.lockDuration;
        currentStartUpLength = attack.startUpLength;
        currentStartUpSpeed = attack.startUpSpeed;
        currentRecoveryLength = attack.recoveryLength;
        currentRecoverySpeed = attack.recoverySpeed;
        currentAttackStateName = !string.IsNullOrEmpty(attack.animationTrigger) ? attack.animationTrigger : null;

        hitboxPending = false;

        if (attack.lungeDistance > 0)
        {
            lungePending = true;
            lungeTriggerTime = Time.time + (attack.lockDuration * attack.lungeFrame);
            lungeEndTime = lungeTriggerTime + attack.lungeDuration;
            currentLungeDistance = attack.lungeDistance;
            currentLungeDuration = attack.lungeDuration;
            lungeDirection = transform.forward;
        }
        else
        {
            lungePending = false;
        }

        if (animator != null && !string.IsNullOrEmpty(attack.animationTrigger))
            animator.Play(attack.animationTrigger, 0, 0f);

        if (attackStartVfxPrefab != null)
        {
            Vector3 pos = transform.position + attack.attackStartVfxPositionOffset;
            Quaternion rot = transform.rotation * Quaternion.Euler(attack.attackStartVfxRotationOffset);
            var go = Object.Instantiate(attackStartVfxPrefab, pos, rot);
            PlayVfx(go);
        }

        if (attack.hitboxDelay > 0f)
        {
            hitboxPending = true;
            hitboxTriggerTime = Time.time + attack.hitboxDelay;
        }
        else
        {
            ExecuteHitbox();
        }
    }
    
    // ========================================================================
    // HITBOX HELPERS
    // ========================================================================
    
    static void PlayVfx(GameObject instance)
    {
        if (instance == null) return;
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play();
    }
    
    /// <summary>
    /// Calculate the hitbox center using range + local-space offset for the current attack.
    /// </summary>
    Vector3 CalculateHitboxCenter()
    {
        AttackData a = currentAttack != null ? currentAttack : basicAttack;
        return transform.position
            + transform.forward * a.range
            + transform.right   * a.hitboxOffset.x
            + transform.up      * a.hitboxOffset.y
            + transform.forward * a.hitboxOffset.z;
    }
    
    /// <summary>
    /// Fires the hitbox: OverlapSphere, damage dealing, knockback, and hit stop.
    /// Each damageable is only hit once per hitbox fire (multiple colliders on same object are deduplicated).
    /// </summary>
    void ExecuteHitbox()
    {
        hitboxPending = false;
        AttackData a = currentAttack != null ? currentAttack : basicAttack;

        Vector3 center = CalculateHitboxCenter();

        Collider[] hits = Physics.OverlapSphere(
            center,
            a.hitboxRadius,
            ~0,
            QueryTriggerInteraction.Ignore
        );
        
        bool didHit = false;
        var alreadyHit = new System.Collections.Generic.HashSet<Component>();

        foreach (var c in hits)
        {
            var damageable = c.GetComponentInParent<IDamageable>();
            if (damageable == null) continue;

            // Don't hit ourselves
            if ((damageable as Component)?.gameObject == gameObject) continue;

            var comp = damageable as Component;
            if (comp != null && alreadyHit.Contains(comp)) continue;
            if (comp != null) alreadyHit.Add(comp);

            Transform targetTransform = (damageable as Component)?.transform;
            if (targetTransform == null) continue;

            // Knockback direction: away from attacker
            Vector3 horizontalDir = targetTransform.position - transform.position;
            horizontalDir.y = 0f;
            if (horizontalDir.sqrMagnitude < 0.001f) horizontalDir = transform.forward;
            horizontalDir.Normalize();

            Vector3 knockbackVector = (horizontalDir * a.knockback)
                                    + (Vector3.up * a.knockbackUp);

            float airborne = a.makesAirborne ? a.airborneDuration : 0f;
            damageable.TakeHit(a.damage, knockbackVector, a.hitstun, airborne, a.hitStopDuration);

            // Freeze target's animator for hit stop
            if (a.hitStopDuration > 0f)
            {
                Animator targetAnim = targetTransform.GetComponentInChildren<Animator>();
                if (targetAnim != null && !frozenAnimators.Any(f => f.animator == targetAnim))
                {
                    frozenAnimators.Add(new FrozenAnimator { animator = targetAnim, originalSpeed = targetAnim.speed });
                    targetAnim.speed = 0f;
                }
            }
            
            didHit = true;
        }
        
        if (didHit && hitConnectVfxPrefab != null)
        {
            Quaternion rot = (center - transform.position).sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(center - transform.position)
                : transform.rotation;
            rot = rot * Quaternion.Euler(a.hitConnectVfxRotationOffset);
            var go = Object.Instantiate(hitConnectVfxPrefab, center + a.hitConnectVfxPositionOffset, rot);
            PlayVfx(go);
        }
        
        // Apply hit stop to attacker if we hit something
        if (didHit && a.hitStopDuration > 0f)
        {
            hitStopEndTime = Time.time + a.hitStopDuration;
            
            if (animator != null && !frozenAnimators.Any(f => f.animator == animator))
            {
                frozenAnimators.Add(new FrozenAnimator { animator = animator, originalSpeed = animator.speed });
                animator.speed = 0f;
            }
        }
    }
    
    void UpdatePendingHitbox()
    {
        if (!hitboxPending) return;
        
        if (Time.time >= hitboxTriggerTime)
        {
            ExecuteHitbox();
        }
    }
    
    // ========================================================================
    // HIT STOP
    // ========================================================================
    
    void UpdateHitStop()
    {
        if (frozenAnimators.Count > 0 && Time.time >= hitStopEndTime)
        {
            foreach (var frozen in frozenAnimators)
            {
                if (frozen.animator != null)
                {
                    frozen.animator.speed = frozen.originalSpeed;
                }
            }
            frozenAnimators.Clear();
        }
    }
    
    void UpdateAttackStartUpSpeed()
    {
        if (animator == null) return;
        if (Time.time >= attackEndTime)
        {
            if ((currentStartUpLength > 0f || currentRecoveryLength > 0f) && !frozenAnimators.Any(f => f.animator == animator))
                animator.speed = 1f;
            return;
        }
        if (frozenAnimators.Any(f => f.animator == animator)) return;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!string.IsNullOrEmpty(currentAttackStateName) && !state.IsName(currentAttackStateName))
        {
            animator.speed = 1f;
            return;
        }
        bool useStartUp = currentStartUpLength > 0f && currentStartUpSpeed < 1f;
        bool useRecovery = currentRecoveryLength > 0f && currentRecoverySpeed < 1f;
        if (!useStartUp && !useRecovery)
        {
            animator.speed = 1f;
            return;
        }
        float nt = state.normalizedTime;
        if (nt >= 1f)
            animator.speed = 1f;
        else if (useStartUp && nt < currentStartUpLength)
            animator.speed = currentStartUpSpeed;
        else if (useRecovery && nt >= (1f - currentRecoveryLength))
            animator.speed = currentRecoverySpeed;
        else
            animator.speed = 1f;
    }
    
    // ========================================================================
    // LUNGE (forward movement during attack)
    // ========================================================================

    /*
     * Same lunge system as player Combat.cs:
     * Moves the enemy forward during the attack at the configured time.
     * Uses CharacterController.Move() for collision-aware movement.
     */
    void UpdateLunge()
    {
        if (!lungePending) return;
        // Freeze attacker position during hitstop
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime) return;

        if (Time.time >= lungeTriggerTime && Time.time < lungeEndTime)
        {
            float moveAmount = (currentLungeDistance / currentLungeDuration) * Time.deltaTime;
            if (cc != null && cc.enabled)
            {
                cc.Move(lungeDirection * moveAmount);
            }
        }

        if (Time.time >= lungeEndTime)
        {
            lungePending = false;
        }
    }
}
