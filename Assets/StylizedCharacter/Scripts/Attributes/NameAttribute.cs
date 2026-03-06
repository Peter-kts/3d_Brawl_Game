using System;

namespace NHance.Assets.Scripts.Attributes
{
    public class NameAttribute : Attribute
    {
        public string Name;

        public NameAttribute(string name)
        {
            Name = name;
        }
    }
}