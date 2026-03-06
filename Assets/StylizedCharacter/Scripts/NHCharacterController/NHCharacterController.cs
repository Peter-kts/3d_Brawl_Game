using System;
using System.Collections;
using System.Collections.Generic;
using NHance.Assets.StylizedCharacter.Scripts;
using UnityEngine;

namespace NHance.Assets.Scripts
{
    [RequireComponent(typeof(CharacterController))]
    public class NHCharacterController : MonoBehaviour
    {
        public CharacterSettings settings;
        
        private CharacterAnimator _characterAnimator;
        private CharacterEngine _characterEngine;
        private CharacterInput _characterInput;
        private CharacterState _state;

        void Awake()
        {
            _characterAnimator = new CharacterAnimator(settings);
            _characterEngine = new CharacterEngine(settings);
            _characterInput = new CharacterInput(settings);
            _state = new CharacterState(settings);
        }

        private void OnUpdateRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }
        
        private void Update()
        {
            _characterInput.Update();
            _characterAnimator.Update();
        }

        private void LateUpdate()
        {
            _characterEngine.Update();
        }

        private void OnDestroy()
        {
            _characterAnimator = null;
            _characterEngine = null;
            _characterInput = null;
            _state = null;
        }
    }
}