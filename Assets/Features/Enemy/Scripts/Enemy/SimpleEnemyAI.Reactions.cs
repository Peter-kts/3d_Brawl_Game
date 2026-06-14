/*
 * SimpleEnemyAI.Reactions.cs — event-driven animation triggers.
 *
 * Lives in a partial of SimpleEnemyAI so it shares animator, animationConfig, health, etc.
 * Hit / thrown / get-up / wall-bounce / death reactions, called by EnemyHealth,
 * Combat (throw), EnemyProneSystem, and Animation Events.
 * OnWallBounceAnimationComplete / OnDeathAnimationComplete are Animation Event
 * callbacks (see AnimationEventMethodCatalog) — do not rename.
 * Per-frame animator parameter driving lives in SimpleEnemyAI.Animation.cs.
 */

using System.Collections.Generic;
using UnityEngine;

public partial class SimpleEnemyAI
{
    private int lastHitStateIndex = -1;  // When using hitStateNames[], avoid playing same state twice in a row
    private Dictionary<string, float> cachedHitStateDurations;  // Clip length per hit state name; avoids GetCurrentAnimatorStateInfo every hit

    /// <summary>
    /// Triggers the hit reaction animation. Called by EnemyHealth when taking damage.
    /// Prefers height-specific states (Hit_High / Hit_Mid / Hit_Low), then falls back to hitStateName or random hitStateNames.
    /// The Stun layer must NOT have an Any State transition for the stun bool, or that transition would override this and always show one state.
    /// </summary>
    /// <param name="hitstun">Duration of the hitstun; animation is scaled to match.</param>
    /// <param name="heavyKnockback">True when the hit's knockback magnitude exceeded the stun threshold. Routes prone hits to KnockbackStunEntry instead of the normal prone hit state.</param>
    public void TriggerHitAnimation(float hitstun, AttackHeight height, bool heavyKnockback = false)
    {
        if (animator == null || animationConfig == null) return;
        // A fresh hit is the only thing that re-arms standing/knockback stun params after wall-bounce.
        suppressStandingStunParamsUntilNextHit = false;
        if (wallBounceStunLatched)
        {
            // Re-hit always wins: any new hit reaction cancels wall-bounce latch immediately.
            wallBounceStunLatched = false;
        }

        // If already in standing stun and hit was a heavy knockback, play KnockbackStunEntry directly.
        // TryRetriggerAsKnockback (called in EnemyHealth.TakeHit) already reset the phase + TriggerWasHeavy.
        if (stunMeter != null && stunMeter.IsStandingStunned)
        {
            if (stunMeter.TriggerWasHeavy
                && !string.IsNullOrEmpty(animationConfig.knockbackStunEntryStateName))
            {
                reactionStateMachine?.RequestKnockbackEntry(Time.time, animationConfig.knockbackStunEntryPriorityGrace);
                if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                    animator.SetBool(animationConfig.stunLayerParameter, true);
                animator.Play(animationConfig.knockbackStunEntryStateName, animationConfig.standingStunLayer, 0f);
            }
            // Normal hits while stunned — no animation change, let the stun continue uninterrupted
            return;
        }

        // If the enemy is prone, route to the appropriate hit state.
        bool currentlyProne = proneSystem != null && proneSystem.IsInProne;
        if (currentlyProne)
        {
            // Heavy knockback while prone — play the knockback stun entry (same state as standing stun knockback)
            if (heavyKnockback
                && !string.IsNullOrEmpty(animationConfig.knockbackStunEntryStateName)
                && AnimatorHasStateOnLayer(animator, animationConfig.standingStunLayer, animationConfig.knockbackStunEntryStateName))
            {
                reactionStateMachine?.RequestKnockbackEntry(Time.time, animationConfig.knockbackStunEntryPriorityGrace);
                if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                    animator.SetBool(animationConfig.stunLayerParameter, true);
                animator.Play(animationConfig.knockbackStunEntryStateName, animationConfig.standingStunLayer, 0f);
                return;
            }
            // Normal hit while prone — play prone-specific hit state if configured
            if (!string.IsNullOrEmpty(animationConfig.proneHitStateName)
                && AnimatorHasStateOnLayer(animator, animationConfig.hitAnimationLayer, animationConfig.proneHitStateName))
            {
                animator.Play(animationConfig.proneHitStateName, animationConfig.hitAnimationLayer, 0f);
                return;
            }
        }

        // Keep knockback-stun entry visual priority over normal hit reactions.
        if (reactionStateMachine != null
            && reactionStateMachine.IsKnockbackEntryActive(
                Time.time,
                animator,
                animationConfig.standingStunLayer,
                animationConfig.knockbackStunEntryStateName))
            return;

        // Prefer height-only state first (Hit_High / Hit_Mid / Hit_Low); fallback to existing random/single setup.
        string stateToPlay;
        string typedState = $"Hit_{height}";
        if (AnimatorHasStateOnLayer(animator, animationConfig.hitAnimationLayer, typedState))
        {
            stateToPlay = typedState;
            lastHitStateIndex = -1;
        }
        else if (animationConfig.hitStateNames != null && animationConfig.hitStateNames.Length > 0)
        {
            int chosenIndex;
            do
            {
                chosenIndex = Random.Range(0, animationConfig.hitStateNames.Length);
            }
            while (animationConfig.hitStateNames.Length >= 2 && chosenIndex == lastHitStateIndex);
            lastHitStateIndex = chosenIndex;
            stateToPlay = animationConfig.hitStateNames[chosenIndex];
        }
        else
        {
            lastHitStateIndex = -1;
            stateToPlay = animationConfig.hitStateName;
        }

        // Set stun bool first so the Stun layer doesn't immediately transition out of Stunned (UpdateAnimator sets it next frame; we need it true now).
        if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
            animator.SetBool(animationConfig.stunLayerParameter, true);
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
        animator.Play(stateToPlay, animationConfig.hitAnimationLayer, 0f);
        animator.Update(0f);  // One frame so GetCurrentAnimatorStateInfo below returns this state

        // Use cached duration when available to avoid GetCurrentAnimatorStateInfo every hit.
        float baseDuration = baseHitAnimDuration;
        if (cachedHitStateDurations != null && cachedHitStateDurations.TryGetValue(stateToPlay, out float cached))
            baseDuration = cached;
        else
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(animationConfig.hitAnimationLayer);
            if (stateInfo.IsName(stateToPlay) && stateInfo.length > 0f)
            {
                if (cachedHitStateDurations == null)
                    cachedHitStateDurations = new Dictionary<string, float>();
                cachedHitStateDurations[stateToPlay] = stateInfo.length;
                baseDuration = stateInfo.length;
            }
        }

