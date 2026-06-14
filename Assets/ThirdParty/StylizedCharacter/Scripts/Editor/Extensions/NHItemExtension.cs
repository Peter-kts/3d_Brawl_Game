using System;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts.Attributes;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.Scripts.Items;
using NHance.Assets.Scripts.Utils;
using NHance.Assets.StylizedCharacter.Scripts.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    public static class NHItemExtension
    {
        public static void OnEnable(this NHItem item)
        {
            InitializationHelper.InitializeWrappers(ref item.Wrappers);
        }

        public static ItemTypeDescriptorAttribute TypeDescriptor(this NHItem item)
        {
            var enumType = typeof(ItemTypeEnum);
            return UtilsAttributes.GetAttribute<ItemTypeDescriptorAttribute>(enumType.GetMember(item.Type.ToString())[0]);
        }
        
        

        public static void DrawMesh(this NHItem item)
        {
            GUILayout.Label("By selecting the checkboxes below, the item will turn off body parts at the base.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);

            foreach (var w in item.Wrappers)
            {
                w.Enabled = GUILayout.Toggle(w.Enabled, w.Type.ToString());
                GUILayout.Space(5);
            }

            GUILayout.Space(10);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear targets"))
                    item.ClearTargets();

                if (GUILayout.Button("Set Default targets"))
                    item.SetDefaultTargets();
            }
        }

        public static void DrawMaterial(this NHItem item)
        {
            using (new GUILayout.VerticalScope())
            {
                if (GUILayout.Button("Add wrapper", GUILayout.Height(35)))
                    item.Mappers.Add(new MaterialMapper().GetInitialized());

                var mappersToRemove = new List<MaterialMapper>();

                GUIStyle mapperStyle = new StyleBuilder()
                    .Alignment(TextAnchor.MiddleRight)
                    .Background(77)
                    .Margin(20, 0)
                    .Build();
                
                GUIStyle meterialStyle = new StyleBuilder()
                    .Alignment(TextAnchor.MiddleRight)
                    .Background(88)
                    .Padding(10)
                    .Build();
                
                GUIStyle mapperHeader = new StyleBuilder().Padding(10).Build();
                
                GUIStyle partsStyle = new StyleBuilder().Padding(13, 10, 10, 10).Build();
                
                var style = new StyleBuilder()
                    .FontColor(Color.white)
                    .FontStyle(FontStyle.Bold)
                    .FontSize(16)
                    .Alignment(TextAnchor.MiddleCenter);

                foreach (var mapper in item.Mappers)
                {
                    using (new GUILayout.VerticalScope(mapperStyle))
                    {
                        using (new GUILayout.HorizontalScope(mapperHeader))
                        {
                            GUILayout.Label($"Wrapper", style.Build(), GUILayout.Height(25));
                            GUI.color = Color.red;
                            if (GUILayout.Button("X", GUILayout.Height(25), GUILayout.Width(25)))
                                mappersToRemove.Add(mapper);
                            GUI.color = Color.white;
                        }
                        
                        using (new GUILayout.VerticalScope(meterialStyle))
                        {
                            using (new GUILayout.HorizontalScope())
                            {
                                GUILayout.Label($"Materials ", style.Alignment(TextAnchor.MiddleLeft).Build(), GUILayout.Height(25));
                                if (GUILayout.Button("Add material", GUILayout.Height(25), GUILayout.Width(180)))
                                    mapper.Materials.Add(null);
                            }
                            
                            GUILayout.Space(10);

                            var materialsToRemove = new List<int>();

                            for (var i = 0; i < mapper.Materials.Count; i++)
                            {
                                using (new GUILayout.HorizontalScope())
                                {
                                    mapper.Materials[i] =
                                        (Material) EditorGUILayout.ObjectField(mapper.Materials[i], typeof(Material),
                                            false,
                                            GUILayout.ExpandWidth(true), GUILayout.Height(25));

                                    GUI.color = Color.red;
                                    if (GUILayout.Button("X", GUILayout.Height(25), GUILayout.Width(25)))
                                        materialsToRemove.Add(i);
                                    GUI.color = Color.white;
                                }
                            }

                            materialsToRemove.ForEach(i => mapper.Materials.RemoveAt(i));
                        }

                        using (new GUILayout.VerticalScope(partsStyle))
                        {
                            mapper.isOpened = EditorGUILayout.Foldout(mapper.isOpened, "Target body parts", true);

                            if (mapper.isOpened)
                            {
                                GUILayout.Label("The selected body parts will be overlaid with the materials above.", EditorStyles.wordWrappedLabel);
                                GUILayout.Space(10);

                                using (new GUILayout.VerticalScope())
                                    foreach (var wrapper in mapper.Wrappers)
                                        wrapper.Enabled = GUILayout.Toggle(wrapper.Enabled, wrapper.Type.ToString());
                            }
                        }
                    }
                    GUILayout.Space(15);
                }

                item.Mappers = item.Mappers.Where(i => !mappersToRemove.Contains(i)).ToList();
                
                if (GUI.changed)
                {
                    EditorUtility.SetDirty(item);
                    EditorSceneManager.MarkSceneDirty(item.gameObject.scene);
                }
            }
        }
    }
}