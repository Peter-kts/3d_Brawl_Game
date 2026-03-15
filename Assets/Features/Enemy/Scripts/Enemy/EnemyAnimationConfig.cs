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

    [Header("Prone (after crash landing)")]
    [Tooltip("Animator state name for the prone (lying on ground) looping animation. Played during groundedDuration after the crash animation finishes. Leave empty to freeze on crash pose.")]
    public string proneStateName = "Prone";

    [Tooltip("Animator layer index for the prone state.")]
    public int proneLayer = 1;

    [Tooltip("Animator state name played when the enemy is hit while already prone. Leave empty to use the normal hit reaction instead.")]
    public string proneHitStateName = "Prone_Hit";

    [Header("Get Up (after prone)")]
    [Tooltip("Animator state name for the get-up animation. Played after groundedDuration expires.")]
    public string getUpStateName = "GetUp";

    [Tooltip("Animator layer index for the get-up state.")]
    public int getUpLayer = 1;
}
