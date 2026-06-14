using System.Collections.Generic;

namespace NHance.Assets.Scripts.Enums
{
    public static class ItemCategoryString
    {
        public const string Armor = "Armor";
        public const string Head = "Head";
        public const string Weapon = "Weapon";

        public static readonly List<string> Categories = new List<string>()
        {
            Head,
            Armor,
            Weapon
        };
    }
}