using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ComboSet))]
public class ComboSetEditor : Editor
{
    private const string PrefsKeyPrefix = "ComboSetEditor_SelectedIndex_";
    private static readonly string[] MoveNames = { "Forward Jab 1", "Forward Jab 2", "Neutral Jab 1", "Neutral Jab 2", "Heavy Attack" };
    private static readonly string[] PropertyNames = { "forwardJab", "forwardJab2", "neutralJab", "neutralJab2", "heavyAttack" };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Script reference
        SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
        if (scriptProp != null)
            EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(scriptProp);
        if (scriptProp != null)
            EditorGUI.EndDisabledGroup();

        // Combo timing (always visible)
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Combo Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("comboWindowDelay"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("comboWindowDuration"));

        // Dropdown: which move to edit
        EditorGUILayout.Space(6);
        string prefsKey = PrefsKeyPrefix + target.GetInstanceID();
        int selectedIndex = EditorPrefs.GetInt(prefsKey, 0);
        selectedIndex = EditorGUILayout.Popup("Edit move:", selectedIndex, MoveNames);
        EditorPrefs.SetInt(prefsKey, selectedIndex);

        // Selected move's AttackData (uses AttackDataDrawer when drawn as property)
        EditorGUILayout.Space(4);
        SerializedProperty attackProp = serializedObject.FindProperty(PropertyNames[selectedIndex]);
        if (attackProp != null)
            EditorGUILayout.PropertyField(attackProp, new GUIContent(MoveNames[selectedIndex]), true);

        serializedObject.ApplyModifiedProperties();
    }
}
