/*
 * ============================================================================
 * ENTITYHEALTH.CS - Abstract base class for damageable entities
 * ============================================================================
 *
 * WHY A BASE CLASS HERE?
 * ----------------------
 * Both PlayerHealth and EnemyHealth share:
 *   - The same knockback physics fields and ApplyKnockback() logic
 *   - The same hitstun/airborne timer pattern (IsHitstunned, IsAirborne)
 *   - The same IDamageable properties (CurrentHp, MaxHp)
 *   - The same "random SFX with no-repeat" helper
 *
 * Rather than duplicating this in two files, EntityHealth holds the shared
 * implementation. PlayerHealth and EnemyHealth inherit from it and only add
 * what is unique to them (animations, blocking, AI references, etc.).
 *
 * INHERITANCE vs COMPOSITION:
 * ---------------------------
 * This is an appropriate use of inheritance because:
 *   - PlayerHealth IS-A EntityHealth (not just HAS-A)
 *   - The base behaviour is genuinely shared, not coincidentally similar
 *   - The derived classes extend (not replace) the base behaviour
 *
 * ============================================================================
 */

using UnityEngine;

/// <summary>
/// Abstract base for any damageable entity. Handles shared knockback physics,
/// hitstun/airborne timers, and SFX utilities. Subclasses implement TakeHit().
/// </summary>
public abstract class EntityHealth : MonoBehaviour, IDamageable
{
    // ========================================================================
    // HEALTH (shared)
    // ========================================================================

    protected int hp;       // Current hit points; decremented by TakeHit(), checked against 0 for death
    public int CurrentHp => hp;
    public abstract int MaxHp { get; }

    // ========================================================================
    // KNOCKBACK PHYSICS STATE (shared)
    // ========================================================================

    protected Vector3 kbVel;                    // Current knockback velocity in world space; decays each frame via ApplyKnockback
    protected float hitstunUntil;               // Time.time when hitstun expires — entity cannot act before this
    protected float airborneUntil;              // Time.time when airborne state ends — gravity suspends and entity can be juggled
    protected float hitStopEndTime;             // Time.time when hit-stop ends; position is frozen until then (set to 0 when expired)
    protected Vector3 pendingKnockback;         // Launcher knockback held in reserve — not applied until hitstun ends (so we "cut to midair")
    protected float pendingAirborneDuration;    // Airborne duration paired with pendingKnockback; both applied together when pendingLaunchApplyTime fires
    protected float pendingLaunchApplyTime;     // Time.time when the pending launch fires; 0 = no pending launch
    protected CharacterController cc;           // Cached for collision-safe knockback movement; may be null (falls back to transform.position)
    private Vector3 lastWallNormal;             // Most recent horizontal collision normal from OnControllerColliderHit; used for wall bounce reflection

    // ========================================================================
    // IDAMAGEABLE PROPERTIES (shared)
    // ========================================================================

    public bool IsHitstunned => Time.time < hitstunUntil;
    public bool IsAirborne => Time.time < airborneUntil;
    public float HitstunRemaining => Mathf.Max(0f, hitstunUntil - Time.time);
    public Vector3 KnockbackVelocity => kbVel;

    // ========================================================================
    // IDAMAGEABLE INTERFACE (subclasses implement)
    // ========================================================================

    public abstract void TakeHit(
        int damage,
        Vector3 knockback,
        float hitstun,
        float airborneDuration,
        float hitStopDuration = 0f,
        AttackHeaviness heaviness = AttackHeaviness.Medium,
        AttackHeight height = AttackHeight.Mid
    );

    // ========================================================================
    // KNOCKBACK PHYSICS (shared)
    // ========================================================================

    /// <summary>
    /// Apply knockback each frame: move by kbVel * deltaTime, then decay (exponential).
    /// During hit-stop the position is frozen; delayed launches apply when hitstun ends.
    /// </summary>
    protected void ApplyKnockback(float knockbackFriction)
    {
        // During hit-stop: freeze position
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime)
            return;
        if (hitStopEndTime > 0f && Time.time >= hitStopEndTime)
            hitStopEndTime = 0f;

        // When hitstop ends, apply delayed launch so we "cut to midair" (launcher attacks)
        if (pendingLaunchApplyTime > 0f && Time.time >= pendingLaunchApplyTime)
        {
            kbVel += pendingKnockback;
            airborneUntil = Mathf.Max(airborneUntil, Time.time + pendingAirborneDuration);
            pendingLaunchApplyTime = 0f;
        }

        if (kbVel.sqrMagnitude > 0.0001f)
        {
            Vector3 movement = kbVel * Time.deltaTime;
            // Only move when CC is enabled (e.g. skip while thrown — throw system disables CC and moves the root)
            if (cc != null && cc.enabled)
            {
                CollisionFlags flags = cc.Move(movement);
                // Side collision while moving fast = wall hit; notify subclass so it can bounce
                if ((flags & CollisionFlags.Sides) != 0)
                    OnWallBounce(lastWallNormal);
            }
            else if (cc == null)
                transform.position += movement;
            // else: CC exists but disabled — don't call Move (avoids "Move called on inactive controller")

            // Exponential decay: framerate-independent slide feel
            kbVel = Vector3.Lerp(kbVel, Vector3.zero, 1f - Mathf.Exp(-knockbackFriction * Time.deltaTime));
        }
    }

    // Captures the wall normal from the most recent CharacterController side collision.
    // Only stores near-horizontal normals (walls) to ignore floor/ceiling contacts.
    // Also fires OnEnemyKnockbackCollision when actively knocked back into another entity.
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (Mathf.Abs(hit.normal.y) < 0.5f)
            lastWallNormal = hit.normal;

        if (kbVel.sqrMagnitude > 0.01f)
        {
            var other = hit.gameObject.GetComponent<EntityHealth>();
            if (other != null && other != this)
                OnEnemyKnockbackCollision(other, kbVel);
        }
    }

    // Called when this entity (while being knocked back) collides with another entity.
    // velocity is this entity's current kbVel at the moment of contact.
    protected virtual void OnEnemyKnockbackCollision(EntityHealth other, Vector3 velocity) { }

    // Called when the entity hits a wall during knockback movement.
    // Override in subclasses to apply bounce logic (reflect kbVel, play animation, etc.).
    protected virtual void OnWallBounce(Vector3 wallNormal) { }

    // ========================================================================
    // RANDOM SFX HELPER (shared)
    // ========================================================================

    /// <summary>
    /// Play a random clip from <paramref name="clips"/> on <paramref name="source"/>,
    /// never repeating the same clip twice in a row. Pass <paramref name="lastIndex"/>
    /// by ref so the no-repeat tracking persists across calls.
    /// </summary>
    protected static void PlayRandomSfx(
        AudioSource source,
        AudioClip[] clips,
        ref int lastIndex,
        float pitchMin,
        float pitchMax,
        float volume)
    {
        if (source == null || clips == null || clips.Length == 0) return;

        int chosen = 0;
        if (clips.Length >= 2)
        {
            // Re-roll until we pick a different index than last time so the same clip never plays twice in a row
            do { chosen = Random.Range(0, clips.Length); }
            while (chosen == lastIndex);
        }
        lastIndex = chosen;

        AudioClip clip = clips[chosen];
        if (clip == null) return;

        source.pitch = Random.Range(
            Mathf.Min(pitchMin, pitchMax),
            Mathf.Max(pitchMin, pitchMax));
        source.PlayOneShot(clip, Mathf.Max(0f, volume));
    }
}
