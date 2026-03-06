using System;

namespace NHance.Assets.Scripts.Enums
{
#if UNITY_EDITOR
    [Serializable]
    public enum TextureType
    {
        Diffuse,
        Normal, 
        Emission
    }
#endif
}