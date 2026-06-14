using System;
using NHance.Assets.Scripts.Enums;

namespace NHance.Assets.StylizedCharacter.Scripts.Containers
{
    [Serializable]
    public class ItemBoneMatch
    {
        public ItemTypeEnum ItemType;
        public BoneType BoneType = BoneType.None;

        public ItemBoneMatch(ItemTypeEnum itemType, BoneType boneType)
        {
            ItemType = itemType;
            BoneType = boneType;
        }
    }
}