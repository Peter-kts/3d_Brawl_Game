using System;
using NHance.Assets.Scripts.Enums;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    [Serializable]
    public class ItemCache
    {
        public GameObject Item;
        public ItemTypeEnum Type;

        public ItemCache(GameObject item, ItemTypeEnum type)
        {
            Item = item;
            Type = type;
        }
    }
}