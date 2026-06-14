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
    [CustomEditor(typeof(NHAvatar), true)]
    public class NHAvatarEditor : Editor
    {
        private NHAvatar _instance;
        private TabComponent _component;

        private void OnEnable()
        {
            _instance = target as NHAvatar;
            _component = new TabComponent(
                new TabMessage("Items", DrawItems),
                new TabMessage("Skins", DrawSkins),
                new TabMessage("Setup", DrawSetup));

            var values = Enum.GetValues(typeof(TargetBodyparts));

            if (_instance.PartsMap.Count != values.Length)
            {
                _instance.PartsMap.Clear();

                foreach (var v in values)
                    _instance.PartsMap.Add(new BodypartWrapper((TargetBodyparts) v, null));
            }
        }

        private void OnDisable()
        {
            _instance = null;
            if (_component != null)
            {
                _component.Destroy();
                _component = null;
            }
        }

        private void ClearAnotherGender()
        {
            foreach (ItemTypeEnum type in new ItemTypeQuery().ExcludeGender(_instance.Gender).Build())
            {
                _instance.Items.Remove(type);
                _instance.CleanCache(type);
            }
        }

        private void DrawItems()
        {
                foreach (var category in ItemCategoryString.Categories)
                {
                    
                    GUILayout.Label(category, new StyleBuilder()
                    .FontSize(18)
                    .FontStyle(FontStyle.Bold)
                    .FontColor(Color.white)
                    .Build());

                    DrawItemByType(new ItemTypeQuery()
                   .WithCategory(ItemCategory.Item)
                   .WithFilter(category)
                   .WithGender(_instance.Gender)
                   .WithOrder(OrderType.Ascending)
                  
                   .Build().ToArray());
              
                GUILayout.Space(15);
                }
            
        }

        private void DrawItemByType(params ItemTypeEnum[] types)
        {
            foreach (var type in types)
               
                    if (!type.TypeDescriptor().hasNamespace || type.TypeDescriptor().hasNamespace && !AvatarHasItemOfTypesAndBlocksAnotherItems(new ItemTypeQuery()
                        .WithNamespace(type.TypeDescriptor().Namespace)
                        .Exclude(type)
                        .Build().ToArray()))
                        DrawSelector(type.ToString(), _instance.Items[type], type, o =>
                        {
                            _instance.Items[type] = o as NHItem;
                            if (_instance.Auto)
                                _instance.Clean().Compile();
                        },
                            () =>
                            {
                                _instance.Items.Remove(type);
                                if (_instance.Auto)
                                    _instance.Clean().Compile();
                            });
                
        }

        private bool AvatarHasItemOfTypesAndBlocksAnotherItems(params ItemTypeEnum[] types)
        {
            foreach (ItemTypeEnum type in types)
                if (_instance.Items[type] != null && _instance.Items[type].IsTakeTwoHands)
                    return true;

            return false;
        }

        private void DrawSkins()
        {
            DrawItemByType(new ItemTypeQuery()
                .WithCategory(ItemCategory.Skin)
                .WithGender(_instance.Gender)
                .WithOrder(OrderType.Ascending)
                .Build().ToArray());
        }

        private void DrawSetup()
        {
            GUIStyle headerStyle = new GUIStyle();
            headerStyle.fontSize = 20;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = Color.white;

            GUIStyle mapperStyle = new GUIStyle();
            mapperStyle.margin = new RectOffset(20, 20, 0, 0);

            _instance.Gender = GUIUtils.DrawEnumWithLabel(_instance.Gender, "Race & Gender");
            switch (_instance.Gender)
            {
                case Gender.HumanFemale:
                    _instance.characterPrefix = "Hu_F";
                    break;
                case Gender.HumanMale:
                    _instance.characterPrefix = "Hu_M";
                    break;
                case Gender.OrcMale:
                    _instance.characterPrefix = "Or_M";
                    break;
                case Gender.OrcFemale:
                    _instance.characterPrefix = "Or_F";
                    break;
                case Gender.DwarfMale:
                    _instance.characterPrefix = "Dw_M";
                    break;
                case Gender.DwarfFemale:
                    _instance.characterPrefix = "Dw_F";
                    break;
                default:
                    break;
            }
            
            var bone = (Transform) EditorGUILayout.ObjectField("Root bone", _instance.rootBone,
                typeof(Transform), allowSceneObjects: true);
            if (_instance.rootBone != bone)
            {
                var auto = true;
                if (_instance.rootBone != null && bone != null)
                    auto = EditorUtility.DisplayDialog("Root bone changed",
                        "You just change root bone of Avatar. Previous root bone exists. Did you want activate auto setup for new root bone?",
                        "Yes", "No");
                _instance.rootBone = bone;
                if (auto && _instance.rootBone != null)
                {
                    _instance.AutoSocketTargetSetup();
                }
            }
            
            var geometry = (Transform) EditorGUILayout.ObjectField("Root Geometry", _instance.rootGeometry,
                typeof(Transform), allowSceneObjects: true);
            if (_instance.rootGeometry != geometry)
            {
                var auto = true;
                if (_instance.rootGeometry != null && geometry != null)
                    auto = EditorUtility.DisplayDialog("Root geometry changed",
                        "You just change root geometry of Avatar. Previous root geometry exists. Did you want activate auto setup for new root geometry?",
                        "Yes", "No");
                _instance.rootGeometry = geometry;
                if (auto && _instance.rootGeometry != null)
                {
                    _instance.AutoBodyPartsSetup();
                }
            }
            
            
            GUILayout.Space(20);

            GUIUtils.DrawMaterialList("Body Materials To Replace", _instance.BodyMaterialsToReplace);
            GUIUtils.DrawMaterialList("Head Materials To Replace", _instance.HeadMaterialsToReplace);
            GUIUtils.DrawMaterialList("Default Body Materials", _instance.DefaultBodyMaterial);
            GUIUtils.DrawMaterialList("Default Head Materials", _instance.DefaultHeadMaterial);
            GUIUtils.DrawMaterialList("Default Eyes Materials", _instance.DefaultEyesMaterial);
            if (_instance.Gender == Gender.HumanFemale)
                GUIUtils.DrawMaterialList("Default Female Brows Materials", _instance.DefaultFemBrowsMaterial);
            else
                _instance.DefaultFemBrowsMaterial.Clear();
            if (_instance.Gender == Gender.OrcFemale)
                GUIUtils.DrawMaterialList("Default Orc Female Brows Materials", _instance.DefaultOrcFemBrowsMaterial);
            else
                _instance.DefaultOrcFemBrowsMaterial.Clear();

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.wordWrap = true;

            GUILayout.Space(20);
            _instance.FoldoutItemToBoneMapper = EditorGUILayout.Foldout(_instance.FoldoutItemToBoneMapper,
                "Items Socketing Setup (PRO)",
                toggleOnLabelClick: true);
            if (_instance.FoldoutItemToBoneMapper)
            {
                GUILayout.Label(
                    "This section is for configuring sockets for objects by their type, which imply a specific position in the bones of the animator.",
                    style);
                GUILayout.Space(10);
                using (new GUILayout.VerticalScope())
                    foreach (ItemTypeEnum type in new ItemTypeQuery()
                        .WithGender(_instance.Gender)
                        .ItemCanBeInSocket(SocketingType.CanBeInSocket)
                        .WithOrder(OrderType.Ascending)
                        .Build())
                        using (new GUILayout.HorizontalScope())
                            _instance.itemToBoneMapper[type] =
                                (BoneType) EditorGUILayout.EnumPopup(type.ToString(), _instance.itemToBoneMapper[type]);
            }

            GUILayout.Space(20);

            using (new GUILayout.HorizontalScope())
            {
                _instance.FoldoutSocketsSetup = EditorGUILayout.Foldout(_instance.FoldoutSocketsSetup,
                    "Socket Target Setup (PRO)",  toggleOnLabelClick: true);
                if (_instance.rootBone != null && GUILayout.Button("Auto", GUILayout.Width(100)))
                    _instance.AutoSocketTargetSetup();
            }

            if (_instance.FoldoutSocketsSetup)
            {
                GUILayout.Label(
                    "This section is for configuring sockets in main character object, which imply a specific position in the bones of the animator.",
                    style);
                GUILayout.Space(10);
                using (new GUILayout.VerticalScope())
                    foreach (BoneType type in Enum.GetValues(typeof(BoneType)))
                        if (type != BoneType.None)
                            using (new GUILayout.HorizontalScope())
                            {
                                _instance.SocketMap[type] = (Transform) EditorGUILayout.ObjectField(type.ToString(),
                                    _instance.SocketMap[type], typeof(Transform), allowSceneObjects: true);
                            }
            }

            GUILayout.Space(20);
            _instance.FoldoutSocketsTransformSetup = EditorGUILayout.Foldout(_instance.FoldoutSocketsTransformSetup,
                "Socket Transform Setup(PRO)",
                toggleOnLabelClick: true);
            if (_instance.FoldoutSocketsTransformSetup)
            {
                using (new GUILayout.VerticalScope(mapperStyle))
                {
                    foreach (BoneType type in Enum.GetValues(typeof(BoneType)))
                    {
                        if (type == BoneType.None)
                            continue;

                        if (_instance.SocketMap[type] == null)
                            _instance.SocketMap[type] = null;

                        var socket = _instance.SocketMap.Get(type);

                        socket.Setup.Foldout = EditorGUILayout.Foldout(socket.Setup.Foldout, type.ToString());
                        if (socket.Setup.Foldout)
                            using (new GUILayout.VerticalScope())
                            {
                                socket.Setup.Position =
                                    GUIUtils.DrawVector3FieldWithLabel(socket.Setup.Position, "Position");
                                socket.Setup.Rotation =
                                    GUIUtils.DrawVector3FieldWithLabel(socket.Setup.Rotation, "Rotation");
                            }
                    }
                }
            }


            GUILayout.Space(20);
            using (new GUILayout.HorizontalScope())
            {
                _instance.FoldoutBodyPartSetup = EditorGUILayout.Foldout(_instance.FoldoutBodyPartSetup,
                    "Body parts setup (PRO)",
                    toggleOnLabelClick: true);
                if (_instance.rootGeometry != null && GUILayout.Button("Auto", GUILayout.Width(100)))
                    _instance.AutoBodyPartsSetup();
            }
            
            if (_instance.FoldoutBodyPartSetup)
            {
                GUILayout.Label(
                    "This section is for customizing the body parts of the character. They are used in item settings, if an item is hiding a part, it will hide that particular part.",
                    style);
                GUILayout.Space(10);
                foreach (var e in _instance.PartsMap)
                {
                    if ((_instance.Gender == Gender.HumanMale || _instance.Gender==Gender.OrcMale || _instance.Gender == Gender.OrcFemale || _instance.Gender == Gender.DwarfMale || _instance.Gender == Gender.DwarfFemale)  && e.Type == TargetBodyparts.FemBrows)
                        continue;
                    else if ((_instance.Gender == Gender.HumanMale || _instance.Gender == Gender.OrcMale || _instance.Gender == Gender.HumanFemale || _instance.Gender == Gender.DwarfMale || _instance.Gender == Gender.DwarfFemale) && e.Type == TargetBodyparts.OrcFemaleBrows)
                        continue;
                    else if ((_instance.Gender == Gender.HumanMale || _instance.Gender == Gender.HumanFemale || _instance.Gender == Gender.DwarfMale || _instance.Gender == Gender.DwarfFemale) && e.Type == TargetBodyparts.Tusks)
                        continue;
                    
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Label(e.Type.ToString());
                        GUILayout.FlexibleSpace();
                        var eTarget = e.Target;
                        e.Target = (GameObject) EditorGUILayout.ObjectField(e.Target, typeof(GameObject), allowSceneObjects: true,
                            GUILayout.Width(200));
                        if (eTarget != e.Target)
                            e.Clean();
                    }
                }
            }
        }

        public override void OnInspectorGUI()
        {
            GUIUtils.DrawJoinButton();

            _instance.showAnimationControls =
                GUIUtils.DrawEnumWithLabel(_instance.showAnimationControls, "Show animation controls");

            GUILayout.Space(10);

            ClearAnotherGender();
            _component.Draw();

            GUILayout.Space(20);
            _instance.Auto = EditorGUILayout.Toggle("Auto Compile", _instance.Auto);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save prefab"))
                {
                    _instance.Clean();
                    _instance.Compile();
                    _instance.Save();
                }
                
                if (GUILayout.Button("Compile"))
                {
                    _instance.Clean();
                    _instance.Compile();
                }

                if (GUILayout.Button("Clean"))
                    _instance.Clean();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(_instance);
                if (!Application.isPlaying)
                    EditorSceneManager.MarkSceneDirty(_instance.gameObject.scene);
            }
        }

        private void DrawSelector<T>(string title, T obj, ItemTypeEnum type, Action<object> save, Action clean)
            where T : NHItem
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, GUILayout.ExpandWidth(false), GUILayout.Width(80),
                    GUILayout.Height(25));
                if (obj != null)
                    EditorGUILayout.TextArea(obj.name, GUILayout.ExpandWidth(true), GUILayout.Height(25));
                else
                    EditorGUILayout.LabelField("", GUILayout.ExpandWidth(true), GUILayout.Height(25));

                if (GUILayout.Button("S", GUILayout.Width(25), GUILayout.Height(25)))
                    SelectorWindow.Init<T>(obj, type, _instance.characterPrefix, save);

                if (GUILayout.Button("X", GUILayout.Width(25), GUILayout.Height(25)))
                    clean?.Invoke();
            }
        }
    }
}