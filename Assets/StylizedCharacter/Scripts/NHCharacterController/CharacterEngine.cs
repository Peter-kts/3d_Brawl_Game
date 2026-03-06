using NHance.Assets.Scripts.Enums;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    public class CharacterEngine
    {
        private CharacterSettings _settings;

        public CharacterEngine(CharacterSettings settings)
        {
            _settings = settings;
        }

        private void UpdateSmoothedMovementDirection()
        {
            var forward = Vector3.forward;
            if (_settings.MovementSpace != null)
                forward = _settings.MovementSpace.TransformDirection(forward);

            forward.y = 0;
            forward = forward.normalized;

            var right = new Vector3(forward.z, 0, -forward.x);

            var v = _settings.MovementAxis.x;
            var h = _settings.MovementAxis.y;

            if (v < -0.2)
                _settings.IsMovingBack = true;
            else
                _settings.IsMovingBack = false;

            var wasMoving = _settings.IsMoving;
            _settings.IsMoving = Mathf.Abs(h) > 0.1 || Mathf.Abs(v) > 0.1;
            var targetDirection = h * right + v * forward;

            if (_settings.IsGrounded)
            {
                _settings.TimeSinceLastMove += Time.deltaTime;
                if (_settings.IsMoving)
                    _settings.TimeSinceLastMove = 0.0f;

                if (targetDirection != Vector3.zero)
                {
                    _settings.MoveDirection = Vector3.RotateTowards(_settings.MoveDirection, targetDirection,
                        _settings.RotateSpeed * Mathf.Deg2Rad * Time.deltaTime, 1000);
                    _settings.MoveDirection = _settings.MoveDirection.normalized;
                }

                var curSmooth = _settings.SpeedSmoothing * Time.deltaTime;
                var targetSpeed = Mathf.Min(targetDirection.magnitude, 1.0f);


                if (_settings.IsSprinting)
                {
                    targetSpeed *= _settings.SprintSpeed;
                }
                else if (_settings.IsWalking)
                {
                    targetSpeed *= _settings.WalkSpeed;
                }
                else
                {
                    targetSpeed *= _settings.RunSpeed;
                }

                _settings.MoveSpeed = Mathf.Lerp(_settings.MoveSpeed, targetSpeed, curSmooth);
                if (_settings.MoveSpeed > 0.01f && _settings.CharacterState != CharacterStateEnum.IsMoving)
                    _settings.CharacterState = CharacterStateEnum.IsMovingStarted;
            }
            else
            {
                if (_settings.CharacterState == CharacterStateEnum.JumpStarted)
                    _settings.TimeSinceLastMove = 0.0f;

                if (targetDirection != Vector3.zero)
                {
                    _settings.MoveDirection = Vector3.RotateTowards(_settings.MoveDirection, targetDirection,
                        _settings.RotateSpeed * Mathf.Deg2Rad * Time.deltaTime, 1000);

                    _settings.MoveDirection = _settings.MoveDirection.normalized;
                }

                if (_settings.IsMoving)
                    _settings.InAirVelocity +=
                        targetDirection.normalized * Time.deltaTime * _settings.InAirControlAcceleration;
            }
        }


        private void ApplyJumping()
        {
            if (_settings.LastJumpTime + _settings.JumpRepeatTime > Time.time)
                return;

            if (_settings.IsGrounded)
            {
                if (Time.time < _settings.LastJumpButtonTime + _settings.JumpTimeout)
                {
                    _settings.VerticalSpeed = CalculateJumpVerticalSpeed(_settings.JumpHeight);
                    _settings.CharacterState = CharacterStateEnum.JumpStarted;
                }
            }
        }

        private void ApplyGravity()
        {
            if (_settings.IsEnabled)
            {
                _settings.IsJumpingReachedApex = _settings.VerticalSpeed <= 0.0;

                if (_settings.IsGrounded)
                    _settings.VerticalSpeed = 0.0f;
                else
                    _settings.VerticalSpeed -= _settings.Gravity * Time.deltaTime;
            }
        }

        private float CalculateJumpVerticalSpeed(float targetJumpHeight)
        {
            return Mathf.Sqrt(2 * targetJumpHeight * _settings.Gravity);
        }

        public void Update()
        {
            if (!_settings.IsEnabled) return;

            CheckGround();
            UpdateSmoothedMovementDirection();
            ApplyGravity();
            ApplyJumping();
            ApplyAirborn();
            ApplyMovement();
            ApplyRotation();
            ResetIfGround();
            ApplyIdle();
        }

        private void ApplyAirborn()
        {
            if (_settings.CharacterState == CharacterStateEnum.JumpStarted)
                return;
            var distanceToBeGroundedReached = Mathf.Abs(_settings.JumpEndAnimation.length * 1.6f -
                                                        Mathf.Sqrt(_settings.distanceToGround * 2 /
                                                                   _settings.Gravity)) <= 0.1f;
            if (_settings.distanceToGround > 0.3f && _settings.AirTime > 0.4f)
                if (distanceToBeGroundedReached && _settings.IsJumpingReachedApex &&
                    _settings.CharacterState != CharacterStateEnum.LandingInProgress)
                    _settings.CharacterState = CharacterStateEnum.LandingStarted;

            if (_settings.AirTime > 0.8f)
                if (!distanceToBeGroundedReached && _settings.CharacterState != CharacterStateEnum.InAir &&
                    _settings.CharacterState != CharacterStateEnum.LandingInProgress)
                    _settings.CharacterState = CharacterStateEnum.InAirStarted;
        }

        private void ResetIfGround()
        {
            if (_settings.IsGrounded)
            {
                _settings.AirTime = 0;
                _settings.InAirVelocity = Vector3.zero;
            }
            else
                _settings.AirTime += Time.deltaTime;
        }

        private void ApplyRotation()
        {
            if (_settings.MoveDirection != Vector3.zero)
                _settings.Transform.rotation = Quaternion.LookRotation(_settings.MoveDirection);
        }

        private void ApplyMovement()
        {
            Physics.Raycast(_settings.Transform.position, -_settings.Transform.up, out var hit, 3);
            var dir = Vector3.ProjectOnPlane(_settings.MoveDirection, hit.normal);

            var SlopeForward = Vector3.Cross(_settings.Transform.right, hit.normal);
            var angle = SlopeForward.y < 0
                ? -Vector3.Angle(_settings.MoveDirection, dir)
                : Vector3.Angle(_settings.MoveDirection, dir);
            
            if(angle > 0)
                dir = Vector3.ProjectOnPlane(_settings.MoveDirection, hit.normal); 
            
            var move = dir * _settings.MoveSpeed;

            if (angle > 45)
                move = Vector3.zero;
            
            var movement = move + new Vector3(0, _settings.VerticalSpeed, 0) +
                               _settings.InAirVelocity;

            movement *= Time.deltaTime;

            _settings.CollisionFlags = _settings.Controller.Move(movement);
        }

        private void ApplyIdle()
        {
            if (_settings.Controller.velocity.sqrMagnitude < 0.1f && Mathf.Abs(_settings.VerticalSpeed) < 0.01f &&
                _settings.CharacterState != CharacterStateEnum.IdleInProgress)
                _settings.CharacterState = CharacterStateEnum.IdleStarted;

            if (_settings.CharacterState != CharacterStateEnum.IdleLongStared &&
                _settings.TimeSinceLastMove > _settings.NextLongIdleAnimationTime)
            {
                _settings.NextLongIdleAnimationTime = Random.Range(4, 8);
                _settings.CharacterState = CharacterStateEnum.IdleLongStared;
                _settings.TimeSinceLastMove = 0;
            }
        }

        private void CheckGround()
        {
            _settings.IsGrounded = (_settings.CollisionFlags & CollisionFlags.CollidedBelow) != 0;
        }
    }
}