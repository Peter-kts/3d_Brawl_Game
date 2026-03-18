using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyCombat))]
public class EnemyCombatEditor : Editor
{
    static AttackData CloneAttackData(AttackData source)
    {
        var clone = new AttackData();
        if (source != null)
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), clone);
        return clone;
    }

    static EnemyMoveEntry BuildEntry(string moveId, AttackData attack, float minDistance, float maxDistance, bool requiresRecentDodge, int priority)
    {
        return new EnemyMoveEntry
        {
            moveId = moveId,
            attack = CloneAttackData(attack),
            minDistance = minDistance,
            maxDistance = maxDistance,
            requiresRecentDodge = requiresRecentDodge,
            priority = priority
        };
    }

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

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Enemy Combo Set", EditorStyles.boldLabel);
        if (GUILayout.Button("Create and assign combo set (from legacy attacks)"))
        {
            var enemy = (EnemyCombat)target;
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Enemy Combo Set",
                $"{enemy.name}_ComboSet",
                "asset",
                "Choose where to save the generated EnemyComboSet asset."
            );

            if (!string.IsNullOrEmpty(path))
            {
                EnemyComboSet comboSet = ScriptableObject.CreateInstance<EnemyComboSet>();

                float threshold = enemy.punchRangeThreshold;
                comboSet.moves.Add(BuildEntry("DodgePunish", enemy.dodgePunishAttack, 0f, 100f, true, 100));
                comboSet.moves.Add(BuildEntry("PunchClose", enemy.basicAttack, 0f, threshold, false, 10));
                comboSet.moves.Add(BuildEntry("KickFar", enemy.kickAttack, threshold, 100f, false, 0));

                AssetDatabase.CreateAsset(comboSet, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Undo.RecordObject(enemy, "Assign Enemy Combo Set");
                enemy.enemyComboSet = comboSet;
                EditorUtility.SetDirty(enemy);
            }
        }
    }
}
