/*
 * SimpleEnemyAI.Movement.cs — behavior tick/transitions, steering, rotation, gravity.
 *
 * Lives in a partial of SimpleEnemyAI so it shares cc, player, personality, stateMachine, etc.
 * HandleMovement gates on CanAct, then EvaluateBehaviorTransition decides Chase vs Standoff
 * and stateMachine.Tick() runs the active behavior. Behaviors call back into
 * GetSeparationSteering / RotateTowardWithDelay / TargetAnimSpeed from here.
 */

using UnityEngine;

public partial class SimpleEnemyAI
{
    private Vector3 velocity;         // Vertical only; used in ApplyGravity then cc.Move(velocity * dt)

    /// <summary>Desired speed for this frame: 0 = idle, 0.5 = circling/strafe, 1 = full run. Set by current behavior in HandleMovement; reset to 0 at start of FixedUpdate.</summary>
    private float targetAnimSpeed;

    private float faceTurnDelayTimer;
    private bool isFaceTurnReady = true;
    private Vector3 lastFaceDirection = Vector3.forward;

    /// <summary>
    /// Target animation speed this frame. Set by behaviors (ChaseBehavior = 1, StandoffBehavior = 0.5 or 0).
    /// Values: 0 = idle, 0.5 = strafing/circling, 1 = full run.
    /// UpdateAnimator() lerps currentAnimSpeed toward this and passes it to the Animator's Speed parameter;
    /// the Animator Controller uses that parameter in transition conditions (e.g. Speed &gt; 0.1 to leave Idle).
    /// </summary>
    public float TargetAnimSpeed { get => targetAnimSpeed; set => targetAnimSpeed = value; }

    // ========================================================================
    // MOVEMENT
    // ========================================================================
    
    void HandleMovement()
    {
        if (player == null) return;
        if (!cc.enabled) return;

        // Block everything when not in Normal state (hitstun, airborne, crash, prone, get-up, dying).
        // CanAct reads CurrentState which is the priority-ordered interrupt check — one gate for all behavior.
        if (!CanAct) return;

        EvaluateBehaviorTransition(); // decide what state to be in
        stateMachine.Tick();          // run it
    }

    /*
     * ApplySeparation — enforce a hard minimum distance between enemies.
     *
     * When two enemies breach minimumSpacing, instantly displace this enemy
     * outward to exactly that distance. No continuous force — nothing happens
     * until the floor is breached, so it's invisible during normal play.
     * Runs every frame regardless of combat state.
     */
    /// <summary>
    /// Returns a steering vector that pushes away from nearby enemies.
    /// Strength falls off linearly: full at overlap, zero at separationRadius.
    /// Behaviors blend this into their moveDir before calling cc.Move.
    /// </summary>
    public Vector3 GetSeparationSteering()
    {
        float radius = personality != null ? personality.separationRadius : 2f;
        if (radius <= 0f) return Vector3.zero;

        Vector3 steer = Vector3.zero;
        Collider[] nearby = Physics.OverlapSphere(transform.position, radius);
        foreach (Collider col in nearby)
        {
            if (col.gameObject == gameObject) continue;
            if (col.GetComponent<SimpleEnemyAI>() == null) continue;

            Vector3 away = transform.position - col.transform.position;
            away.y = 0f;
            float dist = away.magnitude;

            if (dist < 0.001f)
                away = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized;
            else
                away = (away / dist) * (1f - dist / radius); // stronger when closer, 0 at boundary

            steer += away;
        }
        return steer;
    }

