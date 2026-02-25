using UnityEngine;

/// <summary>
/// One release variant for multi-throw: damage, knockback, facing. Use via OnThrowRelease(profileIndex) from Animation Event.
/// </summary>
[System.Serializable]
public struct ThrowReleaseProfile
{
    [Tooltip("Rotate victim to face throw direction at release.")]
    public bool faceTowardThrowDirection;
    public int endDamage;
    public float endKnockback;
    public float endKnockbackUp;
    public float endHitstun;
    public float endAirborneDuration;
}

/// <summary>
/// Config for the synced throw move: attempted grab (phase 1) then throw on success (phase 2),
/// with hit stop and VFX on grab connect, damage/knockback at throw end.
/// Serializes inline on ComboSet so the throw appears in the moveset in the Inspector.
/// </summary>
[System.Serializable]
public struct ThrowData
{
    [Header("Throw")]
    [Tooltip("Enable the throw move. Uncheck to disable without losing values.")]
    public bool enableThrow;

    [Header("Phase 1 - Attempted Grab")]
    [Tooltip("Player animation state name for the grab attempt (plays first; whiff ends after attemptLockDuration).")]
    public string grabAttemptAnimationTrigger;
    [Tooltip("How long the attempt lock lasts if no target is grabbed (whiff).")]
    public float attemptLockDuration;
    [Tooltip("Delay in seconds before the grab hitbox is checked (sync to when hands reach out).")]
    public float hitboxDelay;

    [Header("Phase 2 - Throw (on success)")]
    [Tooltip("Player animation state name when grab connects (synced with enemy thrown anim).")]
    public string throwAnimationTrigger;
    [Tooltip("Player animation for back throw. If empty, use throwAnimationTrigger for both.")]
    public string backThrowAnimationTrigger;
    [Tooltip("Duration of the throw phase (enemy stun and both animations).")]
    public float throwPhaseDuration;

    [Header("Grab Connect")]
    [Tooltip("Hit stop duration when grab connects (freeze player + victim animators).")]
    public float grabHitStopDuration;
    [Tooltip("VFX spawned at hitbox center when grab connects. Optional.")]
    public GameObject grabConnectVfxPrefab;
    [Tooltip("VFX spawned when throw ends (when damage/knockback applied). Optional.")]
    public GameObject throwEndVfxPrefab;

    [Header("Hitbox")]
    public float range;
    public float hitboxRadius;
    public Vector3 hitboxOffset;

    [Header("End of Throw")]
    [Tooltip("If true, at release we apply damage + knockback + airborne (TakeHit). If false, only unparent and restore victim — let root motion animation play out entirely with no launch.")]
    public bool launchVictimOnRelease;
    [Tooltip("Damage applied to victim when throw ends (only if Launch Victim On Release is true).")]
    public int endDamage;
    [Tooltip("Knockback force applied when throw ends (direction = away from player).")]
    public float endKnockback;
    [Tooltip("Vertical component of knockback at throw end.")]
    public float endKnockbackUp;
    [Tooltip("Hitstun duration after throw end.")]
    public float endHitstun;
    [Tooltip("Airborne duration when launched at release (~0.8 = short arc). Enemy plays airborne/fall anim.")]
    public float endAirborneDuration;
    [Tooltip("If true, victim is rotated to face the throw direction at release. Turn off for throws where they stay facing you.")]
    public bool faceVictimTowardThrowDirection;

    [Header("Release profiles (optional)")]
    [Tooltip("Different release behaviors per throw type. In the throw Animation Event set Int to 0, 1, 2... to use the profile at that index. Leave empty to use the values above.")]
    public ThrowReleaseProfile[] releaseProfiles;

    [Header("Enemy")]
    [Tooltip("Animator state name for the enemy (throw receiver) while locked for the throw duration.")]
    public string enemyThrownStateName;
    [Tooltip("Enemy state for back throw. If empty, use enemyThrownStateName for both.")]
    public string backEnemyThrownStateName;

    [Header("Cooldown")]
    [Tooltip("Seconds before another throw can be started.")]
    public float throwCooldown;
}
