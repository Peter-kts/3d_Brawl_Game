/*
 * ============================================================================
 * LOCKONSYSTEM.CS - Threat-based focus system (NO hard lock-on)
 * ============================================================================
 * 
 * THREAT FOCUS PHILOSOPHY:
 * ------------------------
 * 
 * This is NOT a lock-on system. There is no target tethering.
 * 
 * Instead, it provides AWARENESS:
 *   - Tracks nearby threats
 *   - Scores them by relevance (distance, angle, recent interaction)
 *   - Provides a "soft focus" target for other systems
 *   - NEVER forces camera or player rotation
 * 
 * Other systems USE this information but make their own decisions:
 *   - PlayerController: Soft auto-face within limits
 *   - Combat: Direction sampling (no auto-aim)
 *   - Camera: May bias toward threats (never snap)
 * 
 * The player must still manually:
 *   - Position themselves
 *   - Maintain facing
 *   - Manage spacing
 * 
 * ============================================================================
 */

using UnityEngine;
using System.Collections.Generic;

public class LockOnSystem : MonoBehaviour
{
    // ========================================================================
    // THREAT DETECTION SETTINGS
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
    [Tooltip("Half-angle of the focus cone where soft auto-facing works")]
    public float focusConeAngle = 60f;

    [Tooltip("Max range for snap-on-enter (focus cone + this range). Snap when entering combat if a threat is in focus cone within this distance.")]
    public float snapOnEnterMaxRange = 12f;

    [Header("Threat Scoring")]
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
    [Tooltip("New top-scored target must beat current target's score by this margin to switch (reduces flicker)")]
    public float switchThreshold = 0.2f;
    [Tooltip("After LT (look-at) target, suppress auto-switch for this duration so focus doesn't jump to the other enemy in cone")]
    public float lookAtOverrideDuration = 0.8f;

    // ========================================================================
    // PUBLIC PROPERTIES
    // ========================================================================
    
    public Transform SoftTarget { get; private set; }
    public bool HasSoftTarget => SoftTarget != null;
    public List<ThreatInfo> TrackedThreats { get; private set; } = new List<ThreatInfo>();
    
    // Legacy compatibility
    public Transform Target => SoftTarget;
    public bool HasTarget => HasSoftTarget;

    // ========================================================================
    // THREAT INFO STRUCT
    // ========================================================================
    
    public struct ThreatInfo
    {
        public Transform transform;
        public float score;
        public float distance;
        public float angle;
    }

    // ========================================================================
    // PRIVATE STATE
    // ========================================================================
    
    private Dictionary<Transform, float> recentInteractions = new Dictionary<Transform, float>();
    private List<Transform> interactionCleanupBuffer = new List<Transform>();
    private HashSet<Transform> trackedSet = new HashSet<Transform>();
    private Camera mainCamera;
    
    // Performance: Throttled detection
    private float nextDetectionTime;

    // Stickiness: score of current SoftTarget when selected (for hysteresis)
    private float currentTargetScore;
    // After SetTargetToLookAt: don't auto-switch to a different target until this time
    private float lookAtOverrideEndTime;

    // Performance: Pre-allocated physics array (avoids GC)
    private const int MAX_COLLIDERS = 32;
    private Collider[] colliderBuffer = new Collider[MAX_COLLIDERS];

    // ========================================================================
    // UNITY LIFECYCLE
    // ========================================================================
    
    void Start()
    {
        mainCamera = Camera.main;
    }
    
