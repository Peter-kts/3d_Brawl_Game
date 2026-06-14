using System;
using System.Collections;
using System.Collections.Generic;
using NHance.Assets.Scripts;
using UnityEditor;
using UnityEngine;

namespace NHance.Assets
{
    [RequireComponent(typeof(Camera))]
    public class NHCamera : MonoBehaviour
    {
        [SerializeField] private Transform focus = default;
        [SerializeField] private bool TryToFindCharacter = true;
        [SerializeField, Range(1f, 20f)] private float distance = 5f;
        [SerializeField, Range(1f, 20f)] private float minDistance = 1;
        [SerializeField, Range(1f, 20f)] private float maxDistance = 1;
        [SerializeField] private Vector3 focusOffset = Vector3.zero;

        [SerializeField] private float rotationSpeed = 90f;
        [SerializeField, Range(-89f, 89f)] private float minVerticalAngle = -45f, maxVerticalAngle = 45f;
        [SerializeField] private LayerMask obstructionMask = -1;
        [SerializeField] private float horizontalSmoothTime = 0.1f;
        [SerializeField] private float verticalSmoothTime = 0.2f;
        [SerializeField] private float flySpeed = 1f;

        public bool ControlsFoldout = true;

        private Transform _transform;
        private Camera regularCamera;
        private Vector3 focusPoint;
        private Vector2 orbitAngles = new Vector2(22f, 0f);
        private Vector3 lastFocusOffset;
        private float targetDistance;
        private float zoomVelocity;
        private float currentXVelocity;
        private float currentYVelocity;
        private float currentZVelocity;

        private Vector3 CameraHalfExtends
        {
            get
            {
                Vector3 halfExtends;
                halfExtends.y =
                    regularCamera.nearClipPlane *
                    Mathf.Tan(0.5f * Mathf.Deg2Rad * regularCamera.fieldOfView);
                halfExtends.x = halfExtends.y * regularCamera.aspect;
                halfExtends.z = 0f;
                return halfExtends;
            }
        }

        void OnValidate()
        {
            if (maxVerticalAngle < minVerticalAngle)
            {
                maxVerticalAngle = minVerticalAngle;
            }
        }

        void Awake()
        {
            if(focus == null && TryToFindCharacter)
                focus = FindObjectOfType<NHCharacterController>().gameObject.transform;
            _transform = transform;
            regularCamera = GetComponent<Camera>();
            focusPoint = (focus != null ? focus.position : Vector3.zero) + focusOffset;
            _transform.localRotation = Quaternion.Euler(orbitAngles);
            targetDistance = distance;
            lastFocusOffset = focusOffset;
        }

        void Update()
        {
            UpdateFocusPoint();
            Quaternion lookRotation;
            lookRotation = _transform.localRotation;
            Cursor.visible = !Input.GetMouseButton(0);
            if (ManualRotation())
            {
                ConstrainAngles();
                lookRotation = Quaternion.Euler(orbitAngles);
            }

            FocusOffsetMove();

            if (Input.mouseScrollDelta.y != 0)
                targetDistance = Mathf.Clamp(targetDistance - Input.mouseScrollDelta.y / 5, minDistance, maxDistance);

            distance = Mathf.SmoothDamp(distance, targetDistance, ref zoomVelocity, 0.3f);

            Vector3 lookDirection = lookRotation * Vector3.forward;
            Vector3 lookPosition = focusPoint - lookDirection * distance;

            Vector3 rectOffset = lookDirection * regularCamera.nearClipPlane;
            Vector3 rectPosition = lookPosition + rectOffset;
            Vector3 castFrom = focusPoint;
            Vector3 castLine = rectPosition - castFrom;
            float castDistance = castLine.magnitude;
            Vector3 castDirection = castLine / castDistance;

            if (Physics.BoxCast(
                castFrom, CameraHalfExtends, castDirection, out RaycastHit hit,
                lookRotation, castDistance, obstructionMask
            ))
            {
                rectPosition = castFrom + castDirection * hit.distance;
                lookPosition = rectPosition - rectOffset;
            }
            
            _transform.position = lookPosition;
            _transform.rotation = lookRotation;
        }

        void UpdateFocusPoint()
        {
            var position = (focus != null ? focus.position : Vector3.zero) + focusOffset;
            var x = Mathf.SmoothDamp(focusPoint.x, position.x, ref currentXVelocity,
                horizontalSmoothTime);
            var y = Mathf.SmoothDamp(focusPoint.y, position.y, ref currentYVelocity,
                verticalSmoothTime);
            var z = Mathf.SmoothDamp(focusPoint.z, position.z, ref currentZVelocity,
                horizontalSmoothTime);
            focusPoint = new Vector3(x, y, z);
        }

        bool ManualRotation()
        {
            if (!Input.GetMouseButton(0)) return false;

            Vector2 input = new Vector2(
                -Input.GetAxis("Mouse Y"),
                Input.GetAxis("Mouse X")
            );
            const float e = 0.001f;
            if (input.x < -e || input.x > e || input.y < -e || input.y > e)
            {
                orbitAngles += rotationSpeed * Time.unscaledDeltaTime * input;
                return true;
            }

            return false;
        }

        void FocusOffsetMove()
        {
            if (!(Input.GetMouseButton(1) || focus == null))
            {
                focusOffset = lastFocusOffset;
                return;
            }
            var up = Input.GetAxis("Vertical") * _transform.forward * Time.deltaTime * flySpeed;
            var right = Input.GetAxis("Horizontal") * _transform.right * Time.deltaTime * flySpeed;
            var forward = (Input.GetKey(KeyCode.Q) ? -1 : Input.GetKey(KeyCode.E) ? 1 : 0) * _transform.up *
                          Time.deltaTime * flySpeed;
            focusOffset += up + right + forward;
        }

        void ConstrainAngles()
        {
            orbitAngles.x =
                Mathf.Clamp(orbitAngles.x, minVerticalAngle, maxVerticalAngle);

            if (orbitAngles.y < 0f)
            {
                orbitAngles.y += 360f;
            }
            else if (orbitAngles.y >= 360f)
            {
                orbitAngles.y -= 360f;
            }
        }
    }
}