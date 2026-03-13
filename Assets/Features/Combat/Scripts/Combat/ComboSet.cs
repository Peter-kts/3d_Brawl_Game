using UnityEngine;

/// <summary>
/// Holds all attack data and combo timing for the player. Assign to Combat.comboSet
/// so the Inspector stays short; edit this asset via its custom editor (dropdown per move).
/// </summary>
[CreateAssetMenu(fileName = "ComboSet", menuName = "Combat/Combo Set", order = 0)]
public class ComboSet : ScriptableObject
{
    [Header("Forward Jab 1 (Normal / Tap)")]
    public AttackData forwardJabNormal = new AttackData
    {
        range = 1.6f,
        damage = 8,
        hitboxRadius = 0.6f,
        lockDuration = 0.3f,
        cooldown = 0.15f,
        knockback = 3f,
        knockbackUp = 0f,
        hitstun = 0.12f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0.2f,
        lungeDistance = 0.35f,
        lungeDuration = 0.08f
    };

    [Header("Forward Jab 1 (Charged / Hold)")]
    public AttackData forwardJab = new AttackData
    {
        range = 1.6f,
        damage = 10,
        hitboxRadius = 0.6f,
        lockDuration = 0.35f,
        cooldown = 0.2f,
        knockback = 4f,
        knockbackUp = 0f,
        hitstun = 0.15f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0.2f,
        lungeDistance = 0.5f,
        lungeDuration = 0.1f
    };

    [Header("Forward Jab 2 (Normal / Tap)")]
    public AttackData forwardJab2Normal = new AttackData
    {
        range = 1.6f,
        damage = 10,
        hitboxRadius = 0.6f,
        lockDuration = 0.4f,
        cooldown = 0.3f,
        knockback = 4f,
        knockbackUp = 0f,
        hitstun = 0.16f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0.2f,
        lungeDistance = 0.35f,
        lungeDuration = 0.08f
    };

    [Header("Forward Jab 2 (Charged / Hold)")]
    public AttackData forwardJab2 = new AttackData
    {
        range = 1.6f,
        damage = 12,
        hitboxRadius = 0.6f,
        lockDuration = 0.5f,
        cooldown = 0.4f,
        knockback = 6f,
        knockbackUp = 0f,
        hitstun = 0.2f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0.2f,
        lungeDistance = 0.5f,
        lungeDuration = 0.1f
    };

    [Header("Neutral Jab 1 (Normal / Tap)")]
    public AttackData neutralJabNormal = new AttackData
    {
        range = 1.6f,
        damage = 7,
        hitboxRadius = 0.6f,
        lockDuration = 0.28f,
        cooldown = 0.12f,
        knockback = 2.5f,
        knockbackUp = 0f,
        hitstun = 0.1f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0f,
        lungeDistance = 0f,
        lungeDuration = 0f
    };

    [Header("Neutral Jab 1 (Charged / Hold)")]
    public AttackData neutralJab = new AttackData
    {
        range = 1.6f,
        damage = 8,
        hitboxRadius = 0.6f,
        lockDuration = 0.3f,
        cooldown = 0.15f,
        knockback = 3f,
        knockbackUp = 0f,
        hitstun = 0.12f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0f,
        lungeDistance = 0f,
        lungeDuration = 0f
    };

    [Header("Neutral Jab 2 (Normal / Tap)")]
    public AttackData neutralJab2Normal = new AttackData
    {
        range = 1.6f,
        damage = 9,
        hitboxRadius = 0.6f,
        lockDuration = 0.36f,
        cooldown = 0.24f,
        knockback = 4f,
        knockbackUp = 0f,
        hitstun = 0.14f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0f,
        lungeDistance = 0f,
        lungeDuration = 0f
    };

    [Header("Neutral Jab 2 (Charged / Hold)")]
    public AttackData neutralJab2 = new AttackData
    {
        range = 1.6f,
        damage = 10,
        hitboxRadius = 0.6f,
        lockDuration = 0.4f,
        cooldown = 0.3f,
        knockback = 5f,
        knockbackUp = 0f,
        hitstun = 0.18f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "Punch",
        lungeFrame = 0f,
        lungeDistance = 0f,
        lungeDuration = 0f
    };

    [Header("Heavy Attack")]
    public AttackData heavyAttack = new AttackData
    {
        range = 2.0f,
        damage = 22,
        hitboxRadius = 0.6f,
        lockDuration = 0.8f,
        cooldown = 0.75f,
        knockback = 10f,
        knockbackUp = 2f,
        hitstun = 0.4f,
        makesAirborne = true,
        airborneDuration = 0.6f,
        animationTrigger = "HeavyPunch",
        lungeFrame = 0.15f,
        lungeDistance = 0.8f,
        lungeDuration = 0.15f
    };

    [Header("RB + X Attack")]
    public AttackData rbXAttack = new AttackData
    {
        range = 1.9f,
        damage = 18,
        hitboxRadius = 0.6f,
        lockDuration = 0.65f,
        cooldown = 0.55f,
        knockback = 8f,
        knockbackUp = 1f,
        hitstun = 0.3f,
        makesAirborne = false,
        airborneDuration = 0f,
        animationTrigger = "HeavyPunch",
        lungeFrame = 0.15f,
        lungeDistance = 0.7f,
        lungeDuration = 0.12f
    };

    [Header("Combo Settings")]
    [Tooltip("How long after Jab 1 starts before the cancel window opens")]
    public float comboWindowDelay = 0.1f;

    [Tooltip("How long the cancel window stays open")]
    public float comboWindowDuration = 0.25f;

    [Header("Throw")]
    [Tooltip("Throw move config (attempted grab then synced throw on success). Expand to edit; uncheck Enable Throw to disable.")]
    public ThrowData throwData = new ThrowData
    {
        enableThrow = true,
        grabAttemptAnimationTrigger = "GrabAttempt",
        attemptLockDuration = 0.5f,
        hitboxDelay = 0.25f,
        throwAnimationTrigger = "Throw",
        backThrowAnimationTrigger = "",
        throwPhaseDuration = 1f,
        grabHitStopDuration = 0.08f,
        range = 1.4f,
        hitboxRadius = 0.6f,
        launchVictimOnRelease = true,
        endDamage = 15,
        endKnockback = 6f,
        endKnockbackUp = 0f,
        endHitstun = 0.2f,
        endAirborneDuration = 0.8f,
        faceVictimTowardThrowDirection = true,
        enemyThrownStateName = "Thrown",
        backEnemyThrownStateName = "",
        throwCooldown = 0.8f
    };
}
