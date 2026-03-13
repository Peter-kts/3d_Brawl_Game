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

    [Header("Block Reaction")]
    [Tooltip("Animator state name to play when a hit is blocked. Uses existing hit state by default.")]
    public string blockHitStateName = "Hit";
    [Tooltip("Animator layer index for blocked hit reaction.")]
    public int blockHitAnimationLayer = 0;
    [Tooltip("Multiplier applied to knockback, hitstun, and hitstop on blocked hits.")]
    [Range(0f, 1f)]
    public float blockedHitEffectMultiplier = 0.5f;

    [Header("Hurt SFX (optional)")]
    [Tooltip("Audio source used for hurt sounds. Auto-finds on this object/children if not assigned.")]
    public AudioSource hurtSfxSource;
    [Tooltip("Hurt voice clips. If multiple are assigned, one is chosen at random (never the same twice in a row).")]
    public AudioClip[] hurtSfxClips;
    [Tooltip("Lowest random pitch used for hurt SFX.")]
    [Range(0.5f, 1.5f)]
    public float hurtSfxPitchMin = 0.96f;
    [Tooltip("Highest random pitch used for hurt SFX.")]
    [Range(0.5f, 1.5f)]
    public float hurtSfxPitchMax = 1.04f;
    [Tooltip("Volume scale for hurt SFX.")]
    [Range(0f, 1f)]
    public float hurtSfxVolume = 1f;

    [Header("Death SFX (optional)")]
    [Tooltip("Audio source used for death sounds. Auto-finds on this object/children if not assigned.")]
    public AudioSource deathSfxSource;
    [Tooltip("Death voice clips. If multiple are assigned, one is chosen at random (never the same twice in a row).")]
    public AudioClip[] deathSfxClips;
    [Tooltip("Lowest random pitch used for death SFX.")]
    [Range(0.5f, 1.5f)]
    public float deathSfxPitchMin = 0.96f;
    [Tooltip("Highest random pitch used for death SFX.")]
    [Range(0.5f, 1.5f)]
    public float deathSfxPitchMax = 1.04f;
    [Tooltip("Volume scale for death SFX.")]
    [Range(0f, 1f)]
    public float deathSfxVolume = 1f;

    [Header("Block SFX (optional)")]
    [Tooltip("Audio source used for block sounds. Auto-finds on this object/children if not assigned.")]
    public AudioSource blockSfxSource;
    [Tooltip("Block clips played when a hit is blocked. If multiple are assigned, one is chosen at random (never the same twice in a row).")]
    public AudioClip[] blockSfxClips;
    [Tooltip("Lowest random pitch used for block SFX.")]
    [Range(0.5f, 1.5f)]
    public float blockSfxPitchMin = 0.96f;
    [Tooltip("Highest random pitch used for block SFX.")]
    [Range(0.5f, 1.5f)]
    public float blockSfxPitchMax = 1.04f;
    [Tooltip("Volume scale for block SFX.")]
    [Range(0f, 1f)]
    public float blockSfxVolume = 1f;
    
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
    private PlayerController playerController;
    private bool isDead;
    
    // Airborne animation: one clip split into Liftoff (0→liftoffEnd), Loop (loopStart→loopEnd), Crash (crashStart→crashEnd)
    private enum AirbornePhase { None, Liftoff, Loop, Crash }
    private AirbornePhase airbornePhase = AirbornePhase.None;
    private bool wasAirborne;              // Previous frame airborne state (for rising/falling edge)
    private int lastHitStateIndex = -1;     // So we don't play the same random hit state twice in a row
    private int lastHurtSfxIndex = -1;      // So we don't play the same hurt clip twice in a row
    private int lastDeathSfxIndex = -1;     // So we don't play the same death clip twice in a row
    private int lastBlockSfxIndex = -1;     // So we don't play the same block clip twice in a row

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        hp = maxHp;
        cc = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
        if (animator == null) animator = PlayerController.FindAnimator(gameObject);
        if (hurtSfxSource == null) hurtSfxSource = GetComponent<AudioSource>();
        if (hurtSfxSource == null) hurtSfxSource = GetComponentInChildren<AudioSource>();
        if (deathSfxSource == null) deathSfxSource = hurtSfxSource;
        if (deathSfxSource == null) deathSfxSource = GetComponent<AudioSource>();
        if (deathSfxSource == null) deathSfxSource = GetComponentInChildren<AudioSource>();
        if (blockSfxSource == null) blockSfxSource = hurtSfxSource;
        if (blockSfxSource == null) blockSfxSource = GetComponent<AudioSource>();
        if (blockSfxSource == null) blockSfxSource = GetComponentInChildren<AudioSource>();
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

    /// <summary>
    /// Play blocked-hit reaction animation.
    /// </summary>
    void TriggerBlockHitAnimation()
    {
        if (animator == null) return;
        if (string.IsNullOrEmpty(blockHitStateName)) return;
        animator.Play(blockHitStateName, blockHitAnimationLayer, 0f);
        animator.Update(0f);
    }

    void PlayHurtSfx()
    {
        if (hurtSfxSource == null || hurtSfxClips == null || hurtSfxClips.Length == 0) return;

        int chosenIndex = 0;
        if (hurtSfxClips.Length >= 2)
        {
            do { chosenIndex = Random.Range(0, hurtSfxClips.Length); }
            while (chosenIndex == lastHurtSfxIndex);
        }
        lastHurtSfxIndex = chosenIndex;

        AudioClip clip = hurtSfxClips[chosenIndex];
        if (clip == null) return;

        float minPitch = Mathf.Min(hurtSfxPitchMin, hurtSfxPitchMax);
        float maxPitch = Mathf.Max(hurtSfxPitchMin, hurtSfxPitchMax);
        hurtSfxSource.pitch = Random.Range(minPitch, maxPitch);
        hurtSfxSource.PlayOneShot(clip, Mathf.Max(0f, hurtSfxVolume));
    }

    void PlayDeathSfx()
    {
        if (deathSfxSource == null || deathSfxClips == null || deathSfxClips.Length == 0) return;

        int chosenIndex = 0;
        if (deathSfxClips.Length >= 2)
        {
            do { chosenIndex = Random.Range(0, deathSfxClips.Length); }
            while (chosenIndex == lastDeathSfxIndex);
        }
        lastDeathSfxIndex = chosenIndex;

        AudioClip clip = deathSfxClips[chosenIndex];
        if (clip == null) return;

        float minPitch = Mathf.Min(deathSfxPitchMin, deathSfxPitchMax);
        float maxPitch = Mathf.Max(deathSfxPitchMin, deathSfxPitchMax);
        deathSfxSource.pitch = Random.Range(minPitch, maxPitch);
        deathSfxSource.PlayOneShot(clip, Mathf.Max(0f, deathSfxVolume));
    }

    void PlayBlockSfx()
    {
        if (blockSfxSource == null || blockSfxClips == null || blockSfxClips.Length == 0) return;

        int chosenIndex = 0;
        if (blockSfxClips.Length >= 2)
        {
            do { chosenIndex = Random.Range(0, blockSfxClips.Length); }
            while (chosenIndex == lastBlockSfxIndex);
        }
        lastBlockSfxIndex = chosenIndex;

        AudioClip clip = blockSfxClips[chosenIndex];
        if (clip == null) return;

        float minPitch = Mathf.Min(blockSfxPitchMin, blockSfxPitchMax);
        float maxPitch = Mathf.Max(blockSfxPitchMin, blockSfxPitchMax);
        blockSfxSource.pitch = Random.Range(minPitch, maxPitch);
        blockSfxSource.PlayOneShot(clip, Mathf.Max(0f, blockSfxVolume));
    }

    // ========================================================================
    // IDAMAGEABLE
    // ========================================================================

    public bool IsStunned => Time.time < stunUntil;
    public bool IsAirborne => Time.time < airborneUntil;
    public int CurrentHp => hp;
    public int MaxHp => maxHp;

    public void TakeHit(
        int damage,
        Vector3 knockback,
        float hitstun,
        float airborneDuration,
        float hitStopDuration = 0f,
        AttackHeaviness heaviness = AttackHeaviness.Medium,
        AttackHeight height = AttackHeight.Mid
    )
    {
        if (isDead) return;

        Combat combat = GetComponent<Combat>();
        bool isBlocking = playerController != null && playerController.IsBlocking;
        if (isBlocking)
        {
            damage = 0;
            float multiplier = Mathf.Clamp01(blockedHitEffectMultiplier);
            knockback *= multiplier;
            hitstun *= multiplier;
            hitStopDuration *= multiplier;
            PlayBlockSfx();
        }

        hp -= damage;
        ScreenShake.RequestShake();
        if (damage > 0)
        {
            // Safety reset: if Combat slowed animator speed (startup/recovery/charge),
            // restore baseline speed immediately when real damage is taken.
            if (animator != null)
                animator.speed = 1f;
            PlayHurtSfx();
        }
        bool forceChargeInterruptToStun = damage > 0 && !isBlocking && combat != null && combat.IsChargingAttack;
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

        // Don't shorten an existing longer stun; launcher: apply knockback when stun ends.
        // If damage landed during charge, ensure at least a tiny stun so interrupt always takes over.
        float effectiveHitstun = forceChargeInterruptToStun ? Mathf.Max(hitstun, 0.1f) : hitstun;
        stunUntil = Mathf.Max(stunUntil, Time.time + effectiveHitstun);
        if (airborneDuration > 0f)
            pendingLaunchApplyTime = (hitStopDuration > 0f) ? (Time.time + hitStopDuration) : Time.time;  // launch when hit stop ends

        if (animator != null)
        {
            if (isBlocking) TriggerBlockHitAnimation();
            else TriggerHitAnimation(effectiveHitstun);
        }

        if (forceChargeInterruptToStun)
            combat.InterruptAttackAndChargeForStun();

        // Airborne for launch attacks is applied when hitstun ends (in ApplyKnockback)

        if (hp <= 0)
        {
            isDead = true;
            PlayDeathSfx();
            // Disable input by disabling components; game-over flow can be added later
            var controller = GetComponent<PlayerController>();
            if (controller != null) controller.enabled = false;
            var combatComponent = GetComponent<Combat>();
            if (combatComponent != null) combatComponent.enabled = false;
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
