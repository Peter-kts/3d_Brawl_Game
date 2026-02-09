/*
 * ============================================================================
 * PLAYERCONTROLLER.CS - Dual-mode movement: Free Roam + Combat Mode
 * ============================================================================
 * 
 * COMBAT MOVEMENT PHILOSOPHY:
 * ---------------------------
 * 
 * This system has TWO distinct modes:
 * 
 * 1. FREE ROAM (default):
 *    - Camera-relative movement
 *    - Character turns freely with movement
 *    - For navigation, exploration, escape
 *    - NOT for fighting
 * 
 * 2. COMBAT MODE (hold LT/Right Mouse):
 *    - Character-relative footwork
 *    - Deliberate steps: advance, backstep, sidestep
 *    - Facing maintained toward threats
 *    - Attacks only allowed here
 * 
 * CORE RULE: Attacks never solve spacing or targeting.
 * The player must manually position and face enemies.
 * 
 * ============================================================================
 */

using UnityEngine;
using UnityEngine.InputSystem;

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
        
        // Handle movement based on mode
        if (IsInCombatMode)
        {
            HandleCombatMovement();
        }
        else
        {
            HandleFreeRoamMovement();
        }
        
        ApplyGravity();
        UpdateAnimator();
    }
    
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
        
        // Snap facing to camera aim when entering combat mode (only if enemy in snap cone)
        if (IsInCombatMode && !wasInCombatMode)
        {
            if (Time.time - lastCombatSnapTime >= combatSnapCooldown)
            {
                if (threatSystem != null && threatSystem.HasThreatInSnapCone())
                {
                    SnapFacingToCamera();
                    lastCombatSnapTime = Time.time;
                }
            }
        }
        
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
         * COMBAT MODE FOOTWORK:
         * 
         * Movement is CHARACTER-RELATIVE, not camera-relative:
         * - Forward stick = advance step (toward where character faces)
         * - Back stick = backstep (away from facing)
         * - Left/Right = sidestep (strafe)
         * 
         * Speeds are asymmetric:
         * - Backstep is fastest (disengage)
         * - Sidestep is medium
         * - Advance is slowest (controlled approach)
         * 
         * Diagonals are penalized (less stable footwork)
         */
        
        // Block movement during attacks
        if (combat != null && combat.IsAttacking)
        {
            targetAnimSpeed = 0f;
            return;
        }
        
        Vector2 stickInput = GetStickInput();
        CombatStickInput = stickInput;  // Store for attack direction sampling
        
        // Handle facing first (soft auto-face toward threats)
        HandleCombatFacing();
        
        // No movement if no input
        if (stickInput.sqrMagnitude < 0.01f)
        {
            targetAnimSpeed = 0f;
            return;
        }
        
        // Determine movement type based on stick direction
        float forward = stickInput.y;  // Positive = advance, Negative = backstep
        float lateral = stickInput.x;  // Left/Right = sidestep
        
        // Calculate speeds based on direction
        float forwardSpeed = forward > 0 ? advanceSpeed : backstepSpeed;
        float lateralSpeed = sidestepSpeed;
        
        // Build movement vector in character space
        Vector3 charSpaceMove = Vector3.zero;
        charSpaceMove += transform.forward * forward * forwardSpeed;
        charSpaceMove += transform.right * lateral * lateralSpeed;
        
        // Apply diagonal penalty (less stable footwork)
        bool isDiagonal = Mathf.Abs(forward) > 0.3f && Mathf.Abs(lateral) > 0.3f;
        if (isDiagonal)
        {
            charSpaceMove *= diagonalPenalty;
        }
        
        // Normalize to prevent faster diagonal movement, then apply magnitude
        float inputMag = Mathf.Clamp01(stickInput.magnitude);
        if (charSpaceMove.sqrMagnitude > 0.01f)
        {
            charSpaceMove = charSpaceMove.normalized * charSpaceMove.magnitude * inputMag;
        }
        
        // Move
        cc.Move(charSpaceMove * Time.deltaTime);
        
        // Set animation speed (lower in combat mode for footwork feel)
        targetAnimSpeed = inputMag * 0.5f;  // Half speed in combat mode
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
