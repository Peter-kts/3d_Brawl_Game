/*
 * ============================================================================
 * PLAYERCONTROLLER.CS - Ninja Gaiden style: camera-relative movement + soft lock
 * ============================================================================
 *
 * OVERVIEW:
 * ---------
 * Handles input (stick, LT/RMB lock-on, B dash), movement (free roam vs combat
 * strafe), and animator (Speed, InCombatMode). Dash logic lives in
 * PlayerController.Dash.cs (partial). Combat mode = hold LT/RMB; lock-on =
 * press LT to set/cycle target, R3/Tab to clear. Movement is always
 * camera-relative; with a soft target we face the target and strafe.
 *
 * MOVEMENT:
 * ---------
 * - Camera-relative in all situations (free roam and combat).
 * - Character turns to face movement direction (free roam) or soft target (combat).
 * - Hold LT/RMB toggles IsInCombatMode (animator/UI); movement style follows it.
 *
 * SOFT LOCK:
 * ----------
 * - LockOnSystem gives one soft focus target; we read it for facing and dash.
 * - No snap-on-enter; no forced facing. Idle in combat: soft rotate toward threat in cone.
 * - Attacks allowed anytime (no combat-mode gate).
 *
 * CORE RULE: Attacks never solve spacing or targeting.
 *
 * ============================================================================
 */

