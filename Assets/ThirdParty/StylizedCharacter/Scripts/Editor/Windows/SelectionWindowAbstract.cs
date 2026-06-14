using System;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.Scripts.Items;
using NHance.Assets.Scripts.Utils;
using UnityEditor;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    public abstract class SelectionWindowAbstract
    {
        protected Action<object> _onSelected;
        protected Action _onClose;
        protected int _selected = -1;
        protected int _width = 5;
        protected int _parentWidth;
        protected Vector2 _scrollPosition;
        protected string _prefix;

        public virtual void Init(NHItem obj, ItemTypeEnum type, string prefix, Action<object> onSelected, Action onClose, int parentWidth)
        {
            _onSelected = onSelected;
            _onClose = onClose;
            _parentWidth = parentWidth;
            _prefix = prefix;
        }

        public abstract void OnGUI();
    }

    public class SelectionWindowAbstract<T> : SelectionWindowAbstract where T : NHItem
    {
        private List<T> _items = new List<T>();
        private Dictionary<int, GUIContent> _activeItems = new Dictionary<int, GUIContent>();
        private GUIContent[] _activeItemsSearched;
        private GUIContent _currentObject;
        private string _search = "";

        public override void Init(NHItem obj, ItemTypeEnum type, string prefix, Action<object> onSelected, Action onClose, int parentWidth)
        {
            base.Init(obj, type, prefix, onSelected, onClose, parentWidth);
            _items.Clear();
            _activeItems.Clear();

            var typeDescriptor = type.TypeDescriptor();

            _items = UtilsAssets.LoadPrefabsContaining<T>();
            _items = _items
                .Where(i => i.Type == type).ToList();
                
            if(typeDescriptor.IsCanBeFilteredByCharacterPrefix && !string.IsNullOrEmpty(prefix))
                _items = _items.Where(i => i.name.ToLower().StartsWith(prefix.ToLower())).ToList();
                
            for (var index = 0; index < _items.Count; index++)
            {
                var i = _items[index];

                var view = new GUIContent(i.name, AssetPreview.GetMiniThumbnail(i.gameObject));

                if (_currentObject == null && obj != null && obj.GetInstanceID() == i.GetInstanceID())
                {
                    _currentObject = view;
                    _selected = _activeItems.Count;
                }

                _activeItems.Add(index, view);
            }

            _activeItemsSearched = _activeItems.Values.ToArray();
        }

        public override void OnGUI()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label("Search:",GUILayout.Width(50), GUILayout.Height(25));
                    var nsearch = GUILayout.TextField(_search, GUILayout.ExpandWidth(true), GUILayout.Height(25));
                    if (nsearch != _search)
                    {
                        _search = nsearch;
                        _activeItemsSearched = _activeItems.Where(a => a.Value.text.ToLower().Contains(_search.ToLower())).Select(s => s.Value).ToArray();
                        var founded = false;
                        for (int i = 0; i < _activeItemsSearched.ToList().Count; i++)
                        {
                            if (_activeItemsSearched[i] == _currentObject)
                            {
                                _selected = i;
                                founded = true;
                                break;
                            }
                        }

                        if (!founded)
                            _selected = -1;
                    }
                }

                
                using (var scope = new GUILayout.ScrollViewScope(_scrollPosition, false,
                    true, GUIStyle.none, GUI.skin.verticalScrollbar))
                {
                    _scrollPosition = scope.scrollPosition;
                    var newSelected = GUILayout.SelectionGrid(_selected, _activeItemsSearched, _width,
                        GUILayout.Width(_parentWidth - 20));
                    if (newSelected != _selected)
                    {
                        _selected = newSelected;
                        var item = _activeItems.FirstOrDefault(s => s.Value == _activeItemsSearched[_selected]);
                        _currentObject = item.Value;
                        _onSelected?.Invoke(_items[item.Key]);
                    }
                }
            }
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape || (ev.keyCode == KeyCode.Space && _selected != -1))
            {
                _onClose?.Invoke();
            }
        }
    }
}