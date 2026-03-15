/*
 * ============================================================================
 * ENEMYHEALTH.CS - Handles enemy HP, damage, knockback, and death
 * ============================================================================
 *
 * Inherits from EntityHealth for shared knockback physics, stun/airborne
 * timers, IDamageable properties, and the random-SFX helper.
 *
 * This class adds what is unique to enemies:
 *   - Crash relaunch (one juggle hit during landing phase)
 *   - Prone/grounded hit response (no knockback while on the floor)
 *   - Get-up sequence after crash landing
 *   - Throw-victim state (stun without hit reaction)
 *   - Airborne speed multiplier for crash relaunch animation timing
 *   - AI and combat component references for animation/death callbacks
 *
 * DESIGN PATTERN - COMPONENT-BASED:
 * ----------------------------------
 * Notice how EnemyHealth handles ONLY health-related logic.
 * Movement is in SimpleEnemyAI. Visual feedback would be separate.
 *
 * COLLISION-AWARE KNOCKBACK:
 * --------------------------
 * Uses CharacterController.Move() for knockback so enemies won't pass through
 * walls or overlap when knocked into each other (inherited from EntityHealth).
 *
 * ============================================================================
 */

using UnityEngine;

public class EnemyHealth : EntityHealth
{
    // ========================================================================
    // SERIALIZED FIELDS
    // ========================================================================

    [Header("Health")]
    [Tooltip("Starting/maximum health points. 60 HP with 10 damage = 6 hits to kill.")]
    public int maxHp = 60;
    public override int MaxHp => maxHp;

    [Header("Hit Reaction")]
    [Tooltip("How quickly knockback velocity decays. Higher = stops faster (heavy enemy), Lower = slides further (light enemy).")]
    public float knockbackFriction = 12f;

    [Tooltip("Default airborne duration for crash relaunch when the hitting move has no launch (makesAirborne false).")]
    public float crashRelaunchAirborneDurationDefault = 0.5f;

