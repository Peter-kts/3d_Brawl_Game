using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyComboSet))]
public class EnemyComboSetEditor : Editor
{
    private const string PrefsKeyPrefix = "EnemyComboSetEditor_SelectedIndex_";
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty scriptProp = serializedObject.FindProperty("m_Script");
        if (scriptProp != null)
            EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(scriptProp);
        if (scriptProp != null)
            EditorGUI.EndDisabledGroup();

        SerializedProperty movesProp = serializedObject.FindProperty("moves");
        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(movesProp, includeChildren: false);
        if (movesProp != null && movesProp.isExpanded)
        {
            EditorGUI.indentLevel++;
            int newSize = Mathf.Max(0, EditorGUILayout.IntField("Size", movesProp.arraySize));
            if (newSize != movesProp.arraySize)
                movesProp.arraySize = newSize;
            EditorGUI.indentLevel--;
        }


        int moveCount = movesProp != null ? movesProp.arraySize : 0;
        if (moveCount <= 0)
        {
            EditorGUILayout.HelpBox("Add at least one move entry to edit move properties, or use Populate From EnemyCombat.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        string prefsKey = PrefsKeyPrefix + target.GetInstanceID();
        int selectedIndex = Mathf.Clamp(EditorPrefs.GetInt(prefsKey, 0), 0, moveCount - 1);
        string[] options = BuildMoveOptions(movesProp, moveCount);

        EditorGUILayout.Space(6);
        selectedIndex = EditorGUILayout.Popup("Edit move", selectedIndex, options);
        EditorPrefs.SetInt(prefsKey, selectedIndex);

        SerializedProperty selectedMove = movesProp.GetArrayElementAtIndex(selectedIndex);
        SerializedProperty selectedAttack = selectedMove != null ? selectedMove.FindPropertyRelative("attack") : null;

        EditorGUILayout.Space(4);
        if (selectedMove != null)
            EditorGUILayout.PropertyField(selectedMove, new GUIContent("Selected Move"), includeChildren: true);

        if (selectedAttack != null)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Move Template", EditorStyles.boldLabel);
            MoveTemplate loadTemplate = (MoveTemplate)EditorGUILayout.ObjectField("Template", null, typeof(MoveTemplate), false);
            if (GUILayout.Button("Load into selected move") && loadTemplate != null)
            {
                serializedObject.ApplyModifiedProperties();
                AttackData clone = JsonUtility.FromJson<AttackData>(JsonUtility.ToJson(loadTemplate.attackData));
                EnemyComboSet comboSet = (EnemyComboSet)target;
                if (comboSet.moves != null && selectedIndex >= 0 && selectedIndex < comboSet.moves.Count)
                {
                    comboSet.moves[selectedIndex].attack = clone;
                    EditorUtility.SetDirty(comboSet);
                }
                serializedObject.Update();
            }

            if (GUILayout.Button("Save selected move as template"))
            {
                serializedObject.ApplyModifiedProperties();
                EnemyComboSet comboSet = (EnemyComboSet)target;
                if (comboSet.moves != null && selectedIndex >= 0 && selectedIndex < comboSet.moves.Count)
                {
                    AttackData current = comboSet.moves[selectedIndex].attack;
                    string path = EditorUtility.SaveFilePanelInProject("Save move as template", "EnemyMoveTemplate", "asset", "Save MoveTemplate asset");
                    if (!string.IsNullOrEmpty(path))
                    {
                        MoveTemplate template = CreateInstance<MoveTemplate>();
                        template.attackData = JsonUtility.FromJson<AttackData>(JsonUtility.ToJson(current));
                        AssetDatabase.CreateAsset(template, path);
                        AssetDatabase.SaveAssets();
                    }
                }
                serializedObject.Update();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static AttackData CloneAttackData(AttackData source)
    {
        AttackData clone = new AttackData();
        if (source != null)
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), clone);
        return clone;
    }

    private static string[] BuildMoveOptions(SerializedProperty movesProp, int moveCount)
    {
        List<string> options = new List<string>(moveCount);
        for (int i = 0; i < moveCount; i++)
        {
            SerializedProperty moveProp = movesProp.GetArrayElementAtIndex(i);
            SerializedProperty idProp = moveProp != null ? moveProp.FindPropertyRelative("moveId") : null;
            string label = idProp != null ? idProp.stringValue : null;
            if (string.IsNullOrEmpty(label))
                label = "Move " + (i + 1);
            options.Add(label);
        }
        return options.ToArray();
    }
}
