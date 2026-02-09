/*
 * ============================================================================
 * PLAYERHEALTH.CS - Player HP, damage, knockback, hit stun, and hit reaction
 * ============================================================================
 *
 * Implements IDamageable so enemy attacks (EnemyCombat) can damage the player
 * when the enemy hitbox overlaps the player's CharacterController (hurtbox).
 *
 * Handles: HP, TakeHit, knockback (with hit-stop position freeze), hit stun,
 * hit animation trigger, and optional death (disable on zero HP).
 * Animator freeze for hit stop is applied by EnemyCombat on the target.
 */

using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [Tooltip("Starting/maximum health points.")]
    public int maxHp = 100;

    [Header("Hit Reaction")]
    [Tooltip("How quickly knockback velocity decays. Higher = stops faster.")]
    public float knockbackFriction = 12f;

    [Header("Hit Animation")]
    [Tooltip("Animator for hit reaction. Auto-finds on this object or children if not set.")]
    public Animator animator;
    [Tooltip("Trigger parameter name to play hit/hurt animation.")]
    public string hitTriggerName = "Hit";

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private int hp;
    private Vector3 kbVel;
    private float stunUntil;
    private float airborneUntil;
    private float hitStopEndTime;
    private CharacterController cc;
    private bool isDead;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        hp = maxHp;
        cc = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (isDead) return;
        ApplyKnockback();
    }

    // ========================================================================
    // KNOCKBACK PHYSICS
    // ========================================================================

    void ApplyKnockback()
    {
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime)
            return;
        if (hitStopEndTime > 0f && Time.time >= hitStopEndTime)
            hitStopEndTime = 0f;

        if (kbVel.sqrMagnitude > 0.0001f)
        {
            Vector3 movement = kbVel * Time.deltaTime;
            if (cc != null)
                cc.Move(movement);
            else
                transform.position += movement;
            kbVel = Vector3.Lerp(kbVel, Vector3.zero, 1f - Mathf.Exp(-knockbackFriction * Time.deltaTime));
        }
    }

    // ========================================================================
    // IDAMAGEABLE
    // ========================================================================

    public bool IsStunned => Time.time < stunUntil;
    public bool IsAirborne => Time.time < airborneUntil;
    public int CurrentHp => hp;
    public int MaxHp => maxHp;

    public void TakeHit(int damage, Vector3 knockback, float hitstun, float airborneDuration, float hitStopDuration = 0f)
    {
        if (isDead) return;

        hp -= damage;
        kbVel += knockback;
        if (hitStopDuration > 0f)
            hitStopEndTime = Time.time + hitStopDuration;

        stunUntil = Mathf.Max(stunUntil, Time.time + hitstun);

        if (animator != null && !string.IsNullOrEmpty(hitTriggerName))
            animator.SetTrigger(hitTriggerName);

        if (airborneDuration > 0f)
            airborneUntil = Mathf.Max(airborneUntil, Time.time + airborneDuration);

        if (hp <= 0)
        {
            isDead = true;
            // Disable input by disabling components; game-over flow can be added later
            var controller = GetComponent<PlayerController>();
            if (controller != null) controller.enabled = false;
            var combat = GetComponent<Combat>();
            if (combat != null) combat.enabled = false;
        }
    }
}
