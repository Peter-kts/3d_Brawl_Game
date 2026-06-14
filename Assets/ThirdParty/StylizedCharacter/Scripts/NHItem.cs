using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NHance.Assets.Scripts.Attributes;
using NHance.Assets.Scripts.Enums;
using UnityEngine;

namespace NHance.Assets.Scripts.Items
{
    [HelpURL("https://github.com/DustCoron/NhanceRPGDocumentation/wiki/Items#nhitem")]
    public class NHItem : MonoBehaviour
    {
        public ItemTypeEnum Type;    
        public List<MaterialMapper> Mappers = new List<MaterialMapper>();
        public bool IsTakeTwoHands = false;

        public bool CopySkinMaterial;
        public List<BodypartWrapper> Wrappers = new List<BodypartWrapper>();

        
        public void SetDefaultTargets()
        {
            Wrappers[(int)TargetBodyparts.Boots].Enabled = true;
            Wrappers[(int)TargetBodyparts.Bracers].Enabled = true;
            Wrappers[(int)TargetBodyparts.Hands].Enabled = true;
            Wrappers[(int)TargetBodyparts.Pants].Enabled = true;

            foreach (var partType in GetPartByType())
                Wrappers[(int) partType].Enabled = true;
        }

        public void ClearTargets()
        {
            Wrappers.ForEach(w => w.Enabled = false);
        }

        public List<TargetBodyparts> GetPartByType()
        {
            List<TargetBodyparts> parts = new List<TargetBodyparts>();
            
            switch (Type)
            {
                case ItemTypeEnum.Chest:
                    parts.Add(TargetBodyparts.Torso);
                    break;
                case ItemTypeEnum.Boots:
                    parts.Add(TargetBodyparts.Boots);
                    parts.Add(TargetBodyparts.Feet);
                    break;
                case ItemTypeEnum.Pants:
                    parts.Add(TargetBodyparts.Pants);
                    break;
            }

            return parts;
        }
    }
}