using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    [Tooltip("Duration of the throw phase (enemy stun and both animations).")]
    public float throwPhaseDuration;

    [Header("Phase 2 - Start Up")]
    [Tooltip("Normalized (0-1) portion of the throw animation played as start-up. 0 = no start-up slowdown.")]
    [Range(0f, 1f)]
    public float throwStartUpLength;
    [Tooltip("Playback speed during start-up (e.g. 0.5 = half speed).")]
    [Range(0.01f, 1f)]
    public float throwStartUpSpeed;

    [Header("Phase 2 - Recovery")]
    [Tooltip("Normalized (0-1) portion at the end of the throw animation played as recovery. 0 = no recovery slowdown.")]
    [Range(0f, 1f)]
    public float throwRecoveryLength;
    [Tooltip("Playback speed during recovery (e.g. 0.5 = half speed).")]
    [Range(0.01f, 1f)]
    public float throwRecoverySpeed;

    [Header("Grab Connect")]
    [Tooltip("Name of a Transform in the player's hierarchy to use as the grab socket for this throw. Leave empty to use the default grabSocket on Combat.")]
    public string grabSocketName;
    [Tooltip("Snap the player to this distance from the victim on grab connect (ensures consistent throw alignment). 0 = no position snap, keep current distance.")]
    public float grabSnapDistance;
    [Tooltip("Hit stop duration when grab connects (freeze player + victim animators).")]
    public float grabHitStopDuration;
    [Tooltip("VFX spawned at hitbox center when grab connects. Optional.")]
    public GameObject grabConnectVfxPrefab;
    [Tooltip("VFX spawned when throw ends (when damage/knockback applied). Optional.")]
    public GameObject throwEndVfxPrefab;

    [Header("SFX (optional)")]
    [Tooltip("Flexible throw SFX cues (start, grab connect, and animation-event keyed).")]
    public List<AttackSfxCue> sfxCues;

    [Header("Hitbox")]
    public float range;
    public float hitboxRadius;
    public Vector3 hitboxOffset;

    [Header("End of Throw")]
    [Tooltip("Damage applied to victim when throw ends.")]
    public int endDamage;
    [Tooltip("Knockback force applied when throw ends (direction = away from player).")]
    public float endKnockback;
    [Tooltip("Invert knockback direction so the victim is knocked toward the player instead of away.")]
    public bool invertKnockbackDirection;
    [Tooltip("Vertical component of knockback at throw end.")]
    public float endKnockbackUp;
    [Tooltip("Send the enemy airborne on throw release (liftoff/loop/crash sequence).")]
    public bool launchOnRelease;
    [Tooltip("Make the enemy enter the airborne state on release (uses endAirborneDuration). Works independently of launchOnRelease.")]
    public bool airborneOnRelease;
    [Tooltip("Airborne duration (seconds) when launchOnRelease or airborneOnRelease is enabled.")]
    public float endAirborneDuration;
    [Tooltip("Override how long the enemy lies prone after landing from this throw (seconds). 0 = use the enemy's default groundedDuration.")]
    public float proneDuration;
    [Tooltip("Default prone variant for this throw. Release profile can override this.")]
    public ProneVariant proneVariant;
    [Tooltip("If enabled, rotate victim prone facing by 180 degrees for this throw type.")]
    public bool invertProneRotation;
    [Header("Release Facing")]
    [FormerlySerializedAs("faceVictimTowardThrowDirection")]
    [Tooltip("Throw release facing: ON = victim faces player, OFF = victim faces away from player.")]
    public bool faceVictimTowardPlayerOnRelease;

    [Header("Enemy")]
    [Tooltip("Animator state name for the enemy (throw receiver) while locked for the throw duration.")]
    public string enemyThrownStateName;

    [Header("Charge (optional)")]
    [Tooltip("Enable throw charge. When true, holding the throw button after grab connects slows the animation and scales knockback on release.")]
    public bool enableCharge;
    [Tooltip("Max seconds the player can hold before auto-release fires at full charge.")]
    public float maxChargeTime;
    [Tooltip("Knockback multiplier at full charge. Lerps from 1x (tap) to this value (full hold).")]
    public float chargeKnockbackMultiplier;
    [Tooltip("Damage multiplier at full charge. Set to 1 to leave damage unchanged.")]
    public float chargeDamageMultiplier;
    [Tooltip("Hit-stop duration multiplier at full charge. Lerps from 1x (tap) to this value (full hold).")]
    public float chargeHitStopMultiplier;
    [Tooltip("Animator speed while the player is holding the charge. Lower = more dramatic freeze.")]
    public float chargeAnimatorSpeed;

    [Header("Directional Throw (optional — requires Charge enabled)")]
    [Tooltip(
        "If enabled, the player can steer the throw direction during the charge window by holding a movement direction. " +
        "Both the thrower and the grabbed enemy rotate together toward the held direction. " +
        "At release the throw launches in whatever direction they ended up facing."
    )]
    public bool enableDirectionalThrow;
    [Tooltip("Rotation speed (degrees per second) used to turn thrower + victim toward the held direction during charge. 360 = full turn in one second.")]
    public float directionalThrowRotationSpeed;

    [Header("Suck To Target (optional)")]
    [Tooltip("If enabled, the player steps toward the soft target (or nearest threat) during the grab attempt window.")]
    public bool suckToTarget;
    [Tooltip("Total distance (units) covered toward the target over the full grab attempt window (hitboxDelay). Mirrors attack lungeDistance.")]
    public float throwSuckDistance;
    [Tooltip("Stop when within this distance of the target — prevents overlapping them.")]
    public float throwSuckStopDistance;

    [Header("Battle Momentum (optional)")]
    [Tooltip("If true, this throw costs momentum to use. If the player lacks enough, the throw is blocked.")]
    public bool consumesMomentum;
    [Tooltip("Amount of momentum consumed (and required) when this throw fires.")]
    [Min(0f)]
    public float momentumCost;

    [Header("Cooldown")]
    [Tooltip("Seconds before another throw can be started.")]
    public float throwCooldown;
}
