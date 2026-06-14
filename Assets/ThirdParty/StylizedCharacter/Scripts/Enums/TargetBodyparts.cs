using System;
using NHance.Assets.Scripts.Attributes;

namespace NHance.Assets.Scripts.Enums
{
    [Serializable]
    public enum TargetBodyparts
    {
        [Name("Head 1")]
        Head = 0,
        [Name("Ears_01")]
        Ears = 1,
        [Name("Eyes")]
        Eyes = 2,
        [Name("Torso_Naked")]
        Torso = 4,
        [Name("Bracers_Naked")]
        Bracers = 5,
        [Name("Hands")]
        Hands = 6,
        [Name("Pants")]
        Pants = 7,
        [Name("Boots_Naked")]
        Boots = 8,
        [Name("Feet")]
        Feet = 9,
        [Name("")]
		FemBrows = 10,
        [Name("Tusks_01")]
        Tusks = 11,
        [Name("")]
        OrcFemaleBrows = 12,
    }
}