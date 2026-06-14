using System;
using System.Linq;
using NHance.Assets.Scripts.Enums;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    public class CharacterAnimator
    {
        private CharacterSettings _settings;

        public CharacterAnimator(CharacterSettings settings)
        {
            _settings = settings;
            // _settings.Pool.OnStateChanged += OnStateChanged;
            _settings.Animator.CrossFade(_settings.IdleAnimation.name, _settings.TransitionTime, 0);
        }

        public void Update()
        {
            if (!_settings.Animator)
                return;

            if (_settings.CharacterState == CharacterStateEnum.LandingStarted)
            {
                _settings.Animator.speed = _settings.LandAnimationSpeed;
                _settings.CharacterState = CharacterStateEnum.LandingInProgress;
                _settings.Animator.CrossFade(_settings.JumpEndAnimation.name, _settings.TransitionTime,
                    0);
            }

            if (_settings.CharacterState == CharacterStateEnum.InAirStarted)
            {
                _settings.Animator.speed = 1;
                _settings.CharacterState = CharacterStateEnum.InAir;
                _settings.Animator.CrossFade(_settings.JumpPoseAnimation.name, 0.1f,
                    0);
            }

            if (_settings.CharacterState == CharacterStateEnum.JumpStarted && !_settings.IsJumpingReachedApex)
            {
                _settings.CharacterState = CharacterStateEnum.InAir;
                _settings.Animator.speed = _settings.JumpAnimationSpeed;
                _settings.Animator.CrossFade(_settings.JumpStartAnimation.name, _settings.TransitionTime, 0);
            }

            if (_settings.CharacterState == CharacterStateEnum.IdleLongStared)
            {
                _settings.CharacterState = CharacterStateEnum.IdleInProgress;
                _settings.Animator.speed = 1;
                _settings.Animator.CrossFade(_settings.IdleLongAnimation.name, _settings.TransitionTime, 0);
            }

            if (_settings.CharacterState == CharacterStateEnum.IdleStarted)
            {
                _settings.CharacterState = CharacterStateEnum.IdleInProgress;
                _settings.Animator.speed = 1;
                _settings.Animator.CrossFade(_settings.IdleAnimation.name, _settings.TransitionTime, 0);
            }

            if (_settings.CharacterState == CharacterStateEnum.IsMovingStarted)
            {
                _settings.Animator.CrossFade(_settings.MovementBlendName, _settings.TransitionTime, 0);
                _settings.CharacterState = CharacterStateEnum.IsMoving;
            }

            if (_settings.CharacterState == CharacterStateEnum.IsMoving)
            {
                var normalize = _settings.MoveSpeed / _settings.SprintSpeed;
                _settings.Animator.SetFloat(_settings.MovementValueName, normalize);
                _settings.Animator.speed = Mathf.Clamp(_settings.Controller.velocity.magnitude,
                    0.0f,
                    _settings.MoveSpeed <= _settings.WalkSpeed
                        ? Mathf.Clamp(_settings.MoveSpeed / _settings.WalkSpeed, 0.5f, 1)
                        : 1);
            }
        }
    }
}