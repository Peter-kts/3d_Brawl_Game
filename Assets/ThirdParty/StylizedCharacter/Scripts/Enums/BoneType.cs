using System;
using NHance.Assets.Scripts.Attributes;

namespace NHance.Assets.Scripts.Enums
{
    [Serializable]
    public enum BoneType
    {
        None = 0,
        [Name("Weapon_L")]
        WeaponL = 1,
        [Name("Weapon_R")]
        WeaponR = 2,
        [Name("Back_Quiver")]
        Quiver = 3,
        [Name("Back_L")]
        BackL = 4,
        [Name("Back_R")]
        BackR = 5,
        [Name("Back_M")]
        BackM = 6,
        [Name("Back_Bow")]
        BackBow = 7,
        [Name("Back_2HL")]
        Back2HL = 8,
        [Name("Hip_R")]
        HipR = 9,
        [Name("Hip_L")]
        HipL = 10,
        [Name("Helmet")]
        Head = 11,
        [Name("Arrow")]
        Arrow = 12,
        [Name("Weapon_Shield")]
        Shield = 13
    }
}