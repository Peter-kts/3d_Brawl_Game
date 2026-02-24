/*
 * ============================================================================
 * PLAYERCONTROLLER.CS - Ninja Gaiden style: camera-relative movement + soft lock
 * ============================================================================
 * 
 * MOVEMENT:
 * ---------
 * - Camera-relative movement in all situations (free roam and when holding LT).
 * - Character turns to face movement direction; no hard lock to target.
 * - Hold LT/RMB still toggles "combat mode" (animator/UI); movement is unchanged.
 * 
 * SOFT LOCK:
 * ----------
 * - LockOnSystem provides a soft focus target for attack tracking and camera bias.
 * - No snap-on-enter; no forced facing. Idle in combat: soft rotate toward threat in cone.
 * - Attacks allowed anytime (no combat-mode gate).
 * 
 * CORE RULE: Attacks never solve spacing or targeting.
 * 
 * ============================================================================
 */

using UnityEngine;
using UnityEngine.InputSystem;

public enum DashDirectionType { Forward, Back, Left, Right }

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    // ========================================================================
    // FREE ROAM SETTINGS
    // ========================================================================
    
    [Header("Free Roam Mode")]
    [Tooltip("Movement speed when not in combat mode")]
    public float freeRoamSpeed = 5.5f;
    
    [Tooltip("How fast character turns to face movement direction")]
    public float freeRoamTurnSpeed = 600f;
    [Tooltip("Starting turn speed when beginning a free roam turn")]
    public float freeRoamTurnSpeedMin = 120f;
    [Tooltip("How quickly free roam turn speed ramps up (deg/sec^2)")]
    public float freeRoamTurnAcceleration = 900f;
    
    [Header("Free Roam - Step Sync")]
    [Tooltip("Enable step-synced movement (speed varies with walk animation steps)")]
    public bool enableStepSync = true;
    
    [Tooltip("Duration of one full walk cycle (two steps) in seconds")]
    public float stepCycleDuration = 0.8f;
    
    [Tooltip("Normalized time (0-1) when first step push starts")]
    public float step1StartTime = 0.0f;
    
    [Tooltip("Normalized time (0-1) when first step push ends")]
    public float step1EndTime = 0.25f;
    
    [Tooltip("Normalized time (0-1) when second step push starts")]
    public float step2StartTime = 0.5f;
    
    [Tooltip("Normalized time (0-1) when second step push ends")]
    public float step2EndTime = 0.75f;
    
    [Tooltip("Speed multiplier during step push (1.0 = normal speed)")]
    public float stepPushMultiplier = 1.3f;
    
    [Tooltip("Speed multiplier between steps (1.0 = normal speed)")]
    public float stepSlowMultiplier = 0.7f;

    // ========================================================================
    // COMBAT MODE SETTINGS
    // ========================================================================
    
    [Header("Combat Mode - Footwork")]
    [Tooltip("Speed of forward advance step (slow, controlled)")]
    public float advanceSpeed = 2.2f;
    
    [Tooltip("Speed of backstep (fast disengage)")]
    public float backstepSpeed = 3.6f;
    
    [Tooltip("Speed of sidestep (lateral movement)")]
    public float sidestepSpeed = 2.7f;
    
    [Tooltip("Speed multiplier for diagonal movement (less stable)")]
    [Range(0.5f, 1f)]
    public float diagonalPenalty = 0.7f;
    
    [Header("Combat Mode - Facing")]
    [Tooltip("How fast character rotates toward focus target")]
    public float combatTurnSpeed = 300f;
    
    [Tooltip("Maximum angle for soft auto-facing toward threats (degrees)")]
    public float autoFaceAngleLimit = 25f;

    [Header("Combat Mode - Toggle Safeguards")]
    [Tooltip("Minimum time to stay in combat mode after entering (prevents quick snap abuse)")]
    public float minCombatHoldTime = 0.35f;
    [Tooltip("Minimum time between snap rotations when entering combat mode")]
    public float combatSnapCooldown = 0.5f;

    // ========================================================================
    // SHARED SETTINGS
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

    [Header("Physics")]
    public float gravity = -20f;
    
    [Header("References")]
    [Tooltip("Reference to threat focus system")]
    public LockOnSystem threatSystem;
    
    [Tooltip("Reference to combat system")]
    public Combat combat;
    
    [Tooltip("Animator for locomotion animations. Auto-finds on this object or children if not set.")]
    public Animator animator;
    
    // ========================================================================
    // ANIMATION SETTINGS
    // ========================================================================
    
    [Header("Animation")]
    [Tooltip("Animator parameter name for movement speed (0 = idle, 1 = full speed)")]
    public string speedParameter = "Speed";
    
    [Tooltip("Animator parameter name for combat mode (bool)")]
    public string combatModeParameter = "InCombatMode";
    
    [Tooltip("How quickly the animation speed blends (higher = snappier)")]
    public float animationDamping = 10f;

    // ========================================================================
    // PUBLIC STATE (readable by other systems)
    // ========================================================================
    
    /// <summary>
    /// True when player is holding the combat mode button (LT/RMB)
    /// Other systems (Combat.cs) check this to allow/deny attacks
    /// </summary>
    public bool IsInCombatMode { get; private set; }

    /// <summary>True while the player is in the middle of a dash (dodge).</summary>
    public bool IsDashing => Time.time < dashEndTime;

    /// <summary>True if the player's dodge ended within the last windowSeconds. Used by enemies to choose punish attacks.</summary>
    public bool RecentlyDodged(float windowSeconds) => lastDashEndTime > 0f && (Time.time - lastDashEndTime) <= windowSeconds;
    
    /// <summary>
    /// Current stick input in character space (for attack direction sampling)
    /// </summary>
    public Vector2 CombatStickInput { get; private set; }

    /// <summary>
    /// Normalized step cycle (0-1) for debug visualization.
    /// </summary>
    public float StepCycleNormalizedTime => stepCycleDuration > 0 ? stepCycleTimer / stepCycleDuration : 0f;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================
    
    private CharacterController cc;
    private PlayerHealth playerHealth;
    private Vector3 verticalVelocity;
    private float currentFreeRoamTurnSpeed;
    private bool wasInCombatMode;
    private float combatModeLockUntil;
    private float lastCombatSnapTime;
    
    // Animation state
    private float currentAnimSpeed;  // Smoothed animation speed value
    private float targetAnimSpeed;   // Target speed this frame (set by movement handlers)
    
    // Step sync state
    private float stepCycleTimer = 0f;  // Current position in the step cycle (0 to stepCycleDuration)

    // Dash state
    private float dashEndTime = 0f;
    private float nextDashTime = 0f;
    private Vector3 dashDirection;
    private float dashSpeed;
    private DashDirectionType dashDirType;
    private Transform dashCapTarget;
    private float lastDashEndTime = -999f;
    private bool wasDashing;

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    void Awake()
    {
        cc = GetComponent<CharacterController>();
        playerHealth = GetComponent<PlayerHealth>();
        if (threatSystem == null) threatSystem = GetComponent<LockOnSystem>();
        if (combat == null) combat = GetComponent<Combat>();
        
        // Auto-find animator on this object or children (e.g., on the visual model)
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // Reset animation speed each frame (movement handlers will set it if moving)
        targetAnimSpeed = 0f;
        
        // During hit stun: skip input and movement, keep gravity and animator
        if (playerHealth != null && playerHealth.IsStunned)
        {
            ApplyGravity();
            UpdateAnimator();
            return;
        }
        
        // Check combat mode input
        UpdateCombatModeState();

        // Right stick click (R3) or Tab: clear lock-on
        bool clearLockPressed = (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame) ||
                               (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame);
        if (clearLockPressed && threatSystem != null)
            threatSystem.ReleaseFocus();

        // LT (or RMB) press: lock on to center of view, or cycle to next target if already locked on
        bool ltPressed = (Gamepad.current != null && Gamepad.current.leftTrigger.wasPressedThisFrame) ||
                         (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
        if (ltPressed && threatSystem != null)
        {
            if (threatSystem.HasSoftTarget)
                threatSystem.CycleToNextTargetInLookDirection();
            else
                threatSystem.SetTargetToLookAt();
        }

        // Dash input (B / keyboard B): dash in movement stick direction, camera-relative
        bool dashPressed = (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame) ||
                          (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame);
        if (dashPressed && Time.time >= nextDashTime && Time.time >= dashEndTime && (combat == null || !combat.IsInAttackLock))
        {
            StartDash();
        }
        
        // If dashing, apply dash movement and skip normal movement
        if (Time.time < dashEndTime)
        {
            if (dashDirType == DashDirectionType.Forward)
            {
                // Only suck toward enemy / stop past enemy when we started dash with a lock-on target
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

                // Stop-past-enemy cap (only when we had a target at dash start)
                Transform capTarget = dashCapTarget != null ? dashCapTarget : suckTarget;
                if (enableDashStopPastEnemy && capTarget != null)
                {
                    Vector3 capPoint = capTarget.position;
                    capPoint.y = transform.position.y;
                    capPoint += dashDirection * dashStopDistancePastEnemy;

                    Vector3 desiredPos = transform.position + dashDirection * dashSpeed * Time.deltaTime;
                    desiredPos.y = transform.position.y;

                    if (Vector3.Dot(desiredPos - capPoint, dashDirection) > 0f)
                    {
                        // Would overshoot: clamp to cap and end dash
                        Vector3 clampMove = capPoint - transform.position;
                        clampMove.y = 0f;
                        if (Vector3.Dot(clampMove, dashDirection) > 0f)
                            cc.Move(clampMove);
                        dashEndTime = Time.time;
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
            else
            {
                // Back/Left/Right: no suck, no rotation override, just move
                cc.Move(dashDirection * dashSpeed * Time.deltaTime);
            }

        ApplyGravity();
        UpdateAnimator();
        TrackDashEnd();
        return;
        }

        // Handle movement based on mode (no movement during dash cooldown)
        if (Time.time >= nextDashTime)
        {
            if (IsInCombatMode)
            {
                HandleCombatMovement();
            }
            else
            {
                HandleFreeRoamMovement();
            }
        }

        ApplyGravity();
        UpdateAnimator();
        TrackDashEnd();
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
                        Vector3 camForward = cam.transform.forward;
                        camForward.y = 0f; camForward.Normalize();
                        Vector3 camRight = cam.transform.right;
                        camRight.y = 0f; camRight.Normalize();
                        Vector3 worldMove = (camForward * stick.y + camRight * stick.x).normalized;
                        Vector3 localMove = transform.InverseTransformDirection(worldMove);

                        if (Mathf.Abs(localMove.x) > Mathf.Abs(localMove.z))
                            dashDirType = localMove.x < 0f ? DashDirectionType.Left : DashDirectionType.Right;
                        else
                            dashDirType = localMove.z >= 0f ? DashDirectionType.Forward : DashDirectionType.Back;
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
            // Not locked on: dash in movement stick direction (camera-relative)
            Vector2 stick = GetStickInput();
            if (stick.sqrMagnitude >= 0.01f)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Vector3 camForward = cam.transform.forward;
                    camForward.y = 0f; camForward.Normalize();
                    Vector3 camRight = cam.transform.right;
                    camRight.y = 0f; camRight.Normalize();
                    moveDir = (camForward * stick.y + camRight * stick.x).normalized;
                }
                else
                {
                    moveDir = transform.forward;
                    moveDir.y = 0f;
                    if (moveDir.sqrMagnitude < 0.01f) moveDir = Vector3.forward;
                    else moveDir.Normalize();
                }
                // Set dash dir type for animation (optional; Forward used if stick ~forward)
                Vector3 localMove = transform.InverseTransformDirection(moveDir);
                if (Mathf.Abs(localMove.x) > Mathf.Abs(localMove.z))
                    dashDirType = localMove.x < 0f ? DashDirectionType.Left : DashDirectionType.Right;
                else
                    dashDirType = localMove.z >= 0f ? DashDirectionType.Forward : DashDirectionType.Back;
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

        // Play dash animation state directly
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
            var enemyHealth = dashOverlapBuffer[i].GetComponentInParent<EnemyHealth>();
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
    
    private const int DASH_OVERLAP_SIZE = 24;
    private Collider[] dashOverlapBuffer = new Collider[DASH_OVERLAP_SIZE];
    
    // ========================================================================
    // COMBAT MODE INPUT
    // ========================================================================
    
    void UpdateCombatModeState()
    {
        /*
         * Combat Mode is entered by HOLDING:
         * - Gamepad: Left Trigger (LT)
         * - Keyboard: Right Mouse Button (as LT substitute)
         * - Alternative: Left Shift
         * 
         * Releasing exits combat mode immediately
         */
        bool combatHeld = false;
        
        if (Gamepad.current != null)
        {
            combatHeld |= Gamepad.current.leftTrigger.isPressed;
        }
        
        if (Mouse.current != null)
        {
            combatHeld |= Mouse.current.rightButton.isPressed;
        }
        
        if (Keyboard.current != null)
        {
            combatHeld |= Keyboard.current.leftShiftKey.isPressed;
        }
        
        // Apply hold safeguard to prevent instant swap abuse
        if (combatHeld)
        {
            IsInCombatMode = true;
            combatModeLockUntil = Time.time + minCombatHoldTime;
        }
        else
        {
            IsInCombatMode = Time.time < combatModeLockUntil;
        }
        
        // Ninja Gaiden style: no snap when entering combat (soft lock only)
        wasInCombatMode = IsInCombatMode;
    }

    void SnapFacingToCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        
        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        
        transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    // ========================================================================
    // FREE ROAM MOVEMENT
    // ========================================================================
    
    void HandleFreeRoamMovement()
    {
        /*
         * FREE ROAM:
         * - Camera-relative movement (standard 3rd person)
         * - Character turns to face movement direction
         * - Full speed, free turning
         * - This is for NAVIGATION, not fighting
         */
        
        // Block movement during attacks
        if (combat != null && combat.IsAttacking)
        {
            targetAnimSpeed = 0f;
            GetStepSyncMultiplier(false);  // Reset step cycle when stopped
            return;
        }
        
        Vector2 stickInput = GetStickInput();
        Vector3 input = new Vector3(stickInput.x, 0f, stickInput.y);
        input = Vector3.ClampMagnitude(input, 1f);
        
        if (input.sqrMagnitude < 0.01f)
        {
            CombatStickInput = Vector2.zero;
            currentFreeRoamTurnSpeed = 0f;
            targetAnimSpeed = 0f;
            GetStepSyncMultiplier(false);  // Reset step cycle when stopped
            return;
        }
        
        // Transform to camera space
        Camera cam = Camera.main;
        if (cam == null) return;
        
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0f;
        camForward.Normalize();
        
        Vector3 camRight = cam.transform.right;
        camRight.y = 0f;
        camRight.Normalize();
        
        Vector3 moveDir = (camForward * input.z + camRight * input.x).normalized;
        
        // Turn to face movement direction (ramp up turn speed)
        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        
        if (currentFreeRoamTurnSpeed <= 0f)
        {
            currentFreeRoamTurnSpeed = freeRoamTurnSpeedMin;
        }
        
        currentFreeRoamTurnSpeed = Mathf.MoveTowards(
            currentFreeRoamTurnSpeed,
            freeRoamTurnSpeed,
            freeRoamTurnAcceleration * Time.deltaTime
        );
        
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            currentFreeRoamTurnSpeed * Time.deltaTime
        );
        
        // Move with step sync
        float stepMultiplier = GetStepSyncMultiplier(true);
        cc.Move(moveDir * freeRoamSpeed * stepMultiplier * input.magnitude * Time.deltaTime);
        
        // Store for attack direction (not used in free roam, but keep updated)
        CombatStickInput = stickInput;
        
        // Set animation speed (0-1 based on input magnitude)
        targetAnimSpeed = input.magnitude;
    }

    // ========================================================================
    // COMBAT MODE MOVEMENT
    // ========================================================================
    
    void HandleCombatMovement()
    {
        /*
         * COMBAT STRAFE:
         * - Face the soft lock target (from LockOnSystem)
         * - Move camera-relative but keep facing the target (strafing)
         * - Use directional speeds: advance / backstep / sidestep
         * - Falls back to free roam style if no soft target
         */
        
        // Block movement during attacks
        if (combat != null && combat.IsAttacking)
        {
            targetAnimSpeed = 0f;
            GetStepSyncMultiplier(false);
            return;
        }
        
        Vector2 stickInput = GetStickInput();
        CombatStickInput = stickInput;
        Vector3 input = new Vector3(stickInput.x, 0f, stickInput.y);
        input = Vector3.ClampMagnitude(input, 1f);
        
        bool hasTarget = threatSystem != null && threatSystem.HasSoftTarget;
        
        if (input.sqrMagnitude < 0.01f)
        {
            currentFreeRoamTurnSpeed = 0f;
            targetAnimSpeed = 0f;
            GetStepSyncMultiplier(false);
            HandleCombatFacing();  // Soft face threat when idle
            return;
        }
        
        Camera cam = Camera.main;
        if (cam == null) return;
        
        // Build camera-relative move direction
        Vector3 camForward = cam.transform.forward;
        camForward.y = 0f;
        camForward.Normalize();
        Vector3 camRight = cam.transform.right;
        camRight.y = 0f;
        camRight.Normalize();
        Vector3 moveDir = (camForward * input.z + camRight * input.x).normalized;
        
        if (hasTarget)
        {
            // --- STRAFE MODE: face target, move freely ---
            Vector3 toTarget = threatSystem.SoftTarget.position - transform.position;
            toTarget.y = 0f;
            
            if (toTarget.sqrMagnitude > 0.01f)
            {
                // Rotate to face target
                Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, combatTurnSpeed * Time.deltaTime);
                
                // Calculate directional speed relative to facing
                Vector3 localMove = transform.InverseTransformDirection(moveDir);
                float forward = localMove.z;  // positive = toward target, negative = away
                float lateral = Mathf.Abs(localMove.x);
                
                float speed;
                bool isDiagonal = Mathf.Abs(forward) > 0.2f && lateral > 0.2f;
                
                if (isDiagonal)
                {
                    // Blend between forward/back and lateral speeds, apply diagonal penalty
                    float fwdSpeed = forward >= 0f ? advanceSpeed : backstepSpeed;
                    speed = Mathf.Lerp(sidestepSpeed, fwdSpeed, Mathf.Abs(forward)) * diagonalPenalty;
                }
                else if (lateral > Mathf.Abs(forward))
                {
                    speed = sidestepSpeed;
                }
                else
                {
                    speed = forward >= 0f ? advanceSpeed : backstepSpeed;
                }
                
                float stepMultiplier = GetStepSyncMultiplier(true);
                cc.Move(moveDir * speed * stepMultiplier * input.magnitude * Time.deltaTime);
            }
            else
            {
                float stepMultiplier = GetStepSyncMultiplier(true);
                cc.Move(moveDir * sidestepSpeed * stepMultiplier * input.magnitude * Time.deltaTime);
            }
        }
        else
        {
            // --- NO TARGET: free roam style turning + movement ---
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            if (currentFreeRoamTurnSpeed <= 0f)
                currentFreeRoamTurnSpeed = freeRoamTurnSpeedMin;
            currentFreeRoamTurnSpeed = Mathf.MoveTowards(
                currentFreeRoamTurnSpeed, freeRoamTurnSpeed, freeRoamTurnAcceleration * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, currentFreeRoamTurnSpeed * Time.deltaTime);
            
            float stepMultiplier = GetStepSyncMultiplier(true);
            cc.Move(moveDir * freeRoamSpeed * stepMultiplier * input.magnitude * Time.deltaTime);
        }
        
        targetAnimSpeed = input.magnitude;
    }
    
    void HandleCombatFacing()
    {
        /*
         * COMBAT FACING:
         * 
         * Soft auto-facing toward highest-threat enemy:
         * - Only if threat is within focus cone (same forward as LockOnSystem: camera when available)
         * - Only rotates up to autoFaceAngleLimit per frame
         * - Player can break facing via lateral movement
         * - Never snaps or locks hard
         * 
         * If no threat in cone, facing is maintained (player controls it)
         */
        
        // Don't auto-face during attacks
        if (combat != null && combat.IsAttacking) return;
        
        if (threatSystem == null || !threatSystem.HasSoftTarget) return;
        
        float coneAngle = threatSystem.focusConeAngle;
        Transform threat = threatSystem.SoftTarget;
        
        // Calculate direction to threat
        Vector3 toThreat = threat.position - transform.position;
        toThreat.y = 0f;
        
        if (toThreat.sqrMagnitude < 0.01f) return;
        
        toThreat.Normalize();
        
        // Use same forward as LockOnSystem for cone check (camera when available) so "in cone" matches scoring
        Vector3 forward = transform.forward;
        Camera cam = Camera.main;
        if (cam != null)
        {
            forward = cam.transform.forward;
        }
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();
        
        float angleToThreat = Vector3.Angle(forward, toThreat);
        if (angleToThreat > coneAngle) return;  // Outside cone, don't auto-face
        
        // Soft rotate toward threat (limited by autoFaceAngleLimit)
        Quaternion targetRot = Quaternion.LookRotation(toThreat, Vector3.up);
        
        // Limit rotation speed and total rotation
        float maxRotation = Mathf.Min(combatTurnSpeed * Time.deltaTime, autoFaceAngleLimit);
        
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRot,
            maxRotation
        );
    }

    // ========================================================================
    // INPUT HELPERS
    // ========================================================================
    
    Vector2 GetStickInput()
    {
        Vector2 input = Vector2.zero;
        
        if (Keyboard.current != null)
        {
            float h = 0f, v = 0f;
            if (Keyboard.current.aKey.isPressed) h -= 1f;
            if (Keyboard.current.dKey.isPressed) h += 1f;
            if (Keyboard.current.sKey.isPressed) v -= 1f;
            if (Keyboard.current.wKey.isPressed) v += 1f;
            input = new Vector2(h, v);
        }
        
        if (Gamepad.current != null)
        {
            input += Gamepad.current.leftStick.ReadValue();
        }
        
        return Vector2.ClampMagnitude(input, 1f);
    }
    
    // ========================================================================
    // STEP SYNC
    // ========================================================================
    
    /// <summary>
    /// Updates the step cycle timer and returns the speed multiplier for step-synced movement.
    /// Call this when moving to get the current step-based speed modifier.
    /// </summary>
    float GetStepSyncMultiplier(bool isMoving)
    {
        if (!enableStepSync) return 1f;
        
        if (isMoving)
        {
            // Advance the step cycle timer
            stepCycleTimer += Time.deltaTime;
            if (stepCycleTimer >= stepCycleDuration)
            {
                stepCycleTimer -= stepCycleDuration;
            }
            
            // Calculate normalized position in cycle (0-1)
            float normalizedTime = stepCycleTimer / stepCycleDuration;
            
            // Check if we're in a step push phase
            bool inStep1 = normalizedTime >= step1StartTime && normalizedTime < step1EndTime;
            bool inStep2 = normalizedTime >= step2StartTime && normalizedTime < step2EndTime;
            
            if (inStep1 || inStep2)
            {
                return stepPushMultiplier;
            }
            else
            {
                return stepSlowMultiplier;
            }
        }
        else
        {
            // Reset cycle when not moving (so steps start fresh)
            stepCycleTimer = 0f;
            return 1f;
        }
    }

    // ========================================================================
    // GRAVITY
    // ========================================================================
    
    void ApplyGravity()
    {
        if (cc.isGrounded && verticalVelocity.y < 0f)
        {
            verticalVelocity.y = -2f;
        }
        
        verticalVelocity.y += gravity * Time.deltaTime;
        cc.Move(verticalVelocity * Time.deltaTime);
    }

    // ========================================================================
    // ANIMATION
    // ========================================================================
    
    void UpdateAnimator()
    {
        if (animator == null) return;
        
        // Smooth the animation speed for natural transitions
        currentAnimSpeed = Mathf.Lerp(
            currentAnimSpeed, 
            targetAnimSpeed, 
            1f - Mathf.Exp(-animationDamping * Time.deltaTime)
        );
        
        // Snap to 0 when very close (prevents floating-point drift to tiny values like 6e-29)
        if (currentAnimSpeed < 0.001f && targetAnimSpeed == 0f)
        {
            currentAnimSpeed = 0f;
        }
        
        // Set animator parameters
        animator.SetFloat(speedParameter, currentAnimSpeed);
        animator.SetBool(combatModeParameter, IsInCombatMode);
    }
    
}
