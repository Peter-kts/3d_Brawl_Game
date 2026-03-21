/*
 * ============================================================================
 * LOCKONSYSTEM.CS - Threat-based focus system (NO hard lock-on)
 * ============================================================================
 *
 * WHAT THIS DOES:
 * ---------------
 * Tracks nearby enemies (via OverlapSphere + EnemyHealth), scores them by
 * distance/angle/screen position/recent interaction, and exposes a single
 * "soft target" (SoftTarget) that other systems read. The player chooses
 * when to lock on (LT/RMB = SetTargetToLookAt), when to cycle (LT again =
 * CycleToNextTargetInLookDirection), and when to clear (R3/Tab = ReleaseFocus).
 * The lock is sticky — it never auto-switches unless the target leaves range or dies.
 *
 * THREAT FOCUS PHILOSOPHY:
 * ------------------------
 * This is NOT a classic lock-on. There is no target tethering.
 *
 * Instead, it provides AWARENESS:
 *   - Tracks nearby threats (EnemyHealth within detectionRadius)
 *   - Scores them by relevance (distance, angle, screen center, recent interaction)
 *   - Exposes one "soft focus" target (SoftTarget) for other systems
 *   - NEVER forces camera or player rotation
 *
 * Other systems USE this information but decide for themselves:
 *   - PlayerController: Soft auto-face within limits, dash toward target
 *   - Combat: Direction sampling (no auto-aim), throw at SoftTarget
 *   - Camera: May bias toward threats (never snap)
 *
 * The player must still manually:
 *   - Position themselves, maintain facing, manage spacing
 *
 * FLOW (high level):
 *   Update (throttled) -> UpdateThreatTracking (OverlapSphere, score, sort)
 *   -> stickiness logic decides if SoftTarget changes.
 *   Player input -> SetTargetToLookAt / CycleToNextTargetInLookDirection /
 *   ReleaseFocus -> clears SoftTarget (player releases lock).
 *
 * ============================================================================
 */

using UnityEngine;
using System.Collections.Generic;

public class LockOnSystem : MonoBehaviour
{
    // ========================================================================
    // THREAT DETECTION SETTINGS
    // OverlapSphere finds colliders in range; we keep only those with EnemyHealth.
    // ========================================================================

    [Header("Detection")]
    [Tooltip("Maximum distance to track threats")]
    public float detectionRadius = 15f;
    
    [Tooltip("Threats beyond this distance are deprioritized")]
    public float preferredRange = 8f;
    
    [Tooltip("Which layers contain threats")]
    public LayerMask threatMask = ~0;
    
    [Tooltip("How often to update threat detection (times per second). Lower = better performance.")]
    [Range(5f, 30f)]
    public float detectionRate = 10f;
    
    [Header("Focus Cone")]
    [Tooltip("Half-angle of the focus cone where soft auto-facing works (used by PlayerController/Combat)")]
    public float focusConeAngle = 60f;

    [Tooltip("Max range for snap-on-enter. HasThreatInSnapCone uses focusConeAngle + this range.")]
    public float snapOnEnterMaxRange = 12f;

    [Header("Threat Scoring")]
    // Score = weighted sum of distance, angle to stick direction, and recent interaction.
    [Tooltip("Weight for distance scoring (closer = higher)")]
    public float distanceWeight = 1f;

    [Tooltip("Weight for angle scoring (more aligned with stick direction = higher; falls back to player forward when stick is neutral)")]
    public float angleWeight = 0.8f;
    
    [Tooltip("Bonus for threats that recently attacked or were attacked")]
    public float recentInteractionBonus = 2f;

    [Tooltip("How long interaction bonus lasts (seconds)")]
    public float interactionMemory = 3f;

    [Header("Lock-On Indicator")]
    [Tooltip("Prefab to show above the locked target. Leave null for an auto-generated gold ring.")]
    public GameObject lockOnIndicatorPrefab;
    [Tooltip("World-space offset from the locked target's position (above head)")]
    public Vector3 lockOnIndicatorOffset = new Vector3(0f, 2.2f, 0f);


    // ========================================================================
    // PUBLIC PROPERTIES (read by PlayerController, Combat, Camera, etc.)
    // ========================================================================

