using System;
using UnityEditor;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    [CustomEditor(typeof(NHCharacterController))]
    public class NHCharacterControllerEditor : Editor
    {
        private NHCharacterController _instance;
        
        private void OnEnable()
        {
            _instance = target as NHCharacterController;
        }

        public override void OnInspectorGUI()
        {
            GUIStyle header = new GUIStyle();
            header.margin = new RectOffset(30, 30, 0, 0);
            GUIStyle foldoutStyle = new GUIStyle();
            foldoutStyle.margin = new RectOffset(15, 15, 0, 0);
            
            GUILayout.Label("Settings");
            using (new GUILayout.VerticalScope(header))
            {
                _instance.settings.IsEnabled = EditorGUILayout.Toggle("Enabled",
                    _instance.settings.IsEnabled);
                _instance.settings.Controller = (CharacterController)EditorGUILayout.ObjectField("Controller", _instance.settings.Controller, typeof(CharacterController),
                    allowSceneObjects: true);
                _instance.settings.Animator = (Animator)EditorGUILayout.ObjectField("Animator", _instance.settings.Animator, typeof(Animator),
                    allowSceneObjects: true);
                _instance.settings.Transform = (Transform)EditorGUILayout.ObjectField("Transform", _instance.settings.Transform, typeof(Transform),
                    allowSceneObjects: true);
                _instance.settings.MovementSpace = (Transform)EditorGUILayout.ObjectField("Movement Space", _instance.settings.MovementSpace, typeof(Transform),
                    allowSceneObjects: true);
            
                
                _instance.settings.AnimationFoldout = EditorGUILayout.Foldout(_instance.settings.AnimationFoldout, "Animation", toggleOnLabelClick: true);
                if(_instance.settings.AnimationFoldout)
                    using (new GUILayout.VerticalScope(foldoutStyle))
                    {
                        _instance.settings.TransitionTime = EditorGUILayout.FloatField("Transition Time", _instance.settings.TransitionTime);
                        GUILayout.Space(10);
                        _instance.settings.IdleAnimation = (AnimationClip)EditorGUILayout.ObjectField("Idle", _instance.settings.IdleAnimation, typeof(AnimationClip),
                            allowSceneObjects: false);
                        _instance.settings.IdleLongAnimation = (AnimationClip)EditorGUILayout.ObjectField("Idle Additional", _instance.settings.IdleLongAnimation, typeof(AnimationClip),
                            allowSceneObjects: false);
                        GUILayout.Space(10);
                        _instance.settings.JumpStartAnimation = (AnimationClip)EditorGUILayout.ObjectField("Jump Start", _instance.settings.JumpStartAnimation, typeof(AnimationClip),
                            allowSceneObjects: false);
                        _instance.settings.JumpAnimationSpeed = EditorGUILayout.FloatField("Jump Animation Speed", _instance.settings.JumpAnimationSpeed);
                        GUILayout.Space(5);
                        _instance.settings.JumpPoseAnimation = (AnimationClip)EditorGUILayout.ObjectField("Jump Loop", _instance.settings.JumpPoseAnimation, typeof(AnimationClip),
                            allowSceneObjects: false);
                        GUILayout.Space(5);
                        _instance.settings.JumpEndAnimation = (AnimationClip)EditorGUILayout.ObjectField("Jump End", _instance.settings.JumpEndAnimation, typeof(AnimationClip),
                            allowSceneObjects: false);
                        _instance.settings.LandAnimationSpeed = EditorGUILayout.FloatField("Land Animation Speed", _instance.settings.LandAnimationSpeed);
                        GUILayout.Space(10);
                        _instance.settings.MovementBlendName = EditorGUILayout.TextField("Movement Blend Name", _instance.settings.MovementBlendName);
                        _instance.settings.MovementValueName = EditorGUILayout.TextField("Movement Value Name", _instance.settings.MovementValueName);
                    }
                
                _instance.settings.WalkSpeed = EditorGUILayout.FloatField("Walk Speed", _instance.settings.WalkSpeed);
                _instance.settings.RunSpeed = EditorGUILayout.FloatField("Run Speed", _instance.settings.RunSpeed);
                _instance.settings.SprintSpeed = EditorGUILayout.FloatField("Sprint Speed", _instance.settings.SprintSpeed);
                _instance.settings.RotateSpeed = EditorGUILayout.FloatField("Rotate Speed", _instance.settings.RotateSpeed);
                _instance.settings.JumpHeight = EditorGUILayout.FloatField("Jump Height", _instance.settings.JumpHeight);
                _instance.settings.InAirControlAcceleration = EditorGUILayout.FloatField("in Air Control Acceleration", _instance.settings.InAirControlAcceleration);
                GUILayout.Space(5);
                _instance.settings.Gravity = EditorGUILayout.FloatField("Gravity", _instance.settings.Gravity);
                GUILayout.Space(5);
                _instance.settings.SpeedSmoothing = EditorGUILayout.FloatField("Speed Smoothing", _instance.settings.SpeedSmoothing);
                _instance.settings.JumpRepeatTime = EditorGUILayout.FloatField("Jump Repeat Time", _instance.settings.JumpRepeatTime);
                _instance.settings.JumpTimeout = EditorGUILayout.FloatField("Jump Timeout", _instance.settings.JumpTimeout);
            }

            
            EditorGUI.BeginDisabledGroup(true);
            _instance.settings.DebugFoldout = EditorGUILayout.Foldout(_instance.settings.DebugFoldout, "Debug", toggleOnLabelClick: true);
            if (_instance.settings.DebugFoldout)
            {
                using (new GUILayout.VerticalScope(header))
                {
                    GUILayout.Label("Flags");
                    using (new GUILayout.VerticalScope(foldoutStyle))
                    {
                        EditorGUILayout.Toggle("Is Jumping Reached Apex", _instance.settings.IsJumpingReachedApex);
                        EditorGUILayout.Toggle("Is Moving Back", _instance.settings.IsMovingBack);
                        EditorGUILayout.Toggle("is Moving", _instance.settings.IsMoving);
                        EditorGUILayout.Toggle("Is Grounded", _instance.settings.IsGrounded);
                        EditorGUILayout.Toggle("Is Running", _instance.settings.IsSprinting);
                    }
                    GUILayout.Space(5);
                    GUILayout.Label("Values");
                    using (new GUILayout.VerticalScope(foldoutStyle))
                    {
                        EditorGUILayout.EnumPopup("State", _instance.settings.CharacterState);
                        EditorGUILayout.EnumPopup("Collision Flags", _instance.settings.CollisionFlags);
                        GUILayout.Space(5);
                        EditorGUILayout.FloatField("Time Since Last Move", _instance.settings.TimeSinceLastMove);
                        EditorGUILayout.FloatField("Vertical Speed", _instance.settings.VerticalSpeed);
                        EditorGUILayout.FloatField("Move Speed", _instance.settings.MoveSpeed);
                        EditorGUILayout.FloatField("Last Jump Button Time", _instance.settings.LastJumpButtonTime);
                        EditorGUILayout.FloatField("Last Jump Time", _instance.settings.LastJumpTime);
                        EditorGUILayout.FloatField("Last Jump Start Height", _instance.settings.LastJumpStartHeight);
                        EditorGUILayout.FloatField("Last Grounded Time", _instance.settings.AirTime);
                        EditorGUILayout.FloatField("Smooth Time", _instance.settings.SmoothTime);
                        EditorGUILayout.FloatField("Next Long Idle Animation Time", _instance.settings.NextLongIdleAnimationTime);
                        GUILayout.Space(5);
                        EditorGUILayout.Vector3Field("Move Direction", _instance.settings.MoveDirection);
                        EditorGUILayout.Vector3Field("In Air Velocity", _instance.settings.InAirVelocity);
                        EditorGUILayout.Vector3Field("Current Velocity", _instance.settings.CurrentVelocity);
                        EditorGUILayout.Vector2Field("Movement Axis", _instance.settings.MovementAxis);
                        
                    }
                }
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}