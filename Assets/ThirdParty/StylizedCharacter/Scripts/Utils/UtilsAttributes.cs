using System;
using System.Reflection;
using NHance.Assets.Scripts.Attributes;
using NHance.Assets.Scripts.Enums;

namespace NHance.Assets.Scripts.Utils
{
    public static class UtilsAttributes
    {
        public static TAttributeType GetAttribute<TAttributeType>(MemberInfo memInfo) where TAttributeType : Attribute
        {
            var attributes = memInfo.GetCustomAttributes(typeof(TAttributeType), false);
            foreach (var attribute in attributes)
                if(attribute.GetType() == typeof(TAttributeType))
                    return((TAttributeType)attribute);

            return null;
        }
        public static ItemTypeDescriptorAttribute TypeDescriptor(this ItemTypeEnum type)
        {
            var enumType = typeof(ItemTypeEnum);
            return UtilsAttributes.GetAttribute<ItemTypeDescriptorAttribute>(enumType.GetMember(type.ToString())[0]);
        }
        public static NameAttribute TypeDescriptor(this BoneType type)
        {
            var enumType = typeof(BoneType);
            return UtilsAttributes.GetAttribute<NameAttribute>(enumType.GetMember(type.ToString())[0]);
        }
        public static NameAttribute TypeDescriptor(this TargetBodyparts type)
        {
            var enumType = typeof(TargetBodyparts);
            return UtilsAttributes.GetAttribute<NameAttribute>(enumType.GetMember(type.ToString())[0]);
        }
    }
}