    /// <summary>The current soft focus target (one enemy transform). Null when not locked on.</summary>
    public Transform SoftTarget { get; private set; }
    public bool HasSoftTarget => SoftTarget != null;
    /// <summary>True when hard lock-on is active (Dark Souls style). Player explicitly locked onto SoftTarget.</summary>
    public bool IsLockedOn { get; private set; }
    /// <summary>
    /// True while the player is temporarily free-looking (R3 held).
    /// Lock-on target is preserved — camera just orbits freely until R3 again or an attack.
    /// </summary>
    public bool IsFreeLooking { get; private set; }
    /// <summary>All threats currently in range, sorted by score descending (best first).</summary>
    public List<ThreatInfo> TrackedThreats { get; private set; } = new List<ThreatInfo>();

    // Legacy compatibility
    public Transform Target => SoftTarget;
    public bool HasTarget => HasSoftTarget;

    // ========================================================================
    // THREAT INFO STRUCT
    // One entry per unique enemy in range; filled during UpdateThreatTracking.
    // ========================================================================

    public struct ThreatInfo
    {
        public Transform transform;  // The enemy's transform (EnemyHealth.transform)
        public float score;          // Combined score (higher = better candidate)
        public float distance;      // XZ distance from us
        public float angle;         // Angle from camera forward (degrees)
    }

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================

    private Dictionary<Transform, float> recentInteractions = new Dictionary<Transform, float>(); // Maps enemy transform → Time.time of last interaction; drives the interaction bonus in scoring
    private List<Transform> interactionCleanupBuffer = new List<Transform>();                       // Reused by CleanupInteractionMemory to avoid allocating during cleanup

    private HashSet<Transform> trackedSet = new HashSet<Transform>(); // Prevents duplicate entries when an enemy has multiple colliders
    private Camera mainCamera;
    private PlayerController playerController;

    private float nextDetectionTime;  // Time.time of the next allowed threat scan; throttles detection to detectionRate Hz

    private GameObject lockOnIndicatorInstance; // Instantiated once; repositioned/shown each frame when locked on
    private Transform lockOnIndicatorLastTarget; // Tracks when the locked target changes so we can refresh the height cache
    private float lockOnIndicatorCachedTopY;     // Cached height above target.position for the current locked target

    private const int MAX_COLLIDERS = 32;                                // Max simultaneous overlaps; increase if the scene has more than ~32 enemies at once
    private Collider[] colliderBuffer = new Collider[MAX_COLLIDERS];     // Reused every detection tick to avoid per-frame allocation

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Start()
    {
        mainCamera = Camera.main;
        playerController = GetComponent<PlayerController>();
        InitializeLockOnIndicator();
    }

    void InitializeLockOnIndicator()
    {
        if (lockOnIndicatorPrefab != null)
            lockOnIndicatorInstance = Instantiate(lockOnIndicatorPrefab);
        else
            lockOnIndicatorInstance = CreateDefaultIndicator();

        if (lockOnIndicatorInstance != null)
            lockOnIndicatorInstance.SetActive(false);
    }

    void Update()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // Destroyed-target fast-clear. Unity's == null returns true for destroyed objects.
        // Without this, a destroyed SoftTarget persists until the next 10 Hz tick (up to 100 ms).
        if (SoftTarget == null)
            SoftTarget = null;  // Replace the destroyed wrapper with a true C# null.

        // Run threat detection at fixed rate (e.g. 10 Hz) instead of every frame for performance.
        if (Time.time >= nextDetectionTime)
        {
            UpdateThreatTracking();
            CleanupInteractionMemory();
            nextDetectionTime = Time.time + (1f / detectionRate);
        }

