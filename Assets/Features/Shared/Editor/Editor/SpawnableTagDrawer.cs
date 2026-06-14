using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(SpawnableTagAttribute))]
public class SpawnableTagDrawer : PropertyDrawer
{
    private const string RequiredTag = "Spawnable";

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float baseHeight = EditorGUIUtility.singleLineHeight;
        if (NeedsWarning(property))
            baseHeight += EditorGUIUtility.singleLineHeight * 2f + EditorGUIUtility.standardVerticalSpacing;
        return baseHeight;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Scene objects can only be stored in serialized objects that themselves live in a scene
        // (e.g. MonoBehaviour). ScriptableObject assets (like ComboSet) can only hold asset references.
        bool allowSceneObjects = !EditorUtility.IsPersistent(property.serializedObject.targetObject);

        Rect fieldRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        var current = property.objectReferenceValue as GameObject;

        EditorGUI.BeginChangeCheck();
        var picked = EditorGUI.ObjectField(fieldRect, label, current, typeof(GameObject), allowSceneObjects) as GameObject;
        if (EditorGUI.EndChangeCheck())
            property.objectReferenceValue = picked;

        if (NeedsWarning(property))
        {
            float warningY = fieldRect.yMax + EditorGUIUtility.standardVerticalSpacing;
            Rect warningRect = new Rect(position.x, warningY, position.width, EditorGUIUtility.singleLineHeight * 2f);
            EditorGUI.HelpBox(warningRect,
                $"This object is not tagged \"{RequiredTag}\". Set its Tag to \"{RequiredTag}\" in the Inspector.",
                MessageType.Warning);
        }

        EditorGUI.EndProperty();
    }

    private bool NeedsWarning(SerializedProperty property)
    {
        if (property.propertyType != SerializedPropertyType.ObjectReference) return false;
        var go = property.objectReferenceValue as GameObject;
        return go != null && !go.CompareTag(RequiredTag);
    }
}
