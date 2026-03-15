/*
 * ============================================================================
 * PLAYERHEALTH.CS - Player HP, damage, knockback, hit stun, and hit reaction
 * ============================================================================
 *
 * Inherits from EntityHealth for shared knockback physics, stun/airborne
 * timers, IDamageable properties, and the random-SFX helper.
 *
 * This class adds what is unique to the player:
 *   - Blocking (reduces damage/knockback/hitstun)
 *   - Hit/block animation system (multiple random states, base-layer cancel)
 *   - Airborne animation phases (Liftoff / Loop / Crash)
 *   - Gamepad rumble on hit-stop
 *   - Combat interrupt on damage
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

public class PlayerHealth : EntityHealth
{
    [Header("Health")]
    [Tooltip("Starting/maximum health points.")]
    public int maxHp = 100;
    public override int MaxHp => maxHp;

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
    [Tooltip("Base-layer fallback state used to force-exit attack animations on damage interrupt if hit state doesn't exist on Base Layer.")]
    public string interruptFallbackBaseStateName = "Idle";

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

    private PlayerController playerController;  // Cached for IsBlocking check and disabling on death
    private Combat combat;                      // Cached for IsInChargeFlow/IsAttacking checks and CancelBufferedAttackInputs() on damage
    private bool isDead;                        // True once HP reaches 0; blocks further TakeHit() calls

    // Airborne animation: one clip split into Liftoff (0→liftoffEnd), Loop (loopStart→loopEnd), Crash (crashStart→crashEnd)
    private enum AirbornePhase { None, Liftoff, Loop, Crash }
    private AirbornePhase airbornePhase = AirbornePhase.None;  // Which section of the airborne clip is currently playing
    private bool wasAirborne;              // Previous frame airborne state — used to detect the rising edge (became airborne) and falling edge (landed)
    private int lastHitStateIndex  = -1;   // Index of the last randomly played hit state — prevents the same state playing twice in a row
    private int lastHurtSfxIndex   = -1;   // Index of the last hurt clip played — prevents repeat
    private int lastDeathSfxIndex  = -1;   // Index of the last death clip played — prevents repeat
    private int lastBlockSfxIndex  = -1;   // Index of the last block clip played — prevents repeat

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        hp = maxHp;
        cc = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
        combat = GetComponent<Combat>();
        if (animator == null) animator = PlayerController.FindAnimator(gameObject);
        if (hurtSfxSource == null) hurtSfxSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>();
        deathSfxSource = ResolveSfxSource(deathSfxSource);
        blockSfxSource = ResolveSfxSource(blockSfxSource);
    }

    void Update()
    {
        if (isDead) return;
        ApplyKnockback(knockbackFriction);
        UpdateAirborneAnimation();
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// <summary>Returns the assigned source if set, otherwise falls back to hurtSfxSource, then any AudioSource on this object.</summary>
    private AudioSource ResolveSfxSource(AudioSource assigned) =>
        assigned != null ? assigned :
        hurtSfxSource != null ? hurtSfxSource :
        GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>();

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
    // HIT ANIMATION (state + layer, speed scaled to hitstun — same as enemy)
    // ========================================================================

    /// <summary>
    /// Checks whether an animator state exists on a given layer.
    /// Unity accepts both short names ("Hit") and full paths ("Base Layer.Hit"), so we try both.
    /// Used before playing states to avoid errors when a state is missing from a layer.
    /// </summary>
    bool AnimatorHasStateOnLayer(int layerIndex, string stateName)
    {
        if (animator == null || string.IsNullOrEmpty(stateName)) return false;
        if (layerIndex < 0 || layerIndex >= animator.layerCount) return false;

        int shortNameHash = Animator.StringToHash(stateName);
        if (animator.HasState(layerIndex, shortNameHash)) return true;

        string fullPath = animator.GetLayerName(layerIndex) + "." + stateName;
        int fullPathHash = Animator.StringToHash(fullPath);
        return animator.HasState(layerIndex, fullPathHash);
    }

    void TriggerHitAnimation(float hitstun, bool forceBaseLayerCancel = false)
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
        bool baseLayerOverridden = false;
        // When a charge attack is interrupted, also drive Base Layer if possible
        // so the attack animation cannot continue into release frames.
        if (forceBaseLayerCancel && hitAnimationLayer != 0)
        {
            if (AnimatorHasStateOnLayer(0, stateToPlay))
            {
                animator.Play(stateToPlay, 0, 0f);
                baseLayerOverridden = true;
            }
        }

        // Hard fallback: if the hit state is not present on Base Layer, force a safe locomotion/idle state
        // to avoid staying forever in an attack clip when exit-time transitions are disabled.
        if (forceBaseLayerCancel && !baseLayerOverridden)
        {
            string fallback = string.IsNullOrEmpty(interruptFallbackBaseStateName) ? "Idle" : interruptFallbackBaseStateName;
            if (AnimatorHasStateOnLayer(0, fallback))
                animator.Play(fallback, 0, 0f);
        }
        animator.Update(0f);
    }

    void TriggerBlockHitAnimation()
    {
        if (animator == null) return;
        if (string.IsNullOrEmpty(blockHitStateName)) return;
        animator.Play(blockHitStateName, blockHitAnimationLayer, 0f);
        animator.Update(0f);
    }

    void PlayHurtSfx()  => PlayRandomSfx(hurtSfxSource,  hurtSfxClips,  ref lastHurtSfxIndex,  hurtSfxPitchMin,  hurtSfxPitchMax,  hurtSfxVolume);
    void PlayDeathSfx() => PlayRandomSfx(deathSfxSource, deathSfxClips, ref lastDeathSfxIndex, deathSfxPitchMin, deathSfxPitchMax, deathSfxVolume);
    void PlayBlockSfx() => PlayRandomSfx(blockSfxSource, blockSfxClips, ref lastBlockSfxIndex, blockSfxPitchMin, blockSfxPitchMax, blockSfxVolume);

    // ========================================================================
    // IDAMAGEABLE (override)
    // ========================================================================

    public override void TakeHit(
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
            // Clear any armed attack input so held buttons don't auto-fire post-hit.
            if (!isBlocking && combat != null)
                combat.CancelBufferedAttackInputs();
            PlayHurtSfx();
        }
        // Treat the entire charge flow (window + active hold) as interruptible on real damage.
        bool forceAttackInterruptOnDamage = damage > 0 && !isBlocking && combat != null && (combat.IsAttacking || combat.IsInChargeFlow);
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
        // If damage lands during any active attack flow, ensure a tiny stun so interrupt always wins this frame.
        float effectiveHitstun = forceAttackInterruptOnDamage ? Mathf.Max(hitstun, 0.1f) : hitstun;
        stunUntil = Mathf.Max(stunUntil, Time.time + effectiveHitstun);
        if (airborneDuration > 0f)
            pendingLaunchApplyTime = (hitStopDuration > 0f) ? (Time.time + hitStopDuration) : Time.time;

        if (animator != null)
        {
            if (isBlocking) TriggerBlockHitAnimation();
            else TriggerHitAnimation(effectiveHitstun, forceAttackInterruptOnDamage);
        }

        if (forceAttackInterruptOnDamage)
            combat.InterruptAttackAndChargeForStun();

        if (hp <= 0)
        {
            isDead = true;
            PlayDeathSfx();
            // Disable input by disabling components; game-over flow can be added later
            if (playerController != null) playerController.enabled = false;
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
