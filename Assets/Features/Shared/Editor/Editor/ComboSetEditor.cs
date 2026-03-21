using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ComboSet))]
public class ComboSetEditor : Editor
{
    private const string PrefsKeyPrefix = "ComboSetEditor_SelectedIndex_";
    private static readonly string[] MoveNames =
    {
        "Forward Jab 1 (Normal / Tap)",
        "Forward Jab 1 (Charged / Hold)",
        "Forward Jab 2 (Normal / Tap)",
        "Forward Jab 2 (Charged / Hold)",
        "Neutral Jab 1 (Normal / Tap)",
        "Neutral Jab 1 (Charged / Hold)",
        "Neutral Jab 2 (Normal / Tap)",
        "Neutral Jab 2 (Charged / Hold)",
        "Neutral Jab 3 (Normal / Tap)",
        "Neutral Jab 3 (Charged / Hold)",
        "Heavy Attack",
        "RB + X Attack",
        "Throw",
        "Back Throw"
    };
    private static readonly string[] PropertyNames =
    {
        "forwardJabNormal",
        "forwardJab",
        "forwardJab2Normal",
        "forwardJab2",
        "neutralJabNormal",
        "neutralJab",
        "neutralJab2Normal",
        "neutralJab2",
        "neutralJab3Normal",
        "neutralJab3",
        "heavyAttack",
        "rbXAttack",
        "throwData",
        "backThrowData"
    };

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
        EditorGUILayout.PropertyField(serializedObject.FindProperty("loopNeutralCombo"));

        // Dropdown: which of the 6 moves we're editing (selection persisted per asset via EditorPrefs)
        EditorGUILayout.Space(6);
        string prefsKey = PrefsKeyPrefix + target.GetInstanceID();
        int selectedIndex = EditorPrefs.GetInt(prefsKey, 0);
        selectedIndex = EditorGUILayout.Popup("Edit move:", selectedIndex, MoveNames);
        EditorPrefs.SetInt(prefsKey, selectedIndex);

        // Selected move: AttackData for attacks 0..9, ThrowData for Throw/Back Throw (10/11)
        EditorGUILayout.Space(4);
        SerializedProperty moveProp = serializedObject.FindProperty(PropertyNames[selectedIndex]);
        if (moveProp != null)
            EditorGUILayout.PropertyField(moveProp, new GUIContent(MoveNames[selectedIndex]), true);

        // Move template: only for attack moves (not Throw entries)
        if (selectedIndex < 12)
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

    /// <summary>Map dropdown index (0..9) to the corresponding AttackData on the ComboSet. Index 10 is Throw (no AttackData).</summary>
    private static AttackData GetMoveFromComboSet(ComboSet comboSet, int index)
    {
        switch (index)
        {
            case 0:  return comboSet.forwardJabNormal;
            case 1:  return comboSet.forwardJab;
            case 2:  return comboSet.forwardJab2Normal;
            case 3:  return comboSet.forwardJab2;
            case 4:  return comboSet.neutralJabNormal;
            case 5:  return comboSet.neutralJab;
            case 6:  return comboSet.neutralJab2Normal;
            case 7:  return comboSet.neutralJab2;
            case 8:  return comboSet.neutralJab3Normal;
            case 9:  return comboSet.neutralJab3;
            case 10: return comboSet.heavyAttack;
            case 11: return comboSet.rbXAttack;
            default: return comboSet.forwardJabNormal;
        }
    }

    /// <summary>Write AttackData into the selected move slot on the ComboSet. Only used for attack indices (not Throw).</summary>
    private static void ApplyMoveToComboSet(ComboSet comboSet, int index, AttackData data)
    {
        switch (index)
        {
            case 0:  comboSet.forwardJabNormal  = data; break;
            case 1:  comboSet.forwardJab        = data; break;
            case 2:  comboSet.forwardJab2Normal = data; break;
            case 3:  comboSet.forwardJab2       = data; break;
            case 4:  comboSet.neutralJabNormal  = data; break;
            case 5:  comboSet.neutralJab        = data; break;
            case 6:  comboSet.neutralJab2Normal = data; break;
            case 7:  comboSet.neutralJab2       = data; break;
            case 8:  comboSet.neutralJab3Normal = data; break;
            case 9:  comboSet.neutralJab3       = data; break;
            case 10: comboSet.heavyAttack       = data; break;
            case 11: comboSet.rbXAttack         = data; break;
        }
        EditorUtility.SetDirty(comboSet);
    }
}
