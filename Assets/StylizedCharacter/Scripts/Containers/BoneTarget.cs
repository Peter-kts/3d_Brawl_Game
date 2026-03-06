using System;
using NHance.Assets.Scripts.Enums;
using UnityEngine;

namespace NHance.Assets.StylizedCharacter.Scripts.Containers
{
    [Serializable]
    public class BoneTarget
    {
        public Transform Target;
        public BoneType Type = BoneType.None;
        public BoneSetup Setup = new BoneSetup();

        public BoneTarget(Transform target, BoneType type)
        {
            Target = target;
            Type = type;
        }
    }
}