    [Tooltip("Crash relaunch: knockback Y is scaled by this (e.g. 0.25 = 25% of original), so they go further not higher.")]
    [Range(0f, 1f)]
    public float crashRelaunchKnockbackYScale = 0.25f;

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

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private SimpleEnemyAI enemyAI;               // Cached for triggering animations and accessing AirborneSequence/ProneSystem
    private EnemyCombat enemyCombat;             // Cached for CompleteDeath() — disabled when health reaches 0
    private bool isDying;                        // True once death animation starts; blocks further TakeHit() calls
    private float getUpUntil;                   // Time.time when get-up stun expires; 0 = not getting up
    private float throwVictimUntil;             // Time.time while enemy is held in a throw; suppresses normal hit animation during this window
    private bool crashHitAlreadyUsed;           // Allows only one hit during the crash-landing phase; reset when the crash phase ends
    private float airborneSpeedMultiplier = 1f; // 1f normally; set to 1.4f on crash relaunch to speed up animation/timers; reset when sequence ends
    private int lastHurtSfxIndex = -1;          // Index of the last hurt clip played — prevents the same clip twice in a row
    private int lastDeathSfxIndex = -1;         // Index of the last death clip played — prevents repeat

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        hp = maxHp;
        // CharacterController added by SimpleEnemyAI's [RequireComponent]; used here for collision-aware knockback
        cc = GetComponent<CharacterController>();
        enemyAI = GetComponent<SimpleEnemyAI>();
        enemyCombat = GetComponent<EnemyCombat>();
        if (hurtSfxSource == null) hurtSfxSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>();
        deathSfxSource = ResolveSfxSource(deathSfxSource);
    }

    void Update()
    {
        // Always apply knockback (even while dying — lets corpse get pushed around)
        ApplyKnockback(knockbackFriction);

        // Skip other processing while death animation plays
        if (isDying) return;

        UpdateGetUpOnCrash();

        // Clear crash-one-hit flag when not in crash so next crash allows one hit again
        if (enemyAI != null && enemyAI.AirborneSequence != null && !enemyAI.AirborneSequence.InCrash)
            crashHitAlreadyUsed = false;
    }

    // ========================================================================
    // HELPERS
    // ========================================================================

    /// <summary>Returns the assigned source if set, otherwise falls back to hurtSfxSource, then any AudioSource on this object.</summary>
    private AudioSource ResolveSfxSource(AudioSource assigned) =>
        assigned != null ? assigned :
        hurtSfxSource != null ? hurtSfxSource :
        GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>();

    void PlayHurtSfx()  => PlayRandomSfx(hurtSfxSource,  hurtSfxClips,  ref lastHurtSfxIndex,  hurtSfxPitchMin,  hurtSfxPitchMax,  hurtSfxVolume);
    void PlayDeathSfx() => PlayRandomSfx(deathSfxSource, deathSfxClips, ref lastDeathSfxIndex, deathSfxPitchMin, deathSfxPitchMax, deathSfxVolume);

    // ========================================================================
    // GET-UP / AIRBORNE CALLBACKS
    // ========================================================================

    /// <summary>
    /// Clear get-up timer when expired. Get-up is started (and animation triggered) by SimpleEnemyAI when the crash phase finishes.
    /// </summary>
    void UpdateGetUpOnCrash()
    {
        if (getUpUntil > 0f && Time.time >= getUpUntil)
            getUpUntil = 0f;
    }

    /// <summary>
    /// Start the get-up sequence. Called by SimpleEnemyAI when the airborne crash phase has finished.
    /// </summary>
    public void StartGetUp(float duration)
    {
        // Already in get-up sequence; avoid playing get-up twice
        if (getUpUntil > 0f && Time.time < getUpUntil)
            return;
        float scaled = GetAirborneSpeedMultiplier() > 1f ? duration / GetAirborneSpeedMultiplier() : duration;
        getUpUntil = Time.time + scaled;
    }

    /// <summary>
    /// Called by SimpleEnemyAI when the airborne sequence (liftoff/loop/crash) finishes.
    /// If the enemy was killed by an airborne attack, this completes the death.
    /// </summary>
    public void OnAirborneSequenceComplete()
    {
        if (isDying)
        {
            if (enemyAI != null)
                enemyAI.EnterPermanentProneForDeath();
            CompleteDeath();
        }
    }

    /// <summary>
    /// Disables AI, combat, and movement so the corpse stays visible; does not disable the GameObject/mesh.
    /// </summary>
    public void CompleteDeath()
    {
        if (enemyAI != null) enemyAI.enabled = false;
        if (enemyCombat != null) enemyCombat.enabled = false;
        if (cc != null) cc.enabled = false;
        enabled = false;
    }

    // ========================================================================
    // PUBLIC INTERFACE
    // ========================================================================

    /// <summary>True once HP reaches 0 and death animation starts.</summary>
    public bool IsDying => isDying;

    /// <summary>True when the enemy has just landed and is in the get-up stun (playing get-up animation).</summary>
    public bool IsGettingUp => getUpUntil > 0f && Time.time < getUpUntil;

    /// <summary>Speed multiplier for airborne animation (1f normal, 1.4f during crash relaunch). Cleared when sequence ends.</summary>
    public float GetAirborneSpeedMultiplier() => airborneSpeedMultiplier;

    /// <summary>Called when the airborne sequence ends so the next airborne is 1x again.</summary>
    public void OnAirborneSequenceEnded()
    {
        airborneSpeedMultiplier = 1f;
    }

    /// <summary>Clear current knockback velocity and pending knockback. Use when releasing a throw victim.</summary>
    public void ClearKnockback()
    {
        kbVel = Vector3.zero;
        pendingKnockback = Vector3.zero;
        hitStopEndTime = 0f;
    }

    /// <summary>
    /// Start throw-victim state: stun for duration and play the thrown animation.
    /// </summary>
    public void StartThrowVictim(float durationSeconds, string thrownStateName)
    {
        if (isDying) return;
        throwVictimUntil = Mathf.Max(throwVictimUntil, Time.time + durationSeconds);
        stunUntil = Mathf.Max(stunUntil, Time.time + durationSeconds);
        if (enemyAI != null)
            enemyAI.TriggerThrownAnimation(durationSeconds, thrownStateName);
    }

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
        // Ignore hits if already dying (death animation playing)
        if (isDying) return;

        // Crash mode: allow one hit; that hit gets 1.5x knockback and re-launches with 1.4x animation speed
        bool inCrash = enemyAI != null && enemyAI.AirborneSequence != null && enemyAI.AirborneSequence.InCrash;
        bool inGrounded = enemyAI != null && enemyAI.ProneSystem != null && enemyAI.ProneSystem.IsInProne;
        if (inCrash && crashHitAlreadyUsed)
            return;
        if (inCrash && !crashHitAlreadyUsed)
        {
            crashHitAlreadyUsed = true;
            airborneSpeedMultiplier = 1.4f;
            // Y scaled down so knockback goes further, not higher
            knockback.y *= crashRelaunchKnockbackYScale;
            knockback *= 1.5f;
            airborneDuration = (airborneDuration > 0f ? airborneDuration : crashRelaunchAirborneDurationDefault) / airborneSpeedMultiplier;
        }

        // STEP 1: Apply damage
        int hpBeforeDamage = hp;
        hp -= damage;
        ScreenShake.RequestShake();
        if (hp < hpBeforeDamage)
            PlayHurtSfx();

        // When prone (on floor after crash): take damage and play prone hit animation only —
        // no knockback, airborne, hitstop, stun, or normal hit animation.
        if (inGrounded)
        {
            if (hp <= 0)
            {
                isDying = true;
                PlayDeathSfx();
                if (enemyAI != null)
                    enemyAI.TriggerDeathAnimation();
                else
                    CompleteDeath();
            }
            else if (enemyAI != null)
            {
                enemyAI.TriggerHitAnimation(hitstun, height);
            }
            return;
        }

        // STEP 2: Apply knockback (stored; movement frozen during hitstop, resumes after)
        if (airborneDuration > 0f)
        {
            pendingKnockback = knockback;
            pendingAirborneDuration = airborneDuration;
        }
        else
        {
            kbVel += knockback;
        }
        if (hitStopDuration > 0f)
            hitStopEndTime = Time.time + hitStopDuration;

        // STEP 3: Apply hit stun — Mathf.Max so we don't shorten an existing longer stun
        stunUntil = Mathf.Max(stunUntil, Time.time + hitstun);
        if (airborneDuration > 0f)
            pendingLaunchApplyTime = (hitStopDuration > 0f) ? (Time.time + hitStopDuration) : Time.time;

        // STEP 4: Trigger hit animation (skip if currently being thrown)
        bool isBeingThrown = Time.time < throwVictimUntil;
        if (enemyAI != null && !isBeingThrown)
            enemyAI.TriggerHitAnimation(hitstun, height);

        // STEP 5: Check for death
        if (hp <= 0)
        {
            isDying = true;
            PlayDeathSfx();
            /*
             * If killed by an airborne attack, the airborne animation (liftoff/loop/crash)
             * is used as the death — SimpleEnemyAI calls OnAirborneSequenceComplete() when it ends.
             */
            if (airborneDuration > 0f)
            {
                if (enemyAI == null)
                    CompleteDeath();
            }
            else
            {
                if (enemyAI != null)
                    enemyAI.TriggerDeathAnimation();
                else
                    CompleteDeath();
            }
        }
    }
}
