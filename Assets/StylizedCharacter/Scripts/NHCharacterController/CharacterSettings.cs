using System;
using NHance.Assets.Scripts.Enums;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    [Serializable]
    public class CharacterSettings
    {
        public CharacterController Controller;
        public Animator Animator;
        public Transform Transform;
        public Transform MovementSpace;

        public AnimationClip IdleAnimation;
        public AnimationClip IdleLongAnimation;
        public AnimationClip JumpStartAnimation;
        public AnimationClip JumpPoseAnimation;
        public AnimationClip JumpEndAnimation;

        public string MovementBlendName = "MovementBlend";
        public string MovementValueName = "MovementSpeed";
        public float JumpAnimationSpeed = 1.15f;
        public float LandAnimationSpeed = 1.0f;

        public CharacterStateEnum CharacterState;
        public float WalkSpeed = 2.0f;
        public float RunSpeed = 4.0f;
        public float SprintSpeed = 6.0f;
        public float InAirControlAcceleration = 3.0f;
        public float JumpHeight = 0.5f;
        public float Gravity = 20.0f;
        public float SpeedSmoothing = 10.0f;
        public float RotateSpeed = 500.0f;
        public float JumpRepeatTime = 0.05f;
        public float JumpTimeout = 0.15f;
        public float TimeSinceLastMove = 0.0f;
        public Vector3 MoveDirection = Vector3.zero;
        public float VerticalSpeed = 0.0f;
        public float MoveSpeed = 0.0f;
        public CollisionFlags CollisionFlags;
        public bool IsJumpingReachedApex = false;
        public bool IsMovingBack = false;
        public bool IsMoving = false;
        public float LastJumpButtonTime = -10.0f;
        public float LastJumpTime = -1.0f;
        public bool AnimationFoldout = false;
        public bool DebugFoldout = false;
        public bool IsGrounded;
        public float LastJumpStartHeight = 0.0f;
        public Vector3 InAirVelocity = Vector3.zero;
        public float AirTime = 0.0f;
        public bool IsEnabled = true;
        public Vector2 MovementAxis = Vector2.zero;
        public bool IsSprinting;
        public bool IsWalking;
        public Vector3 CurrentVelocity;
        public float SmoothTime = 0.1f;
        public float TransitionTime = 0.025f;
        public float NextLongIdleAnimationTime = 5;

        public float distanceToGround
        {
            get
            {
                if (Physics.Raycast(Transform.position, Vector3.down, out var hit, maxDistance: 5))
                    return hit.distance;
                return int.MaxValue;
            }
        }
    }
}