        // Scale hit layer speed so clip finishes in hitstun seconds: speed = baseDuration / hitstun
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter) && baseDuration > 0f)
        {
            float speed = (hitstun > 0.001f) ? (baseDuration / hitstun) : 1f;
            animator.SetFloat(animationConfig.hitSpeedParameter, speed);
        }
        else if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
    }

    bool AnimatorHasStateOnLayer(Animator targetAnimator, int layerIndex, string stateName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(stateName)) return false;
        if (layerIndex < 0 || layerIndex >= targetAnimator.layerCount) return false;

        int shortNameHash = Animator.StringToHash(stateName);
        if (targetAnimator.HasState(layerIndex, shortNameHash)) return true;

        string fullPath = targetAnimator.GetLayerName(layerIndex) + "." + stateName;
        int fullPathHash = Animator.StringToHash(fullPath);
        return targetAnimator.HasState(layerIndex, fullPathHash);
    }

    /// <summary>
    /// Plays the thrown/grabbed state for the given duration (scaled to match). Used for synced throw.
    /// </summary>
    public void TriggerThrownAnimation(float duration, string stateName)
    {
        if (animator == null || animationConfig == null || string.IsNullOrEmpty(stateName)) return;
        if (health != null && health.IsDying) return;

        // Cancel any active airborne/prone/knockback state so the throw takes full control.
        if (airborneSequence != null) airborneSequence.ForceCancel();
        if (proneSystem != null && proneSystem.IsInProne) proneSystem.ForceCancel();
        if (health != null) health.ClearAirborneAndHitstun();
        wasInKnockbackStun = false;

        // Stop movement so the enemy doesn't slide during the throw.
        velocity = Vector3.zero;
        if (cc != null) cc.enabled = false;

        animator.speed = 1.2f;

        // Force all layers to a clean state so residual animations don't bleed through,
        // but skip Rebind() to preserve current parameter values (bools, floats, triggers).
        for (int i = 0; i < animator.layerCount; i++)
        {
            AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(i);
            animator.Play(info.fullPathHash, i, 0f);
        }
        animator.Update(0f);

        if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
            animator.SetBool(animationConfig.stunLayerParameter, true);
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
        animator.Play(stateName, animationConfig.hitAnimationLayer, 0f);
        animator.Update(0f);

        // Face the player after animation reset so AI rotation or hit-reactions can't override it.
        if (player != null)
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toPlayer.normalized);
        }
        float baseDuration = baseHitAnimDuration;
        if (cachedHitStateDurations != null && cachedHitStateDurations.TryGetValue(stateName, out float cached))
            baseDuration = cached;
        else
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(animationConfig.hitAnimationLayer);
            if (stateInfo.IsName(stateName) && stateInfo.length > 0f)
            {
                if (cachedHitStateDurations == null)
                    cachedHitStateDurations = new Dictionary<string, float>();
                cachedHitStateDurations[stateName] = stateInfo.length;
                baseDuration = stateInfo.length;
            }
        }
        if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter) && baseDuration > 0f && duration > 0.001f)
            animator.SetFloat(animationConfig.hitSpeedParameter, baseDuration / duration);
        else if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, 1f);
    }

    /// <summary>
    /// Called by EnemyProneSystem when the prone timer expires. Plays the get-up animation and starts the get-up stun.
    /// Also resets the airborne speed multiplier (OnAirborneSequenceEnded).
    /// </summary>
    void TriggerGetUpSequence()
    {
        if (health == null || animator == null || animationConfig == null) return;
        health.StartGetUp(getUpDuration);
        health.OnAirborneSequenceEnded();
        if (!string.IsNullOrEmpty(animationConfig.getUpStateName))
        {
            animator.speed = 1f;
            if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                animator.SetBool(animationConfig.stunLayerParameter, true);
            if (!string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            {
                float speed = (baseGetUpAnimDuration > 0f && getUpDuration > 0.001f)
                    ? baseGetUpAnimDuration / getUpDuration
                    : 1f;
                animator.SetFloat(animationConfig.hitSpeedParameter, speed);
            }
            animator.Play(animationConfig.getUpStateName, animationConfig.getUpLayer, 0f);
        }
    }

    /// <summary>
    /// Start get-up after a throw release (no launch). Plays get-up state and sets get-up stun so the enemy stands up.
    /// </summary>
    public void TriggerGetUpFromThrow()
    {
        if (health == null || animator == null || animationConfig == null || string.IsNullOrEmpty(animationConfig.getUpStateName)) return;
        health.StartGetUp(getUpDuration);
        if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
            animator.SetBool(animationConfig.stunLayerParameter, true);
        animator.Play(animationConfig.getUpStateName, animationConfig.getUpLayer, 0f);
        if (baseGetUpAnimDuration > 0f && getUpDuration > 0.001f && !string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
            animator.SetFloat(animationConfig.hitSpeedParameter, baseGetUpAnimDuration / getUpDuration);
    }
    
    /// <summary>
    /// Plays the wall bounce animation when the enemy hits a wall at high velocity during knockback.
    /// Called by EnemyHealth.OnWallBounce(). kbVel is already reflected before this fires.
    /// </summary>
    public void TriggerWallBounce()
    {
        if (animator == null || animationConfig == null) return;
        if (string.IsNullOrEmpty(animationConfig.wallBounceTriggerParameter)) return;
        wallBounceStunLatched = true;
        suppressStandingStunParamsUntilNextHit = true;
        if (!string.IsNullOrEmpty(animationConfig.wallBounceActiveParameter))
            animator.SetBool(animationConfig.wallBounceActiveParameter, true);
        animator.SetTrigger(animationConfig.wallBounceTriggerParameter);
    }

    /// <summary>
    /// Called at the end of the WallBounce animation (Animation Event).
    /// If the enemy is still hitstunned, hand off to prone; otherwise just clear wall-bounce mode.
    /// This keeps re-hits interruptible while still allowing "wall bounce -> prone" when stun outlasts the clip.
    /// </summary>
    public void OnWallBounceAnimationComplete()
    {
        wallBounceStunLatched = false;
        if (health == null || proneSystem == null) return;
        bool hasAnyStun = health.IsHitstunned || (stunMeter != null && stunMeter.IsStunned);
        if (!hasAnyStun) return;
        if (proneSystem.IsInProne) return;
        proneSystem.Enter(
            groundedDuration,
            invertFacing: invertWallBounceProneFacing,
            proneVariant: wallBounceProneVariant,
            preserveCurrentFacing: true);
    }

    /// <summary>
    /// Triggers the death animation. Called by EnemyHealth when HP reaches 0.
    /// The actual disable/hide happens when OnDeathAnimationComplete() is invoked by an Animation Event on the death clip.
    /// </summary>
    public void TriggerDeathAnimation(bool isKnockbackDeath = false)
    {
        if (animator == null || animationConfig == null) return;

        // If the killing hit had high knockback and a dedicated state is configured, play that instead.
        if (isKnockbackDeath
            && !string.IsNullOrEmpty(animationConfig.knockbackDeathStateName)
            && AnimatorHasStateOnLayer(animator, animationConfig.knockbackDeathLayer, animationConfig.knockbackDeathStateName))
        {
            animator.Play(animationConfig.knockbackDeathStateName, animationConfig.knockbackDeathLayer, 0f);
            return;
        }

        animator.SetTrigger(animationConfig.deathTriggerParameter);
    }
    
    /// <summary>
    /// Called by Animation Event at the end of the death animation.
    /// Delegates to health.CompleteDeath() (or deactivates GameObject if no health); corpse can stay visible.
    /// </summary>
    public void OnDeathAnimationComplete()
    {
        // Death should settle into prone and stay there permanently.
        EnterPermanentProneForDeath();

        if (health != null)
            health.CompleteDeath();
        else
            gameObject.SetActive(false);
    }

    /// <summary>
    /// Enter prone with an effectively infinite duration so death remains on the floor.
    /// Safe to call multiple times.
    /// </summary>
    public void EnterPermanentProneForDeath()
    {
        if (proneSystem == null) return;
        proneSystem.Enter(float.PositiveInfinity);
    }
}
