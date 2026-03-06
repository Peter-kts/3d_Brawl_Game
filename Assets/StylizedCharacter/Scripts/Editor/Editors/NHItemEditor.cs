using System;
using System.Collections.Generic;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.Scripts.Items;
using NHance.Assets.StylizedCharacter.Scripts.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    [CustomEditor(typeof(NHItem))]
    public class NHItemEditor : Editor
    {
        protected NHItem _instance;
        protected TabComponent _tabComponent;

        protected virtual void OnEnable()
        {
            _instance = target as NHItem;
            _instance.OnEnable();
            _tabComponent = new TabComponent(
                new TabMessage("Mesh", _instance.DrawMesh),
                new TabMessage("Material", _instance.DrawMaterial));
        }

        protected void OnDestroy()
        {
            if (_instance != null)
                EditorUtility.SetDirty(_instance);
            _instance = null;
        }

        protected virtual void OnDisable()
        {
            if (_instance != null)
                EditorUtility.SetDirty(_instance);
            _instance = null;
        }

        public override void OnInspectorGUI()
        {
            _instance.Type = GUIUtils.DrawEnumWithLabel(_instance.Type, "Item type");
            GUILayout.Space(5);

            var canBeTwoHanded = new List<ItemTypeEnum>
            {
                ItemTypeEnum.WeaponL,
                ItemTypeEnum.WeaponR,
                ItemTypeEnum.Shield
            };
            if (canBeTwoHanded.Contains(_instance.Type))
            {
                _instance.IsTakeTwoHands = GUIUtils.DrawEnumWithLabel(_instance.IsTakeTwoHands, "Two hand weapon");
                GUILayout.Space(5);
            }
            
            _instance.CopySkinMaterial = GUIUtils.DrawEnumWithLabel(_instance.CopySkinMaterial, "Copy Skin Material");
            GUILayout.Space(10);
            _tabComponent.Draw();
            if (GUI.changed)
            {
                EditorUtility.SetDirty(_instance);
                EditorSceneManager.MarkSceneDirty(_instance.gameObject.scene);
            }
        }
    }
}