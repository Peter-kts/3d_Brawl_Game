/*
 * Dash (dodge) system: input, direction resolution, forward suck/stop-past-enemy, movement.
 * Partial of PlayerController; shares cc, animator, combat, threatSystem, GetStickInput.
 */

using UnityEngine;
using UnityEngine.InputSystem;

public enum DashDirectionType { Forward, Back, Left, Right }

public partial class PlayerController
{
    // ========================================================================
    // DASH SETTINGS
    // ========================================================================

    [Header("Dash")]
    [Tooltip("Distance covered by one dash")]
    public float dashDistance = 5f;
    [Tooltip("Duration of the dash in seconds")]
    public float dashDuration = 0.12f;
    [Tooltip("Cooldown before the next dash can be used (seconds)")]
    public float dashCooldown = 0.8f;
    [Tooltip("If an enemy is in this range ahead during dash, you get pulled toward them")]
    public float dashSuckRange = 4f;
    [Tooltip("Half-angle of cone in front (degrees) that triggers dash suck")]
    public float dashSuckConeAngle = 90f;
    [Tooltip("How strongly to curve toward the enemy per second (higher = stronger pull)")]
    public float dashSuckStrength = 8f;

    [Header("Dash - Stop Past Enemy")]
    [Tooltip("Enable stopping at a set distance past the enemy during forward dashes")]
    public bool enableDashStopPastEnemy = true;
    [Tooltip("How far past the enemy the forward dash stops (0 = stop at enemy)")]
    public float dashStopDistancePastEnemy = 1f;

    [Header("Dash - Animation States")]
    [Tooltip("Animator state name for forward dash")]
    public string dashForwardState = "DashForward";
    [Tooltip("Animator state name for back dash")]
    public string dashBackState = "DashBack";
    [Tooltip("Animator state name for left dash")]
    public string dashLeftState = "DashLeft";
    [Tooltip("Animator state name for right dash")]
    public string dashRightState = "DashRight";
    [Tooltip("Crossfade duration when transitioning into dash animation (seconds)")]
    public float dashCrossfadeDuration = 0.1f;

    private float dashEndTime = 0f;             // Time.time when the current dash expires
    private float nextDashTime = 0f;            // Time.time before which a new dash isn't allowed (cooldown)
    private Vector3 dashDirection;              // World-space flat direction the dash travels
    private float dashSpeed;                    // Pre-calculated as dashDistance / dashDuration
    private DashDirectionType dashDirType;      // Forward/Back/Left/Right — resolved from stick input at dash start
    private Transform dashCapTarget;            // Enemy to stop short of during a forward dash; null = no cap
    private float lastDashEndTime = -999f;      // Time.time when the last dash finished; -999 = never dashed (used by RecentlyDodged)
    private bool wasDashing;                    // Previous frame's dash state; used to detect the exact frame the dash ends

    private const int DASH_OVERLAP_SIZE = 24;                               // Buffer size for Physics.OverlapSphereNonAlloc during dash-suck detection
    private Collider[] dashOverlapBuffer = new Collider[DASH_OVERLAP_SIZE]; // Reused every frame to avoid per-frame allocation

    /// <summary>True while the player is in the middle of a dash (dodge).</summary>
    public bool IsDashing => Time.time < dashEndTime;

    /// <summary>True if the player's dodge ended within the last windowSeconds. Used by enemies to choose punish attacks.</summary>
    public bool RecentlyDodged(float windowSeconds) => lastDashEndTime > 0f && (Time.time - lastDashEndTime) <= windowSeconds;

    void TryStartDashFromInput()
    {
        bool dashPressed = (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame) ||
                          (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame);
        if (dashPressed && Time.time >= nextDashTime && Time.time >= dashEndTime && (ActiveCombat == null || !ActiveCombat.IsInAttackLock))
            StartDash();
    }

    void TrackDashEnd()
    {
        bool nowDashing = Time.time < dashEndTime;
        if (wasDashing && !nowDashing)
            lastDashEndTime = Time.time;
        wasDashing = nowDashing;
    }

