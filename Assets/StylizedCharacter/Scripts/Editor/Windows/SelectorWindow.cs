using System;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.Scripts.Items;
using UnityEditor;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    public class SelectorWindow : EditorWindow
    {
        private static SelectorWindow _instance;

        private Action<object> _save;
        private object myImplementation = null;

        public static void Init<T>(NHItem obj, ItemTypeEnum type, string prefix, Action<object> save)
        {
            var window = (SelectorWindow) GetWindow(typeof(SelectorWindow), false, "Selector");
            var size = new Vector2(Screen.currentResolution.width / 2, Screen.currentResolution.height / 2f);

            window.minSize = size;
            window.maxSize = size;
            window.position = new Rect(Screen.currentResolution.width / 4f, Screen.currentResolution.height / 4f, window.minSize.x, window.minSize.y);
            window.myImplementation =
                Activator.CreateInstance(typeof(SelectionWindowAbstract<>).MakeGenericType(typeof(T)));
            (window.myImplementation as SelectionWindowAbstract).Init(obj, type, prefix, window.OnSelect, window.OnClose, (int) size.x);
            window._save = save;
            window.Show();

            _instance = window;
        }

        private void OnSelect(object obj)
        {
            _save?.Invoke(obj);
        }

        private void OnClose()
        {
            _instance.Close();
        }

        private void Clear()
        {
            _instance = null;
        }

        private void OnGUI()
        {
            (myImplementation as SelectionWindowAbstract).OnGUI();
        }

        private void OnDestroy()
        {
            Clear();
        }

        private void OnDisable()
        {
            Clear();
        }
    }
}