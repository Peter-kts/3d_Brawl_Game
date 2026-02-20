using UnityEngine;

/// <summary>
/// Holds all attack data and combo timing for the player. Assign to Combat.comboSet
/// so the Inspector stays short; edit this asset via its custom editor (dropdown per move).
/// </summary>
[CreateAssetMenu(fileName = "ComboSet", menuName = "Combat/Combo Set", order = 0)]
public class ComboSet : ScriptableObject
{
    [Header("Forward Jab 1 (Hold Forward + Attack)")]
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

    [Header("Forward Jab 2 (Combo)")]
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

    [Header("Neutral Jab 1 (No Direction + Attack)")]
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

    [Header("Neutral Jab 2 (Combo)")]
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

    [Header("Combo Settings")]
    [Tooltip("How long after Jab 1 starts before the cancel window opens")]
    public float comboWindowDelay = 0.1f;

    [Tooltip("How long the cancel window stays open")]
    public float comboWindowDuration = 0.25f;
}
