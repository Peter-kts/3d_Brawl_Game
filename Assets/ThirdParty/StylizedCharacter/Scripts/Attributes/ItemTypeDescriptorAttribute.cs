using System;
using NHance.Assets.Scripts.Enums;

namespace NHance.Assets.Scripts.Attributes
{
    [AttributeUsage(AttributeTargets.All, Inherited = false)]
    public class ItemTypeDescriptorAttribute : Attribute
    {
        public string Namespace;
        public ItemCategory Type;
        public Gender PersistInGender;
        public bool BlockAnotherTypesInNamespace;
        public string Category;
        public int Order;
        public bool IsCanBeInSocket;
        public bool IsCanBeFilteredByCharacterPrefix;
        public bool hasNamespace => !string.IsNullOrEmpty(Namespace);

        public ItemTypeDescriptorAttribute(int order = 0, string category = ItemCategoryString.Armor, Gender gender = Gender.All, string nameSpace = "", ItemCategory type = ItemCategory.Item,bool isCanBeInSocket = false, bool isCanBeFilteredByCharacterPrefix = true)
        {
            Order = order;
            Type = type;
            Namespace = nameSpace;
            Category = category;
            PersistInGender = gender;
            IsCanBeInSocket = isCanBeInSocket;
            IsCanBeFilteredByCharacterPrefix = isCanBeFilteredByCharacterPrefix;
        }
    }
}