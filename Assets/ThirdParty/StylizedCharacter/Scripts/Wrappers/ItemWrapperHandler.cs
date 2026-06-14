using System;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.Scripts.Items;
using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts
{
    [Serializable]
    public class ItemWrapperHandler
    {
        public List<ItemWrapper> _list = new List<ItemWrapper>();

        public NHItem this[ItemTypeEnum key]
        {
            get
            {
                return _list.FirstOrDefault(c => c.Type == key)?.Item;
            }
            set
            {
                Remove(key);
                if(value != null)
                    _list.Add(new ItemWrapper(value, key));
            }
        }

        public void Clear()
        {
            _list.Clear();
        }

        public void Remove(ItemTypeEnum type)
        {   
            var prev = _list.FirstOrDefault(c => c.Type == type);
            if (prev != null)
                _list.Remove(prev);
            
        }
    }
}