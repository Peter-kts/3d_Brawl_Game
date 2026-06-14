/*
 * ============================================================================
 * ENEMYCOMBAT.CS - Enemy attack execution using AttackData + WeaponTipHitbox
 * ============================================================================
 *
 * ENEMY ATTACK SYSTEM:
 * --------------------
 *
 * This component handles the mechanics of enemy attacks:
 *   - Hitbox activation via BeginHitbox(int id) / EndHitbox(int id) animation events
 *   - Damage dealing (via IDamageable interface, fired from WeaponTipHitbox.HitConfirmed)
 *   - Attack animation triggering
 *   - Forward lunge during attacks
 *   - Cooldown and lock state tracking
 *
 * It reuses the same AttackData class as the player's Combat.cs.
 *
 * HITBOX SETUP:
 * -------------
 * Add a WeaponTipHitbox component to the enemy's fist/weapon bone child GameObject.
 * Assign it to the 'Weapon Tip Hitbox' slot (id = 0).
 * For additional hitboxes (e.g. kick = id 1) add entries to 'Hitbox Slots'.
 *
 * ANIMATION EVENTS:
 * -----------------
 * On each attack clip, add:
 *   BeginHitbox(int id)  — at the first active frame
 *   EndHitbox(int id)    — at the last active frame
 *
 * USAGE:
 * ------
 * Behaviors (like StandoffBehavior) call DoAttack() when they decide
 * to attack. They check IsAttacking to know when the attack is done.
 *
 * ============================================================================
 */

using UnityEngine;
using System.Collections.Generic;

public class EnemyCombat : MonoBehaviour
{
    // ========================================================================
    // HITBOX SLOTS
    // ========================================================================

    [System.Serializable]
    public struct HitboxSlot
    {
        [Tooltip("Animation-event ID used by BeginHitbox(int)/EndHitbox(int).")]
        public int id;
        [Tooltip("WeaponTipHitbox component to activate for this ID.")]
        public WeaponTipHitbox hitbox;
    }

    [Header("Hitboxes")]
    [Tooltip("Default fist/weapon hitbox. Used by BeginHitbox(0) and EndHitbox(0).")]
    public WeaponTipHitbox weaponTipHitbox;
    [Tooltip("Auto-find WeaponTipHitbox in children if not assigned.")]
    public bool autoFindWeaponTipHitbox = true;
    [Tooltip("Optional additional hitboxes addressable by ID (e.g. 1=kick, 2=elbow).")]
    public HitboxSlot[] hitboxSlots;

    // ========================================================================
    // ATTACK DATA
    // ========================================================================

    [Header("Moveset")]
    [Tooltip("Data-driven moveset. Attack selection uses distance ranges and priority defined per entry.")]
    public EnemyComboSet enemyComboSet;

    [Header("VFX (optional)")]
    [Tooltip("Optional. Spawned when the attack animation starts.")]
    public GameObject attackStartVfxPrefab;
    [Tooltip("Optional. Spawned at hit point when the attack connects.")]
    public GameObject hitConnectVfxPrefab;

    [Header("SFX (optional)")]
    [Tooltip("Audio source used for attack sounds.")]
    public AudioSource sfxSource;
    [Range(0.5f, 1.5f)]
    public float sfxPitchMin = 0.96f;
    [Range(0.5f, 1.5f)]
    public float sfxPitchMax = 1.04f;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private float attackEndTime;
    private AttackData currentAttack;
    private Animator animator;
    private CharacterController cc;
    private EnemyHealth enemyHealth;
    private EnemyStunMeter enemyStunMeter;

    private bool lungePending;
    private float lungeTriggerTime;
    private float lungeEndTime;
    private float currentLungeDistance;
    private float currentLungeDuration;
    private Vector3 lungeDirection;

    private bool hitConfirmedThisAttack; // gates first-hit VFX, SFX, and self hit-stop
    private bool suppressHitboxActivationsUntilNextCommit; // blocks stale animation-event hitbox reactivation after interrupts

    private struct FrozenAnimator
    {
        public Animator animator;
        public float originalSpeed;
    }
    private float hitStopEndTime;
    private List<FrozenAnimator> frozenAnimators = new List<FrozenAnimator>();

    private float currentStartUpLength;
    private float currentStartUpSpeed;
    private float currentRecoveryLength;
    private float currentRecoverySpeed;
    private string currentAttackStateName;

    // ========================================================================
    // PUBLIC PROPERTIES
    // ========================================================================

