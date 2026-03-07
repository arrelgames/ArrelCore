using UnityEngine;

namespace RLGames
{
    [RequireComponent(typeof(CharacterController))]
    public class CharacterMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] float moveSpeed = 5f;
        [SerializeField] float gravity = -9.81f;

        [Header("Look")]
        [SerializeField] float lookSensitivity = 120f;
        [SerializeField] Transform cameraPivot;

        private CharacterController controller;

        private float verticalVelocity;
        private float pitch;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
        }

        public void Execute(InputCommand command)
        {
            HandleLook(command.Look);
            HandleMove(command.Move);
        }

        void HandleMove(Vector2 moveInput)
        {
            Vector3 move =
                transform.right * moveInput.x +
                transform.forward * moveInput.y;

            move *= moveSpeed;

            if (controller.isGrounded && verticalVelocity < 0)
                verticalVelocity = -2f;

            verticalVelocity += gravity * Time.deltaTime;

            move.y = verticalVelocity;

            controller.Move(move * Time.deltaTime);
        }

        void HandleLook(Vector2 lookInput)
        {
            if (cameraPivot == null)
            {
                Debug.LogWarning("CameraPivot not assigned on CharacterMotor!");
                return;
            }

            // Multiply by sensitivity and deltaTime
            float mouseX = lookInput.x * lookSensitivity * Time.deltaTime;
            float mouseY = lookInput.y * Time.deltaTime * lookSensitivity;

            // Update pitch (vertical rotation)
            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, -80f, 80f);

            // Apply vertical rotation to camera pivot
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // Apply horizontal rotation to player transform
            transform.Rotate(Vector3.up * mouseX);

            // Debugging (optional)
            // Debug.Log($"Look Input: {lookInput}, Pitch: {pitch}");
        }
    }
}