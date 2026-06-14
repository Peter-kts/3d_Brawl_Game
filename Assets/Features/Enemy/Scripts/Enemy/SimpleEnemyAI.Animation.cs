/*
 * SimpleEnemyAI.Animation.cs — per-frame animator parameter driving.
 *
 * Lives in a partial of SimpleEnemyAI so it shares animator, animationConfig, health, etc.
 * Owns the smoothed locomotion Speed parameter, visual arbitration between wall-bounce /
 * knockback-entry / stun channels, and knockback-stun facing.
 * Event-driven reaction triggers (hit/thrown/death/...) live in SimpleEnemyAI.Reactions.cs.
 */

using UnityEngine;

public partial class SimpleEnemyAI
{
    // ========================================================================
    // ANIMATION STATE (smoothing + visual arbitration latches)
    // ========================================================================

    /// <summary>Smoothed value sent to the Animator each frame. Lerps toward targetAnimSpeed so transitions aren't instant.</summary>
    private float currentAnimSpeed;
    private string currentReactionDebug = "None"; // Debug-only visual reaction state (None/WallBounce/KnockbackEntry)
    // Wall-bounce latch:
    // - Set true when OnWallBounce fires
    // - While true, stun animator params are forced off so wall-bounce owns presentation
    // - Cleared by re-hit or wall-bounce completion callback
    private bool wallBounceStunLatched;
    // After wall-bounce, keep standing/knockback stun params suppressed until a new hit arrives.
    // This prevents old standing-stun timer state from re-activating those params at wall-bounce end.
    private bool suppressStandingStunParamsUntilNextHit;
    private bool wasInKnockbackStun;              // Tracks entry into knockback-stun mode for direction seeding
    private Vector3 lastKnockbackFacingDir = Vector3.forward; // Last valid horizontal knockback direction used for stable facing as velocity decays

    public string CurrentReactionDebug => currentReactionDebug;

    // ========================================================================
    // ANIMATION
    // ========================================================================
    
    /*
     * UpdateAnimator - Drives the Animator's "Speed" parameter from AI behavior.
     *
     * WHAT THIS DOES:
     * Behaviors set TargetAnimSpeed each frame (0 = idle, 0.5 = circle, 1 = run). We don't pass that
     * value straight to the Animator; we smooth it into currentAnimSpeed and send that. The Animator
     * Controller (e.g. enemy.controller) uses the Speed parameter in transition conditions (e.g.
     * "Speed > 0.1" to leave Idle, "Speed < 0.1" to go to Idle). So this code only updates a parameter;
     * the actual state machine and transitions are defined in the Animator Controller asset.
     *
     * SMOOTHING:
     * Exponential lerp (1 - Exp(-k*dt)) gives framerate-independent decay toward target. Without it,
     * Speed would snap from 1 to 0 when the enemy stops, causing a visible pop. Snapping to 0 when
     * both current and target are near zero avoids floating-point drift (Speed never quite reaching 0).
     */
    void UpdateAnimator()
    {
        if (animator == null) return;

        // Lerp current toward target: smooth, framerate-independent (same formula as PlayerController).
        float t = PlayerController.ExponentialBlendFactor(animationDamping);
        currentAnimSpeed = Mathf.Lerp(currentAnimSpeed, targetAnimSpeed, t);

        // Avoid drift: when we're aiming for 0 and very close, clamp to exactly 0.
        if (currentAnimSpeed < 0.001f && targetAnimSpeed == 0f)
            currentAnimSpeed = 0f;

        if (animationConfig == null) return;

        // Visual arbitration overview:
        // 1) Wall-bounce uses a latch (not a timer) while bounce owns presentation.
        // 2) Knockback-entry uses a short priority window from reactionStateMachine.
        // 3) Debug text reports which visual channel currently has priority.
        bool wallBounceActive = wallBounceStunLatched;
        bool knockbackEntryActive = reactionStateMachine != null &&
                                    reactionStateMachine.IsKnockbackEntryActive(
                                        Time.time,
                                        animator,
                                        animationConfig.standingStunLayer,
                                        animationConfig.knockbackStunEntryStateName);
        currentReactionDebug = wallBounceActive ? "WallBounce" : (knockbackEntryActive ? "KnockbackEntry" : "None");
        if (!string.IsNullOrEmpty(animationConfig.wallBounceActiveParameter))
            animator.SetBool(animationConfig.wallBounceActiveParameter, wallBounceActive);

        // Send to Animator. During prone/airborne, zero locomotion speed so enemy doesn't walk.
        bool inAirbornePhase = airborneSequence != null && airborneSequence.CurrentPhase != AirborneSequence.Phase.None;
        bool inProne = proneSystem != null && proneSystem.IsInProne;
        float speedToApply = (inProne || inAirbornePhase) ? 0f : currentAnimSpeed;
        animator.SetFloat(animationConfig.speedParameter, speedToApply);

        // Airborne + crash: play at 1x (or speed-multiplied during crash relaunch). Prone plays at 1x via its own state.
        if (CurrentState == EnemyState.Airborne || CurrentState == EnemyState.Crashed)
            animator.speed = 1f * (health != null ? health.GetAirborneSpeedMultiplier() : 1f);

        // Drive Animator booleans and reset hit speed when not in special states
        if (health != null)
        {
            // IsAirborne: from AirborneSequence when configured, else from Health
            animator.SetBool(animationConfig.airborneParameter, airborneSequence != null ? airborneSequence.GetAirborneForAnimator(health) : health.IsAirborne);

            // StunLayerActive: hitstun (and not airborne), prone, or get-up all activate the Stun animator layer
            if (!string.IsNullOrEmpty(animationConfig.stunLayerParameter))
                animator.SetBool(animationConfig.stunLayerParameter, ((!inAirbornePhase && !health.IsAirborne && health.IsHitstunned) || inProne || health.IsGettingUp) && !wallBounceActive);

            // When not in stun/airborne/prone/get-up, ensure hit layer plays at 1x (TriggerHitAnimation sets it when hit)
            if (!inAirbornePhase && !health.IsHitstunned && !inProne && !health.IsGettingUp && !string.IsNullOrEmpty(animationConfig.hitSpeedParameter))
                animator.SetFloat(animationConfig.hitSpeedParameter, 1f);

            // StandingStunned: stun meter filled — both paths (normal and knockback) are fully parameter-driven
            if (stunMeter != null)
            {
                bool canShowStandingStunParams = !wallBounceActive && !suppressStandingStunParamsUntilNextHit;
                if (!string.IsNullOrEmpty(animationConfig.standingStunParameter))
                    animator.SetBool(animationConfig.standingStunParameter, canShowStandingStunParams && stunMeter.IsStandingStunned);
                if (!string.IsNullOrEmpty(animationConfig.knockbackStunParameter))
                    animator.SetBool(animationConfig.knockbackStunParameter, canShowStandingStunParams && stunMeter.TriggerWasHeavy && stunMeter.IsStandingStunned);
            }
        }
    }

