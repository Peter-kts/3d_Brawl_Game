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
 * when to lock on (LT/RMB = SetTargetToLookAt) and when to clear (R3/Tab =
 * ReleaseFocus). While locked on, the system can auto-switch to a better
 * target only if it wins by a margin (stickiness), or the player can cycle
 * (CycleToNextTargetInLookDirection).
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
 *   ReleaseFocus -> update SoftTarget and lookAtOverrideEndTime.
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
    // Score = weighted sum of distance, angle to camera, screen-center proximity, and recent interaction.
    [Tooltip("Weight for distance scoring (closer = higher)")]
    public float distanceWeight = 1f;
    
    [Tooltip("Weight for angle scoring (more centered = higher)")]
    public float angleWeight = 0.8f;
    
    [Tooltip("Weight for screen-center proximity")]
    public float screenCenterWeight = 0.5f;
    
    [Tooltip("Bonus for threats that recently attacked or were attacked")]
    public float recentInteractionBonus = 2f;
    
    [Tooltip("How long interaction bonus lasts (seconds)")]
    public float interactionMemory = 3f;

    [Header("Target Stickiness")]
    // Prevents flicker: we only switch SoftTarget when the new top candidate wins by this much.
    [Tooltip("New top-scored target must beat current target's score by this margin to switch (reduces flicker)")]
    public float switchThreshold = 0.2f;
    [Tooltip("After LT (look-at) target, suppress auto-switch for this duration so focus doesn't jump to the other enemy in cone")]
    public float lookAtOverrideDuration = 0.8f;

    // ========================================================================
    // PUBLIC PROPERTIES (read by PlayerController, Combat, Camera, etc.)
    // ========================================================================

    /// <summary>The current soft focus target (one enemy transform). Null when not locked on.</summary>
    public Transform SoftTarget { get; private set; }
    public bool HasSoftTarget => SoftTarget != null;
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

    private float nextDetectionTime;       // Time.time of the next allowed threat scan; throttles detection to detectionRate Hz
    private float currentTargetScore;     // Score of the current SoftTarget; a new target must beat this plus switchThreshold to take over (prevents flicker)
    private float lookAtOverrideEndTime;  // After a manual LT press or cycle, auto-switching is suppressed until this time

    private const int MAX_COLLIDERS = 32;                                // Max simultaneous overlaps; increase if the scene has more than ~32 enemies at once
    private Collider[] colliderBuffer = new Collider[MAX_COLLIDERS];     // Reused every detection tick to avoid per-frame allocation

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================

    void Start()
    {
        mainCamera = Camera.main;
    }

    void Update()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        // Run threat detection at fixed rate (e.g. 10 Hz) instead of every frame for performance.
        if (Time.time >= nextDetectionTime)
        {
            UpdateThreatTracking();
            CleanupInteractionMemory();
            nextDetectionTime = Time.time + (1f / detectionRate);
        }
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
            return;
        }

        Transform candidate = TrackedThreats[0].transform;
        float candidateScore = TrackedThreats[0].score;

        // We never auto-assign SoftTarget; player must press LT to lock on. Until then, SoftTarget stays null.
        if (SoftTarget == null)
            return;

        // Is our current SoftTarget still in the list this frame? (e.g. still in range, not destroyed)
        bool currentStillTracked = false;
        float currentScore = 0f;
        foreach (var t in TrackedThreats)
        {
            if (t.transform == SoftTarget)
            {
                currentStillTracked = true;
                currentScore = t.score;
                break;
            }
        }

        // Current target lost (out of range or dead): switch to best available.
        if (!currentStillTracked || SoftTarget == null)
        {
            SoftTarget = candidate;
            currentTargetScore = candidateScore;
            return;
        }

        // Same target is still top: just refresh its score.
        if (candidate == SoftTarget)
        {
            currentTargetScore = candidateScore;
            return;
        }

        // Player recently used LT to pick this target: don't auto-switch for a short time.
        if (Time.time < lookAtOverrideEndTime && currentStillTracked)
        {
            currentTargetScore = currentScore;
            return;
        }

        // Different target is now top: switch only if it wins by switchThreshold (reduces flicker).
        if (candidateScore > currentTargetScore + switchThreshold)
        {
            SoftTarget = candidate;
            currentTargetScore = candidateScore;
        }
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
            // Reject if we hit ourselves (e.g. camera inside player or player collider in front); avoid locking onto player
            bool hitIsPlayer = hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform) || transform.IsChildOf(hit.collider.transform);
            if (enemyHealth != null && !hitIsPlayer)
            {
                SoftTarget = enemyHealth.transform;
                currentTargetScore = CalculateThreatScore(SoftTarget, out _, out _);
                // Briefly suppress auto-switch so chosen target doesn't immediately flip to another in cone
                lookAtOverrideEndTime = Time.time + lookAtOverrideDuration;
                return;
            }
        }

        // No valid ray hit (nothing in center, or hit was non-enemy/player): choose best tracked threat by angle to camera
        if (TrackedThreats.Count == 0)
        {
            SoftTarget = null;
            return;
        }
        // Camera forward in world (flattened to XZ for angle comparison)
        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.0001f) camForward = transform.forward;
        camForward.Normalize();

        // Find tracked threat with smallest angle to camera forward (closest to crosshair)
        int bestIdx = 0;
        float bestAngle = float.MaxValue;
        for (int i = 0; i < TrackedThreats.Count; i++)
        {
            Vector3 toThreat = TrackedThreats[i].transform.position - transform.position;
            toThreat.y = 0f;
            if (toThreat.sqrMagnitude < 0.01f) continue;
            toThreat.Normalize();
            float angle = Vector3.Angle(camForward, toThreat);
            if (angle < bestAngle)
            {
                bestAngle = angle;
                bestIdx = i;
            }
        }
        SoftTarget = TrackedThreats[bestIdx].transform;
        currentTargetScore = TrackedThreats[bestIdx].score;
        lookAtOverrideEndTime = Time.time + lookAtOverrideDuration;
    }

    /// <summary>
    /// When already locked on: cycle to the next target closest to camera center (excluding current).
    /// Order: all tracked threats sorted by angle to camera forward; next = (currentIndex + 1) % count.
    /// </summary>
    public void CycleToNextTargetInLookDirection()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null || TrackedThreats.Count == 0) return;

        // Camera forward in XZ for angle sorting
        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.0001f) camForward = transform.forward;
        camForward.Normalize();

        // Build list of (transform, angle to camera) and sort by angle so order = "left to right" on screen
        threatsByAngleBuffer.Clear();
        for (int i = 0; i < TrackedThreats.Count; i++)
        {
            Transform t = TrackedThreats[i].transform;
            Vector3 toThreat = t.position - transform.position;
            toThreat.y = 0f;
            float angle = toThreat.sqrMagnitude < 0.01f ? 0f : Vector3.Angle(camForward, toThreat.normalized);
            threatsByAngleBuffer.Add((t, angle));
        }
        threatsByAngleBuffer.Sort((a, b) => a.angle.CompareTo(b.angle));

        // Index of current soft target in the sorted list (-1 if not found)
        int currentIdx = -1;
        for (int i = 0; i < threatsByAngleBuffer.Count; i++)
        {
            if (threatsByAngleBuffer[i].transform == SoftTarget)
            {
                currentIdx = i;
                break;
            }
        }

        // Next target = (currentIndex + 1) wraparound; if current not in list, use first
        int nextIdx = currentIdx < 0 ? 0 : (currentIdx + 1) % threatsByAngleBuffer.Count;
        Transform next = threatsByAngleBuffer[nextIdx].transform;
        SoftTarget = next;
        // Keep score in sync with TrackedThreats for the new target
        foreach (var t in TrackedThreats)
        {
            if (t.transform == next)
            {
                currentTargetScore = t.score;
                break;
            }
        }
        lookAtOverrideEndTime = Time.time + lookAtOverrideDuration;
    }

    /// <summary>
    /// Clear lock-on. Called when player presses R3 or Tab. Next LT will lock on to who they're looking at.
    /// </summary>
    public void ReleaseFocus()
    {
        SoftTarget = null;
    }

    // Reused in CycleToNextTargetInLookDirection to sort threats by angle to camera (left-to-right order).
    private List<(Transform transform, float angle)> threatsByAngleBuffer = new List<(Transform, float)>();

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
                    + ScoreByScreenCenter(threat)
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

    /// <summary>Higher score for threats that are in front of the camera (angle to camera forward closer to 0).</summary>
    float ScoreByAngle(Vector3 toThreat, float distance, out float angle)
    {
        Vector3 forward = transform.forward;
        if (mainCamera != null) forward = mainCamera.transform.forward;
        forward.y = 0f;
        forward.Normalize();

        if (distance > 0.1f)
        {
            toThreat.Normalize();
            angle = Vector3.Angle(forward, toThreat);
            return angleWeight * (1f - (angle / 180f));
        }
        angle = 0f;
        return angleWeight;
    }

    /// <summary>Higher score for threats near the center of the screen (viewport 0.5, 0.5).</summary>
    float ScoreByScreenCenter(Transform threat)
    {
        if (mainCamera == null) return 0f;
        Vector3 screenPos = mainCamera.WorldToViewportPoint(threat.position);
        if (screenPos.z <= 0) return 0f;
        float screenCenterDist = Vector2.Distance(new Vector2(screenPos.x, screenPos.y), new Vector2(0.5f, 0.5f));
        return screenCenterWeight * (1f - Mathf.Clamp01(screenCenterDist * 2f));
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
}