    // -------------------------------------------------------------------------
    // THE CENTRAL DECISION POINT
    // -------------------------------------------------------------------------
    // All Chase <-> Standoff transitions are decided here and ONLY here.
    // To add a new behavioral state (e.g. Flee, Search), write its condition
    // below and call stateMachine.Transition(yourBehavior).
    void EvaluateBehaviorTransition()
    {
        // Hold current behavior until the attack animation finishes —
        // switching mid-swing would cut off the hit and look wrong.
        if (EnemyCombat != null && EnemyCombat.IsAttacking) return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f; // horizontal distance only; ignore height
        float dist = toPlayer.magnitude;

        // Hysteresis band prevents flickering at the boundary:
        //   Chase → Standoff at standoffEnterRange (e.g. 4 units)
        //   Standoff → Chase at standoffExitRange  (e.g. 6 units)
        //   Between 4–6: stay in whatever is already active
        if (stateMachine.CurrentBehavior == chaseBehavior && dist <= StandoffEnterRange)
            stateMachine.Transition(standoffBehavior);
        else if (stateMachine.CurrentBehavior == standoffBehavior && dist > StandoffExitRange)
            stateMachine.Transition(chaseBehavior);
    }

    /// <summary>
    /// Rotates toward the player with a short randomized "reaction" delay.
    /// Call this each frame where facing should happen.
    /// </summary>
    public void RotateTowardWithDelay(Vector3 directionToTarget, float speedMultiplier = 1f)
    {
        directionToTarget.y = 0f;
        if (directionToTarget.sqrMagnitude <= 0.001f) return;

        Vector3 normalizedDirection = directionToTarget.normalized;
        float delayMin = Mathf.Max(0f, faceTurnDelayMin);
        float delayMax = Mathf.Max(delayMin, faceTurnDelayMax);

        // If the facing target changed meaningfully, start a new short reaction delay.
        if (Vector3.Dot(lastFaceDirection, normalizedDirection) < 0.995f)
        {
            if (delayMax <= 0f)
            {
                isFaceTurnReady = true;
                faceTurnDelayTimer = 0f;
            }
            else
            {
                isFaceTurnReady = false;
                faceTurnDelayTimer = Random.Range(delayMin, delayMax);
            }
            lastFaceDirection = normalizedDirection;
        }

        if (!isFaceTurnReady)
        {
            faceTurnDelayTimer -= Time.deltaTime;
            if (faceTurnDelayTimer > 0f) return;
            isFaceTurnReady = true;
        }

        Quaternion targetRot = Quaternion.LookRotation(normalizedDirection, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            rotationSpeed * Mathf.Max(0f, speedMultiplier) * Time.deltaTime
        );
    }
    
    // ========================================================================
    // GRAVITY
    // ========================================================================
    
    /*
     * ApplyGravity:
     * 
     * CharacterController doesn't have built-in gravity
     * We simulate it manually with basic kinematics
     * 
     * This keeps enemies grounded and prevents floating
     * 
     * AIRBORNE STATE:
     * When IsAirborne is true (enemy was launched by an attack),
     * gravity is reduced to let the enemy float longer.
     * This enables "juggle" combos where you can hit enemies in the air.
     */
    /// <summary>Seed vertical velocity so gravity doesn't start from zero (e.g. after throw release).</summary>
    public void KickDownwardVelocity(float speed = -5f) { velocity.y = speed; }

    void ApplyGravity()
    {
        if (!cc.enabled) return;
        // Check if we're airborne (launched by an attack)
        bool isAirborne = health != null && health.IsAirborne;

        // On ground and not airborne: stick to floor with a small downward velocity
        if (cc.isGrounded && velocity.y < 0f && !isAirborne)
        {
            // Small negative value keeps us "pressed" to ground
            // Only reset if NOT airborne (otherwise we'd cancel the launch)
            velocity.y = -2f;
        }

        /*
         * Apply gravity:
         *
         * Normal: Full gravity pulls enemy down
         * Airborne: Reduced gravity (30%) lets enemy float for combos
         *
         * The reduced gravity while airborne creates that "floaty"
         * feeling when enemies are launched, perfect for juggle combos
         */
        float gravityMultiplier = isAirborne ? 0.3f : 1f;
        velocity.y += gravity * gravityMultiplier * Time.deltaTime;

        // CharacterController movement: vertical component only (horizontal is from behaviors via CC)
        cc.Move(velocity * Time.deltaTime);
    }
}
