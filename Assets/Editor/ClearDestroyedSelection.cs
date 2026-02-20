using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prevents MissingReferenceException in the Inspector when the selected object
/// has been destroyed (e.g. enemy with EnemyStateDebugVisual destroyed on load).
/// Clears selection before/during play mode and removes destroyed objects from selection each frame.
/// </summary>
[InitializeOnLoad]
public static class ClearDestroyedSelection
{
    static int _updateFrame;

    static ClearDestroyedSelection()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnUpdate;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
        {
            Selection.activeObject = null;
            ClearInspectorLockIfDestroyed();
        }
    }

    static void OnUpdate()
    {
        _updateFrame++;
        if (ReferenceEquals(Selection.activeObject, null))
        {
            if (_updateFrame % 60 == 0) ClearInspectorLockIfDestroyed();
            return;
        }
        try
        {
            Selection.activeObject.GetInstanceID();
        }
        catch (MissingReferenceException)
        {
            Selection.activeObject = null;
            ClearInspectorLockIfDestroyed();
        }
    }

    /// <summary>If the Inspector is locked to an object that was destroyed, unlock it to avoid MissingReferenceException in the title bar.</summary>
    static void ClearInspectorLockIfDestroyed()
    {
        var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
        if (inspectorType == null) return;
        var lockField = inspectorType.GetField("m_InspectorLock", BindingFlags.NonPublic | BindingFlags.Instance);
        if (lockField == null) return;
        foreach (var win in Resources.FindObjectsOfTypeAll(inspectorType))
        {
            if (win == null) continue;
            try
            {
                if (!(bool)lockField.GetValue(win)) continue;
                var trackerField = inspectorType.GetField("m_TrackedObjects", BindingFlags.NonPublic | BindingFlags.Instance);
                if (trackerField == null) continue;
                var tracked = trackerField.GetValue(win);
                if (tracked == null) continue;
                if (!(tracked is System.Collections.IList list) || list.Count == 0) continue;
                var first = list[0];
                if (first is Editor ed)
                {
                    try
                    {
                        var t = ed.target;
                        if (t != null) t.GetInstanceID();
                    }
                    catch (MissingReferenceException)
                    {
                        lockField.SetValue(win, false);
                    }
                }
            }
            catch { /* ignore */ }
        }
    }
}
