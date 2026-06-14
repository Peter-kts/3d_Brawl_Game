using UnityEngine;

/// <summary>
/// Single source of truth for all enemy Animator parameter names, state names, and layer indices.
/// Create one asset (Assets > Create > Enemy > Animation Config) and assign it to every enemy that
/// shares the same Animator Controller. Changing a name here updates all enemies at once.
///
/// Timing and duration values (hitstun length, getUpDuration, etc.) live on SimpleEnemyAI because
/// they are per-enemy tuning. Airborne clip timing (liftoffEnd, loopStart, etc.) lives in
/// AirborneAnimationSettings on SimpleEnemyAI because it is specific to the animation clip.
/// </summary>
[CreateAssetMenu(fileName = "EnemyAnimationConfig", menuName = "Enemy/Animation Config")]
public class EnemyAnimationConfig : ScriptableObject
{
    [Header("Animator Parameters")]
    [Tooltip("Float parameter name for locomotion blend (0 = idle, 1 = run). Used in Base Layer blend tree.")]
    public string speedParameter = "Speed";

    [Tooltip("Float parameter name for scaling hit/get-up animation speed. Set to baseDuration/hitstun so clip fits the stun window.")]
    public string hitSpeedParameter = "HitSpeed";

    [Tooltip("Bool parameter name that keeps the Animator in the airborne state during Liftoff/Loop/Crash/Prone phases.")]
    public string airborneParameter = "IsAirborne";

    [Tooltip("Bool parameter name that activates the Stun animator layer (hitstun, prone, get-up). Must match the parameter name in the Animator Controller exactly.")]
    public string stunLayerParameter = "StunLayerActive";

    [Tooltip("Trigger parameter name that starts the death animation.")]
    public string deathTriggerParameter = "Death";

    [Tooltip("Animator state name played instead of the death trigger when the killing hit has high knockback (>= EnemyStunMeter.knockbackStunThreshold). Leave empty to always use deathTriggerParameter.")]
    public string knockbackDeathStateName = "";

    [Tooltip("Animator layer index for the knockback death state.")]
    public int knockbackDeathLayer = 1;


    [Header("Hit Reaction")]
    [Tooltip("Animator layer index for hit reaction, prone, get-up, and thrown states (e.g. 1 = Stun layer).")]
    public int hitAnimationLayer = 1;

    [Tooltip("Default hit reaction state name. Used when hitStateNames is empty and no height-specific state (Hit_High/Mid/Low) exists.")]
    public string hitStateName = "Stunned";

    [Tooltip("Optional pool of hit reaction state names. One is chosen at random (never the same twice in a row). Leave empty to use hitStateName only.")]
    public string[] hitStateNames;

    [Header("Throw (as victim)")]
    [Tooltip("Animator state name played on this enemy when thrown by the player.")]
    public string thrownStateName = "Thrown";

    [Header("Prone (ground knockdown)")]
    [Tooltip("Animator state name for the default prone (lying on ground) loop. Used for crash-landing prone and as fallback for other prone entries.")]
    public string proneStateName = "Prone";
    [Tooltip("Optional prone state for FaceDown variant. If empty, proneStateName is used as fallback.")]
    public string proneFaceDownStateName = "";

    [Tooltip("Animator layer index for the prone state.")]
    public int proneLayer = 1;

    [Tooltip("Animator state name played when the enemy is hit while already prone. Leave empty to use the normal hit reaction instead.")]
    public string proneHitStateName = "Prone_Hit";

    [Header("Standing Stun (stun meter filled)")]
    [Tooltip("Bool parameter name set true while the enemy is in standing stun. Must match the Animator Controller exactly.")]
    public string standingStunParameter = "IsStandingStunned";

    [Tooltip("Bool parameter name set true when the triggering hit had high knockback. Use alongside IsStandingStunned in the Animator Controller to route to KnockbackStunEntry/Loop states.")]
    public string knockbackStunParameter = "IsKnockbackStun";

    [Tooltip("Animator state name played directly when the enemy is hit with a heavy knockback move while already in standing stun. Leave empty to rely solely on the Animator Controller bool transitions.")]
    public string knockbackStunEntryStateName = "KnockbackStunEntry";
    [Tooltip("Seconds knockback-stun entry keeps visual priority before normal hit reactions may take over.")]
    public float knockbackStunEntryPriorityGrace = 0.1f;

    [Tooltip("Animator layer index used for both the entry and loop states (e.g. 1 = Stun layer).")]
    public int standingStunLayer = 1;

    [Header("Wall Bounce")]
    [Tooltip("Animator trigger parameter name set when the enemy hits a wall at high knockback velocity. Leave empty to skip the animation.")]
    public string wallBounceTriggerParameter = "WallBounce";
    [Tooltip("Animator bool parameter that stays true while wall-bounce has priority. Use this to gate stun transitions (e.g. require WallBounceActive == false).")]
    public string wallBounceActiveParameter = "WallBounceActive";

    [Header("Get Up (after prone)")]
    [Tooltip("Animator state name for the get-up animation. Played after groundedDuration expires.")]
    public string getUpStateName = "GetUp";

    [Tooltip("Animator layer index for the get-up state.")]
    public int getUpLayer = 1;
}
