/*
 * DebugDrawHelper.cs - Shared materials and primitive/line creation for debug visuals.
 * Used by debug visualizer components so they don't duplicate Shader/Material logic.
 */

using UnityEngine;

public static class DebugDrawHelper
{
    private static Material sharedLineMaterial;
    private static Material sharedPrimitiveMaterial;

    static void EnsureSharedLineMaterial()
    {
        if (sharedLineMaterial != null) return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        sharedLineMaterial = new Material(shader);
    }

    static void EnsureSharedPrimitiveMaterial()
    {
        if (sharedPrimitiveMaterial != null) return;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        sharedPrimitiveMaterial = new Material(shader);
    }

    /// <summary>Get shared material for lines. Used by LineRenderer components.</summary>
    public static Material GetSharedLineMaterial()
    {
        EnsureSharedLineMaterial();
        return sharedLineMaterial;
    }

    /// <summary>Get shared material for primitives (sphere, cube, cylinder).</summary>
    public static Material GetSharedPrimitiveMaterial()
    {
        EnsureSharedPrimitiveMaterial();
        return sharedPrimitiveMaterial;
    }

    /// <summary>Create a LineRenderer child under parent. Caller sets positions and colors.</summary>
    public static LineRenderer CreateLine(Transform parent, float startWidth, float endWidth)
    {
        GameObject lineObj = new GameObject("DebugLine");
        lineObj.transform.SetParent(parent);
        LineRenderer line = lineObj.AddComponent<LineRenderer>();
        line.material = GetSharedLineMaterial();
        line.startWidth = startWidth;
        line.endWidth = endWidth;
        line.positionCount = 2;
        line.useWorldSpace = true;
        return line;
    }

    /// <summary>Create a primitive with collider removed and shared material. Caller sets position/scale/color.</summary>
    public static GameObject CreatePrimitive(PrimitiveType type, string name, bool removeCollider = true)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (removeCollider)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
        }
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.material = GetSharedPrimitiveMaterial();
        return go;
    }
}