    void Update()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        
        // Throttled detection for performance (10Hz default instead of 60+Hz)
        if (Time.time >= nextDetectionTime)
        {
            UpdateThreatTracking();
            CleanupInteractionMemory();
            nextDetectionTime = Time.time + (1f / detectionRate);
        }
    }

    // ========================================================================
    // THREAT TRACKING
    // ========================================================================
    
    void UpdateThreatTracking()
    {
        TrackedThreats.Clear();
        trackedSet.Clear();
        
        // Use NonAlloc version to avoid GC allocations every frame
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
            if (col.transform == transform) continue;
            
            var enemyHealth = col.GetComponentInParent<EnemyHealth>();
            if (enemyHealth == null) continue;
            
            Transform threatTransform = enemyHealth.transform;
            
            // Skip duplicates (multiple colliders on same enemy)
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
        
        TrackedThreats.Sort((a, b) => b.score.CompareTo(a.score));

        // Apply stickiness: only switch when current is lost or new top clearly wins
        if (TrackedThreats.Count == 0)
        {
            SoftTarget = null;
            return;
        }

        Transform candidate = TrackedThreats[0].transform;
        float candidateScore = TrackedThreats[0].score;

        // Don't auto-pick when not locked on; player locks on with LT
        if (SoftTarget == null)
            return;

        // Current target lost (no longer in list or invalid)?
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

        if (!currentStillTracked || SoftTarget == null)
        {
            SoftTarget = candidate;
            currentTargetScore = candidateScore;
            return;
        }

        // Same target: keep and refresh score
        if (candidate == SoftTarget)
        {
            currentTargetScore = candidateScore;
            return;
        }

        // After LT look-at: keep chosen target for a short time so it doesn't cycle to "next best"
        if (Time.time < lookAtOverrideEndTime && currentStillTracked)
        {
            currentTargetScore = currentScore;
            return;
        }

        // Different target: switch only if new one wins by margin
        if (candidateScore > currentTargetScore + switchThreshold)
        {
            SoftTarget = candidate;
            currentTargetScore = candidateScore;
        }
    }

    /// <summary>
    /// Set soft target to the enemy the camera is looking at (raycast from camera center, or closest by angle).
    /// Call when the player presses LT (or equivalent) to snap focus to current aim.
    /// </summary>
    public void SetTargetToLookAt()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

        // Raycast from camera through screen center
        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float maxDist = detectionRadius;
        if (Physics.Raycast(ray, out RaycastHit hit, maxDist, threatMask, QueryTriggerInteraction.Ignore))
        {
            var enemyHealth = hit.collider.GetComponentInParent<EnemyHealth>();
            if (enemyHealth != null && enemyHealth.transform != transform)
            {
                SoftTarget = enemyHealth.transform;
                currentTargetScore = CalculateThreatScore(SoftTarget, out _, out _);
                lookAtOverrideEndTime = Time.time + lookAtOverrideDuration;
                return;
            }
        }

        // No hit: pick tracked threat with smallest angle to camera forward
        if (TrackedThreats.Count == 0)
        {
            SoftTarget = null;
            return;
        }
        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.0001f) camForward = transform.forward;
        camForward.Normalize();

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

        Vector3 camForward = mainCamera.transform.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude < 0.0001f) camForward = transform.forward;
        camForward.Normalize();

        // Sort by angle to camera center (ascending) - reuse a temp list
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

        int currentIdx = -1;
        for (int i = 0; i < threatsByAngleBuffer.Count; i++)
        {
            if (threatsByAngleBuffer[i].transform == SoftTarget)
            {
                currentIdx = i;
                break;
            }
        }

        int nextIdx = currentIdx < 0 ? 0 : (currentIdx + 1) % threatsByAngleBuffer.Count;
        Transform next = threatsByAngleBuffer[nextIdx].transform;
        SoftTarget = next;
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
    /// Clear lock-on. Player returns to free roam; next LT will lock on to who they're looking at.
    /// </summary>
    public void ReleaseFocus()
    {
        SoftTarget = null;
    }

    // Buffer for CycleToNextTargetInLookDirection (angle-sorted list)
    private List<(Transform transform, float angle)> threatsByAngleBuffer = new List<(Transform, float)>();

    float CalculateThreatScore(Transform threat, out float distance, out float angle)
    {
        float score = 0f;
        
        Vector3 toThreat = threat.position - transform.position;
        toThreat.y = 0f;
        distance = toThreat.magnitude;
        
        // Distance scoring
        if (distance < 0.1f)
        {
            score += distanceWeight;
        }
        else if (distance <= preferredRange)
        {
            score += distanceWeight * (1f - (distance / preferredRange) * 0.5f);
        }
        else
        {
            float beyondRatio = (distance - preferredRange) / (detectionRadius - preferredRange);
            score += distanceWeight * (0.5f - beyondRatio * 0.5f);
        }
        
        // Angle scoring (use camera forward so soft lock follows camera aim)
        Vector3 forward = transform.forward;
        if (mainCamera != null)
        {
            forward = mainCamera.transform.forward;
        }
        forward.y = 0f;
        forward.Normalize();
        
        if (distance > 0.1f)
        {
            toThreat.Normalize();
            angle = Vector3.Angle(forward, toThreat);
            float angleScore = 1f - (angle / 180f);
            score += angleWeight * angleScore;
        }
        else
        {
            angle = 0f;
            score += angleWeight;
        }
        
        // Screen center scoring
        if (mainCamera != null)
        {
            Vector3 screenPos = mainCamera.WorldToViewportPoint(threat.position);
            if (screenPos.z > 0)
            {
                float screenCenterDist = Vector2.Distance(
                    new Vector2(screenPos.x, screenPos.y),
                    new Vector2(0.5f, 0.5f)
                );
                float screenScore = 1f - Mathf.Clamp01(screenCenterDist * 2f);
                score += screenCenterWeight * screenScore;
            }
        }
        
        // Recent interaction bonus
        if (recentInteractions.TryGetValue(threat, out float lastInteraction))
        {
            float timeSince = Time.time - lastInteraction;
            if (timeSince < interactionMemory)
            {
                float interactionScore = 1f - (timeSince / interactionMemory);
                score += recentInteractionBonus * interactionScore;
            }
        }
        
        return score;
    }

    // ========================================================================
    // INTERACTION TRACKING
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
            {
                interactionCleanupBuffer.Add(kvp.Key);
            }
        }
        
        foreach (var key in interactionCleanupBuffer)
        {
            recentInteractions.Remove(key);
        }
    }

    // ========================================================================
    // UTILITY METHODS
    // ========================================================================
    
    // Reusable lists to avoid GC allocations
    private List<ThreatInfo> threatsInConeBuffer = new List<ThreatInfo>();
    private List<ThreatInfo> topThreatsBuffer = new List<ThreatInfo>();
    
    /// <summary>
    /// Get all threats within the specified cone angle.
    /// WARNING: Returns a reused buffer - don't hold references to this list!
    /// </summary>
    public List<ThreatInfo> GetThreatsInCone(float coneAngle)
    {
        threatsInConeBuffer.Clear();
        
        foreach (var threat in TrackedThreats)
        {
            if (threat.angle <= coneAngle)
            {
                threatsInConeBuffer.Add(threat);
            }
        }
        
        return threatsInConeBuffer;
    }
    
    /// <summary>
    /// Get the top N threats by score.
    /// WARNING: Returns a reused buffer - don't hold references to this list!
    /// </summary>
    public List<ThreatInfo> GetTopThreats(int count)
    {
        topThreatsBuffer.Clear();
        
        int max = Mathf.Min(count, TrackedThreats.Count);
        for (int i = 0; i < max; i++)
        {
            topThreatsBuffer.Add(TrackedThreats[i]);
        }
        
        return topThreatsBuffer;
    }

    public bool HasThreatInSnapCone()
    {
        float range = Mathf.Min(snapOnEnterMaxRange, detectionRadius);
        foreach (var threat in TrackedThreats)
        {
            if (threat.transform == null) continue;
            if (threat.angle <= focusConeAngle && threat.distance <= range)
            {
                return true;
            }
        }
        return false;
    }
}
