using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts.Editor
{
    public static class GUIUtils
    {
        public static T DrawObjectFieldWithLabel<T>(T data, string label) where T : Material
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label(label);
                return (T) EditorGUILayout.ObjectField(data, typeof(T), allowSceneObjects: true,
                    GUILayout.Width(200));
            }
        }

        public static Vector3 DrawVector3FieldWithLabel(Vector3 data, string label)
        {
            return EditorGUILayout.Vector3Field(label, data, GUILayout.ExpandWidth(true));
        }

        public static T DrawEnumWithLabel<T>(T data, string label) where T : Enum
        {
            using (new GUILayout.HorizontalScope())
            {
                return (T) EditorGUILayout.EnumPopup(label, data);
            }
        }

        public static bool DrawEnumWithLabel(bool data, string label)
        {
            using (new GUILayout.HorizontalScope())
            {
                return EditorGUILayout.Toggle(label, data);
            }
        }

        public static void DrawJoinButton()
        {
            if(GUILayout.Button("Join our community", new StyleBuilder()
                .FontSize(20)
                .FontColor(Color.white)
                .Background(44)
                .Alignment(TextAnchor.MiddleCenter)
                .Margin(10)
                .Padding(10)
                .Build()))
                Application.OpenURL("https://discord.gg/4PH2Wfz2nT");
        }

        public static void DrawMaterialList(string label, List<Material> materials)
        {
            using (new GUILayout.VerticalScope())
            {
                using (new GUILayout.VerticalScope(new StyleBuilder()
                    .Alignment(TextAnchor.MiddleRight)
                    .Background(77)
                    .Padding(10)
                    .Build()))
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Label(label, new StyleBuilder()
                            .FontColor(Color.white)
                            .FontStyle(FontStyle.Bold)
                            .FontSize(14)
                            .Alignment(TextAnchor.MiddleLeft)
                            .Build(), GUILayout.Height(25));
                        if (GUILayout.Button("Add material", GUILayout.Height(25), GUILayout.Width(180)))
                            materials.Add(null);
                    }

                    GUILayout.Space(10);

                    var materialsToRemove = new List<int>();

                    for (var i = 0; i < materials.Count; i++)
                    {
                        using (new GUILayout.HorizontalScope())
                        {
                            materials[i] =
                                (Material) EditorGUILayout.ObjectField(materials[i], typeof(Material),
                                    false,
                                    GUILayout.ExpandWidth(true), GUILayout.Height(25));

                            GUI.color = Color.red;
                            if (GUILayout.Button("X", GUILayout.Height(25), GUILayout.Width(25)))
                                materialsToRemove.Add(i);
                            GUI.color = Color.white;
                        }
                    }

                    materialsToRemove.ForEach(i => materials.RemoveAt(i));
                }

                GUILayout.Space(15);
            }
        }
    }
}