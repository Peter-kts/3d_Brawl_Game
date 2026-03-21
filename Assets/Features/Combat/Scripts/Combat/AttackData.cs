using System.Collections.Generic;
using UnityEngine;

public enum AttackHeaviness
{
    Light = 0,
    Medium = 1,
    Heavy = 2
}

public enum AttackHeight
{
    Low = 0,
    Mid = 1,
    High = 2
}

public enum AttackSfxTriggerType
{
    OnAttackStart = 0,
    OnHitConfirm = 1,
    OnAnimEvent = 2
}

[System.Serializable]
public class AttackSfxCue
{
    [Tooltip("When this cue should be triggered.")]
    public AttackSfxTriggerType trigger = AttackSfxTriggerType.OnAttackStart;

    [Tooltip("Only used when trigger is OnAnimEvent.")]
    public int eventId = 0;

    [Tooltip("One or more clips for this cue. One will be picked randomly.")]
    public AudioClip[] clips;

    [Tooltip("Volume scale applied when this cue plays.")]
    [Range(0f, 2f)]
    public float volume = 1f;
}

/// <summary>
/// Holds all configurable properties for an attack.
/// Used by Combat.cs to define light, heavy, and combo attacks.
/// </summary>
[System.Serializable]
public class AttackData
{
    [Header("Damage")]
    [Tooltip("Damage dealt on hit")]
    public int damage = 10;

    [Header("Attack Type")]
    [Tooltip("Weight class of the move. Used for typed hit reactions.")]
    public AttackHeaviness heaviness = AttackHeaviness.Medium;

    [Tooltip("Target height of the move. Used for typed hit reactions.")]
    public AttackHeight height = AttackHeight.Mid;
    
    [Header("Timing")]
    [Tooltip("How long the player is locked in place (can't move or turn)")]
    public float lockDuration = 0.4f;
    
    [Tooltip("How long before you can attack again (can be shorter than lockDuration for combos)")]
    public float cooldown = 0.3f;
    
    [Header("Combo Window (optional)")]
    [Tooltip("If >= 0, delay after this move before the combo cancel window opens. If negative, ComboSet's generic combo window is used.")]
    public float comboWindowDelay = -1f;
    
    [Tooltip("If >= 0, how long the combo cancel window stays open for this move. If negative, ComboSet's generic value is used.")]
    public float comboWindowDuration = -1f;
    
    [Header("Knockback")]
    [Tooltip("Horizontal knockback force")]
    public float knockback = 4f;
    
    [Tooltip("Vertical knockback force")]
    public float knockbackUp = 0f;
    
    [Header("Hitstun & Airborne")]
    [Tooltip("How long the target is stunned")]
    public float hitstun = 0.15f;

    [Header("Stun Meter")]
    [Tooltip("Stun buildup added to the target's stun meter on hit (0–1 scale, e.g. 0.2 = 5 hits to fill).")]
    // TODO: replace with per-move ComboSet stun value when system is ready
    public float stunBuildup = 0.2f;
    
    [Tooltip("Whether this attack launches the target airborne")]
    public bool makesAirborne = false;
    
    [Tooltip("How long the target stays airborne")]
    public float airborneDuration = 0f;
    [Tooltip("Prone variant to request when this move causes a knockdown/prone sequence. Default falls back to regular prone.")]
    public ProneVariant proneVariant = ProneVariant.Default;
    
    [Header("Animation")]
    [Tooltip("Animation state name to play")]
    public string animationTrigger = "Punch";
    
    [Header("Start up")]
    [Tooltip("Normalized (0-1) portion of the animation played as start-up. 0 = no start-up slowdown.")]
    [Range(0f, 1f)]
    public float startUpLength = 0f;
    
    [Tooltip("Playback speed for the start-up portion (e.g. 0.5 = half speed). Rest of animation plays at 1.")]
    [Range(0.01f, 1f)]
    public float startUpSpeed = 1f;
    
    [Header("Recovery")]
    [Tooltip("Normalized (0-1) portion of the animation at the end played as recovery. E.g. 0.3 = last 30%, slowdown starts at 0.7. 0 = no recovery slowdown.")]
    [Range(0f, 1f)]
    public float recoveryLength = 0f;
    
    [Tooltip("Playback speed for the recovery portion (e.g. 0.5 = half speed). Middle of animation plays at 1. Ensure the attack Animator state does not exit early (e.g. Exit Time = 1) or recovery won't apply.")]
    [Range(0.01f, 1f)]
    public float recoverySpeed = 1f;
    
    [Header("Forward Lunge")]
    [Tooltip("The normalized time (0-1) at which to apply forward movement")]
    public float lungeFrame = 0.2f;
    
    [Tooltip("Distance to move forward during the lunge")]
    public float lungeDistance = 0.5f;
    
    [Tooltip("Duration of the lunge movement in seconds")]
    public float lungeDuration = 0.1f;

    [Tooltip("If true, the lunge stops once it reaches suckToTargetStopDistance from the target " +
             "instead of travelling the full lungeDistance. Useful for big lunges that would overshoot.")]
    public bool suckToTarget = false;

