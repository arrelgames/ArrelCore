using UnityEngine;

namespace RLGames
{
    [RequireComponent(typeof(CharacterController))]
    public class CharacterMotor : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Horizontal movement speed in world units per second.")]
        [SerializeField] private float moveSpeed = 5f;
        [Tooltip("Horizontal movement speed when sprinting (world units per second).")]
        [SerializeField] private float sprintSpeed = 8f;
        [Tooltip("Forward input (moveInput.y) must be above this for sprint. 0 = any forward allows sprint; higher (e.g. 0.5) = mostly forward required.")]
        [SerializeField] [Range(0f, 1f)] private float sprintForwardThreshold = 0f;
        [Tooltip("Initial upward velocity when jumping (world units per second).")]
        [SerializeField] private float jumpForce = 7f;
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
            HandleMove(command.Move, command.Sprint, command.Jump);
        }

        private void HandleMove(Vector2 moveInput, bool sprint, bool jump)
        {
            Vector3 move =
                transform.right * moveInput.x +
                transform.forward * moveInput.y;

            bool canSprint = sprint && moveInput.y > sprintForwardThreshold;
            float speed = canSprint ? sprintSpeed : moveSpeed;
            move *= speed;

            if (controller.isGrounded && jump)
                verticalVelocity = jumpForce;

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
