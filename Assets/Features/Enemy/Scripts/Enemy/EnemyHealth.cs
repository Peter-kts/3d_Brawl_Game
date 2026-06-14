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

using System.Collections.Generic;
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

    [Header("Wall Bounce")]
    [Tooltip("Minimum kbVel speed (units/sec) to trigger a wall bounce. Below this the enemy just stops against the wall normally.")]
    public float wallBounceMinSpeed = 6f;
    [Tooltip("How much velocity is kept after bouncing (0 = stop dead, 1 = full elastic). 0.5–0.7 gives a satisfying bounce.")]
    [Range(0f, 1f)]
    public float wallBounceDamping = 0.6f;
    [Tooltip("Minimum remaining standing-stun time after a valid wall bounce. Only applies while standing stun is active.")]
    public float wallBounceMinStandingStunAfterBounce = 0.35f;

    [Header("Enemy Collision")]
    [Tooltip("Minimum kbVel speed (units/sec) for a collision to deal damage to another enemy.")]
    public float collisionMinSpeed = 4f;
    [Tooltip("Damage dealt per unit of collision speed. E.g. 0.5 at speed 10 = 5 damage.")]
    [Min(0f)]
    public float collisionDamagePerSpeed = 0.5f;
    [Tooltip("Fraction of this enemy's kbVel transferred as knockback to the hit enemy.")]
    [Range(0f, 1f)]
    public float collisionKnockbackTransfer = 0.7f;
    [Tooltip("Hitstun applied to the hit enemy.")]
    [Min(0f)]
    public float collisionHitstun = 0.25f;
    [Tooltip("Seconds before the same enemy can be hit again by this collision. Prevents multi-frame spam.")]
    [Min(0f)]
    public float collisionCooldown = 0.2f;

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
    private EnemyStunMeter stunMeter;            // Cached to check for heavy-hit re-trigger during standing stun
    private bool isDying;                        // True once death animation starts; blocks further TakeHit() calls
    private float getUpUntil;                   // Time.time when get-up stun expires; 0 = not getting up
    private float throwVictimUntil;             // Time.time while enemy is held in a throw; suppresses normal hit animation during this window
    private bool crashHitAlreadyUsed;           // Allows only one hit during the crash-landing phase; reset when the crash phase ends
    private float airborneSpeedMultiplier = 1f; // 1f normally; set to 1.4f on crash relaunch to speed up animation/timers; reset when sequence ends
    private int lastHurtSfxIndex = -1;          // Index of the last hurt clip played — prevents the same clip twice in a row
    private int lastDeathSfxIndex = -1;         // Index of the last death clip played — prevents repeat
    private Dictionary<int, float> collisionCooldowns = new Dictionary<int, float>(); // Per-enemy cooldown: instanceID → last hit time
    private bool deathCollisionDisabled;        // True once corpse collision has been fully disabled after knockback settles
    private bool dyingAwaitingKnockbackEnd;     // True while dying but knockback is still active; collision stays on
    private bool throwDeathPending;             // True when hp hit 0 during a throw; death deferred until throw releases victim
    private Vector3 throwDeathKnockback;        // Knockback saved from the hit that would have killed during throw
    private float throwDeathAirborneDuration;   // Airborne duration saved from the lethal hit during throw
    private string _pendingThrownStateName;     // Thrown animation state waiting for OnThrowAttach to play
    private float _pendingThrownDuration;       // Duration for the pending thrown animation

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
        stunMeter = GetComponent<EnemyStunMeter>();
        if (hurtSfxSource == null) hurtSfxSource = GetComponent<AudioSource>() ?? GetComponentInChildren<AudioSource>();
        // ResolveSfxSource (from EntityHealth) falls back to hurtSfxSource so the death clips
        // share the same AudioSource when no dedicated death source is wired up in the Inspector.
        deathSfxSource = ResolveSfxSource(deathSfxSource, hurtSfxSource);
    }

    void Update()
    {
        // Always apply knockback (even while dying — lets corpse get pushed around)
        ApplyKnockback(knockbackFriction);

        // While dying: keep collision alive until knockback settles, then disable.
        if (dyingAwaitingKnockbackEnd)
        {
            if (kbVel.sqrMagnitude < 0.01f && pendingKnockback.sqrMagnitude < 0.01f)
            {
                dyingAwaitingKnockbackEnd = false;
                DisableCollisionOnDeath();
            }
        }

        // Deferred throw death: throw has expired, process the pending death now.
        if (throwDeathPending && Time.time >= throwVictimUntil)
        {
            throwDeathPending = false;
            HandleDeath(throwDeathAirborneDuration, throwDeathKnockback);
            return;
        }

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

    // ResolveSfxSource lives in EntityHealth (base class) — see EntityHealth.cs for details.

    void PlayHurtSfx()  => PlayRandomSfx(hurtSfxSource,  hurtSfxClips,  ref lastHurtSfxIndex,  hurtSfxPitchMin,  hurtSfxPitchMax,  hurtSfxVolume);
    void PlayDeathSfx() => PlayRandomSfx(deathSfxSource, deathSfxClips, ref lastDeathSfxIndex, deathSfxPitchMin, deathSfxPitchMax, deathSfxVolume);

    void BeginDeferredCollisionDisable()
    {
        if (deathCollisionDisabled) return;
        dyingAwaitingKnockbackEnd = true;
    }

    void DisableCollisionOnDeath()
    {
        if (deathCollisionDisabled) return;
        deathCollisionDisabled = true;
        dyingAwaitingKnockbackEnd = false;

        if (cc != null) cc.enabled = false;

        Collider[] colliders = GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

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
        DisableCollisionOnDeath();
        if (enemyAI != null) enemyAI.enabled = false;
        if (enemyCombat != null) enemyCombat.enabled = false;
        if (cc != null) cc.enabled = false;
        Animator enemyAnimator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>();
        if (enemyAnimator != null) enemyAnimator.enabled = false;
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

    /// <summary>Directly set the airborne timer so IsAirborne becomes true immediately.</summary>
    public void SetAirborne(float duration)
    {
        if (duration <= 0f) return;
        airborneUntil = Mathf.Max(airborneUntil, Time.time + duration);
    }

    /// <summary>Clear airborne and hitstun timers so the throw system has full control of the enemy.</summary>
    public void ClearAirborneAndHitstun()
    {
        airborneUntil = 0f;
        hitstunUntil = 0f;
        pendingKnockback = Vector3.zero;
        pendingLaunchApplyTime = 0f;
    }

    // Called by EntityHealth when cc.Move() reports a side collision during knockback.
    // If speed is high enough, reflect velocity off the wall and play the bounce animation.
    protected override void OnWallBounce(Vector3 wallNormal)
    {
        if (kbVel.magnitude < wallBounceMinSpeed) return;
        if (isDying) return;

        // Extend the underlying standing-stun state (not micro hitstun) so bounce has room to resolve.
        if (stunMeter != null && wallBounceMinStandingStunAfterBounce > 0f)
            stunMeter.ExtendStandingStunMinRemaining(wallBounceMinStandingStunAfterBounce);

        // Reflect horizontal velocity off the wall normal, keep vertical component as-is
        Vector3 reflected = Vector3.Reflect(kbVel, wallNormal) * wallBounceDamping;
        kbVel = reflected;

        if (enemyAI != null)
        {
            // Re-seed facing opposite the reflected direction (inverted bounce-facing behavior).
            enemyAI.SetKnockbackFacingDirection(-reflected, snap: true);
            enemyAI.TriggerWallBounce();
        }
    }

    // Called by EntityHealth when this enemy collides with another entity while being knocked back.
    // Deals damage and transfers knockback to the other enemy, scaled by current speed.
protected override void OnEnemyKnockbackCollision(EntityHealth other, Vector3 velocity)
    {
        if (isDying) return;
        // Don't damage entities on the same side (e.g. two enemies knocked together still damage each other,
        // but an ally knocked into an enemy — or vice versa — respects team hostility).
        if (!TeamUtil.AreHostile(team, other.team)) return;

        float speed = velocity.magnitude;
        if (speed < collisionMinSpeed) return;

        int id = other.GetInstanceID();
        if (collisionCooldowns.TryGetValue(id, out float lastTime) && Time.time < lastTime + collisionCooldown)
            return;
        collisionCooldowns[id] = Time.time;

        int damage = Mathf.Max(1, Mathf.RoundToInt(speed * collisionDamagePerSpeed));
        Vector3 knockback = velocity * collisionKnockbackTransfer;
        other.TakeHit(damage, knockback, collisionHitstun, 0f);
    }

    /// <summary>
    /// Start throw-victim state: only sets the throw-victim timer and stores pending animation data.
    /// No visible change on the enemy — stun, animation, and AI lockdown are all deferred to
    /// PlayThrowVictimAnimation() (called from OnThrowAttach) so the enemy shows zero reaction
    /// until the player's hands actually close around them.
    /// </summary>
    public void StartThrowVictim(float durationSeconds, string thrownStateName)
    {
        if (isDying) return;
        throwVictimUntil = Mathf.Max(throwVictimUntil, Time.time + durationSeconds);
        _pendingThrownStateName = thrownStateName;
        _pendingThrownDuration = durationSeconds;
    }

    /// <summary>
    /// Commit the throw: apply stun, play the thrown/receive animation, and lock AI.
    /// Called from OnThrowAttach so the enemy only reacts when the player's hands actually close.
    /// </summary>
    public void PlayThrowVictimAnimation()
    {
        if (isDying) return;
        hitstunUntil = Time.time;
        if (stunMeter != null)
            stunMeter.ForceStandingStun(_pendingThrownDuration, asKnockback: false);
        if (enemyAI != null && !string.IsNullOrEmpty(_pendingThrownStateName))
            enemyAI.TriggerThrownAnimation(_pendingThrownDuration, _pendingThrownStateName);
        _pendingThrownStateName = null;
    }

    /// <summary>
    /// Called when the throw fully releases this victim. Clears the throw-victim window
    /// and processes any death that was deferred while held.
    /// </summary>
    public void EndThrowVictimState()
    {
        throwVictimUntil = 0f;
        _pendingThrownStateName = null;
        if (stunMeter != null)
            stunMeter.ClearStandingStun();
        if (throwDeathPending)
        {
            throwDeathPending = false;
            HandleDeath(throwDeathAirborneDuration, throwDeathKnockback);
        }
    }

    /// <summary>
    /// Victim-side animation-event relay (fallback path): forwards throw release to the owning player Combat.
    /// This allows OnThrowRelease events authored on victim clips to still trigger the release flow.
    /// </summary>
    public void OnThrowRelease()
    {
        Combat[] combats = FindObjectsOfType<Combat>();
        for (int i = 0; i < combats.Length; i++)
        {
            Combat combat = combats[i];
            if (combat == null) continue;
            if (!combat.IsHoldingThrowVictim(this)) continue;
            combat.OnThrowRelease();
            return;
        }
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
        AttackHeight height = AttackHeight.Mid,
        GameObject attacker = null
    )
    {
        if (isDying) return;

        // Bonus damage and knockback when hitting a stunned enemy.
        if (stunMeter != null && stunMeter.IsStunned)
        {
            damage = Mathf.RoundToInt(damage * 1.5f);
            knockback *= 2f;
        }

        // Extreme knockback instantly fills the stun meter and triggers knockback stun.
        if (stunMeter != null && !stunMeter.IsStunned && knockback.magnitude >= stunMeter.knockbackStunThreshold)
            stunMeter.AddStun(1f, knockback.magnitude);

        // Crash relaunch: one free re-juggle hit while the enemy is landing.
        // Modifies knockback/airborneDuration in place; returns false if crash-hit already used (block the hit).
        if (!ApplyCrashRelaunch(ref knockback, ref airborneDuration))
            return;

        // Prone path: enemy is flat on the floor after crashing.
        // Takes damage; heavy hits slide them and extend stun. Always returns after this block.
        bool inGrounded = enemyAI != null && enemyAI.ProneSystem != null && enemyAI.ProneSystem.IsInProne;
        if (inGrounded)
        {
            HandlePronePath(damage, knockback, hitstun, hitStopDuration, height, heaviness);
            return;
        }

        // Normal standing/airborne hit path.
        ApplyDamageAndSfx(damage);
        ApplyHitPhysics(knockback, airborneDuration, hitStopDuration);
        ApplyHitstun(hitstun, airborneDuration, hitStopDuration);
        TriggerHitReaction(knockback, hitstun, height);
        if (hp <= 0)
        {
            if (Time.time < throwVictimUntil)
            {
                throwDeathPending = true;
                throwDeathKnockback = knockback;
                throwDeathAirborneDuration = airborneDuration;
            }
            else
            {
                HandleDeath(airborneDuration, knockback);
            }
        }
    }

    // -------------------------------------------------------------------------
    // TakeHit helpers — each covers one logical concern
    // -------------------------------------------------------------------------

    /// <summary>
    /// Crash relaunch gate: only one hit is allowed during the landing crash phase.
    /// That hit gets 1.5× knockback and 1.4× animation speed so the re-launch looks snappy.
    /// Returns false if the crash-hit quota is already spent (caller should return immediately).
    /// </summary>
    bool ApplyCrashRelaunch(ref Vector3 knockback, ref float airborneDuration)
    {
        bool inCrash = enemyAI != null && enemyAI.AirborneSequence != null && enemyAI.AirborneSequence.InCrash;
        if (!inCrash) return true;         // Not crashing — nothing to do, proceed normally.
        if (crashHitAlreadyUsed) return false; // Crash-hit already spent — ignore this hit.

        crashHitAlreadyUsed     = true;
        airborneSpeedMultiplier = 1.4f;

        // Scale Y down so the enemy flies further across the floor, not straight up.
        knockback.y      *= crashRelaunchKnockbackYScale;
        knockback        *= 1.5f;

        // Shorter airborne duration compensates for the faster animation speed so the clip still fits.
        airborneDuration  = (airborneDuration > 0f ? airborneDuration : crashRelaunchAirborneDurationDefault)
                            / airborneSpeedMultiplier;
        return true;
    }

    /// <summary>
    /// Hit response while the enemy is prone (flat on the floor after a crash landing).
    /// Damage always applies. Heavy hits (above the stun threshold) also slide the body
    /// and extend the stun window so the enemy can't get up instantly.
    /// Light hits only deal damage and play a ground-hit animation.
    /// </summary>
void HandlePronePath(int damage, Vector3 knockback, float hitstun, float hitStopDuration,
                         AttackHeight height, AttackHeaviness heaviness)
    {
        // Apply damage first so HP is correct when we check death.
        int hpBefore = hp;
        hp -= damage;
        ScreenShake.RequestShake();
        if (hp < hpBefore) PlayHurtSfx();

        // A hit counts as "heavy" if the knockback exceeds the stun threshold (tunable on EnemyStunMeter),
        // falling back to the AttackHeaviness enum when no stun meter is present.
        bool heavyKnockback = stunMeter != null
            ? knockback.magnitude >= stunMeter.knockbackStunThreshold
            : heaviness == AttackHeaviness.Heavy;

        if (heavyKnockback)
        {
            kbVel       += knockback;
            hitstunUntil  = Mathf.Max(hitstunUntil, Time.time + hitstun);
            if (hitStopDuration > 0f) hitStopEndTime = Time.time + hitStopDuration;
        }

        if (hp <= 0)
        {
            isDying = true;
            BeginDeferredCollisionDisable();
            PlayDeathSfx();
            if (enemyAI != null) enemyAI.TriggerDeathAnimation(heavyKnockback);
            else                 CompleteDeath();
        }
        else if (enemyAI != null)
        {
            enemyAI.TriggerHitAnimation(hitstun, height, heavyKnockback);
        }
    }

    /// <summary>
    /// Applies damage and plays the hurt SFX. Separated so that the prone and normal
    /// paths both have a consistent damage application step.
    /// </summary>
    void ApplyDamageAndSfx(int damage)
    {
        int hpBefore = hp;
        hp -= damage;
        ScreenShake.RequestShake();
        if (hp < hpBefore) PlayHurtSfx();
    }

    /// <summary>
    /// Stores knockback into the velocity (or into pendingKnockback for launchers)
    /// and records the hit-stop end time.
    /// Launchers use pending knockback so the launch fires after hitstop — giving the
    /// "cut to midair" effect instead of launching from a frozen position.
    /// </summary>
    void ApplyHitPhysics(Vector3 knockback, float airborneDuration, float hitStopDuration)
    {
        if (airborneDuration > 0f)
        {
            pendingKnockback        = knockback;
            pendingAirborneDuration = airborneDuration;
        }
        else
            kbVel += knockback;

        if (hitStopDuration > 0f)
            hitStopEndTime = Time.time + hitStopDuration;
    }

    /// <summary>
    /// Extends hitstun (never shortens an existing longer stun) and sets the pending
    /// launch time for launcher hits so ApplyKnockback() fires the velocity when hitstop ends.
    /// </summary>
    void ApplyHitstun(float hitstun, float airborneDuration, float hitStopDuration)
    {
        hitstunUntil = Mathf.Max(hitstunUntil, Time.time + hitstun);
        if (airborneDuration > 0f)
            pendingLaunchApplyTime = (hitStopDuration > 0f) ? (Time.time + hitStopDuration) : Time.time;
    }

    /// <summary>
    /// Triggers the hit animation on SimpleEnemyAI.
    /// If the enemy is mid-throw, animation is suppressed (the throw animation takes precedence).
    /// If already in standing stun and the hit has heavy knockback, re-enters the knockback
    /// stun entry so the pose resets rather than holding the existing stun freeze.
    /// </summary>
    void TriggerHitReaction(Vector3 knockback, float hitstun, AttackHeight height)
    {
        // During a throw the enemy's animation is controlled by the throw sequence; don't override it.
        bool isBeingThrown = Time.time < throwVictimUntil;

        // Re-trigger: if the enemy is already standing-stunned, a heavy enough new hit should
        // reset the stun pose rather than just extending the timer silently.
        if (stunMeter != null && stunMeter.IsStandingStunned)
            stunMeter.TryRetriggerAsKnockback(knockback.magnitude);

        if (enemyAI != null && !isBeingThrown)
            enemyAI.TriggerHitAnimation(hitstun, height);
    }

    /// <summary>
    /// Sets isDying, plays the death SFX, and delegates to the appropriate death animation path.
    /// Airborne kills use the ongoing liftoff/loop/crash sequence as the death anim;
    /// SimpleEnemyAI calls OnAirborneSequenceComplete() when that sequence ends.
    /// Standing kills trigger TriggerDeathAnimation() immediately.
    /// </summary>
void HandleDeath(float airborneDuration, Vector3 knockback)
    {
        isDying = true;
        BeginDeferredCollisionDisable();
        PlayDeathSfx();

        bool isKnockbackDeath = stunMeter != null
            ? knockback.magnitude >= stunMeter.knockbackStunThreshold
            : false;

        if (isKnockbackDeath)
            kbVel *= 1.5f;  // Extra knockback pop for the death stumble

        if (airborneDuration > 0f)
        {
            // Killed mid-launch: the airborne animation plays out as the death anim.
            // CompleteDeath() is called by OnAirborneSequenceComplete() once it finishes.
            if (enemyAI == null) CompleteDeath();
        }
        else
        {
            if (enemyAI != null) enemyAI.TriggerDeathAnimation(isKnockbackDeath);
            else                 CompleteDeath();
        }
    }
}
