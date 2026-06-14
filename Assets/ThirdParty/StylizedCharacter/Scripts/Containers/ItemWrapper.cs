using System;
using NHance.Assets.Scripts.Enums;
using NHance.Assets.Scripts.Items;
using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts
{
    [Serializable]
    public class ItemWrapper
    {
        public NHItem Item;
        public ItemTypeEnum Type;

        public ItemWrapper(NHItem item, ItemTypeEnum type)
        {
            Item = item;
            Type = type;
        }
    }
}