        UpdateLockOnIndicator();
    }

    // ========================================================================
    // THREAT TRACKING
    // Finds all enemies in range, scores them, sorts by score, then applies
    // stickiness so we don't flicker between two close-scoring targets.
    // ========================================================================

    void UpdateThreatTracking()
    {
        TrackedThreats.Clear();
        trackedSet.Clear();

        // OverlapSphere: all colliders in radius on threatMask layers (no triggers).
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            detectionRadius,
            colliderBuffer,
            threatMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = colliderBuffer[i];
            if (col == null) continue;
            if (col.transform == transform) continue;  // Ignore our own collider

            var enemyHealth = col.GetComponent<EnemyHealth>();
            if (enemyHealth == null) continue;
            if (enemyHealth.IsDying) continue;

            Transform threatTransform = enemyHealth.transform;

            // One enemy can have multiple colliders; we only want one ThreatInfo per transform.
            if (!trackedSet.Add(threatTransform)) continue;

            float score = CalculateThreatScore(threatTransform, out float distance, out float angle);

            TrackedThreats.Add(new ThreatInfo
            {
                transform = threatTransform,
                score = score,
                distance = distance,
                angle = angle
            });
        }

        // Best candidate first (highest score).
        TrackedThreats.Sort((a, b) => b.score.CompareTo(a.score));

        if (TrackedThreats.Count == 0)
        {
            SoftTarget = null;
            IsLockedOn = false;
            return;
        }

        Transform candidate = TrackedThreats[0].transform;

        if (SoftTarget == null)
        {
            // If hard locked on and target was destroyed/left range, re-acquire the best remaining enemy.
            // Otherwise, player must explicitly press LT — never auto-assign.
            if (IsLockedOn)
                SoftTarget = candidate;
            return;
        }

        TryUpdateSoftTarget(candidate);
    }

    /// <summary>
    /// Decides whether SoftTarget should change this tick.
    /// The lock is fully sticky — only switches if the current target left range or was destroyed.
    /// The player controls all intentional switches via LT (cycle) or R3/Tab (release).
    /// Call only after TrackedThreats is sorted and non-empty, and only when SoftTarget != null.
    /// </summary>
    void TryUpdateSoftTarget(Transform candidate)
    {
        bool currentStillTracked = false;
        foreach (var t in TrackedThreats)
        {
            if (t.transform == SoftTarget) { currentStillTracked = true; break; }
        }

        if (!currentStillTracked)
        {
            // Target left range or was destroyed — auto-acquire the best available enemy.
            // The player shouldn't drop out of lock-on just because they killed their target.
            SoftTarget = candidate;
        }
        // Otherwise: keep current target. Only the player changes it (LT to cycle, R3/Tab to release).
    }

    // ========================================================================
    // PLAYER INPUT API (called from PlayerController when player presses LT, R3, etc.)
    // ========================================================================

    /// <summary>
    /// Set soft target to the enemy the camera is looking at (raycast from camera center, or closest by angle).
    /// Call when the player presses LT (or equivalent) to snap focus to current aim.
    /// </summary>
    public void SetTargetToLookAt()
    {
        // Ensure we have a camera to raycast from (used for screen-center aim)
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        // Ray from camera through center of screen (0.5, 0.5 = middle of viewport)
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float maxDist = detectionRadius;
        // Only consider colliders on threat layers; ignore triggers so we don't lock onto zones
        if (Physics.Raycast(ray, out RaycastHit hit, maxDist, threatMask, QueryTriggerInteraction.Ignore))
        {
            // Hit something: must be an enemy (EnemyHealth on the hit collider's GameObject)
            var enemyHealth = hit.collider.GetComponent<EnemyHealth>();
            // EnemyHealth presence is sufficient: if the collider has EnemyHealth, it cannot be the player.
            if (enemyHealth != null && !enemyHealth.IsDying)
            {
                SoftTarget = enemyHealth.transform;
                return;
            }
        }

        // No direct raycast hit: fall back to the tracked enemy closest to camera center.
        Transform best = GetBestTrackedByAngle();
        if (best == null) { SoftTarget = null; return; }
        SoftTarget = best;
    }

    /// <summary>
    /// Returns the tracked threat whose XZ direction is closest to the camera's forward axis.
    /// Used as the fallback in SetTargetToLookAt when no raycast hit lands on an enemy.
    /// Returns null if TrackedThreats is empty.
    /// </summary>
    Transform GetBestTrackedByAngle()
    {
        if (TrackedThreats.Count == 0) return null;

        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.0001f) camForward = transform.forward;
        camForward.Normalize();

        // First pass: prefer enemies that are actually visible (not behind walls).
        int bestIdx = -1;
        float bestAngle = float.MaxValue;
        for (int i = 0; i < TrackedThreats.Count; i++)
        {
            Vector3 toThreat = TrackedThreats[i].transform.position - transform.position;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude < 0.01f) continue;
            float angle = Vector3.Angle(camForward, toThreat.normalized);
            if (angle < bestAngle && HasLineOfSight(TrackedThreats[i].transform))
            {
                bestAngle = angle;
                bestIdx = i;
            }
        }

        // Fallback: no visible enemy found — pick closest by angle ignoring LOS.
        if (bestIdx < 0)
        {
            bestAngle = float.MaxValue;
            for (int i = 0; i < TrackedThreats.Count; i++)
            {
                Vector3 toThreat = TrackedThreats[i].transform.position - transform.position;
                toThreat.y = 0f;
                if (toThreat.sqrMagnitude < 0.01f) continue;
                float angle = Vector3.Angle(camForward, toThreat.normalized);
                if (angle < bestAngle) { bestAngle = angle; bestIdx = i; }
            }
        }

        return bestIdx >= 0 ? TrackedThreats[bestIdx].transform : null;
    }

    /// <summary>
    /// True if there is a clear sightline from the player's eye level to the target.
    /// Casts against all non-trigger geometry, ignoring threat-layer colliders so
    /// enemies don't block each other's line of sight.
    /// </summary>
    bool HasLineOfSight(Transform threat)
    {
        Vector3 eyePos = transform.position + Vector3.up * 1.5f;
        Vector3 targetPos = threat.position + Vector3.up * 0.8f;
        Vector3 dir = targetPos - eyePos;
        float dist = dir.magnitude;
        if (dist < 0.1f) return true;

        // Cast against everything except threats (enemies don't occlude each other) and triggers.
        int obstacleMask = ~threatMask;
        return !Physics.Raycast(eyePos, dir / dist, dist - 0.1f, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Toggle hard lock-on: acquire the best target if not locked, release if already locked.
    /// Call when the player taps LT.
    /// </summary>
    public void ToggleLockOn()
    {
        if (IsLockedOn)
            ReleaseFocus();
        else
            AcquireLockOn();
    }

    void AcquireLockOn()
    {
        SetTargetToLookAt();
        if (SoftTarget != null)
            IsLockedOn = true;
    }

    /// <summary>
    /// Toggle free-look: temporarily lets the camera orbit freely while keeping the lock-on target.
    /// Call on R3 press. A second R3 press or any attack call ExitFreeLook() to snap back.
    /// Has no effect if not currently locked on.
    /// </summary>
    public void ToggleFreeLook()
    {
        if (!IsLockedOn) return;
        IsFreeLooking = !IsFreeLooking;
    }

    /// <summary>Snap camera back to tracking the locked target. Call on attack or second R3 press.</summary>
    public void ExitFreeLook()
    {
        IsFreeLooking = false;
    }

    /// <summary>
    /// Fully release lock-on (LT toggle or Tab). Also clears free-look.
    /// </summary>
    public void ReleaseFocus()
    {
        SoftTarget = null;
        IsLockedOn = false;
        IsFreeLooking = false;
    }

    /// <summary>
    /// Cycle to the enemy that is angularly nearest clockwise (right) of the current target
    /// when viewed from above the player. Camera-independent.
    /// </summary>
    public void CycleRight() => CycleByWorldAngle(1);

    /// <summary>
    /// Cycle to the enemy that is angularly nearest counter-clockwise (left) of the current target
    /// when viewed from above the player. Camera-independent.
    /// </summary>
    public void CycleLeft() => CycleByWorldAngle(-1);

    /// <summary>
    /// Picks the enemy with the smallest signed world-space angular offset from the current target
    /// in the requested direction (measured around the player's Y axis).
    /// direction: +1 = clockwise / right, -1 = counter-clockwise / left.
    /// Always finds someone — wraps around if no enemy exists in that arc.
    /// </summary>
    void CycleByWorldAngle(int direction)
    {
        if (SoftTarget == null || TrackedThreats.Count < 2) return;

        Vector3 toCurrentTarget = SoftTarget.position - transform.position;
        toCurrentTarget.y = 0f;
        if (toCurrentTarget.sqrMagnitude < 0.01f) return;

        Transform best = null;
        float bestAngle = 360f;

        foreach (var threat in TrackedThreats)
        {
            if (threat.transform == SoftTarget) continue;
            Vector3 toThreat = threat.transform.position - transform.position;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude < 0.01f) continue;

            // Signed angle around Y: positive = clockwise, negative = counter-clockwise.
            // Multiply by direction so "forward" is always the requested sweep direction.
            float signed = Vector3.SignedAngle(toCurrentTarget, toThreat, Vector3.up) * direction;

            // Normalise to (0, 360] so every candidate is ahead in the sweep.
            if (signed <= 0f) signed += 360f;

            if (signed < bestAngle) { bestAngle = signed; best = threat.transform; }
        }

        if (best != null) SoftTarget = best;
    }

    // ========================================================================
    // THREAT SCORING
    // Combines distance (closer preferred within preferredRange), angle to
    // camera forward, screen-center proximity, and recent interaction bonus.
    // ========================================================================

    float CalculateThreatScore(Transform threat, out float distance, out float angle)
    {
        Vector3 toThreat = threat.position - transform.position;
        toThreat.y = 0f;
        distance = toThreat.magnitude;

        float score = ScoreByDistance(distance)
                    + ScoreByAngle(toThreat, distance, out angle)
                    + ScoreByRecentInteraction(threat);
        return score;
    }

    /// <summary>Higher score for threats closer than preferredRange; diminishing score beyond it.</summary>
    float ScoreByDistance(float distance)
    {
        if (distance < 0.1f)
            return distanceWeight;                                                           // Essentially on top of us — full score
        if (distance <= preferredRange)
            return distanceWeight * (1f - (distance / preferredRange) * 0.5f);              // Sweet spot: score tapers 1.0 → 0.5 as distance grows to preferredRange
        float beyondRatio = (distance - preferredRange) / (detectionRadius - preferredRange); // 0 at preferredRange edge, 1 at detection boundary
        return distanceWeight * (0.5f - beyondRatio * 0.5f);                                // Beyond sweet spot: score continues tapering 0.5 → 0 at detection edge
    }

    /// <summary>Higher score for threats more aligned with the movement stick direction (camera-relative). Falls back to player forward when stick is neutral.</summary>
    float ScoreByAngle(Vector3 toThreat, float distance, out float angle)
    {
        // Build reference direction from stick (camera-relative), same as GetBestThreatInFront in Combat.
        Vector3 referenceDir = transform.forward;
        if (playerController != null)
        {
            Vector2 stick = playerController.GetStickInput();
            if (stick.sqrMagnitude > 0.01f && mainCamera != null)
            {
                Vector3 camForward = mainCamera.transform.forward; camForward.y = 0f; camForward.Normalize();
                Vector3 camRight   = mainCamera.transform.right;   camRight.y   = 0f; camRight.Normalize();
                Vector3 dir = camForward * stick.y + camRight * stick.x;
                if (dir.sqrMagnitude > 0.001f)
                    referenceDir = dir.normalized;
            }
        }

        if (distance > 0.1f)
        {
            toThreat.Normalize();
            angle = Vector3.Angle(referenceDir, toThreat);
            return angleWeight * (1f - (angle / 180f));
        }
        angle = 0f;
        return angleWeight;
    }

    /// <summary>Bonus score for threats recently hit or that hit the player (fades over interactionMemory seconds).</summary>
    float ScoreByRecentInteraction(Transform threat)
    {
        if (!recentInteractions.TryGetValue(threat, out float lastInteraction)) return 0f;
        float timeSince = Time.time - lastInteraction;
        if (timeSince >= interactionMemory) return 0f;
        return recentInteractionBonus * (1f - (timeSince / interactionMemory));
    }

    // ========================================================================
    // INTERACTION TRACKING
    // Combat/throw call RegisterInteraction when we hit or get hit by a threat;
    // that threat's score gets a bonus for interactionMemory seconds.
    // ========================================================================

    public void RegisterInteraction(Transform threat)
    {
        if (threat == null) return;
        recentInteractions[threat] = Time.time;
    }

    void CleanupInteractionMemory()
    {
        interactionCleanupBuffer.Clear();
        foreach (var kvp in recentInteractions)
        {
            if (Time.time - kvp.Value > interactionMemory)
                interactionCleanupBuffer.Add(kvp.Key);
        }
        foreach (var key in interactionCleanupBuffer)
            recentInteractions.Remove(key);
    }

    // ========================================================================
    // UTILITY METHODS (for PlayerController, Combat, etc.)
    // All returned lists are reused buffers - copy if you need to keep them.
    // ========================================================================

    private List<ThreatInfo> threatsInConeBuffer = new List<ThreatInfo>();
    private List<ThreatInfo> topThreatsBuffer = new List<ThreatInfo>();

    /// <summary>Threats whose angle from camera forward is &lt;= coneAngle. List is reused each call.</summary>
    public List<ThreatInfo> GetThreatsInCone(float coneAngle)
    {
        threatsInConeBuffer.Clear();
        foreach (var threat in TrackedThreats)
        {
            if (threat.angle <= coneAngle)
                threatsInConeBuffer.Add(threat);
        }
        return threatsInConeBuffer;
    }

    /// <summary>First N threats by score (TrackedThreats is already sorted). List is reused each call.</summary>
    public List<ThreatInfo> GetTopThreats(int count)
    {
        topThreatsBuffer.Clear();
        int max = Mathf.Min(count, TrackedThreats.Count);
        for (int i = 0; i < max; i++)
            topThreatsBuffer.Add(TrackedThreats[i]);
        return topThreatsBuffer;
    }

    /// <summary>True if any tracked threat is within focusConeAngle and within snapOnEnterMaxRange (e.g. for "snap on enter combat").</summary>
    public bool HasThreatInSnapCone()
    {
        float range = Mathf.Min(snapOnEnterMaxRange, detectionRadius);
        foreach (var threat in TrackedThreats)
        {
            if (threat.transform == null) continue;
            if (threat.angle <= focusConeAngle && threat.distance <= range)
                return true;
        }
        return false;
    }

    // ========================================================================
    // LOCK-ON INDICATOR
    // ========================================================================

    void UpdateLockOnIndicator()
    {
        if (lockOnIndicatorInstance == null) return;

        if (IsLockedOn && SoftTarget != null)
        {
            // Refresh the height cache whenever the locked target changes.
            if (SoftTarget != lockOnIndicatorLastTarget)
            {
                lockOnIndicatorLastTarget = SoftTarget;
                var r = SoftTarget.GetComponentInChildren<Renderer>();
                lockOnIndicatorCachedTopY = r != null
                    ? r.bounds.max.y - SoftTarget.position.y + 0.15f
                    : lockOnIndicatorOffset.y;
            }

            lockOnIndicatorInstance.SetActive(true);
            lockOnIndicatorInstance.transform.position = SoftTarget.position
                + new Vector3(lockOnIndicatorOffset.x, lockOnIndicatorCachedTopY, lockOnIndicatorOffset.z);
            lockOnIndicatorInstance.transform.Rotate(Vector3.up, 90f * Time.deltaTime, Space.World);
        }
        else
        {
            lockOnIndicatorInstance.SetActive(false);
        }
    }

    /// <summary>
    /// Creates a simple gold spinning ring using a LineRenderer.
    /// Used automatically when no lockOnIndicatorPrefab is assigned.
    /// You can replace this by assigning any prefab to lockOnIndicatorPrefab in the inspector.
    /// </summary>
    GameObject CreateDefaultIndicator()
    {
        var root = new GameObject("LockOnIndicator_Default");
        var lr = root.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.widthMultiplier = 0.04f;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        // Use a simple unlit shader so the ring is always visible.
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        lr.material = new Material(shader);
        lr.startColor = lr.endColor = new Color(1f, 0.85f, 0f, 1f); // Gold

        int segments = 24;
        lr.positionCount = segments;
        float radius = 0.5f;
        for (int i = 0; i < segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }

        return root;
    }
}
