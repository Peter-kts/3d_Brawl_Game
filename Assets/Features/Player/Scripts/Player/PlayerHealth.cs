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
    [Tooltip("Seconds added to the active block window each time a hit is successfully blocked.")]
    [Min(0f)]
    public float blockWindowExtensionOnHit = 0.2f;

    [Header("Parry")]
    [Tooltip("Seconds after pressing block during which an incoming hit counts as a parry (zero damage, zero hitstun). " +
             "Hits that land while blocking but outside this window are treated as regular hits.")]
    [Min(0f)]
    public float parryWindowDuration = 0.2f;

    [Tooltip("Hitstun (seconds) applied to the attacker on a successful parry.")]
    [Min(0f)]
    public float parryCounterHitstun = 0.4f;

    [Tooltip("Stun meter buildup added to the attacker on a successful parry (0–1 scale). " +
             "0.5 = half the meter, which triggers standing stun if they're already at 50%+.")]
    [Range(0f, 1f)]
    public float parryCounterStunBuildup = 0.5f;

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

    [Header("Block / Parry VFX")]
    [Tooltip("Prefab spawned at the weapon tip whenever a hit is blocked. " +
             "Should have a ParticleSystem + AutoDestroy so it cleans itself up.")]
    public GameObject blockVfxPrefab;
    [Tooltip("Prefab spawned at the weapon tip on a successful parry. Falls back to blockVfxPrefab if not assigned.")]
    public GameObject parryVfxPrefab;

    [Header("Parry SFX (optional — plays only during the active block window)")]
    [Tooltip("Audio source used for parry sounds. Falls back to blockSfxSource if not assigned.")]
    public AudioSource parrySfxSource;
    [Tooltip("Parry clips played when a hit lands during the active block window. If empty, falls back to blockSfxClips.")]
    public AudioClip[] parrySfxClips;
    [Tooltip("Lowest random pitch used for parry SFX.")]
    [Range(0.5f, 1.5f)]
    public float parrySfxPitchMin = 0.9f;
    [Tooltip("Highest random pitch used for parry SFX.")]
    [Range(0.5f, 1.5f)]
    public float parrySfxPitchMax = 1.1f;
    [Tooltip("Volume scale for parry SFX.")]
    [Range(0f, 1f)]
    public float parrySfxVolume = 1f;
    [Tooltip("When a parry lands, instantly rotate the player to face the attacker (direction inferred from knockback).")]
    public bool faceAttackerOnParry = true;

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
    // AirbornePhase enum is defined in EntityHealth (shared base class) so it can be reused by any entity with airborne animation.
    private AirbornePhase airbornePhase = AirbornePhase.None;  // Which section of the airborne clip is currently playing
    private bool wasAirborne;              // Previous frame airborne state — used to detect the rising edge (became airborne) and falling edge (landed)
    private int lastHitStateIndex  = -1;   // Index of the last randomly played hit state — prevents the same state playing twice in a row
    private int lastHurtSfxIndex   = -1;   // Index of the last hurt clip played — prevents repeat
    private int lastDeathSfxIndex  = -1;   // Index of the last death clip played — prevents repeat
    private int lastBlockSfxIndex  = -1;   // Index of the last block clip played — prevents repeat
    private int lastParrySfxIndex  = -1;   // Index of the last parry clip played — prevents repeat

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
        // ResolveSfxSource (from EntityHealth) falls back to hurtSfxSource so secondary sources
        // share the same AudioSource when no dedicated one is wired up in the Inspector.
        deathSfxSource = ResolveSfxSource(deathSfxSource, hurtSfxSource);
        blockSfxSource = ResolveSfxSource(blockSfxSource, hurtSfxSource);
        parrySfxSource = ResolveSfxSource(parrySfxSource, hurtSfxSource);
    }

    void Update()
    {
        if (isDead) return;
        ApplyKnockback(knockbackFriction);
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
    void PlayParrySfx()
    {
        // Use dedicated parry clips if assigned; otherwise fall back to block clips.
        if (parrySfxClips != null && parrySfxClips.Length > 0)
            PlayRandomSfx(parrySfxSource, parrySfxClips, ref lastParrySfxIndex, parrySfxPitchMin, parrySfxPitchMax, parrySfxVolume);
        else
            PlayBlockSfx();
    }

    // ========================================================================
    // IDAMAGEABLE (override)
    // ========================================================================

    /// <summary>Fired when real (unblocked) damage is applied to the player. Subscribable by systems like BattleMomentum.</summary>
    public event System.Action<int> OnDamageTaken;

    public override void TakeHit(
        int damage,
        Vector3 knockback,
        float hitstun,
        float airborneDuration,
        float hitStopDuration = 0f,
        AttackHeaviness heaviness = AttackHeaviness.Medium,
        AttackHeight height = AttackHeight.Mid,
        GameObject attacker = null
    )
    {
        if (isDead) return;

        // Two-stage block check:
        //   isParry  — hit landed in the first parryWindowDuration seconds after pressing block
        //              → zero damage, zero hitstun, counter the attacker, parry VFX
        //   isBlock  — block window is active (animation event opened it) but past the parry window
        //              → damage/knockback/hitstun scaled by blockedHitEffectMultiplier, block VFX
        //   otherwise → full damage (block window expired or wasn't open yet)
        bool isParry = IsParryActive();
        bool isBlock = !isParry && playerController != null && playerController.IsBlockWindowActive;
        bool isBlocking = isParry || isBlock; // used below to suppress attack-interrupt and hurt SFX

        if (isParry) HandleParry(ref damage, ref knockback, ref hitstun, ref hitStopDuration, attacker);
        else if (isBlock) HandleBlock(ref damage, ref knockback, ref hitstun, ref hitStopDuration, attacker);

        hp -= damage;
        ScreenShake.RequestShake();

        // forceAttackInterrupt: real damage (not blocked) during an attack flow cancels the current attack.
        bool forceAttackInterrupt = ApplyDamageEffects(damage, isBlocking);

        ApplyHitPhysics(knockback, airborneDuration, hitStopDuration);

        // Don't shorten an existing longer stun; pad to at least 0.1s when interrupting an attack.
        float effectiveHitstun = forceAttackInterrupt ? Mathf.Max(hitstun, 0.1f) : hitstun;
        hitstunUntil = Mathf.Max(hitstunUntil, Time.time + effectiveHitstun);
        if (airborneDuration > 0f)
            pendingLaunchApplyTime = (hitStopDuration > 0f) ? (Time.time + hitStopDuration) : Time.time;

        TriggerHitReaction(isBlocking, effectiveHitstun, forceAttackInterrupt);

        if (forceAttackInterrupt)
            combat.InterruptAttackAndChargeForStun();

        if (hp <= 0)
            HandleDeath();
    }

    // -------------------------------------------------------------------------
    // TakeHit helpers — each covers one logical concern
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true if the block button is held AND the hit landed within parryWindowDuration
    /// seconds of first pressing block (the "perfect" window).
    /// If block is held but past this window, IsParryActive() is false — that path is a regular block.
    /// </summary>
    bool IsParryActive() =>
        playerController != null
        && playerController.IsBlocking
        && (Time.time - playerController.BlockPressTime) <= parryWindowDuration;

    /// <summary>
    /// Called when a hit lands inside the parry window.
    /// Zeroes out damage/hitstun, scales down knockback and hitstop, plays the parry SFX,
    /// optionally turns to face the attacker, and fires the counter-stun on the attacker.
    /// Parameters are ref so TakeHit sees the modified values immediately after this returns.
    /// </summary>
    /// <summary>
    /// Perfect parry: hit landed inside the parry window.
    /// Zeroes damage and hitstun entirely, scales knockback/hitstop by the block multiplier,
    /// spawns the parry VFX, plays parry SFX, optionally faces the attacker,
    /// and fires the counter-hit on the attacker.
    /// </summary>
    void HandleParry(ref int damage, ref Vector3 knockback, ref float hitstun, ref float hitStopDuration, GameObject attacker)
    {
        damage          = 0;
        hitstun         = 0;
        float mult      = Mathf.Clamp01(blockedHitEffectMultiplier);
        knockback      *= mult;
        hitStopDuration *= mult;
        PlayParrySfx();
        GameObject parryVfxToUse = parryVfxPrefab != null ? parryVfxPrefab : blockVfxPrefab;
        var parryVfx = SpawnHitVfx(parryVfxToUse, attacker);
        if (parryVfx != null) Destroy(parryVfx, 1f);

        // knockback points away from the attacker, so reverse it to get the facing direction.
        if (faceAttackerOnParry && knockback != Vector3.zero)
        {
            Vector3 toAttacker = new Vector3(-knockback.x, 0f, -knockback.z).normalized;
            if (toAttacker != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(toAttacker);
        }

        // Counter-hit: attacker plays the High animation, takes hitstun, and gains stun-meter buildup.
        // No damage — the parry is a reversal, not a free punch.
        if (attacker != null)
        {
            var attackerHealth = attacker.GetComponent<EnemyHealth>();
            var attackerStun   = attacker.GetComponent<EnemyStunMeter>();

            if (attackerHealth != null)
                attackerHealth.TakeHit(0, Vector3.zero, parryCounterHitstun, 0f, 0f,
                    AttackHeaviness.Light, AttackHeight.High);

            if (attackerStun != null)
                attackerStun.AddStun(parryCounterStunBuildup);
        }
    }

    /// <summary>
    /// Regular block: holding block but past the parry window.
    /// Scales damage, knockback, hitstun, and hitstop by blockedHitEffectMultiplier
    /// (e.g. 0.5 = half damage and knockback), plays block SFX, and spawns the block VFX.
    /// No counter-hit — the attacker is unaffected.
    /// </summary>
    void HandleBlock(ref int damage, ref Vector3 knockback, ref float hitstun, ref float hitStopDuration, GameObject attacker)
    {
        float mult      = Mathf.Clamp01(blockedHitEffectMultiplier);
        damage          = Mathf.RoundToInt(damage * mult);
        knockback      *= mult;
        hitstun        *= mult;
        hitStopDuration *= mult;
        PlayBlockSfx();
        SpawnHitVfx(blockVfxPrefab, attacker);
        if (playerController != null && blockWindowExtensionOnHit > 0f)
            playerController.ExtendBlockWindow(blockWindowExtensionOnHit);
    }

    /// <summary>
    /// Spawns a VFX prefab at the attacker's weapon tip (WeaponTipHitbox transform),
    /// falling back to the midpoint between player and attacker if no hitbox is found.
    /// Rotated so its forward axis faces away from the attacker (outward clash direction) —
    /// correct for sparks, flashes, shockwaves, etc.
    /// Expects the prefab to have a ParticleSystem + AutoDestroy for self-cleanup.
    /// </summary>
GameObject SpawnHitVfx(GameObject prefab, GameObject attacker)
    {
        if (prefab == null) return null;

        var weaponHitbox = attacker != null ? attacker.GetComponentInChildren<WeaponTipHitbox>() : null;
        Vector3 spawnPos = weaponHitbox != null
            ? weaponHitbox.transform.position
            : attacker != null
                ? Vector3.Lerp(transform.position, attacker.transform.position, 0.5f)
                : transform.position;

        Quaternion spawnRot = Quaternion.identity;
        if (attacker != null)
        {
            Vector3 dir = transform.position - attacker.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                spawnRot = Quaternion.LookRotation(dir.normalized);
        }

        return Instantiate(prefab, spawnPos, spawnRot);
    }

    /// <summary>
    /// Handles side-effects of real (non-blocked) damage: fires OnDamageTaken,
    /// resets animator speed, cancels buffered attack inputs, and plays the hurt SFX.
    /// Returns true if the current attack flow should be interrupted (used to time the stun bump).
    /// </summary>
    bool ApplyDamageEffects(int damage, bool isBlocking)
    {
        if (damage <= 0) return false;

        if (!isBlocking)
            OnDamageTaken?.Invoke(damage);

        // Safety reset: Combat may have slowed animator speed during startup/recovery/charge.
        if (animator != null) animator.speed = 1f;

        // Clear armed inputs so held buttons don't auto-fire once the hitstun ends.
        if (!isBlocking && combat != null)
            combat.CancelBufferedAttackInputs();

        if (!isBlocking) PlayHurtSfx();

        // Treat the entire charge flow (startup window + active hold) as interruptible on real damage.
        return !isBlocking && combat != null && (combat.IsAttacking || combat.IsInChargeFlow);
    }

    /// <summary>
    /// Stores knockback into the velocity (or into pendingKnockback for launchers),
    /// records hit-stop end time, and starts gamepad rumble for the hit-stop duration.
    /// Launchers use pending knockback so the launch fires after hitstop, giving the
    /// "cut to midair" effect rather than launching from a frozen position.
    /// </summary>
    void ApplyHitPhysics(Vector3 knockback, float airborneDuration, float hitStopDuration)
    {
        if (airborneDuration > 0f)
        {
            // Launcher hit: hold knockback; ApplyKnockback() in EntityHealth releases it when hitstop ends.
            pendingKnockback        = knockback;
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
    }

    /// <summary>
    /// Plays the appropriate hit or block-hit animation given the current state.
    /// forceInterrupt also cancels any base-layer attack animation so charge frames can't slip through.
    /// </summary>
    void TriggerHitReaction(bool isBlocking, float effectiveHitstun, bool forceInterrupt)
    {
        if (animator == null) return;
        if (isBlocking) TriggerBlockHitAnimation();
        else            TriggerHitAnimation(effectiveHitstun, forceInterrupt);
    }

    /// <summary>
    /// Sets isDead, plays the death SFX, and disables player input components.
    /// Game-over flow (UI, restart, etc.) is expected to be handled by a separate system.
    /// </summary>
    void HandleDeath()
    {
        isDead = true;
        PlayDeathSfx();
        if (playerController != null) playerController.enabled = false;
        if (combat          != null) combat.enabled           = false;
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
