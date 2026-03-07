using UnityEngine;

namespace RLGames
{
    [RequireComponent(typeof(CharacterController))]
    public class CharacterController3D : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float gravity = -9.81f;

        [Header("Look")]
        [SerializeField] private float lookSensitivity = 120f;
        [SerializeField] private Transform cameraPivot;

        private CharacterController controller;

        private Vector2 moveInput;
        private Vector2 lookInput;

        private float verticalVelocity;
        private float pitch;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            HandleLook();
            HandleMovement();
        }

        public void SetMoveInput(Vector2 input)
        {
            moveInput = input;
        }

        public void SetLookInput(Vector2 input)
        {
            lookInput = input;
        }

        private void HandleMovement()
        {
            Vector3 move =
                transform.right * moveInput.x +
                transform.forward * moveInput.y;

            move *= moveSpeed;

            if (controller.isGrounded && verticalVelocity < 0)
            {
                verticalVelocity = -2f;
            }

            verticalVelocity += gravity * Time.deltaTime;

            move.y = verticalVelocity;

            controller.Move(move * Time.deltaTime);
        }

        private void HandleLook()
        {
            float mouseX = lookInput.x * lookSensitivity * Time.deltaTime;
            float mouseY = lookInput.y * lookSensitivity * Time.deltaTime;

            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, -80f, 80f);

            if (cameraPivot != null)
            {
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            transform.Rotate(Vector3.up * mouseX);
        }
    }
}