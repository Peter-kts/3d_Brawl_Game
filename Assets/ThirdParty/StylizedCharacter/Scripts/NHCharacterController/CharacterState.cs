using NHance.Assets.Scripts.Enums;

namespace NHance.Assets.Scripts
{
    public class CharacterState
    {
        private CharacterSettings _settings;
        public CharacterState(CharacterSettings settings)
        {
            _settings = settings;
            // _settings.Pool.ChangeState += Set;
        }
    }
}