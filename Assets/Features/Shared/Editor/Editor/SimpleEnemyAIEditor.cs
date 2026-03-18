using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SimpleEnemyAI))]
public class SimpleEnemyAIEditor : FoldoutHeaderEditor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        SimpleEnemyAI ai = (SimpleEnemyAI)target;
        EnemyAnimationConfig config = ai != null ? ai.animationConfig : null;
        if (config == null) return;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Animation Config (Inline)", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope("box"))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField("Asset", config, typeof(EnemyAnimationConfig), false);
                if (GUILayout.Button("Ping", GUILayout.Width(56f)))
                    EditorGUIUtility.PingObject(config);
            }

            SerializedObject configSo = new SerializedObject(config);
            configSo.Update();

            SerializedProperty iterator = configSo.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script") continue;
                EditorGUILayout.PropertyField(iterator, true);
            }

            configSo.ApplyModifiedProperties();
            if (GUI.changed)
                EditorUtility.SetDirty(config);
        }
    }
}
