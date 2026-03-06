using System;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.StylizedCharacter.Scripts.Containers;
using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts.Wrappers
{
    [Serializable]
    public class GameObjectByBoneTypeWrapper
    {
        public List<BoneTarget> _list = new List<BoneTarget>();

        public Transform this[BoneType key]
        {
            get
            {
                return _list.FirstOrDefault(c => c.Type == key)?.Target;
            }
            set
            {
                var socket = Get(key);
                if (socket != null)
                    socket.Target = value;
                else
                    _list.Add(new BoneTarget(value, key));
            }
        }

        public BoneTarget Get(BoneType type)
        {
            return _list.FirstOrDefault(c => c.Type == type);
        }

        public void Remove(BoneType type)
        {
            var target = _list.FirstOrDefault(c => c.Type == type);
            if (target != null)
                _list.Remove(target);
        }

        public void Clear()
        {
            _list.Clear();
        }
    }
}