    [Tooltip("Minimum flat (XZ) distance to maintain from the target when suck-to-target is active.")]
    [Min(0f)]
    public float suckToTargetStopDistance = 0.6f;

    [Header("Hit Stop")]
    [Tooltip("Duration in seconds to freeze attacker + target animations on hit (0 = no hit stop)")]
    public float hitStopDuration = 0f;
    
    [Header("Tracking")]
    [Tooltip("For this many seconds after attack start, facing rotates toward the soft target so the hitbox can follow moving enemies. 0 = no tracking.")]
    public float trackingDuration = 0f;
    
    [Tooltip("Max rotation speed toward target in degrees per second (smooth follow, not snap). Used when trackingDuration > 0.")]
    public float trackingSpeed = 540f;

    [Header("SFX (optional)")]
    [Tooltip("Sound played once when this attack starts.")]
    public AudioClip attackStartSfx;
    [Tooltip("Sound played once when this attack successfully connects.")]
    public AudioClip hitConnectSfx;
    [Tooltip("Flexible per-move SFX cues (start, hit confirm, and animation-event keyed).")]
    public List<AttackSfxCue> sfxCues = new List<AttackSfxCue>();
    [Tooltip("Optional dedicated looping charge SFX for this move. If null, Combat-level charge loop settings are used.")]
    public AudioClip chargeLoopSfx;
    [Tooltip("Volume scale for this move's charge loop SFX.")]
    [Range(0f, 1f)]
    public float chargeLoopSfxVolume = 0.7f;
    [Tooltip("Pitch for this move's charge loop SFX.")]
    [Range(0.5f, 1.5f)]
    public float chargeLoopSfxPitch = 1f;

    [Header("VFX (optional)")]
    [Tooltip("Optional per-move attack-start VFX. If null, Combat.attackStartVfxPrefab is used.")]
    public GameObject attackStartVfxPrefab;
    [Tooltip("Optional per-move hit-connect VFX. If null, Combat.hitConnectVfxPrefab is used.")]
    public GameObject hitConnectVfxPrefab;

    [Header("VFX Offsets (optional)")]
    [Tooltip("World-space position offset for attack-start VFX (e.g. swing trail). Applied per move in ComboSet.")]
    public Vector3 attackStartVfxPositionOffset = Vector3.zero;
    [Tooltip("Euler angles (degrees) added to attack-start VFX rotation.")]
    public Vector3 attackStartVfxRotationOffset = Vector3.zero;
    [Tooltip("World-space position offset for hit-connect VFX.")]
    public Vector3 hitConnectVfxPositionOffset = Vector3.zero;
    [Tooltip("Euler angles (degrees) added to hit-connect VFX rotation.")]
    public Vector3 hitConnectVfxRotationOffset = Vector3.zero;

    [Header("Player Launch (optional)")]
    [Tooltip("If true, this attack launches the player into the air when committed.")]
    public bool launchPlayer = false;

    [InspectorName("Player Launch Up Force")]
    [Tooltip("Upward force applied at launch. ~4 = small hop, ~8 = full jump, ~12 = high leap. " +
             "Max height still scales with this value and gravity.")]
    [Min(0f)]
    public float playerLaunchUpSpeed = 6f;

    [Tooltip("Forward speed applied at launch (m/s). 0 = purely vertical. " +
             "Combined with upSpeed and forwardDuration to control total horizontal distance.")]
    [Min(0f)]
    public float playerLaunchForwardSpeed = 0f;

    [Tooltip("How long the forward launch force is applied (seconds). " +
             "Horizontal distance ≈ forwardSpeed × forwardDuration.")]
    [Min(0f)]
    public float playerLaunchForwardDuration = 0.2f;

    [Header("Battle Momentum (optional)")]
    [Tooltip("If true, this attack costs momentum to use. If the player lacks enough, the attack is blocked.")]
    public bool consumesMomentum = false;
    [Tooltip("Amount of momentum consumed (and required) when this attack fires.")]
    [Min(0f)]
    public float momentumCost = 25f;

    [Header("Spawn Object (optional)")]
    [Tooltip("Spawn a prefab during this attack. Enable, assign a prefab, then configure offset and timing below.")]
    public bool spawnObject = false;

    [Tooltip("The object to instantiate. Tag it 'Spawnable' in the Inspector to mark it as a valid spawn target.")]
    [SpawnableTag]
    public GameObject spawnPrefab;

    [Tooltip("Offset from the player's position at commit time. (0,0,0) = player center.\n" +
             "With Spawn In Local Space on: Z = forward, X = right, Y = up (relative to facing).\n" +
             "With it off: raw world-space offset.")]
    public Vector3 spawnOffset = Vector3.zero;

    [Tooltip("If true, the offset axes rotate with the player's facing direction (Z points where they face). " +
             "If false, the offset is applied in world space.")]
    public bool spawnInLocalSpace = true;

    [Tooltip("If true, the spawned object takes the player's rotation at commit time. " +
             "If false, it spawns facing world forward (identity rotation).")]
    public bool inheritPlayerRotation = true;

    [Tooltip("If true, the spawned object is parented to the player and moves with them. " +
             "If false, it is placed in the scene independently.")]
    public bool parentToPlayer = false;

}
