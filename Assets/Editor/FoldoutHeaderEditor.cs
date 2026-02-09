using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Base custom editor that converts [Header] attributes into collapsible foldout groups.
/// Inherit from this and add [CustomEditor(typeof(YourScript))] to opt in.
/// No changes to runtime scripts required -- it reads existing [Header] attributes.
/// </summary>
public class FoldoutHeaderEditor : Editor
{
    // Cached grouping data so we only reflect once (or when the target type changes)
    private List<PropertyGroup> groups;
    private Dictionary<string, bool> foldoutStates;
    private string prefsKeyPrefix;

    /// <summary>One contiguous group of serialized properties under a single [Header].</summary>
    private class PropertyGroup
    {
        public string header;                     // null for ungrouped fields at the top
        public List<string> propertyPaths = new List<string>();
    }

    // ------------------------------------------------------------------
    // Initialization
    // ------------------------------------------------------------------

    void OnEnable()
    {
        RebuildGroups();
    }

    void RebuildGroups()
    {
        groups = new List<PropertyGroup>();
        foldoutStates = new Dictionary<string, bool>();
        prefsKeyPrefix = "FoldoutHeader_" + target.GetType().Name + "_";

        // Current group (starts as ungrouped)
        PropertyGroup current = new PropertyGroup { header = null };

        SerializedProperty iterator = serializedObject.GetIterator();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;

            // Skip the built-in "m_Script" field
            if (iterator.name == "m_Script") continue;

            // Check if the backing field has a [Header] attribute
            string headerName = GetHeaderAttribute(target.GetType(), iterator.name);

            if (headerName != null)
            {
                // Save previous group if it has any properties
                if (current.propertyPaths.Count > 0 || current.header != null)
                    groups.Add(current);

                // Start a new group
                current = new PropertyGroup { header = headerName };

                // Restore persisted foldout state (default: expanded)
                string key = prefsKeyPrefix + headerName;
                foldoutStates[headerName] = EditorPrefs.GetBool(key, true);
            }

            current.propertyPaths.Add(iterator.propertyPath);
        }

        // Don't forget the last group
        if (current.propertyPaths.Count > 0)
            groups.Add(current);
    }

    // ------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw the Script field (read-only, standard Unity convention)
        DrawScriptField();

        foreach (var group in groups)
        {
            if (group.header == null)
            {
                // Ungrouped fields at the top -- draw normally
                DrawProperties(group.propertyPaths);
            }
            else
            {
                // Draw a foldout for this header group
                bool state = foldoutStates.ContainsKey(group.header) && foldoutStates[group.header];
                bool newState = EditorGUILayout.Foldout(state, group.header, true, EditorStyles.foldoutHeader);

                if (newState != state)
                {
                    foldoutStates[group.header] = newState;
                    EditorPrefs.SetBool(prefsKeyPrefix + group.header, newState);
                }

                if (newState)
                {
                    EditorGUI.indentLevel++;
                    DrawProperties(group.propertyPaths);
                    EditorGUI.indentLevel--;
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void DrawScriptField()
    {
        SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
        if (scriptProp != null)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(scriptProp);
            }
        }
    }

    private void DrawProperties(List<string> paths)
    {
        foreach (string path in paths)
        {
            SerializedProperty prop = serializedObject.FindProperty(path);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop, true);
            }
        }
    }

    /// <summary>
    /// Uses reflection to find a [Header] attribute on a field in the given type (or its base types).
    /// Returns the header text, or null if no [Header] attribute is present.
    /// </summary>
    private static string GetHeaderAttribute(Type type, string fieldName)
    {
        FieldInfo field = null;
        Type current = type;

        // Walk up the hierarchy to find the field (handles inheritance)
        while (current != null && field == null)
        {
            field = current.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            current = current.BaseType;
        }

        if (field == null) return null;

        HeaderAttribute header = field.GetCustomAttribute<HeaderAttribute>();
        return header?.header;
    }
}
