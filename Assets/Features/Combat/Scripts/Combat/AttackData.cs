using UnityEngine;

/// <summary>
/// Holds all configurable properties for an attack.
/// Used by Combat.cs to define light, heavy, and combo attacks.
/// </summary>
[System.Serializable]
public class AttackData
{
    [Header("Damage & Range")]
    [Tooltip("How far the hitbox reaches from the player")]
    public float range = 1.6f;
    
    [Tooltip("Damage dealt on hit")]
    public int damage = 10;
    
    [Tooltip("Radius of the attack hitbox")]
    public float hitboxRadius = 0.6f;
    
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
    
    [Tooltip("Whether this attack launches the target airborne")]
    public bool makesAirborne = false;
    
    [Tooltip("How long the target stays airborne")]
    public float airborneDuration = 0f;
    
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
    
    [Header("Hitbox Position")]
    [Tooltip("Local-space offset from origin. X = right, Y = up, Z = forward (added on top of range)")]
    public Vector3 hitboxOffset = Vector3.zero;
    
    [Header("Hitbox Timing")]
    [Tooltip("Delay in seconds before the hitbox activates (0 = instant, match to animation wind-up)")]
    public float hitboxDelay = 0f;
    
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

    [Header("VFX Offsets (optional)")]
    [Tooltip("World-space position offset for attack-start VFX (e.g. swing trail). Applied per move in ComboSet.")]
    public Vector3 attackStartVfxPositionOffset = Vector3.zero;
    [Tooltip("Euler angles (degrees) added to attack-start VFX rotation.")]
    public Vector3 attackStartVfxRotationOffset = Vector3.zero;
    [Tooltip("World-space position offset for hit-connect VFX.")]
    public Vector3 hitConnectVfxPositionOffset = Vector3.zero;
    [Tooltip("Euler angles (degrees) added to hit-connect VFX rotation.")]
    public Vector3 hitConnectVfxRotationOffset = Vector3.zero;
}
