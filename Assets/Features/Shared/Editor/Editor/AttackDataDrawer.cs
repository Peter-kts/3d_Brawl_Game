using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom property drawer for AttackData that renders its [Header] groups
/// as collapsible foldouts inside the parent inspector (e.g. Combat).
/// </summary>
[CustomPropertyDrawer(typeof(AttackData))]
public class AttackDataDrawer : PropertyDrawer
{
    // Per-property foldout states keyed by "propertyPath_headerName"
    private static readonly Dictionary<string, bool> foldoutStates = new Dictionary<string, bool>();

    // Cache: grouped child property info per field path
    private static readonly Dictionary<string, List<ChildGroup>> groupCache = new Dictionary<string, List<ChildGroup>>();

    private class ChildGroup
    {
        public string header;                          // null = ungrouped
        public List<string> relativePaths = new List<string>();
    }

    // ------------------------------------------------------------------
    // Build group structure (once per unique property path)
    // ------------------------------------------------------------------

    private List<ChildGroup> GetGroups(SerializedProperty property)
    {
        string key = property.propertyPath;
        if (groupCache.TryGetValue(key, out var cached))
            return cached;

        var groups = new List<ChildGroup>();
        var current = new ChildGroup { header = null };

        SerializedProperty iter = property.Copy();
        SerializedProperty end = property.GetEndProperty();
        bool enterChildren = true;

        while (iter.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iter, end))
        {
            enterChildren = false;

            string headerName = GetHeaderAttribute(typeof(AttackData), iter.name);

            if (headerName != null)
            {
                if (current.relativePaths.Count > 0 || current.header != null)
                    groups.Add(current);

                current = new ChildGroup { header = headerName };
            }

            current.relativePaths.Add(iter.propertyPath);
        }

        if (current.relativePaths.Count > 0)
            groups.Add(current);

        groupCache[key] = groups;
        return groups;
    }

    // ------------------------------------------------------------------
    // GUI
    // ------------------------------------------------------------------

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Draw the main foldout for the AttackData field itself (e.g. "Forward Jab 1")
        property.isExpanded = EditorGUI.Foldout(
            new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
            property.isExpanded, label, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            var groups = GetGroups(property);

            foreach (var group in groups)
            {
                if (group.header == null)
                {
                    // Ungrouped fields
                    foreach (string path in group.relativePaths)
                    {
                        if (ShouldHideField(property, path)) continue;
                        SerializedProperty child = property.serializedObject.FindProperty(path);
                        if (child != null)
                            EditorGUILayout.PropertyField(child, true);
                    }
                }
                else
                {
                    if (!GroupHasVisibleChildren(property, group))
                        continue;

                    string stateKey = property.propertyPath + "_" + group.header;
                    if (!foldoutStates.ContainsKey(stateKey))
                        foldoutStates[stateKey] = true;

                    foldoutStates[stateKey] = EditorGUILayout.Foldout(
                        foldoutStates[stateKey], group.header, true, EditorStyles.foldoutHeader);

                    if (foldoutStates[stateKey])
                    {
                        EditorGUI.indentLevel++;
                        if (group.relativePaths.Count == 2 && (group.header == "Start up" || group.header == "Recovery"))
                        {
                            SerializedProperty p0 = property.serializedObject.FindProperty(group.relativePaths[0]);
                            SerializedProperty p1 = property.serializedObject.FindProperty(group.relativePaths[1]);
                            if (p0 != null && p1 != null)
                            {
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.PropertyField(p0, new GUIContent("Length"), GUILayout.MinWidth(60f));
                                EditorGUILayout.PropertyField(p1, new GUIContent("Speed"), GUILayout.MinWidth(60f));
                                EditorGUILayout.EndHorizontal();
                            }
                            else
                                DrawGroupChildren(property, group);
                        }
                        else
                            DrawGroupChildren(property, group);
                        EditorGUI.indentLevel--;
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    // ------------------------------------------------------------------
    // Height -- we use GUILayout so return a minimal height for the header line
    // ------------------------------------------------------------------

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        // Only the top foldout line; expanded content is drawn via EditorGUILayout
        return EditorGUIUtility.singleLineHeight;
    }

    private void DrawGroupChildren(SerializedProperty property, ChildGroup group)
    {
        foreach (string path in group.relativePaths)
        {
            if (ShouldHideField(property, path)) continue;
            SerializedProperty child = property.serializedObject.FindProperty(path);
            if (child != null)
                EditorGUILayout.PropertyField(child, true);
        }
    }

    private bool GroupHasVisibleChildren(SerializedProperty property, ChildGroup group)
    {
        for (int i = 0; i < group.relativePaths.Count; i++)
        {
            if (!ShouldHideField(property, group.relativePaths[i]))
                return true;
        }
        return false;
    }

    private bool ShouldHideField(SerializedProperty attackDataProperty, string absolutePath)
    {
        SerializedProperty hitboxTypeProp = attackDataProperty.FindPropertyRelative("hitboxType");
        if (hitboxTypeProp == null) return false;
        if (hitboxTypeProp.enumValueIndex != (int)AttackHitboxType.WeaponStrike) return false;

        string fieldName = absolutePath;
        int dot = absolutePath.LastIndexOf('.');
        if (dot >= 0 && dot < absolutePath.Length - 1)
            fieldName = absolutePath.Substring(dot + 1);

        // Hidden when move type is WeaponStrike (unarmed, timed overlap-sphere hitbox fields).
        return fieldName == "range"
            || fieldName == "hitboxRadius"
            || fieldName == "hitboxOffset"
            || fieldName == "hitboxDelay";
    }

    // ------------------------------------------------------------------
    // Reflection helper (same as FoldoutHeaderEditor)
    // ------------------------------------------------------------------

    private static string GetHeaderAttribute(Type type, string fieldName)
    {
        FieldInfo field = null;
        Type current = type;

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
