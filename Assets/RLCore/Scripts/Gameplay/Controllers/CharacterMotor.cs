using UnityEngine;

namespace RLGames
{
    [RequireComponent(typeof(CharacterController))]
    public class CharacterMotor : MonoBehaviour
    {
        private const float MoveInputEpsilonSq = 1e-4f;

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

        [Header("Horizontal acceleration")]
        [Tooltip("Grounded: max change in horizontal velocity per second toward target when there is move input (units/s²). Very large values approximate instant acceleration.")]
        [SerializeField] private float groundAcceleration = 50f;
        [Tooltip("Grounded: max change in horizontal velocity per second when slowing to idle (units/s²). Higher = snappier stop.")]
        [SerializeField] private float groundDeceleration = 60f;
        [Tooltip("Airborne: max change per second toward desired horizontal velocity when there is move input.")]
        [SerializeField] private float airAcceleration = 25f;
        [Tooltip("Airborne: max change per second toward zero horizontal velocity when there is no move input.")]
        [SerializeField] private float airDeceleration = 20f;

        [Header("Look")]
        [Tooltip("Transform that receives vertical look (pitch). Usually the camera pivot or camera parent.")]
        [SerializeField] private Transform cameraPivot;

        private CharacterController controller;
        private Vector3 horizontalVelocity;
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
            Vector3 wishDir =
                transform.right * moveInput.x +
                transform.forward * moveInput.y;

            bool canSprintEnabled = sprint && moveInput.y > sprintForwardThreshold;
            float speedCap = canSprintEnabled ? sprintSpeed : moveSpeed;
            Vector3 wishVelHorizontal = wishDir * speedCap;

            bool hasMoveInput = wishDir.sqrMagnitude > MoveInputEpsilonSq;
            float dt = Time.deltaTime;

            if (controller.isGrounded)
            {
                if (!hasMoveInput)
                    horizontalVelocity = Vector3.MoveTowards(
                        horizontalVelocity,
                        Vector3.zero,
                        groundDeceleration * dt);
                else
                    horizontalVelocity = Vector3.MoveTowards(
                        horizontalVelocity,
                        wishVelHorizontal,
                        groundAcceleration * dt);
            }
            else
            {
                if (!hasMoveInput)
                    horizontalVelocity = Vector3.MoveTowards(
                        horizontalVelocity,
                        Vector3.zero,
                        airDeceleration * dt);
                else
                    horizontalVelocity = Vector3.MoveTowards(
                        horizontalVelocity,
                        wishVelHorizontal,
                        airAcceleration * dt);
            }

            horizontalVelocity.y = 0f;

            if (controller.isGrounded && jump)
                verticalVelocity = jumpForce;

            if (controller.isGrounded && verticalVelocity < 0)
                verticalVelocity = -2f;

            verticalVelocity += gravity * dt;

            Vector3 move = horizontalVelocity;
            move.y = verticalVelocity;
            controller.Move(move * dt);
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
