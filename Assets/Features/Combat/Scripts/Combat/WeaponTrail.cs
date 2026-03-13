using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Generates a lightsaber-style arc trail between two points on a weapon (base and tip).
/// Safe to place directly on the sword GameObject -- creates its own hidden child
/// for the trail mesh so it never conflicts with the weapon's own MeshFilter/MeshRenderer.
/// Assign bladeBase and bladeTip transforms.
/// Optionally link a Combat reference so the trail only appears during attacks.
/// </summary>
public class WeaponTrail : MonoBehaviour
{
    [Header("Blade Points")]
    [Tooltip("Transform at the base of the blade (near the guard/hilt).")]
    public Transform bladeBase;

    [Tooltip("Transform at the tip of the blade.")]
    public Transform bladeTip;

    [Header("Trail Settings")]
    [Tooltip("How long trail segments persist (seconds).")]
    public float trailDuration = 0.15f;

    [Tooltip("Minimum distance the blade must move before a new segment is added.")]
    public float minVertexDistance = 0.01f;

    [Tooltip("Maximum number of trail segments stored.")]
    public int maxSegments = 64;

    [Header("Appearance")]
    [Tooltip("Trail color. Use HDR values (intensity > 1) so bloom picks it up.")]
    [ColorUsage(true, true)]
    public Color trailColor = new Color(0f, 0.5f, 1f, 1f) * 3f;

    [Tooltip("Alpha at the newest edge of the trail (1 = fully visible).")]
    [Range(0f, 1f)]
    public float startAlpha = 0.8f;

    [Header("Activation")]
    [Tooltip("If assigned, trail only renders while Combat.IsAttacking is true.")]
    public Combat combat;

    [Tooltip("If true and combat is assigned, trail is only active during attacks. If false (or no combat ref), trail is always active while the blade moves.")]
    public bool onlyDuringAttacks = true;

    private struct TrailSegment
    {
        public Vector3 basePos;
        public Vector3 tipPos;
        public float birthTime;
    }

    private readonly List<TrailSegment> segments = new List<TrailSegment>();
    private GameObject trailObject;
    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material trailMaterial;

    private Vector3 lastBasePos;
    private Vector3 lastTipPos;
    private bool wasActive;

    [Header("Debug")]
    [Tooltip("Material to use for the trail. If left empty, a default unlit additive material is created at runtime.")]
    public Material overrideMaterial;

    void Awake()
    {
        trailObject = new GameObject("_WeaponTrailMesh");

        meshFilter = trailObject.AddComponent<MeshFilter>();
        meshRenderer = trailObject.AddComponent<MeshRenderer>();

        mesh = new Mesh { name = "WeaponTrailMesh" };
        mesh.MarkDynamic();
        meshFilter.mesh = mesh;

        if (overrideMaterial != null)
        {
            trailMaterial = new Material(overrideMaterial);
        }
        else
        {
            trailMaterial = new Material(Shader.Find("Sprites/Default"));
            trailMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            trailMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            trailMaterial.SetInt("_ZWrite", 0);
            trailMaterial.renderQueue = 3000;
        }

        meshRenderer.material = trailMaterial;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
    }

    void LateUpdate()
    {
        if (bladeBase == null || bladeTip == null || trailObject == null) return;

        trailObject.transform.position = Vector3.zero;
        trailObject.transform.rotation = Quaternion.identity;

        bool active = IsTrailActive();

        if (active)
        {
            Vector3 basePos = bladeBase.position;
            Vector3 tipPos = bladeTip.position;

            bool moved = segments.Count == 0 ||
                         Vector3.Distance(basePos, lastBasePos) > minVertexDistance ||
                         Vector3.Distance(tipPos, lastTipPos) > minVertexDistance;

            if (!wasActive)
            {
                segments.Clear();
                moved = true;
            }

            if (moved)
            {
                segments.Add(new TrailSegment
                {
                    basePos = basePos,
                    tipPos = tipPos,
                    birthTime = Time.time
                });

                if (segments.Count > maxSegments)
                    segments.RemoveAt(0);

                lastBasePos = basePos;
                lastTipPos = tipPos;
            }
        }

        float cutoff = Time.time - trailDuration;
        while (segments.Count > 0 && segments[0].birthTime < cutoff)
            segments.RemoveAt(0);

        wasActive = active;
        RebuildMesh();
    }

    private bool IsTrailActive()
    {
        if (combat != null && onlyDuringAttacks)
            return combat.IsAttacking;
        return true;
    }

    private void RebuildMesh()
    {
        mesh.Clear();
        int count = segments.Count;
        if (count < 2) return;

        int vertCount = count * 2;
        int triCountPerSide = (count - 1) * 6;

        var verts = new Vector3[vertCount];
        var colors = new Color[vertCount];
        var uvs = new Vector2[vertCount];
        var tris = new int[triCountPerSide * 2]; // double-sided

        float now = Time.time;

        for (int i = 0; i < count; i++)
        {
            var seg = segments[i];
            float age = now - seg.birthTime;
            float t = 1f - Mathf.Clamp01(age / trailDuration);

            verts[i * 2] = seg.basePos;
            verts[i * 2 + 1] = seg.tipPos;

            float alpha = t * startAlpha;
            Color c = trailColor;
            c.a = alpha;
            colors[i * 2] = c;
            colors[i * 2 + 1] = c;

            float u = (float)i / (count - 1);
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }

        int tri = 0;
        for (int i = 0; i < count - 1; i++)
        {
            int bl = i * 2;
            int tl = i * 2 + 1;
            int br = (i + 1) * 2;
            int tr = (i + 1) * 2 + 1;

            // front face
            tris[tri++] = bl;
            tris[tri++] = tl;
            tris[tri++] = tr;
            tris[tri++] = bl;
            tris[tri++] = tr;
            tris[tri++] = br;

            // back face (reversed winding)
            tris[tri++] = bl;
            tris[tri++] = tr;
            tris[tri++] = tl;
            tris[tri++] = bl;
            tris[tri++] = br;
            tris[tri++] = tr;
        }

        mesh.vertices = verts;
        mesh.colors = colors;
        mesh.uv = uvs;
        mesh.triangles = tris;
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (trailMaterial != null) Destroy(trailMaterial);
        if (trailObject != null) Destroy(trailObject);
    }
}
