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
 *
 * GAME CONTEXT:
 * - Same knockback/airborne/hitstun model as EnemyHealth so player and enemies
 *   feel consistent (same formulas for slide decay, launch delay, etc.).
 * - Hit animation speed is scaled so the hit clip length matches hitstun duration.
 * - Airborne animation is split into Liftoff (rising), Loop (in air), Crash (landing).
 */

using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

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
    [Tooltip("Animator state name for the hit reaction (used with animator.Play to match hitstun).")]
    public string hitStateName = "Hit";
    [Tooltip("Animator layer index for the hit reaction (0 = Base Layer, 1 = Stun layer, etc.).")]
    public int hitAnimationLayer = 0;
    [Tooltip("Animator parameter name for hit animation speed multiplier.")]
    public string hitSpeedParameter = "HitSpeed";
    [Tooltip("Base duration of the hit animation clip (seconds). Speed is scaled so animation matches hitstun.")]
    public float baseHitAnimDuration = 0.4f;
    [Tooltip("Optional: multiple hit state names. If set, one is chosen at random (never the same twice in a row). Leave empty to use hitStateName only.")]
    public string[] hitStateNames;
    
    [Header("Airborne Animation (Liftoff / Loop / Crash)")]
    [Tooltip("Settings for splitting a single airborne animation into liftoff, loop, and crash phases. Leave airborneStateName empty to disable.")]
    public AirborneAnimationSettings airborneAnimation = new AirborneAnimationSettings();

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private int hp;                        // Current health
    private Vector3 kbVel;                 // Knockback velocity (world space), decayed each frame
    private float stunUntil;               // Time.time when stun ends (can't act until then)
    private float airborneUntil;           // Time.time when airborne ends (launched by heavy/launcher)
    private float hitStopEndTime;          // Time.time when hit-stop ends (position frozen until then)
    private Vector3 pendingKnockback;      // For launchers: applied when hitstun ends so we "cut to midair"
    private float pendingAirborneDuration;
    private float pendingLaunchApplyTime;  // When hitstun ends, apply knockback/launch so we "cut to midair"
    private CharacterController cc;        // Used for collision-safe knockback (no going through walls)
    private bool isDead;
    
    // Airborne animation: one clip split into Liftoff (0→liftoffEnd), Loop (loopStart→loopEnd), Crash (crashStart→crashEnd)
    private enum AirbornePhase { None, Liftoff, Loop, Crash }
    private AirbornePhase airbornePhase = AirbornePhase.None;
    private bool wasAirborne;              // Previous frame airborne state (for rising/falling edge)
    private int lastHitStateIndex = -1;     // So we don't play the same random hit state twice in a row

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        hp = maxHp;
        cc = GetComponent<CharacterController>();
        if (animator == null) animator = PlayerController.FindAnimator(gameObject);
    }

    void Update()
    {
        if (isDead) return;
        ApplyKnockback();
        UpdateAirborneAnimation();
    }

    // ========================================================================
    // AIRBORNE ANIMATION (Liftoff → Loop → Crash)
    // ========================================================================
    
    /// <summary>
    /// Drives airborne animation: Liftoff (rising) → Loop (in air, can repeat) → Crash (landing).
    /// Uses normalizedTime (0..1 through the clip) to know when to switch phases.
    /// </summary>
    void UpdateAirborneAnimation()
    {
        if (animator == null) return;
        if (!airborneAnimation.IsConfigured) return;
        
        bool isAirborne = IsAirborne;
        
        // Rising edge: just became airborne → start Liftoff (play from liftoffStart in the clip)
        if (isAirborne && !wasAirborne)
        {
            airbornePhase = AirbornePhase.Liftoff;
            
            if (airborneAnimation.crossfadeDuration > 0f)
            {
                animator.CrossFadeInFixedTime(
                    airborneAnimation.airborneStateName,
                    airborneAnimation.crossfadeDuration,
                    airborneAnimation.airborneAnimationLayer,
                    airborneAnimation.liftoffStart);
            }
            else
            {
                animator.Play(
                    airborneAnimation.airborneStateName,
                    airborneAnimation.airborneAnimationLayer,
                    airborneAnimation.liftoffStart);
            }
        }
        
        // Falling edge: was airborne, now grounded → play Crash (landing) from crashStart
        if (!isAirborne && wasAirborne && airbornePhase != AirbornePhase.None)
        {
            airbornePhase = AirbornePhase.Crash;
            animator.Play(
                airborneAnimation.airborneStateName,
                airborneAnimation.airborneAnimationLayer,
                airborneAnimation.crashStart);
        }
        
        wasAirborne = isAirborne;
        
        // Per-frame: use normalizedTime (0 = start of state, 1 = one full cycle) to advance phases
        if (airbornePhase == AirbornePhase.None) return;
        
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(airborneAnimation.airborneAnimationLayer);
        float normalizedTime = stateInfo.normalizedTime; // 0..1 through current state (can go >1 if looping)
        
        switch (airbornePhase)
        {
            case AirbornePhase.Liftoff:
                if (normalizedTime >= airborneAnimation.liftoffEnd)
                {
                    airbornePhase = AirbornePhase.Loop;
                    animator.Play(airborneAnimation.airborneStateName, airborneAnimation.airborneAnimationLayer, airborneAnimation.loopStart);
                }
                break;
                
            case AirbornePhase.Loop:
                // When we pass loopEnd, jump back to loopStart so the "in air" part repeats (juggling)
                if (normalizedTime >= airborneAnimation.loopEnd)
                {
                    animator.Play(airborneAnimation.airborneStateName, airborneAnimation.airborneAnimationLayer, airborneAnimation.loopStart);
                }
                break;
                
            case AirbornePhase.Crash:
                if (normalizedTime >= airborneAnimation.crashEnd)
                {
                    airbornePhase = AirbornePhase.None;
                }
                break;
        }
    }

    // ========================================================================
    // KNOCKBACK PHYSICS
    // ========================================================================

    /// <summary>
    /// Apply knockback each frame: move by kbVel * deltaTime, then decay kbVel (exponential decay).
    /// During hit-stop we skip this so position is frozen; delayed launches apply when hitstun ends.
    /// </summary>
    void ApplyKnockback()
    {
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime)
            return;
        if (hitStopEndTime > 0f && Time.time >= hitStopEndTime)
            hitStopEndTime = 0f;

        // When hitstun ends, apply delayed launch so we "cut to midair" (launcher attacks)
        if (pendingLaunchApplyTime > 0f && Time.time >= pendingLaunchApplyTime)
        {
            kbVel += pendingKnockback;
            airborneUntil = Mathf.Max(airborneUntil, Time.time + pendingAirborneDuration);
            pendingLaunchApplyTime = 0f;
        }

        // Only move if knockback is non-trivial (sqrMagnitude < 0.0001 means ~0.01 units/sec)
        if (kbVel.sqrMagnitude > 0.0001f)
        {
            Vector3 movement = kbVel * Time.deltaTime; // distance = velocity * time
            if (cc != null)
                cc.Move(movement);
            else
                transform.position += movement;
            // Exponential decay: Lerp toward zero with factor (1 - e^(-friction*dt)). Framerate-independent slide feel.
            kbVel = Vector3.Lerp(kbVel, Vector3.zero, 1f - Mathf.Exp(-knockbackFriction * Time.deltaTime));
        }
    }

    // ========================================================================
    // HIT ANIMATION (state + layer, speed scaled to hitstun — same as enemy)
    // ========================================================================

    /// <summary>
    /// Play hit reaction animation.
    /// </summary>
    void TriggerHitAnimation(float hitstun)
    {
        if (animator == null) return;
        if (string.IsNullOrEmpty(hitStateName) && (hitStateNames == null || hitStateNames.Length == 0)) return;

        if (!string.IsNullOrEmpty(hitSpeedParameter))
            animator.SetFloat(hitSpeedParameter, 1f);

        string stateToPlay;
        if (hitStateNames != null && hitStateNames.Length > 0)
        {
            int chosenIndex;
            do { chosenIndex = Random.Range(0, hitStateNames.Length); }
            while (hitStateNames.Length >= 2 && chosenIndex == lastHitStateIndex);
            lastHitStateIndex = chosenIndex;
            stateToPlay = hitStateNames[chosenIndex];
        }
        else
        {
            lastHitStateIndex = -1;
            stateToPlay = hitStateName;
        }

        animator.Play(stateToPlay, hitAnimationLayer, 0f);
        animator.Update(0f);
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
        if (airborneDuration > 0f)
        {
            pendingKnockback = knockback;
            pendingAirborneDuration = airborneDuration;
        }
        else
            kbVel += knockback;
        if (hitStopDuration > 0f)
        {
            hitStopEndTime = Time.time + hitStopDuration;
            if (Gamepad.current != null)
                StartCoroutine(RumbleForSeconds(hitStopDuration));
        }

        // Don't shorten an existing longer stun; launcher: apply knockback when stun ends
        stunUntil = Mathf.Max(stunUntil, Time.time + hitstun);
        if (airborneDuration > 0f)
            pendingLaunchApplyTime = (hitStopDuration > 0f) ? (Time.time + hitStopDuration) : Time.time;  // launch when hit stop ends

        if (animator != null)
            TriggerHitAnimation(hitstun);

        // Airborne for launch attacks is applied when hitstun ends (in ApplyKnockback)

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
    
    /// <summary>Rumble gamepad for hit-stop duration (low = left motor 0.25, high = right motor 0.5). Uses realtime so pause doesn't affect it.</summary>
    IEnumerator RumbleForSeconds(float duration)
    {
        var gamepad = Gamepad.current;
        if (gamepad == null) yield break;
        gamepad.SetMotorSpeeds(0.25f, 0.5f);
        yield return new WaitForSecondsRealtime(duration);
        gamepad.SetMotorSpeeds(0f, 0f);
    }
}
