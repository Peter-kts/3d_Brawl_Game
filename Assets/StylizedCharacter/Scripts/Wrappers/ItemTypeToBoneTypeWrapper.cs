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
    public class ItemTypeToBoneTypeWrapper
    {
        public List<ItemBoneMatch> list = new List<ItemBoneMatch>();

        public BoneType this[ItemTypeEnum key]
        {
            get
            {
                var socket = list.FirstOrDefault(c => c.ItemType == key);
                if (socket == null)
                {
                    socket = new ItemBoneMatch(key, BoneType.None);
                    list.Add(socket);
                }

                return socket.BoneType;
            }
            set
            {
                Remove(key);
                list.Add(new ItemBoneMatch(key, value));
            }
        }

        public void Remove(ItemTypeEnum type)
        {
            var prev = list.FirstOrDefault(c => c.ItemType == type);
            if (prev != null)
                list.Remove(prev);
        }
    }
}