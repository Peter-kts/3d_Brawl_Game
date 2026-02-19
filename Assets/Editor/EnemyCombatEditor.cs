using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyCombat))]
public class EnemyCombatEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Move Template", EditorStyles.boldLabel);
        MoveTemplate loadTemplate = (MoveTemplate)EditorGUILayout.ObjectField("Template", null, typeof(MoveTemplate), false);
        // Copy template's AttackData into this enemy's basic attack via JSON (clone fields, not reference)
        if (GUILayout.Button("Load into basic attack") && loadTemplate != null)
        {
            var enemy = (EnemyCombat)target;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(loadTemplate.attackData), enemy.basicAttack);
            serializedObject.Update();
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }
}
