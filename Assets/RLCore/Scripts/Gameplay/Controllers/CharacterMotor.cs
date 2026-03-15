using UnityEngine;

namespace RLGames
{
    [RequireComponent(typeof(CharacterController))]
    public class CharacterMotor : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Horizontal movement speed in world units per second.")]
        [SerializeField] private float moveSpeed = 5f;
        [Tooltip("Downward acceleration applied when airborne (e.g. -9.81 for earth gravity).")]
        [SerializeField] private float gravity = -9.81f;

        [Header("Look")]
        [Tooltip("Transform that receives vertical look (pitch). Usually the camera pivot or camera parent.")]
        [SerializeField] private Transform cameraPivot;

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

        private void HandleMove(Vector2 moveInput)
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

        private void HandleLook(Vector2 lookDelta)
        {
            if (cameraPivot == null) return;

            pitch -= lookDelta.y;
            pitch = Mathf.Clamp(pitch, -80f, 80f);

            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            transform.Rotate(Vector3.up * lookDelta.x);
        }
    }
}
