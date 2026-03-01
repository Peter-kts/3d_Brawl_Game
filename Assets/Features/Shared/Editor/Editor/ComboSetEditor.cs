using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ComboSet))]
public class ComboSetEditor : Editor
{
    private const string PrefsKeyPrefix = "ComboSetEditor_SelectedIndex_";
    private static readonly string[] MoveNames = { "Forward Jab 1", "Forward Jab 2", "Neutral Jab 1", "Neutral Jab 2", "Heavy Attack", "Throw" };
    private static readonly string[] PropertyNames = { "forwardJab", "forwardJab2", "neutralJab", "neutralJab2", "heavyAttack", "throwData" };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Script reference (read-only)
        SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
        if (scriptProp != null)
            EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(scriptProp);
        if (scriptProp != null)
            EditorGUI.EndDisabledGroup();

        // Combo timing: comboWindowDelay = time after attack before combo window opens; comboWindowDuration = how long player can press next input
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Combo Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("comboWindowDelay"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("comboWindowDuration"));

        // Dropdown: which of the 6 moves we're editing (selection persisted per asset via EditorPrefs)
        EditorGUILayout.Space(6);
        string prefsKey = PrefsKeyPrefix + target.GetInstanceID();
        int selectedIndex = EditorPrefs.GetInt(prefsKey, 0);
        selectedIndex = EditorGUILayout.Popup("Edit move:", selectedIndex, MoveNames);
        EditorPrefs.SetInt(prefsKey, selectedIndex);

        // Selected move: AttackData for attacks 0..4, ThrowData for Throw (5)
        EditorGUILayout.Space(4);
        SerializedProperty moveProp = serializedObject.FindProperty(PropertyNames[selectedIndex]);
        if (moveProp != null)
            EditorGUILayout.PropertyField(moveProp, new GUIContent(MoveNames[selectedIndex]), true);

        // Move template: only for attack moves (not Throw)
        if (selectedIndex < 5)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Move Template", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            MoveTemplate loadTemplate = (MoveTemplate)EditorGUILayout.ObjectField("Template", null, typeof(MoveTemplate), false);
            if (GUILayout.Button("Load into move", GUILayout.Width(120)) && loadTemplate != null)
            {
                serializedObject.ApplyModifiedProperties();
                AttackData copy = JsonUtility.FromJson<AttackData>(JsonUtility.ToJson(loadTemplate.attackData));
                ApplyMoveToComboSet((ComboSet)target, selectedIndex, copy);
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Save current move as template"))
            {
                serializedObject.ApplyModifiedProperties();
                AttackData current = GetMoveFromComboSet((ComboSet)target, selectedIndex);
                string path = EditorUtility.SaveFilePanelInProject("Save move as template", "MoveTemplate", "asset", "Save MoveTemplate asset");
                if (!string.IsNullOrEmpty(path))
                {
                    var template = CreateInstance<MoveTemplate>();
                    template.attackData = JsonUtility.FromJson<AttackData>(JsonUtility.ToJson(current));
                    AssetDatabase.CreateAsset(template, path);
                    AssetDatabase.SaveAssets();
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>Map dropdown index (0..4) to the corresponding AttackData on the ComboSet. Index 5 is Throw (no AttackData).</summary>
    private static AttackData GetMoveFromComboSet(ComboSet comboSet, int index)
    {
        switch (index)
        {
            case 0: return comboSet.forwardJab;
            case 1: return comboSet.forwardJab2;
            case 2: return comboSet.neutralJab;
            case 3: return comboSet.neutralJab2;
            case 4: return comboSet.heavyAttack;
            default: return comboSet.forwardJab;
        }
    }

    /// <summary>Write AttackData into the selected move slot on the ComboSet. Only used for indices 0..4 (not Throw).</summary>
    private static void ApplyMoveToComboSet(ComboSet comboSet, int index, AttackData data)
    {
        switch (index)
        {
            case 0: comboSet.forwardJab = data; break;
            case 1: comboSet.forwardJab2 = data; break;
            case 2: comboSet.neutralJab = data; break;
            case 3: comboSet.neutralJab2 = data; break;
            case 4: comboSet.heavyAttack = data; break;
        }
        EditorUtility.SetDirty(comboSet);
    }
}
