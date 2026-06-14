using UnityEngine;

namespace NHance.Assets.Scripts
{
    public class CharacterInput
    {
        private CharacterSettings _settings;
        public CharacterInput(CharacterSettings settings)
        {
            _settings = settings;
        }

        public void Update()
        {
            if (!_settings.IsEnabled)
                Input.ResetInputAxes();

            Sprint();
            Walk();
            Jump();
            Movement();
        }

        private void Walk()
        {
            _settings.IsWalking = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        private void Sprint()
        {
            _settings.IsSprinting = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        private void Jump()
        {
            if (Input.GetButtonDown("Jump"))
                _settings.LastJumpButtonTime = Time.time;
        }

        private void Movement()
        {
            if (Input.GetMouseButton(1))
            {
                _settings.MovementAxis = Vector2.zero;
                return;
            }
            Vector2 playerInput;
            playerInput.x = Input.GetAxis("Vertical");
            playerInput.y = Input.GetAxis("Horizontal");
            playerInput = Vector2.ClampMagnitude(playerInput, 1f);
            _settings.MovementAxis = playerInput;
        }
    }
}