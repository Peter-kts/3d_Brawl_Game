using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EnemyCombat))]
public class EnemyCombatEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
    }
}