using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public partial class PlayerController : MonoBehaviour
{
    // ========================================================================
    // FREE ROAM SETTINGS (navigation when not in combat mode)
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
    // COMBAT MODE SETTINGS (when IsInCombatMode: strafe speeds and soft-facing)
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
    [Tooltip("Movement speed multiplier while blocking.")]
    [Range(0f, 1f)]
    public float blockMoveMultiplier = 0.3f;
    
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
    // SHARED SETTINGS (gravity; refs resolved in Awake if null)
    // ========================================================================

    [Header("Physics")]
    public float gravity = -20f;

    [Header("References")]
    [Tooltip("Reference to threat focus system")]
    public LockOnSystem threatSystem;
    
    [Tooltip("Reference to combat system")]
    public Combat combat;
    [Tooltip("Optional explicit weapon combat reference. If assigned, this is used for attack lock checks.")]
    public WeaponCombat weaponCombat;
    
    [Tooltip("Animator for locomotion animations. Auto-finds on this object or children if not set.")]
    public Animator animator;
    
    // ========================================================================
    // ANIMATION SETTINGS (animator parameter names and blend speed)
    // ========================================================================

    [Header("Animation")]
    [Tooltip("Animator parameter name for movement speed (0 = idle, 1 = full speed)")]
    public string speedParameter = "Speed";
    
    [Tooltip("Animator parameter name for combat mode (bool)")]
    public string combatModeParameter = "InCombatMode";

    [Tooltip("Animator parameter name for active blocking pose (bool).")]
    public string blockParameter = "IsBlocking";

    [Tooltip("Animator parameter name for hitstun (bool). True while the player is in a hit reaction.")]
    public string stunParameter = "IsStunned";

    [Tooltip("Animator parameter for local lateral movement (-1 left, +1 right)")]
    public string moveXParameter = "MoveX";

    [Tooltip("Animator parameter for local forward/back movement (-1 back, +1 forward)")]
    public string moveZParameter = "MoveZ";
    
    [Tooltip("How quickly the animation speed blends (higher = snappier)")]
    public float animationDamping = 10f;

    [Tooltip("How quickly movement magnitude ramps to full speed (units/sec). Lower = longer walk-to-run startup.")]
    public float movementRampSpeed = 3f;

    // ========================================================================
    // PUBLIC STATE (readable by other systems)
    // ========================================================================
    
    /// <summary>
    /// True when player is holding the combat mode button (LT/RMB)
    /// Other systems (Combat.cs) check this to allow/deny attacks
    /// </summary>
    public bool IsInCombatMode { get; private set; }

    /// <summary>
    /// True while block input is held (Q / Left Shoulder).
    /// </summary>
    public bool IsBlocking { get; private set; }
    
    /// <summary>
    /// Current stick input in character space (for attack direction sampling)
    /// </summary>
    public Vector2 CombatStickInput { get; private set; }

    /// <summary>
    /// Normalized step cycle (0-1) for debug visualization.
    /// </summary>
    public float StepCycleNormalizedTime => stepCycleDuration > 0 ? stepCycleTimer / stepCycleDuration : 0f;

    /// <summary>
    /// True if the player's attack lock ended within the last windowSeconds.
    /// Enemies use this to time punish attacks during the player's recovery frames.
    /// Mirrors RecentlyDodged() — same pattern, different trigger.
    /// </summary>
    public bool RecentlyAttacked(float windowSeconds) => ActiveCombat != null && ActiveCombat.RecentlyAttacked(windowSeconds);

    /// <summary>
    /// Seconds remaining in the current attack lock. 0 when not attacking.
    /// Cautious enemies use this to decide whether interrupting is safe.
    /// </summary>
    public float AttackLockTimeRemaining => ActiveCombat?.AttackLockTimeRemaining ?? 0f;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private CharacterController cc;
    private PlayerHealth playerHealth;
    private Camera mainCamera;
    private Vector3 verticalVelocity;           // Used by ApplyGravity — not called from Update yet; available if jump/fall is added later
    private float currentFreeRoamTurnSpeed;     // Ramps from freeRoamTurnSpeedMin → freeRoamTurnSpeed as the player starts turning; resets to 0 when idle
    private bool wasInCombatMode;               // Tracks previous frame's combat mode; used to detect the exact frame the mode changes
    private float combatModeLockUntil;          // Combat mode can't exit before this time — prevents snapping out on a brief tap of LT
    private float lastCombatSnapTime;           // Unused — reserved for future snap-to-target behaviour
    private float currentAnimSpeed;            // Smoothed Speed value sent to the Animator each frame
    private float targetAnimSpeed;             // Desired speed this frame (0 = idle, 1 = full run); movement handlers set this
    private float targetMoveX;                 // Desired MoveX blend value (-1 left, +1 right) for the combat strafe blend tree
    private float targetMoveZ;                 // Desired MoveZ blend value (-1 back, +1 forward)
    private float currentMoveX;               // Smoothed MoveX actually sent to the Animator
    private float currentMoveZ;               // Smoothed MoveZ actually sent to the Animator
    private float currentMoveMagnitude;        // Smoothed 0–1 magnitude; both cc.Move() and the Animator use this so they stay in sync
    private bool hasBlockParameter;            // Cached at Awake: true if the Animator has the block bool parameter (avoids searching every frame)
    private bool hasStunParameter;             // Cached at Awake: true if the Animator has the stun bool parameter
    private bool blockJustPressedThisFrame;    // True only on the first frame of a block press; forces Speed=0 so the block-entry pose snaps in cleanly
    private float stepCycleTimer = 0f;         // Advances while moving; wraps at stepCycleDuration; used by GetStepSyncMultiplier
    private Combat ActiveCombat => weaponCombat != null ? weaponCombat : combat;  // Returns WeaponCombat when equipped, plain Combat otherwise

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    /// <summary>Single place for player Animator lookup. Used by Combat, PlayerController, PlayerHealth. Looks on same GameObject only.</summary>
    public static Animator FindAnimator(GameObject gameObject)
    {
        return gameObject.GetComponent<Animator>();
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        playerHealth = GetComponent<PlayerHealth>();
        mainCamera = Camera.main;
        if (threatSystem == null) threatSystem = GetComponentInChildren<LockOnSystem>(true);
        if (weaponCombat == null) weaponCombat = GetComponent<WeaponCombat>();
        if (combat == null) combat = weaponCombat != null ? weaponCombat : GetComponent<Combat>();
        if (animator == null) animator = FindAnimator(gameObject);
        hasBlockParameter = HasBoolParameter(animator, blockParameter);
        hasStunParameter  = HasBoolParameter(animator, stunParameter);
    }

    void Update()
    {
        // Reset per-frame targets to idle defaults. Movement handlers below will
        // override these if the player is actually moving this frame.
        targetAnimSpeed = 0f;
        targetMoveX = 0f;
        targetMoveZ = 0f;

        // Block pose checked every frame regardless of state (Q / Left Shoulder).
        UpdateBlockState();

        // Hitstunned players skip all input and movement; just keep animator/gravity ticking.
        if (TryHandleHitstunnedState()) return;

        // Read held inputs to determine combat mode and lock-on state this frame.
        UpdateCombatModeState();  // Hold LT/RMB/Shift toggles IsInCombatMode
        HandleLockOnInput();      // R3/Tab clears lock; LT/RMB sets or cycles soft target
        TryStartDashFromInput();  // B starts a dash if off cooldown and not attack-locked

        // Dashing overrides normal movement entirely; still ramp magnitude down
        // so the character decelerates smoothly out of the dash.
        if (IsDashing)
        {
            ApplyDashMovement();
            UpdateMoveMagnitude();
            UpdateAnimator();
            TrackDashEnd();
            ApplyGravity();
            return;
        }

        // Normal movement: dispatches to HandleFreeRoamMovement or HandleCombatMovement
        // based on IsInCombatMode. These set targetAnimSpeed, targetMoveX/Z, and call
        // cc.Move() using the previous frame's currentMoveMagnitude for physical speed.
        if (Time.time >= nextDashTime)
            HandleMovementByMode();

        // Ramp currentMoveMagnitude toward targetAnimSpeed (now set by movement handler).
        // This drives both the physical cc.Move() speed next frame and the BlendTree
        // magnitude this frame, keeping animation and movement in sync.
        UpdateMoveMagnitude();

        // Push smoothed Speed, MoveX, MoveZ, InCombatMode, IsBlocking to the animator.
        UpdateAnimator();

        TrackDashEnd();
        ApplyGravity();
    }

    /// <summary>If player is in hitstun (hit reaction), update animator and return true so Update skips input/movement.</summary>
    bool TryHandleHitstunnedState()
    {
        if (playerHealth != null && playerHealth.IsHitstunned)
        {
            UpdateAnimator();
            ApplyGravity();
            return true;
        }
        return false;
    }

    /// <summary>R3 or Tab = clear soft target. LT or RMB = lock on to look-at target, or cycle to next if already locked.</summary>
    void HandleLockOnInput()
    {
        bool clearLockPressed = (Gamepad.current != null && Gamepad.current.rightStickButton.wasPressedThisFrame) ||
                               (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame);
        if (clearLockPressed && threatSystem != null)
            threatSystem.ReleaseFocus();

        bool ltPressed = (Gamepad.current != null && Gamepad.current.leftTrigger.wasPressedThisFrame) ||
                         (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame) ||
                         (Keyboard.current != null && Keyboard.current.leftShiftKey.wasPressedThisFrame);
        if (ltPressed && threatSystem != null)
        {
            if (threatSystem.HasSoftTarget)
                threatSystem.CycleToNextTargetInLookDirection();
            else
                threatSystem.SetTargetToLookAt();
        }
    }

    // ========================================================================
    // COMBAT MODE INPUT
    // ========================================================================

    /// <summary>Sets IsBlocking from dedicated held inputs (Q / A).</summary>
    void UpdateBlockState()
    {
        bool blockHeld = false;
        bool blockPressedThisFrame = false;
        if (Keyboard.current != null) blockHeld |= Keyboard.current.qKey.isPressed;
        if (Keyboard.current != null) blockPressedThisFrame |= Keyboard.current.qKey.wasPressedThisFrame;
        if (Gamepad.current != null) blockHeld |= Gamepad.current.buttonSouth.isPressed;
        if (Gamepad.current != null) blockPressedThisFrame |= Gamepad.current.buttonSouth.wasPressedThisFrame;
        IsBlocking = blockHeld;
        blockJustPressedThisFrame = blockPressedThisFrame;
    }

    /// <summary>Sets IsInCombatMode from LT/RMB/Shift hold. Uses minCombatHoldTime so releasing doesn't exit instantly.</summary>
    void UpdateCombatModeState()
    {
        bool combatHeld = false;
        if (Gamepad.current != null) combatHeld |= Gamepad.current.leftTrigger.isPressed;
        if (Mouse.current != null) combatHeld |= Mouse.current.rightButton.isPressed;
        if (Keyboard.current != null) combatHeld |= Keyboard.current.leftShiftKey.isPressed;

        if (combatHeld)
        {
            IsInCombatMode = true;
            combatModeLockUntil = Time.time + minCombatHoldTime;
        }
        else
        {
            IsInCombatMode = Time.time < combatModeLockUntil;
        }
        wasInCombatMode = IsInCombatMode;
    }

    /// <summary>Instantly face camera forward (XZ). Used when entering combat or similar; not called from Update by default.</summary>
    void SnapFacingToCamera()
    {
        Camera cam = mainCamera;
        if (cam == null) return;
        Vector3 forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    // ========================================================================
    // FREE ROAM MOVEMENT
    // ========================================================================

    /// <summary>Dispatches to combat strafe or free roam based on IsInCombatMode. No movement during dash cooldown.</summary>
    void HandleMovementByMode()
    {
        if (Time.time < nextDashTime) return;
        if (IsInCombatMode)
            HandleCombatMovement();
        else
            HandleFreeRoamMovement();
    }

    /// <summary>Camera-relative move; turn to face move direction with ramping turn speed. Blocked during attacks.</summary>
    void HandleFreeRoamMovement()
    {
        if (ActiveCombat != null && ActiveCombat.IsAttacking)
        {
            targetAnimSpeed = 0f;
            CombatStickInput = Vector2.zero;
            GetStepSyncMultiplier(false);
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
            GetStepSyncMultiplier(false);
            return;
        }

        Camera cam = mainCamera;
        if (cam == null) return;

        GetFlatCameraAxes(cam, out Vector3 camForward, out Vector3 camRight);
        Vector3 moveDir = (camForward * input.z + camRight * input.x).normalized;

        Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
        if (currentFreeRoamTurnSpeed <= 0f)
            currentFreeRoamTurnSpeed = freeRoamTurnSpeedMin;
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

        float stepMultiplier = GetStepSyncMultiplier(true);
        float blockMultiplier = IsBlocking ? blockMoveMultiplier : 1f;
        cc.Move(moveDir * freeRoamSpeed * stepMultiplier * blockMultiplier * currentMoveMagnitude * Time.deltaTime);
        CombatStickInput = stickInput;
        targetAnimSpeed = input.magnitude;
        targetMoveX = 0f;
        targetMoveZ = 1f;
    }

    // ========================================================================
    // COMBAT MODE MOVEMENT
    // ========================================================================

    /// <summary>With soft target: face target, move camera-relative (strafe) with advance/backstep/sidestep speeds. No target: free roam style.</summary>
    void HandleCombatMovement()
    {
        if (ActiveCombat != null && ActiveCombat.IsAttacking)
        {
            targetAnimSpeed = 0f;
            CombatStickInput = Vector2.zero;
            GetStepSyncMultiplier(false);
            return;
        }

        Vector2 stickInput = GetStickInput();
        CombatStickInput = stickInput;
        float blockMultiplier = IsBlocking ? blockMoveMultiplier : 1f;
        Vector3 input = new Vector3(stickInput.x, 0f, stickInput.y);
        input = Vector3.ClampMagnitude(input, 1f);
        bool hasTarget = threatSystem != null && threatSystem.HasSoftTarget;

        if (input.sqrMagnitude < 0.01f)
        {
            currentFreeRoamTurnSpeed = 0f;
            targetAnimSpeed = 0f;
            GetStepSyncMultiplier(false);
            HandleCombatFacing();
            return;
        }

        Camera cam = mainCamera;
        if (cam == null) return;

        GetFlatCameraAxes(cam, out Vector3 camForward, out Vector3 camRight);
        Vector3 moveDir = (camForward * input.z + camRight * input.x).normalized;

        if (hasTarget)
        {
            Vector3 toTarget = threatSystem.SoftTarget.position - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, combatTurnSpeed * Time.deltaTime);

                // Move in world direction but speed depends on local direction: forward = advance, back = backstep, lateral = sidestep
                Vector3 localMove = transform.InverseTransformDirection(moveDir);
                targetMoveX = localMove.x;
                targetMoveZ = localMove.z;
                float speed = SelectCombatMoveSpeed(localMove);
                float stepMultiplier = GetStepSyncMultiplier(true);
                cc.Move(moveDir * speed * stepMultiplier * blockMultiplier * currentMoveMagnitude * Time.deltaTime);
            }
            else
            {
                float stepMultiplier = GetStepSyncMultiplier(true);
                cc.Move(moveDir * sidestepSpeed * stepMultiplier * blockMultiplier * currentMoveMagnitude * Time.deltaTime);
            }
        }
        else
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            if (currentFreeRoamTurnSpeed <= 0f)
                currentFreeRoamTurnSpeed = freeRoamTurnSpeedMin;
            currentFreeRoamTurnSpeed = Mathf.MoveTowards(
                currentFreeRoamTurnSpeed, freeRoamTurnSpeed, freeRoamTurnAcceleration * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, currentFreeRoamTurnSpeed * Time.deltaTime);
            float stepMultiplier = GetStepSyncMultiplier(true);
            cc.Move(moveDir * freeRoamSpeed * stepMultiplier * blockMultiplier * currentMoveMagnitude * Time.deltaTime);
            targetMoveX = 0f;
            targetMoveZ = 1f;
        }

        targetAnimSpeed = input.magnitude;
    }
    
    /// <summary>When idle in combat with a soft target: softly rotate toward threat if it's in focus cone (camera forward). Capped by autoFaceAngleLimit.</summary>
    void HandleCombatFacing()
    {
        if (ActiveCombat != null && ActiveCombat.IsAttacking) return;
        if (threatSystem == null || !threatSystem.HasSoftTarget) return;

        float coneAngle = threatSystem.focusConeAngle;
        Transform threat = threatSystem.SoftTarget;
        Vector3 toThreat = threat.position - transform.position;
        toThreat.y = 0f;
        if (toThreat.sqrMagnitude < 0.01f) return;
        toThreat.Normalize();

        Vector3 forward = transform.forward;
        Camera cam = mainCamera;
        if (cam != null) forward = cam.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();

        // Use camera forward (not player forward) for the cone check — the focus cone describes
        // where the *camera* is pointing, so soft-facing only assists when the threat is in view.
        float angleToThreat = Vector3.Angle(forward, toThreat);
        if (angleToThreat > coneAngle) return;

        Quaternion targetRot = Quaternion.LookRotation(toThreat, Vector3.up);
        float maxRotation = Mathf.Min(combatTurnSpeed * Time.deltaTime, autoFaceAngleLimit);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, maxRotation);
    }

    // ========================================================================
    // INPUT HELPERS
    // ========================================================================

    /// <summary>Framerate-independent lerp factor: 1 - e^(-damping * dt). Pass as the 't' in Mathf.Lerp to get smooth exponential decay.</summary>
    public static float ExponentialBlendFactor(float damping) =>
        1f - Mathf.Exp(-damping * Time.deltaTime);

    /// <summary>
    /// Returns the strafe speed for a given local-space move direction.
    /// Diagonal blends sidestep and forward/back; pure lateral = sidestep; pure forward/back = advance/backstep.
    /// </summary>
    float SelectCombatMoveSpeed(Vector3 localMove)
    {
        float fwd     = localMove.z;
        float lateral = Mathf.Abs(localMove.x);
        bool isDiagonal = Mathf.Abs(fwd) > 0.2f && lateral > 0.2f;

        if (isDiagonal)
        {
            float fwdSpeed = fwd >= 0f ? advanceSpeed : backstepSpeed;
            return Mathf.Lerp(sidestepSpeed, fwdSpeed, Mathf.Abs(fwd)) * diagonalPenalty;
        }
        if (lateral > Mathf.Abs(fwd)) return sidestepSpeed;
        return fwd >= 0f ? advanceSpeed : backstepSpeed;
    }

    /// <summary>Returns camera forward and right projected onto the XZ plane and normalized. Used wherever movement is camera-relative.</summary>
    static void GetFlatCameraAxes(Camera cam, out Vector3 forward, out Vector3 right)
    {
        forward = cam.transform.forward; forward.y = 0f; forward.Normalize();
        right   = cam.transform.right;   right.y   = 0f; right.Normalize();
    }

    /// <summary>WASD or left stick, clamped to unit circle. Used for movement and dash direction.</summary>
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
            input += Gamepad.current.leftStick.ReadValue();
        return Vector2.ClampMagnitude(input, 1f);
    }

    // ========================================================================
    // STEP SYNC (optional speed variation during walk cycle: push vs slow phase)
    // ========================================================================

    /// <summary>If enableStepSync: advances step cycle when moving and returns stepPushMultiplier or stepSlowMultiplier; when not moving resets cycle and returns 1.</summary>
    float GetStepSyncMultiplier(bool isMoving)
    {
        if (!enableStepSync) return 1f;

        if (isMoving)
        {
            stepCycleTimer += Time.deltaTime;
            if (stepCycleTimer >= stepCycleDuration)
                stepCycleTimer -= stepCycleDuration;
            float normalizedTime = stepCycleTimer / stepCycleDuration;
            bool inStep1 = normalizedTime >= step1StartTime && normalizedTime < step1EndTime;
            bool inStep2 = normalizedTime >= step2StartTime && normalizedTime < step2EndTime;
            return (inStep1 || inStep2) ? stepPushMultiplier : stepSlowMultiplier;
        }
        stepCycleTimer = 0f;
        return 1f;
    }

    // ========================================================================
    // GRAVITY (not called from Update currently; available for use if needed)
    // ========================================================================

    void ApplyGravity()
    {
        if (cc.isGrounded && verticalVelocity.y < 0f)
            verticalVelocity.y = -2f;
        verticalVelocity.y += gravity * Time.deltaTime;
        cc.Move(verticalVelocity * Time.deltaTime);
    }

    // ========================================================================
    // MOVEMENT MAGNITUDE RAMP
    // ========================================================================

    /// <summary>Ramps currentMoveMagnitude toward targetAnimSpeed at movementRampSpeed. Called before movement so both cc.Move() and animator use the same frame-accurate value.</summary>
    void UpdateMoveMagnitude()
    {
        currentMoveMagnitude = Mathf.MoveTowards(
            currentMoveMagnitude, targetAnimSpeed, movementRampSpeed * Time.deltaTime);
    }

    // ========================================================================
    // ANIMATION
    // ========================================================================

    /// <summary>Blends currentAnimSpeed toward targetAnimSpeed (set by movement handlers), sets Speed and InCombatMode on animator.</summary>
    void UpdateAnimator()
    {
        if (animator == null) return;
        float blendAlpha = ExponentialBlendFactor(animationDamping);
        currentAnimSpeed = Mathf.Lerp(currentAnimSpeed, targetAnimSpeed, blendAlpha);
        if (currentAnimSpeed < 0.001f && targetAnimSpeed == 0f)
            currentAnimSpeed = 0f;
        currentMoveX = Mathf.Lerp(currentMoveX, targetMoveX, blendAlpha);
        currentMoveZ = Mathf.Lerp(currentMoveZ, targetMoveZ, blendAlpha);
        bool forceBlockEntryFrame = IsBlocking && blockJustPressedThisFrame;
        animator.SetFloat(speedParameter, forceBlockEntryFrame ? 0f : currentAnimSpeed);
        animator.SetFloat(moveXParameter, forceBlockEntryFrame ? 0f : (currentMoveX * currentMoveMagnitude));
        animator.SetFloat(moveZParameter, forceBlockEntryFrame ? 0f : (currentMoveZ * currentMoveMagnitude));
        animator.SetBool(combatModeParameter, IsInCombatMode);
        if (hasBlockParameter)
            animator.SetBool(blockParameter, IsBlocking);
        if (hasStunParameter)
            animator.SetBool(stunParameter, playerHealth != null && playerHealth.IsHitstunned);
    }

    static bool HasBoolParameter(Animator targetAnimator, string parameterName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(parameterName)) return false;
        var parameters = targetAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == parameterName && parameters[i].type == AnimatorControllerParameterType.Bool)
                return true;
        }
        return false;
    }
}