    void StartDash()
    {
        bool hasTarget = threatSystem != null && threatSystem.HasSoftTarget;
        Vector3 moveDir;
        dashDirType = DashDirectionType.Forward;
        dashCapTarget = null;

        if (hasTarget)
        {
            Vector3 toTarget = threatSystem.SoftTarget.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude > 0.01f)
            {
                toTarget.Normalize();

                Vector2 stick = GetStickInput();
                if (stick.sqrMagnitude >= 0.01f)
                {
                    Camera cam = Camera.main;
                    if (cam != null)
                    {
                        GetFlatCameraAxes(cam, out Vector3 camForward, out Vector3 camRight);
                        Vector3 worldMove = (camForward * stick.y + camRight * stick.x).normalized;
                        Vector3 localMove = transform.InverseTransformDirection(worldMove);
                        dashDirType = ClassifyLocalDirection(localMove);
                    }
                }

                switch (dashDirType)
                {
                    case DashDirectionType.Forward:
                        moveDir = toTarget;
                        dashCapTarget = threatSystem.SoftTarget;
                        break;
                    case DashDirectionType.Back:
                        moveDir = -toTarget;
                        break;
                    case DashDirectionType.Left:
                        moveDir = -transform.right;
                        moveDir.y = 0f; moveDir.Normalize();
                        break;
                    case DashDirectionType.Right:
                        moveDir = transform.right;
                        moveDir.y = 0f; moveDir.Normalize();
                        break;
                    default:
                        moveDir = toTarget;
                        break;
                }
            }
            else
            {
                moveDir = transform.forward;
                moveDir.y = 0f;
                if (moveDir.sqrMagnitude < 0.01f) moveDir = Vector3.forward;
                else moveDir.Normalize();
            }
        }
        else
        {
            Vector2 stick = GetStickInput();
            if (stick.sqrMagnitude >= 0.01f)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    GetFlatCameraAxes(cam, out Vector3 camForward, out Vector3 camRight);
                    moveDir = (camForward * stick.y + camRight * stick.x).normalized;
                }
                else
                {
                    moveDir = transform.forward;
                    moveDir.y = 0f;
                    if (moveDir.sqrMagnitude < 0.01f) moveDir = Vector3.forward;
                    else moveDir.Normalize();
                }
                Vector3 localMove = transform.InverseTransformDirection(moveDir);
                dashDirType = ClassifyLocalDirection(localMove);
            }
            else
            {
                moveDir = transform.forward;
                moveDir.y = 0f;
                if (moveDir.sqrMagnitude < 0.01f) moveDir = Vector3.forward;
                else moveDir.Normalize();
            }
        }

        dashDirection = moveDir;
        dashSpeed = dashDuration > 0f ? dashDistance / dashDuration : 0f;
        dashEndTime = Time.time + dashDuration;
        nextDashTime = Time.time + dashCooldown;

        if (animator != null)
        {
            string state = null;
            switch (dashDirType)
            {
                case DashDirectionType.Forward: state = dashForwardState; break;
                case DashDirectionType.Back:    state = dashBackState;    break;
                case DashDirectionType.Left:    state = dashLeftState;    break;
                case DashDirectionType.Right:   state = dashRightState;   break;
            }
            if (!string.IsNullOrEmpty(state))
            {
                if (dashCrossfadeDuration > 0f)
                    animator.CrossFadeInFixedTime(state, dashCrossfadeDuration, 0, 0f);
                else
                    animator.Play(state, 0, 0f);
            }
        }
    }

    /// <summary>Returns nearest enemy in a cone ahead (dash direction). Used for dash suck.</summary>
    Transform GetNearestEnemyInDashCone(float range, float coneHalfAngle)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, range, dashOverlapBuffer, -1, QueryTriggerInteraction.Ignore);
        Transform nearest = null;
        float nearestDist = range + 1f;
        Vector3 forward = dashDirection;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.01f) return null;
        forward.Normalize();
        for (int i = 0; i < hitCount; i++)
        {
            if (dashOverlapBuffer[i] == null) continue;
            var enemyHealth = dashOverlapBuffer[i].GetComponent<EnemyHealth>();
            if (enemyHealth == null) continue;
            Vector3 toEnemy = enemyHealth.transform.position - transform.position;
            toEnemy.y = 0f;
            float dist = toEnemy.magnitude;
            if (dist < 0.01f) continue;
            float angle = Vector3.Angle(forward, toEnemy / dist);
            if (angle > coneHalfAngle) continue;
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = enemyHealth.transform;
            }
        }
        return nearest;
    }

    void ApplyDashMovement()
    {
        if (dashDirType == DashDirectionType.Forward)
            ApplyForwardDashStep();
        else
            cc.Move(dashDirection * dashSpeed * Time.deltaTime);
    }

    void ApplyForwardDashStep()
    {
        // Only search for a suck target on forward dashes toward the soft-lock target; side/back dashes never pull
        Transform suckTarget = (dashCapTarget != null) ? GetNearestEnemyInDashCone(dashSuckRange, dashSuckConeAngle) : null;
        if (suckTarget != null)
        {
            Vector3 toEnemy = suckTarget.position - transform.position;
            toEnemy.y = 0f;
            if (toEnemy.sqrMagnitude > 0.01f)
            {
                toEnemy.Normalize();
                dashDirection = Vector3.Slerp(dashDirection, toEnemy, dashSuckStrength * Time.deltaTime).normalized;
                transform.rotation = Quaternion.LookRotation(toEnemy, Vector3.up);
            }
        }

        Transform capTarget = dashCapTarget != null ? dashCapTarget : suckTarget;
        if (enableDashStopPastEnemy && capTarget != null)
        {
            Vector3 capPoint = capTarget.position;
            capPoint.y = transform.position.y;
            capPoint += dashDirection * dashStopDistancePastEnemy;

            Vector3 desiredPos = transform.position + dashDirection * dashSpeed * Time.deltaTime;
            desiredPos.y = transform.position.y;

            // Positive dot product means desiredPos is further along dashDirection than capPoint — we've overshot, so clamp
            if (Vector3.Dot(desiredPos - capPoint, dashDirection) > 0f)
            {
                Vector3 clampMove = capPoint - transform.position;
                clampMove.y = 0f;
                if (Vector3.Dot(clampMove, dashDirection) > 0f) // Only move if cap is still ahead (not already past)
                    cc.Move(clampMove);
                dashEndTime = Time.time; // End the dash immediately so the player stops here
            }
            else
            {
                cc.Move(dashDirection * dashSpeed * Time.deltaTime);
            }
        }
        else
        {
            cc.Move(dashDirection * dashSpeed * Time.deltaTime);
        }
    }

    /// <summary>Maps a local-space move vector to a DashDirectionType based on which axis dominates.</summary>
    static DashDirectionType ClassifyLocalDirection(Vector3 localMove)
    {
        if (Mathf.Abs(localMove.x) > Mathf.Abs(localMove.z))
            return localMove.x < 0f ? DashDirectionType.Left : DashDirectionType.Right;
        return localMove.z >= 0f ? DashDirectionType.Forward : DashDirectionType.Back;
    }
}