    // Keep knockback-stun entry/loop oriented toward movement direction so the reaction reads consistently
    // from all impact angles (including reflected wall bounces).
    void UpdateKnockbackStunFacing()
    {
        if (health == null) return;
        // Rotation ownership rule:
        // while prone, EnemyProneSystem locks yaw every frame to the chosen prone orientation.
        // If knockback-facing also ran here, the two systems would fight and cause one-frame snaps.
        if (proneSystem != null && proneSystem.IsInProne)
        {
            wasInKnockbackStun = false;
            return;
        }

        bool inKnockbackStun = !wallBounceStunLatched
            && stunMeter != null
            && stunMeter.IsStandingStunned
            && stunMeter.TriggerWasHeavy;

        if (!inKnockbackStun)
        {
            wasInKnockbackStun = false;
            return;
        }

        Vector3 horizontalVel = health.KnockbackVelocity;
        horizontalVel.y = 0f;

        if (horizontalVel.sqrMagnitude > 0.0004f)
            lastKnockbackFacingDir = horizontalVel.normalized;
        else if (!wasInKnockbackStun)
        {
            Vector3 fallback = transform.forward;
            fallback.y = 0f;
            if (fallback.sqrMagnitude > 0.0001f)
                lastKnockbackFacingDir = fallback.normalized;
        }

        if (lastKnockbackFacingDir.sqrMagnitude > 0.0001f)
        {
            Vector3 facingDir = invertKnockbackStunFacing ? -lastKnockbackFacingDir : lastKnockbackFacingDir;
            Quaternion targetRot = Quaternion.LookRotation(facingDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRot,
                knockbackStunFacingTurnSpeed * Time.deltaTime);
        }

        wasInKnockbackStun = true;
    }

    /// <summary>
    /// Inject a world-space knockback facing direction (used by wall-bounce reflection).
    /// Keeps knockback-stun orientation coherent when velocity direction flips on impact.
    /// </summary>
    public void SetKnockbackFacingDirection(Vector3 worldDirection, bool snap = false)
    {
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude <= 0.0001f) return;
        lastKnockbackFacingDir = worldDirection.normalized;
        wasInKnockbackStun = true;

        if (!snap) return;
        Vector3 facingDir = invertKnockbackStunFacing ? -lastKnockbackFacingDir : lastKnockbackFacingDir;
        transform.rotation = Quaternion.LookRotation(facingDir, Vector3.up);
    }
}
