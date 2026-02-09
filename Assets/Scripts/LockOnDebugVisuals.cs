/*
 * ============================================================================
 * LOCKONDEBUGVISUALS.CS - Debug visualization for LockOnSystem
 * ============================================================================
 *
 * Draws focus cone, threat lines, and threat indicators when DebugSettings
 * enables them. Add this component alongside LockOnSystem to see threat
 * focus in the editor / at runtime. Optional; LockOnSystem works without it.
 *
 * ============================================================================
 */

using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(LockOnSystem))]
public class LockOnDebugVisuals : MonoBehaviour
{
    [Tooltip("LockOnSystem to visualize. Auto-found on this object if not set.")]
    public LockOnSystem lockOnSystem;

    private Camera mainCamera;
    private static Material sharedVisualizationMaterial;

    private GameObject coneVisual;
    private MeshFilter coneMeshFilter;
    private MeshRenderer coneRenderer;
    private List<LineRenderer> threatLines = new List<LineRenderer>();
    private List<GameObject> threatIndicators = new List<GameObject>();

    private Mesh cachedFocusConeMesh;
    private float cachedFocusConeAngle;
    private float cachedFocusConeRange;

    void Awake()
    {
        if (lockOnSystem == null) lockOnSystem = GetComponent<LockOnSystem>();
    }

    void Start()
    {
        mainCamera = Camera.main;
        EnsureSharedMaterial();
        CreateConeVisual();
    }

