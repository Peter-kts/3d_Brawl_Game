/*
 * ============================================================================
 * THIRDPERSONCAMERA.CS - Follows the player smoothly
 * ============================================================================
 * 
 * CAMERA FUNDAMENTALS:
 * --------------------
 * 
 * In Unity, a Camera component renders the scene from its Transform's perspective.
 * For 3rd person games, we need to:
 *   1. FOLLOW: Keep the camera at a fixed offset from the player
 *   2. LOOK: Point the camera at something interesting (the player)
 * 
 * This script handles both with smooth interpolation (no jarring snaps).
 * 
 * WHY LATEUPDATE?
 * ---------------
 * Unity's update order: Update() → LateUpdate() → Render
 * 
 * - Player moves in Update()
 * - Camera follows in LateUpdate()
 * - This ensures camera sees the FINAL player position each frame
 * - If camera updated in Update(), it might use stale position data
 * 
 * ============================================================================
 */

using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCamera : MonoBehaviour
{
    // ========================================================================
    // SERIALIZED FIELDS (tweak these in the Inspector)
    // ========================================================================
    
    /*
     * target: The Transform to follow (usually the Player)
     * 
     * In Unity, you drag-and-drop the Player GameObject into this field
     * in the Inspector. Unity stores the reference automatically.
     */
    public Transform target;
    
    [Header("Auto Target")]
    [Tooltip("If target is not assigned, try to find the player automatically")]
    public bool autoFindTarget = true;
    [Tooltip("Tag to look for when auto-finding target (optional)")]
    public string playerTag = "Player";
    
    /*
     * offset: Position relative to target
     * 
     * Default (0, 2.5, -5.5) means:
     *   - X: 0 = directly behind (no left/right offset)
     *   - Y: 2.5 = above player's head
     *   - Z: -5.5 = behind player (negative Z is "back" in Unity)
     * 
     * Note: This is a WORLD-SPACE offset, not relative to player's rotation
     * For more advanced cameras, you'd rotate offset by player's rotation
     */
    public Vector3 offset = new Vector3(0f, 3.2f, -8.5f);
    [Tooltip("How far behind the target the camera sits. Overrides the Z component of offset.")]
    public float cameraDistance = 8.5f;
    [Tooltip("If true, offset is applied in the player's local space (so camera stays behind them)")]
    public bool useLocalOffset = true;

    // offset with Z replaced by -cameraDistance so the distance slider is the single source of truth.
    Vector3 CameraOffset => new Vector3(offset.x, offset.y, -cameraDistance);

    [Header("Orbit Controls")]
    [Tooltip("Rotate camera around the target with right stick / mouse")]
    public bool useOrbitControls = true;
    [Tooltip("Yaw speed (degrees per second) for right stick input")]
    public float yawSpeed = 160f;
    [Tooltip("Pitch speed (degrees per second) for right stick input")]
    public float pitchSpeed = 110f;
    [Tooltip("Mouse look sensitivity (degrees per pixel)")]
    public float mouseSensitivity = 0.15f;
    [Tooltip("Invert vertical look for mouse/stick")]
    public bool invertY = true;
    [Tooltip("Clamp pitch (vertical angle) between min and max degrees")]
    public Vector2 pitchLimits = new Vector2(-20f, 70f);
    
    /*
     * followSpeed/lookSpeed: How quickly the camera catches up
     * 
     * Higher = snappier, more responsive
     * Lower = smoother, more cinematic (but can feel "floaty")
     * 
     * These are used with exponential interpolation for consistent feel
     * regardless of framerate (more on this below)
     */
    public float followSpeed = 8f;
    public float lookSpeed = 8f;

    [Header("Look Settings")]
    [Tooltip("How high above the player to look (roughly chest/head level)")]
    public float lookHeight = 1.2f;

    [Header("Soft Target Look")]
    [Tooltip("Optional threat system for soft target camera bias")]
    public LockOnSystem threatSystem;
    [Tooltip("Bias the look point slightly toward the soft target")]
    public bool useSoftTargetLook = true;
    [Range(0f, 1f)]
    [Tooltip("How much to blend toward the soft target (0 = none, 1 = full)")]
    public float softTargetLookWeight = 0.35f;
    [Tooltip("Max angle from player forward to allow soft target look bias")]
    public float softTargetMaxAngle = 70f;
    [Tooltip("Max distance to allow soft target look bias")]
    public float softTargetMaxDistance = 12f;

    [Header("Hard Lock-On Camera")]
    [Tooltip("How fast the camera auto-rotates (yaw) to position behind the player when locked on. Higher = snappier.")]
    public float lockOnRotateSpeed = 4f;
    [Tooltip("How far toward the locked target the camera looks (0 = player only, 0.5 = midpoint, 1 = target only).")]
    [Range(0f, 1f)]
    public float lockOnLookBlend = 0.4f;
    [Tooltip("Pitch (up/down) speed when locked on. Right stick Y still controls vertical angle.")]
    public float lockOnPitchSpeed = 110f;
    [Tooltip("Vertical angle limits when locked on (min, max degrees). Tighter than free orbit to keep enemy in frame.")]
    public Vector2 lockOnPitchLimits = new Vector2(-10f, 50f);
    [Tooltip("Max degrees the camera tilts sideways when the right stick is held left/right while locked on.")]
    public float lockOnTiltAmount = 50f;
    [Tooltip("How fast the tilt snaps back to center when the stick is released (higher = snappier).")]
    public float lockOnTiltSnapSpeed = 8f;

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================
    
    private float yaw;
    private float pitch;
    private bool orbitInitialized;
    private float lockOnTiltYaw;        // Current smoothed tilt offset (degrees); blends toward target and snaps back to 0
    private PlayerController playerController; // Resolved lazily from target; source of LockOnTiltX

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    // Main camera loop: follow target position, compute look point, and smooth rotation.
    void LateUpdate()
    {
        // Ensure we have something to follow
        EnsureTarget();
        if (target == null) return;

        // --------------------------------------------------------------------
        // STEP 1: Calculate and move to desired position
        // --------------------------------------------------------------------
        
        // Base desired camera position from target + configured offset.
        Vector3 desiredPos = target.position + (useLocalOffset ? target.TransformDirection(CameraOffset) : CameraOffset);

        if (useOrbitControls)
        {
            // Orbit mode overrides base offset with yaw/pitch driven offset around target.
            InitializeOrbitIfNeeded();

            // Lazy-resolve threatSystem from the target if not assigned.
            if (threatSystem == null && target != null)
                threatSystem = target.GetComponent<LockOnSystem>();

            bool isHardLocked = threatSystem != null && threatSystem.IsLockedOn && threatSystem.SoftTarget != null;
            bool isFreeLooking = threatSystem != null && threatSystem.IsFreeLooking;

            if (isHardLocked && !isFreeLooking)
            {
                // Auto-rotate yaw so the camera sits behind the player, facing the locked target.
                // desiredYaw = angle from player toward target → places camera on the opposite side.
                Vector3 toTarget = threatSystem.SoftTarget.position - target.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.01f)
                {
                    float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
                    yaw = Mathf.LerpAngle(yaw, desiredYaw, 1f - Mathf.Exp(-lockOnRotateSpeed * Time.deltaTime));
                }

                // Right stick Y still controls pitch; X is handled by PlayerController (tap/hold).
                UpdateLockOnPitch();

                // Tilt: right stick held sideways adds a temporary yaw offset that snaps back.
                if (playerController == null && target != null)
                    playerController = target.GetComponent<PlayerController>();
                float targetTilt = playerController != null ? playerController.LockOnTiltX * lockOnTiltAmount : 0f;
                lockOnTiltYaw = Mathf.Lerp(lockOnTiltYaw, targetTilt, 1f - Mathf.Exp(-lockOnTiltSnapSpeed * Time.deltaTime));
            }
            else
            {
                // Free-look (R3 held) or not locked: full orbit control with right stick.
                // Tilt snaps to zero so it doesn't persist when returning from free-look.
                UpdateOrbitAngles();
                lockOnTiltYaw = Mathf.Lerp(lockOnTiltYaw, 0f, 1f - Mathf.Exp(-lockOnTiltSnapSpeed * Time.deltaTime));
            }

            Quaternion orbitRot = Quaternion.Euler(pitch, yaw + lockOnTiltYaw, 0f);
            desiredPos = target.position + (orbitRot * CameraOffset);
        }
        
        /*
         * EXPONENTIAL INTERPOLATION (Lerp with exponential decay):
         * 
         * Standard Lerp:
         *   Vector3.Lerp(current, target, t)
         *   - t is typically Time.deltaTime * speed
         *   - PROBLEM: Framerate-dependent! At 30fps vs 60fps, behavior differs
         * 
         * Exponential decay formula:
         *   1 - Mathf.Exp(-speed * Time.deltaTime)
         *   - Mathematically framerate-independent
         *   - Gives same visual result at any framerate
         *   - Higher speed = faster convergence
         * 
         * Why it works:
         *   - Exp(-x) approaches 0 as x increases
         *   - At high framerates: small deltaTime, many small steps
         *   - At low framerates: large deltaTime, fewer big steps
         *   - Total movement over 1 second is the same either way
         */
        transform.position = Vector3.Lerp(
            transform.position,        // Current position
            desiredPos,                // Target position
            1f - Mathf.Exp(-followSpeed * Time.deltaTime)  // Smoothing factor
        );

        // --------------------------------------------------------------------
        // STEP 2: Calculate look point (above the player)
        // --------------------------------------------------------------------
        
        // Default look point is a point above the target (chest/head framing).
        Vector3 lookPoint = target.position + Vector3.up * lookHeight;

        // Hard lock-on: look at a point blended between the player and the locked target.
        // This keeps both player and enemy in frame (Dark Souls style).
        if (threatSystem != null && threatSystem.IsLockedOn && threatSystem.SoftTarget != null)
        {
            Vector3 targetLookPoint = threatSystem.SoftTarget.position + Vector3.up * lookHeight;
            lookPoint = Vector3.Lerp(lookPoint, targetLookPoint, lockOnLookBlend);
        }
        else if (useSoftTargetLook)
        {
            // Soft look bias (subtle, not a lock).
            if (threatSystem == null && target != null)
                threatSystem = target.GetComponent<LockOnSystem>();

            if (threatSystem != null && threatSystem.HasSoftTarget)
            {
                Transform softTarget = threatSystem.SoftTarget;
                if (softTarget != null)
                {
                    Vector3 toTarget = softTarget.position - target.position;
                    toTarget.y = 0f;
                    float dist = toTarget.magnitude;

                    if (dist > 0.1f)
                    {
                        Vector3 forward = target.forward;
                        forward.y = 0f;
                        forward.Normalize();
                        toTarget.Normalize();

                        float angle = Vector3.Angle(forward, toTarget);
                        if (angle <= softTargetMaxAngle && dist <= softTargetMaxDistance)
                        {
                            float angleFactor = 1f - (angle / softTargetMaxAngle);
                            float distFactor = 1f - (dist / softTargetMaxDistance);
                            float blend = softTargetLookWeight * Mathf.Clamp01(angleFactor * distFactor);
                            Vector3 softLookPoint = softTarget.position + Vector3.up * lookHeight;
                            lookPoint = Vector3.Lerp(lookPoint, softLookPoint, blend);
                        }
                    }
                }
            }
        }

        /*
         * Quaternion.LookRotation():
         * Creates a rotation that "looks at" a direction
         * 
         * (lookPoint - transform.position) = direction FROM camera TO target
         * .normalized = make it unit length (required for LookRotation)
         * Vector3.up = which way is "up" (prevents camera from tilting sideways)
         */
        Quaternion desiredRot = Quaternion.LookRotation(
            (lookPoint - transform.position).normalized,
            Vector3.up
        );
        
        /*
         * Quaternion.Slerp (Spherical Linear Interpolation):
         * - Smoothly interpolates between two rotations
         * - "Slerp" for rotations, "Lerp" for positions
         * - Takes the shortest path on the rotation "sphere"
         * 
         * Same exponential decay formula for framerate independence
         */
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            1f - Mathf.Exp(-lookSpeed * Time.deltaTime)
        );
    }

    // Assign target when missing (tag first, then PlayerController fallback).
    void EnsureTarget()
    {
        if (target != null || !autoFindTarget) return;
        
        // Prefer tag lookup if a tag is specified
        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject tagged = GameObject.FindGameObjectWithTag(playerTag);
            if (tagged != null)
            {
                target = tagged.transform;
                return;
            }
        }
        
        // Fallback: find a PlayerController in the scene
        PlayerController player = FindObjectOfType<PlayerController>();
        if (player != null)
        {
            target = player.transform;
        }
    }

    // Seed yaw/pitch from current camera placement so orbit starts from current view.
    void InitializeOrbitIfNeeded()
    {
        if (orbitInitialized || target == null) return;
        
        Vector3 toCam = transform.position - target.position;
        if (toCam.sqrMagnitude < 0.001f) return;
        
        Vector3 flat = new Vector3(toCam.x, 0f, toCam.z);
        if (flat.sqrMagnitude > 0.001f)
        {
            yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }
        pitch = Mathf.Clamp(-Mathf.Asin(toCam.normalized.y) * Mathf.Rad2Deg, pitchLimits.x, pitchLimits.y);
        
        orbitInitialized = true;
    }
    
    // Read right stick/mouse input and update yaw/pitch with clamped vertical angle.
    void UpdateOrbitAngles()
    {
        Vector2 lookDelta = Vector2.zero;

        bool hasGamepad = Gamepad.current != null;

        if (hasGamepad)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            lookDelta += new Vector2(stick.x * yawSpeed, stick.y * pitchSpeed) * Time.deltaTime;
        }

        if (!hasGamepad && Mouse.current != null)
        {
            Vector2 mouse = Mouse.current.delta.ReadValue();
            lookDelta += mouse * mouseSensitivity;
        }

        if (invertY) lookDelta.y = -lookDelta.y;

        yaw += lookDelta.x;
        pitch = Mathf.Clamp(pitch + lookDelta.y, pitchLimits.x, pitchLimits.y);
    }

    // When locked on, yaw is auto-driven. Only read the vertical axis for pitch.
    void UpdateLockOnPitch()
    {
        float pitchDelta = 0f;

        if (Gamepad.current != null)
        {
            float stickY = Gamepad.current.rightStick.ReadValue().y;
            pitchDelta += stickY * lockOnPitchSpeed * Time.deltaTime;
        }
        else if (Mouse.current != null)
        {
            pitchDelta += Mouse.current.delta.ReadValue().y * mouseSensitivity;
        }

        if (invertY) pitchDelta = -pitchDelta;

        pitch = Mathf.Clamp(pitch + pitchDelta, lockOnPitchLimits.x, lockOnPitchLimits.y);
    }
}
