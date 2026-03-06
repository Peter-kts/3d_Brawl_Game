using System;
using System.Collections.Generic;
using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts.Editor
{
    public class TabComponent
    {
        private List<TabContainer> _tabs = new List<TabContainer>();

        public TabComponent(params TabMessage[] drawActions)
        {
            foreach (var m in drawActions)
                _tabs.Add(new TabContainer(m));

            if (_tabs.Count > 0)
                _tabs[0].Switch(true);
        }

        public void Draw()
        {
            using (new GUILayout.VerticalScope())
            {
                using (new GUILayout.HorizontalScope())
                    foreach (var t in _tabs)
                        t.DrawButton(() => _tabs.ForEach(tab => tab.Switch(false)));

                GUILayout.Space(10);
                foreach (var t in _tabs)
                    t.DrawContent();
            }
        }

        public void Destroy()
        {
            _tabs.ForEach(t => t.Destroy());
            _tabs.Clear();
        }
    }

    public class TabMessage
    {
        public Action Action;
        public string Title;

        public TabMessage(string title, Action action)
        {
            Action = action;
            Title = title;
        }
    }

    public class TabContainer
    {
        private TabMessage _message;
        private bool _state;

        public TabContainer(TabMessage message)
        {
            _message = message;
        }

        public void Switch(bool state)
        {
            _state = state;
        }

        public bool DrawButton(Action disableRest = null)
        {
            if (_state)
                GUI.color = Color.green;
            
            if (GUILayout.Button(_message.Title, GUILayout.Height(50)))
            {
                disableRest?.Invoke();
                _state = true;
            }

            GUI.color = Color.white;

            return _state;
        }

        public void DrawContent()
        {
            if (_state)
                _message.Action?.Invoke();
        }

        public void Destroy()
        {
            _message.Action = null;
            _message = null;
        }
    }
}