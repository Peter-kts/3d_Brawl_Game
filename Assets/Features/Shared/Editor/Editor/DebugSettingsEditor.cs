using UnityEngine;
using UnityEditor;

public static class DebugSettingsMenu
{
    [MenuItem("GameObject/Debug/Create Debug Settings", false, 10)]
    static void CreateDebugSettings(MenuCommand menuCommand)
    {
        var existing = Object.FindObjectOfType<DebugSettings>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            return;
        }
        var go = new GameObject("DebugSettings");
        go.AddComponent<DebugSettings>();
        Selection.activeGameObject = go;
        Undo.RegisterCreatedObjectUndo(go, "Create Debug Settings");
    }
}