    void LateUpdate()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        if (lockOnSystem == null) return;
        UpdateVisualizations();
    }

    void OnDestroy()
    {
        if (cachedFocusConeMesh != null) Destroy(cachedFocusConeMesh);
        if (coneVisual != null) Destroy(coneVisual);
        foreach (var line in threatLines) if (line != null) Destroy(line.gameObject);
        foreach (var indicator in threatIndicators) if (indicator != null) Destroy(indicator);
    }

    static void EnsureSharedMaterial()
    {
        if (sharedVisualizationMaterial != null) return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        sharedVisualizationMaterial = new Material(shader);
    }

    static Material GetSharedMaterial()
    {
        EnsureSharedMaterial();
        return sharedVisualizationMaterial;
    }

    void CreateConeVisual()
    {
        coneVisual = new GameObject("FocusConeVisual");
        coneMeshFilter = coneVisual.AddComponent<MeshFilter>();
        coneRenderer = coneVisual.AddComponent<MeshRenderer>();
        coneRenderer.material = GetSharedMaterial();
        cachedFocusConeAngle = lockOnSystem.focusConeAngle;
        cachedFocusConeRange = lockOnSystem.preferredRange;
        cachedFocusConeMesh = CreateConeMesh(lockOnSystem.focusConeAngle, lockOnSystem.preferredRange, 24);
        coneMeshFilter.mesh = cachedFocusConeMesh;
    }

    static Mesh CreateConeMesh(float angle, float length, int segments)
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[segments + 2];
        vertices[0] = Vector3.zero;
        float halfAngleRad = angle * Mathf.Deg2Rad;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float currentAngle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
            float x = Mathf.Sin(currentAngle) * length;
            float z = Mathf.Cos(currentAngle) * length;
            vertices[i + 1] = new Vector3(x, 0f, z);
        }
        int[] triangles = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        return mesh;
    }

    void UpdateVisualizations()
    {
        DebugSettings debug = DebugSettings.Instance;
        var trackedThreats = lockOnSystem.TrackedThreats;
        Transform source = lockOnSystem.transform;

        UpdateConeVisual(debug, source, trackedThreats);
        UpdateThreatLines(debug, source, trackedThreats);
        UpdateThreatIndicators(debug, trackedThreats);
    }

    void UpdateConeVisual(DebugSettings debug, Transform source, List<LockOnSystem.ThreatInfo> trackedThreats)
    {
        if (coneVisual == null) return;
        bool shouldShow = debug.ShouldShow(debug.showFocusCone);
        coneVisual.SetActive(shouldShow);
        if (!shouldShow) return;

        coneVisual.transform.position = source.position + Vector3.up * 0.1f;
        Vector3 forward = source.forward;
        if (mainCamera != null) forward = mainCamera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f)
            coneVisual.transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        float focusConeAngle = lockOnSystem.focusConeAngle;
        float preferredRange = lockOnSystem.preferredRange;
        bool needsRebuild = cachedFocusConeMesh == null ||
            Mathf.Abs(cachedFocusConeAngle - focusConeAngle) > 0.1f ||
            Mathf.Abs(cachedFocusConeRange - preferredRange) > 0.1f;
        if (needsRebuild)
        {
            if (cachedFocusConeMesh != null) Destroy(cachedFocusConeMesh);
            cachedFocusConeAngle = focusConeAngle;
            cachedFocusConeRange = preferredRange;
            cachedFocusConeMesh = CreateConeMesh(focusConeAngle, preferredRange, 24);
            coneMeshFilter.mesh = cachedFocusConeMesh;
        }
        coneRenderer.material.color = debug.focusConeColor;
    }

    void UpdateThreatLines(DebugSettings debug, Transform source, List<LockOnSystem.ThreatInfo> trackedThreats)
    {
        bool shouldShow = debug.ShouldShow(debug.showThreatLines);
        if (!shouldShow)
        {
            for (int i = 0; i < threatLines.Count; i++)
            {
                if (threatLines[i] != null) threatLines[i].enabled = false;
            }
            return;
        }
        while (threatLines.Count < trackedThreats.Count)
        {
            GameObject lineObj = new GameObject("ThreatLine");
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.material = GetSharedMaterial();
            lr.startWidth = 0.05f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            threatLines.Add(lr);
        }
        for (int i = 0; i < threatLines.Count; i++)
        {
            LineRenderer lr = threatLines[i];
            if (lr == null) continue;
            if (i >= trackedThreats.Count) { lr.enabled = false; continue; }
            var threat = trackedThreats[i];
            if (threat.transform == null) { lr.enabled = false; continue; }
            lr.enabled = true;
            float t = (float)i / Mathf.Max(1, trackedThreats.Count);
            Color lineColor = debug.threatLineColor;
            lineColor.a = Mathf.Lerp(0.8f, 0.2f, t);
            lr.startColor = lineColor;
            lr.endColor = lineColor;
            lr.SetPosition(0, source.position + Vector3.up * 1f);
            lr.SetPosition(1, threat.transform.position + Vector3.up * 1f);
        }
    }

    void UpdateThreatIndicators(DebugSettings debug, List<LockOnSystem.ThreatInfo> trackedThreats)
    {
        bool shouldShow = debug.ShouldShow(debug.showThreatIndicators);
        if (!shouldShow)
        {
            for (int i = 0; i < threatIndicators.Count; i++)
            {
                if (threatIndicators[i] != null) threatIndicators[i].SetActive(false);
            }
            return;
        }
        while (threatIndicators.Count < trackedThreats.Count)
        {
            GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "ThreatIndicator";
            Collider col = indicator.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            indicator.GetComponent<MeshRenderer>().material = GetSharedMaterial();
            threatIndicators.Add(indicator);
        }
        for (int i = 0; i < threatIndicators.Count; i++)
        {
            GameObject indicator = threatIndicators[i];
            if (indicator == null) continue;
            if (i >= trackedThreats.Count) { indicator.SetActive(false); continue; }
            var threat = trackedThreats[i];
            if (threat.transform == null) { indicator.SetActive(false); continue; }
            indicator.SetActive(true);
            indicator.transform.position = threat.transform.position + Vector3.up * 2.5f;
            float size = Mathf.Lerp(0.4f, 0.2f, (float)i / Mathf.Max(1, trackedThreats.Count));
            indicator.transform.localScale = Vector3.one * size;
            MeshRenderer mr = indicator.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                float t = (float)i / Mathf.Max(1, trackedThreats.Count);
                Color indicatorColor = Color.Lerp(Color.red, Color.yellow, t);
                indicatorColor.a = 0.7f;
                if (i == 0)
                    indicatorColor.a = Mathf.PingPong(Time.time * 4f, 0.3f) + 0.7f;
                mr.material.color = indicatorColor;
            }
        }
    }
}
