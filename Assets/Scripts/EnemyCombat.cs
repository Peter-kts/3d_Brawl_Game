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

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private float attackEndTime;           // When the current attack lock expires
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
        UpdateLunge();
        UpdatePendingHitbox();
        UpdateHitStop();
    }

    // ========================================================================
    // ATTACK EXECUTION
    // ========================================================================

    /// <summary>
    /// Execute the basic attack. Creates a hitbox, deals damage, plays animation.
    /// Called by behaviors (e.g., StandoffBehavior) when the enemy decides to attack.
    /// </summary>
    public void DoAttack()
    {
        // Set the attack lock (enemy can't act until this expires)
        attackEndTime = Time.time + basicAttack.lockDuration;
        
        // Cancel any pending hitbox from previous attack
        hitboxPending = false;

        // --------------------------------------------------------------------
        // Forward lunge setup
        // --------------------------------------------------------------------

        /*
         * Same lunge system as player Combat.cs:
         * The enemy steps forward during the attack for added reach/impact.
         * lungeFrame determines WHEN during the attack the step happens.
         * lungeDistance/lungeDuration control HOW FAR and HOW FAST.
         */
        if (basicAttack.lungeDistance > 0)
        {
            lungePending = true;
            lungeTriggerTime = Time.time + (basicAttack.lockDuration * basicAttack.lungeFrame);
            lungeEndTime = lungeTriggerTime + basicAttack.lungeDuration;
            currentLungeDistance = basicAttack.lungeDistance;
            currentLungeDuration = basicAttack.lungeDuration;
            lungeDirection = transform.forward;
        }

        // --------------------------------------------------------------------
        // Animation
        // --------------------------------------------------------------------

        if (animator != null && !string.IsNullOrEmpty(basicAttack.animationTrigger))
        {
            if (basicAttack.crossfadeDuration > 0f)
            {
                animator.CrossFadeInFixedTime(
                    basicAttack.animationTrigger,
                    basicAttack.crossfadeDuration,
                    0, 0f
                );
            }
            else
            {
                animator.Play(basicAttack.animationTrigger, 0, 0f);
            }
        }

        // --------------------------------------------------------------------
        // Hitbox (scheduled with optional delay)
        // --------------------------------------------------------------------

        if (basicAttack.hitboxDelay > 0f)
        {
            // Schedule hitbox for later (syncs with animation)
            hitboxPending = true;
            hitboxTriggerTime = Time.time + basicAttack.hitboxDelay;
        }
        else
        {
            // Fire immediately (backward compatible, delay = 0)
            ExecuteHitbox();
        }
    }
    
    // ========================================================================
    // HITBOX HELPERS
    // ========================================================================
    
    /// <summary>
    /// Calculate the hitbox center using range + local-space offset.
    /// </summary>
    Vector3 CalculateHitboxCenter()
    {
        return transform.position
            + transform.forward * basicAttack.range
            + transform.right   * basicAttack.hitboxOffset.x
            + transform.up      * basicAttack.hitboxOffset.y
            + transform.forward * basicAttack.hitboxOffset.z;
    }
    
    /// <summary>
    /// Fires the hitbox: OverlapSphere, damage dealing, knockback, and hit stop.
    /// Each damageable is only hit once per hitbox fire (multiple colliders on same object are deduplicated).
    /// </summary>
    void ExecuteHitbox()
    {
        hitboxPending = false;
        
        Vector3 center = CalculateHitboxCenter();

        Collider[] hits = Physics.OverlapSphere(
            center,
            basicAttack.hitboxRadius,
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

            Vector3 knockbackVector = (horizontalDir * basicAttack.knockback)
                                    + (Vector3.up * basicAttack.knockbackUp);

            float airborne = basicAttack.makesAirborne ? basicAttack.airborneDuration : 0f;
            damageable.TakeHit(basicAttack.damage, knockbackVector, basicAttack.hitstun, airborne, basicAttack.hitStopDuration);
            
            // Freeze target's animator for hit stop
            if (basicAttack.hitStopDuration > 0f)
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
        
        // Apply hit stop to attacker if we hit something
        if (didHit && basicAttack.hitStopDuration > 0f)
        {
            hitStopEndTime = Time.time + basicAttack.hitStopDuration;
            
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
            if (cc != null)
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