    /// <summary>True while the enemy is locked in an attack animation.</summary>
    public bool IsAttacking => Time.time < attackEndTime;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        cc = GetComponent<CharacterController>();
        enemyHealth = GetComponent<EnemyHealth>();
        enemyStunMeter = GetComponent<EnemyStunMeter>();
        if (sfxSource == null) sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = GetComponentInChildren<AudioSource>();
    }

    void OnDisable()
    {
        suppressHitboxActivationsUntilNextCommit = true;
        currentAttack = null;
        EndAllHitboxes();
    }

    void Update()
    {
        var h = enemyHealth;
        if ((h != null && h.IsHitstunned) || (enemyStunMeter != null && enemyStunMeter.IsStandingStunned))
        {
            RestoreAnimatorSpeedStateAfterDamageOrStun();
            attackEndTime = 0f;
            currentAttack = null;
            suppressHitboxActivationsUntilNextCommit = true;
            hitConfirmedThisAttack = false;
            EndAllHitboxes();
            lungePending = false;
            return;
        }
        UpdateLunge();
        UpdateHitStop();
        UpdateAttackStartUpSpeed();
    }

    void RestoreAnimatorSpeedStateAfterDamageOrStun()
    {
        for (int i = 0; i < frozenAnimators.Count; i++)
        {
            Animator a = frozenAnimators[i].animator;
            if (a != null) a.speed = frozenAnimators[i].originalSpeed;
        }
        frozenAnimators.Clear();
        hitStopEndTime = 0f;
        if (animator != null && animator.speed != 0f) animator.speed = 1f;
    }

    // ========================================================================
    // ATTACK EXECUTION
    // ========================================================================

    /// <summary>Execute a specific attack by passing its AttackData directly.</summary>
    public void DoAttack(AttackData attack)
    {
        currentAttack = attack;
        hitConfirmedThisAttack = false;
        suppressHitboxActivationsUntilNextCommit = false;

        attackEndTime = Time.time + attack.lockDuration;

        currentStartUpLength   = attack.startUpLength;
        currentStartUpSpeed    = attack.startUpSpeed;
        currentRecoveryLength  = attack.recoveryLength;
        currentRecoverySpeed   = attack.recoverySpeed;
        currentAttackStateName = !string.IsNullOrEmpty(attack.animationTrigger) ? attack.animationTrigger : null;

        // Reset per-attack hit cache on all hitboxes
        EnsureWeaponTipHitbox();
        if (weaponTipHitbox != null) weaponTipHitbox.ResetHitCache();
        if (hitboxSlots != null)
            for (int i = 0; i < hitboxSlots.Length; i++)
                hitboxSlots[i].hitbox?.ResetHitCache();

        EndAllHitboxes();

        if (attack.lungeDistance > 0)
        {
            lungePending         = true;
            lungeTriggerTime     = Time.time + (attack.lockDuration * attack.lungeFrame);
            lungeEndTime         = lungeTriggerTime + attack.lungeDuration;
            currentLungeDistance = attack.lungeDistance;
            currentLungeDuration = attack.lungeDuration;
            lungeDirection       = transform.forward;
        }
        else
        {
            lungePending = false;
        }

        if (animator != null && !string.IsNullOrEmpty(attack.animationTrigger))
            animator.Play(attack.animationTrigger, 0, 0f);

        PlayAttackCues(attack, AttackSfxTriggerType.OnAttackStart, 0, useLegacyFallback: true);

        if (attackStartVfxPrefab != null)
        {
            Vector3 pos    = transform.position + attack.attackStartVfxPositionOffset;
            Quaternion rot = transform.rotation * Quaternion.Euler(attack.attackStartVfxRotationOffset);
            PlayVfx(Object.Instantiate(attackStartVfxPrefab, pos, rot));
        }

        // Hitbox activation is driven by BeginHitbox/EndHitbox animation events on the attack clip.
    }

    /// <summary>
    /// Try to select an attack from the EnemyComboSet based on distance.
    /// Among all matching entries, picks randomly from those tied at the highest priority.
    /// Returns false if no set is assigned or no entry matches.
    /// </summary>
    public bool TrySelectAttack(float distanceToPlayer, out AttackData selectedAttack)
    {
        selectedAttack = null;

        if (enemyComboSet == null || enemyComboSet.moves == null || enemyComboSet.moves.Count == 0)
            return false;

        int bestPriority = int.MinValue;
        int candidateCount = 0;

        // Two-pass: first find the highest priority that matches, then pick randomly among ties.
        for (int i = 0; i < enemyComboSet.moves.Count; i++)
        {
            EnemyMoveEntry entry = enemyComboSet.moves[i];
            if (entry == null || entry.attack == null) continue;
            if (string.IsNullOrEmpty(entry.attack.animationTrigger)) continue;

            float minDist = Mathf.Min(entry.minDistance, entry.maxDistance);
            float maxDist = Mathf.Max(entry.minDistance, entry.maxDistance);
            if (distanceToPlayer < minDist || distanceToPlayer > maxDist) continue;

            if (entry.priority > bestPriority)
            {
                bestPriority   = entry.priority;
                selectedAttack = entry.attack;
                candidateCount = 1;
            }
            else if (entry.priority == bestPriority)
            {
                // Reservoir sampling: replace with 1/n probability so all ties are equally likely.
                candidateCount++;
                if (Random.Range(0, candidateCount) == 0)
                    selectedAttack = entry.attack;
            }
        }

        return selectedAttack != null;
    }

    // ========================================================================
    // HITBOX ANIMATION EVENT HOOKS
    // ========================================================================

    /// <summary>Animation event: activate the hitbox with the given ID.</summary>
    public void BeginHitbox(int id)
    {
        WeaponTipHitbox hitbox = GetHitboxById(id);
        if (hitbox == null || currentAttack == null) return;
        if (suppressHitboxActivationsUntilNextCommit) return;
        if (!IsAttacking) return;

        hitbox.HitConfirmed -= OnHitConfirmed;
        hitbox.HitConfirmed += OnHitConfirmed;
        hitbox.BeginActiveFrames(transform, currentAttack);
    }

    /// <summary>Animation event: deactivate the hitbox with the given ID.</summary>
    public void EndHitbox(int id)
    {
        WeaponTipHitbox hitbox = GetHitboxById(id);
        if (hitbox == null) return;
        hitbox.HitConfirmed -= OnHitConfirmed;
        hitbox.EndActiveFrames();
    }

    /// <summary>Force-close every configured hitbox (called on interrupt or disable).</summary>
    public void EndAllHitboxes()
    {
        EnsureWeaponTipHitbox();
        StopHitbox(weaponTipHitbox);

        if (hitboxSlots == null) return;
        for (int i = 0; i < hitboxSlots.Length; i++)
        {
            WeaponTipHitbox h = hitboxSlots[i].hitbox;
            if (h == null || h == weaponTipHitbox) continue;
            StopHitbox(h);
        }
    }

    void StopHitbox(WeaponTipHitbox hitbox)
    {
        if (hitbox == null) return;
        hitbox.HitConfirmed -= OnHitConfirmed;
        hitbox.EndActiveFrames();
    }

    void EnsureWeaponTipHitbox()
    {
        if (weaponTipHitbox != null || !autoFindWeaponTipHitbox) return;
        weaponTipHitbox = GetComponentInChildren<WeaponTipHitbox>(true);
    }

    WeaponTipHitbox GetHitboxById(int id)
    {
        if (hitboxSlots != null)
            for (int i = 0; i < hitboxSlots.Length; i++)
                if (hitboxSlots[i].id == id && hitboxSlots[i].hitbox != null)
                    return hitboxSlots[i].hitbox;

        if (id != 0) return null;
        EnsureWeaponTipHitbox();
        return weaponTipHitbox;
    }

    // ========================================================================
    // HIT CONFIRMATION (fired by WeaponTipHitbox per unique target)
    // ========================================================================

    void OnHitConfirmed(AttackData attack, Transform targetTransform, Vector3 hitPoint)
    {
        if (attack == null || targetTransform == null) return;

        if (attack.hitStopDuration > 0f)
        {
            Animator targetAnim = targetTransform.GetComponentInChildren<Animator>();
            if (targetAnim != null && !IsFrozen(targetAnim))
            {
                frozenAnimators.Add(new FrozenAnimator { animator = targetAnim, originalSpeed = targetAnim.speed });
                targetAnim.speed = 0f;
            }
        }

        // VFX, SFX, and self hit-stop fire only once per attack (on first confirmed hit)
        if (!hitConfirmedThisAttack)
        {
            hitConfirmedThisAttack = true;

            if (hitConnectVfxPrefab != null)
            {
                Quaternion rot = (hitPoint - transform.position).sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(hitPoint - transform.position)
                    : transform.rotation;
                rot = rot * Quaternion.Euler(attack.hitConnectVfxRotationOffset);
                PlayVfx(Object.Instantiate(hitConnectVfxPrefab, hitPoint + attack.hitConnectVfxPositionOffset, rot));
            }

            PlayAttackCues(attack, AttackSfxTriggerType.OnHitConfirm, 0, useLegacyFallback: true);

            if (attack.hitStopDuration > 0f)
            {
                hitStopEndTime = Time.time + attack.hitStopDuration;
                attackEndTime += attack.hitStopDuration; // keep lock timing aligned with frozen attacker animation
                if (animator != null && !IsFrozen(animator))
                {
                    frozenAnimators.Add(new FrozenAnimator { animator = animator, originalSpeed = animator.speed });
                    animator.speed = 0f;
                }
            }
        }
    }

    // ========================================================================
    // SFX ANIMATION EVENT HOOKS
    // ========================================================================

    public void OnAttackSfxEvent(int eventId)
    {
        PlayAttackCues(currentAttack, AttackSfxTriggerType.OnAnimEvent, eventId, useLegacyFallback: false);
    }

    public void OnAttackSfxEvent() { OnAttackSfxEvent(0); }
    public void OnAttackSfxEvent(float eventId) { OnAttackSfxEvent(Mathf.RoundToInt(eventId)); }

    public void OnAttackSfxEvent(string eventId)
    {
        int parsed;
        OnAttackSfxEvent(int.TryParse(eventId, out parsed) ? parsed : 0);
    }

    public void OnAttackSFXEvent()              { OnAttackSfxEvent(0); }
    public void OnAttackSFXEvent(int eventId)   { OnAttackSfxEvent(eventId); }

    // ========================================================================
    // HELPERS
    // ========================================================================

    static void PlayVfx(GameObject instance)
    {
        if (instance == null) return;
        foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            ps.Play();
    }

    void PlayAttackSfxClip(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || sfxSource == null) return;
        float minPitch  = Mathf.Min(sfxPitchMin, sfxPitchMax);
        float maxPitch  = Mathf.Max(sfxPitchMin, sfxPitchMax);
        sfxSource.pitch = Random.Range(minPitch, maxPitch);
        sfxSource.PlayOneShot(clip, Mathf.Max(0f, volumeScale));
    }

    void PlayAttackCues(AttackData attack, AttackSfxTriggerType trigger, int eventId, bool useLegacyFallback)
    {
        if (attack == null) return;

        bool hasCueList = attack.sfxCues != null && attack.sfxCues.Count > 0;
        if (hasCueList)
        {
            for (int i = 0; i < attack.sfxCues.Count; i++)
            {
                AttackSfxCue cue = attack.sfxCues[i];
                if (cue == null || cue.trigger != trigger) continue;
                if (trigger == AttackSfxTriggerType.OnAnimEvent && cue.eventId != eventId) continue;

                AudioClip chosenClip = null;
                if (cue.clips != null && cue.clips.Length > 0)
                    chosenClip = cue.clips[Random.Range(0, cue.clips.Length)];
                if (chosenClip == null) continue;
                PlayAttackSfxClip(chosenClip, cue.volume);
            }
            return;
        }

        if (!useLegacyFallback) return;
        if (trigger == AttackSfxTriggerType.OnAttackStart)
            PlayAttackSfxClip(attack.attackStartSfx);
        else if (trigger == AttackSfxTriggerType.OnHitConfirm)
            PlayAttackSfxClip(attack.hitConnectSfx);
    }

    bool IsFrozen(Animator anim)
    {
        for (int i = 0; i < frozenAnimators.Count; i++)
            if (frozenAnimators[i].animator == anim) return true;
        return false;
    }

    // ========================================================================
    // HIT STOP
    // ========================================================================

    void UpdateHitStop()
    {
        if (frozenAnimators.Count == 0) return;
        if (Time.time < hitStopEndTime) return;

        foreach (var frozen in frozenAnimators)
            if (frozen.animator != null)
                frozen.animator.speed = frozen.originalSpeed;
        frozenAnimators.Clear();
    }

    // ========================================================================
    // STARTUP / RECOVERY SPEED SCALING
    // ========================================================================

    void UpdateAttackStartUpSpeed()
    {
        if (animator == null) return;

        if (Time.time >= attackEndTime)
        {
            if ((currentStartUpLength > 0f || currentRecoveryLength > 0f) && !IsFrozen(animator))
                animator.speed = 1f;
            return;
        }

        if (IsFrozen(animator)) return;

        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);

        if (!string.IsNullOrEmpty(currentAttackStateName) && !info.IsName(currentAttackStateName))
        {
            animator.speed = 1f;
            return;
        }

        bool useStartUp  = currentStartUpLength > 0f && currentStartUpSpeed < 1f;
        bool useRecovery = currentRecoveryLength > 0f && currentRecoverySpeed < 1f;
        if (!useStartUp && !useRecovery) { animator.speed = 1f; return; }

        float nt = info.normalizedTime;

        if (nt >= 1f)
            animator.speed = 1f;
        else if (useStartUp && nt < currentStartUpLength)
            animator.speed = currentStartUpSpeed;
        else if (useRecovery && nt >= (1f - currentRecoveryLength))
            animator.speed = currentRecoverySpeed;
        else
            animator.speed = 1f;
    }

    // ========================================================================
    // LUNGE
    // ========================================================================

    void UpdateLunge()
    {
        if (!lungePending) return;
        if (hitStopEndTime > 0f && Time.time < hitStopEndTime) return;

        if (Time.time >= lungeTriggerTime && Time.time < lungeEndTime)
        {
            float moveAmount = (currentLungeDistance / currentLungeDuration) * Time.deltaTime;
            if (cc != null && cc.enabled)
                cc.Move(lungeDirection * moveAmount);
        }

        if (Time.time >= lungeEndTime)
            lungePending = false;
    }
}
