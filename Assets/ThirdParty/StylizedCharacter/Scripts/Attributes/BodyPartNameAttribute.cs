using System;
using NHance.Assets.Scripts.Enums;

namespace NHance.Assets.Scripts.Attributes
{
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    sealed class BodyPartNameAttribute : Attribute
    {
        public TargetBodyparts BodyPartName;
        public BodyPartNameAttribute(TargetBodyparts name)
        {
            BodyPartName = name;
        }
    }
}