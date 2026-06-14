using UnityEditor;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    [CustomEditor(typeof(NHCamera))]
    public class NHCameraEditor : Editor
    {
        protected NHCamera _instance;

        protected virtual void OnEnable()
        {
            _instance = target as NHCamera;
        }

        protected void OnDestroy()
        {
            _instance = null;
        }

        protected void OnDisable()
        {
            _instance = null;
        }

        public override void OnInspectorGUI()
        {
            GUIStyle offset = new GUIStyle();
            offset.margin = new RectOffset(35, 15, 0, 0);
            _instance.ControlsFoldout =
                EditorGUILayout.Foldout(_instance.ControlsFoldout, "Controls", toggleOnLabelClick: true);
            if (_instance.ControlsFoldout)
                using (new GUILayout.VerticalScope(offset))
                {
                    GUILayout.Label("Left mouse button - rotation");
                    GUILayout.Label("Mouse wheel - zoom");
                    GUILayout.Label("Right mouse button - flying");
                    using (new GUILayout.VerticalScope(offset))
                    {
                        GUILayout.Label("W - Forward");
                        GUILayout.Label("S - Backward");
                        GUILayout.Label("A - Left");
                        GUILayout.Label("D - Right");
                        GUILayout.Label("Q - Up");
                        GUILayout.Label("E - Down");
                    }
                }

            GUILayout.Space(10);
            base.OnInspectorGUI();
        }
    }
}