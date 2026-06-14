using NHance.Assets.Scripts.Items;

namespace NHance.Assets.Scripts
{
    public static class MaterialMapperExtension
    {
        public static MaterialMapper GetInitialized(this MaterialMapper mapper)
        {
            InitializationHelper.InitializeWrappers(ref mapper.Wrappers);
            return mapper;
        }
    }
}