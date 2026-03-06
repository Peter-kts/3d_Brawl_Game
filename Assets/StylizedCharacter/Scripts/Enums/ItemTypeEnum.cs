using System;
using NHance.Assets.Scripts.Attributes;

namespace NHance.Assets.Scripts.Enums
{
    [Serializable]
    public enum ItemTypeEnum
    {
        [ItemTypeDescriptor(Order = 1, Type = ItemCategory.Skin)]
        Skin = 0,
        [ItemTypeDescriptor(Order = 2, Type = ItemCategory.Skin)]
        HeadSkin = 1,
        [ItemTypeDescriptor(Order = 3, Type = ItemCategory.Skin, IsCanBeFilteredByCharacterPrefix = false, IsCanBeInSocket = false)]
        Eyes = 2,
        [ItemTypeDescriptor(Order = 0, Category = ItemCategoryString.Head)]
        Hair = 3,
        [ItemTypeDescriptor(Order = 7 )]
        Chest = 4,
        [ItemTypeDescriptor(Order = 8)]
        Cape = 5,
        [ItemTypeDescriptor(Order = 10)]
        Belt = 6,
        [ItemTypeDescriptor(Order = 12)]
        Boots = 7,
        [ItemTypeDescriptor(Order = 15, Category = ItemCategoryString.Weapon, IsCanBeFilteredByCharacterPrefix = false, IsCanBeInSocket = true)]
        Quiver = 8,
        [ItemTypeDescriptor(Order = 13, Category = ItemCategoryString.Weapon, IsCanBeFilteredByCharacterPrefix = false, Namespace = "Weapon,LWeapon", IsCanBeInSocket = true)]
        WeaponL = 9,
        [ItemTypeDescriptor(Order = 14,  Category = ItemCategoryString.Weapon, IsCanBeFilteredByCharacterPrefix = false, Namespace = "Weapon", IsCanBeInSocket = true)]
        WeaponR = 10,
        [ItemTypeDescriptor(Order = 1, Category = ItemCategoryString.Head, PersistInGender = Gender.HumanMale)]
        Beard = 11,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.HumanMale)]
        Brows = 12,
        [ItemTypeDescriptor(Order = 1, Category = ItemCategoryString.Head, PersistInGender = Gender.HumanFemale)]
        Piercing = 13,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.HumanFemale)]
        Earrings = 14,
        [ItemTypeDescriptor(Order = 6, Type = ItemCategory.Skin, IsCanBeFilteredByCharacterPrefix = false)]
        ChestSkin = 15,
        [ItemTypeDescriptor(Order = 7, Type = ItemCategory.Skin, IsCanBeFilteredByCharacterPrefix = false)]
        GlovesSkin = 16,
        [ItemTypeDescriptor(Order = 8, Type = ItemCategory.Skin, IsCanBeFilteredByCharacterPrefix = false)]
        PantsSkin = 17,
        [ItemTypeDescriptor(Order = 5, Type = ItemCategory.Skin)]
        UnderwearSkin = 18,
        [ItemTypeDescriptor(Order = 4, Type = ItemCategory.Skin, IsCanBeFilteredByCharacterPrefix = false)]
        BodyAdditional= 19,
        [ItemTypeDescriptor(Order = 9 )]
        Gloves = 20,
        [ItemTypeDescriptor(Order = 4 )]
        Helmet = 21,
        [ItemTypeDescriptor(Order = 11 )]
        Pants = 22,
        [ItemTypeDescriptor(Order = 5)]
        Shoulders = 23,
        [ItemTypeDescriptor(Order = 9, Type = ItemCategory.Skin, PersistInGender = Gender.HumanFemale)]
		FemBrows = 24,
        [ItemTypeDescriptor(Order = 16, Category = ItemCategoryString.Weapon, IsCanBeFilteredByCharacterPrefix = false, IsCanBeInSocket = true, Namespace = "LWeapon")]
        Shield = 25,
        [ItemTypeDescriptor(Order = 1, Category = ItemCategoryString.Head, PersistInGender = Gender.OrcMale, Namespace = "Orc")]
        BeardStyle = 26,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.OrcMale, Namespace = "Orc")]
        BrowStyle = 27,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.OrcMale, Namespace = "Orc")]
        Tusks = 28,
        [ItemTypeDescriptor(Order = 2, Type = ItemCategory.Skin)]
        HeadAdditional = 29,
        [ItemTypeDescriptor(Order = 1, Category = ItemCategoryString.Head, PersistInGender = Gender.OrcFemale, Namespace = "Orc")]
        OrF_Piercing = 30,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.OrcFemale, Namespace = "Orc")]
        OrF_Earrings = 31,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.OrcFemale, Namespace = "Orc")]
        OrF_Tusks = 32,
        [ItemTypeDescriptor(Order = 9, Type = ItemCategory.Skin, PersistInGender = Gender.OrcFemale)]
        OrF_Brows = 33,
        [ItemTypeDescriptor(Order = 1, Category = ItemCategoryString.Head, PersistInGender = Gender.DwarfMale, Namespace = "Dwarf")]
        DwM_Beard = 34,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.DwarfMale, Namespace = "Dwarf")]
        DwM_Moustache = 35,
        [ItemTypeDescriptor(Order = 2, Category = ItemCategoryString.Head, PersistInGender = Gender.DwarfMale, Namespace = "Dwarf")]
        DwM_Brows = 36,
        [ItemTypeDescriptor(Order = 2, Type = ItemCategory.Skin)]
        HeadScars = 37,
        [ItemTypeDescriptor(Order = 1, Category = ItemCategoryString.Head, PersistInGender = Gender.DwarfFemale, Namespace = "Dwarf")]
        DwF_Brows = 38,
    }
}