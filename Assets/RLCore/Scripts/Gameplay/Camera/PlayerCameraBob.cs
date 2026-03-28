using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Cosmetic camera bob driven by actual movement velocity.
    /// Keeps all behavior additive and separate from movement/look gameplay logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerCameraBob : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Transform that receives bob offset. Defaults to this transform.")]
        [SerializeField] private Transform targetTransform;
        [Tooltip("CharacterController used to read resolved movement velocity.")]
        [SerializeField] private CharacterController characterController;
        [Tooltip("Optional Unit reference used to detect sprint intent.")]
        [SerializeField] private Unit unit;

        [Header("Speed Mapping")]
        [Tooltip("Horizontal speed under this is treated as no movement.")]
        [SerializeField] private float movementDeadzone = 0.05f;
        [Tooltip("Speed considered full walk bob intensity.")]
        [SerializeField] private float walkSpeedReference = 5f;
        [Tooltip("Speed considered full sprint bob intensity.")]
        [SerializeField] private float sprintSpeedReference = 8f;

        [Header("Walk Bob")]
        [Tooltip("Horizontal (left-right) walk bob amplitude in meters.")]
        [SerializeField] private float walkHorizontalAmplitude = 0.004f;
        [Tooltip("Vertical walk bob amplitude in meters.")]
        [SerializeField] private float walkVerticalAmplitude = 0.006f;
        [Tooltip("Walk bob cycle speed.")]
        [SerializeField] private float walkFrequency = 9f;

        [Header("Sprint Bob")]
        [Tooltip("Horizontal (left-right) sprint bob amplitude in meters.")]
        [SerializeField] private float sprintHorizontalAmplitude = 0.007f;
        [Tooltip("Vertical sprint bob amplitude in meters.")]
        [SerializeField] private float sprintVerticalAmplitude = 0.010f;
        [Tooltip("Sprint bob cycle speed.")]
        [SerializeField] private float sprintFrequency = 12f;

        [Header("Smoothing")]
        [Tooltip("How quickly bob reaches the target offset.")]
        [SerializeField] private float bobSmooth = 14f;
        [Tooltip("How quickly bob returns to neutral when not moving.")]
        [SerializeField] private float returnSmooth = 10f;

        private Vector3 baseLocalPosition;
        private Vector3 currentOffset;
        private float bobTimer;

        private void Awake()
        {
            if (targetTransform == null)
                targetTransform = transform;

            if (characterController == null)
                characterController = GetComponentInParent<CharacterController>();

            if (unit == null)
                unit = GetComponentInParent<Unit>();
        }

        private void Start()
        {
            baseLocalPosition = targetTransform.localPosition;
        }

        private void LateUpdate()
        {
            if (targetTransform == null || characterController == null)
                return;

            Vector3 velocity = characterController.velocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;

            bool sprintIntent = unit != null && unit.command.Sprint;
            bool useSprintProfile = sprintIntent && speed > movementDeadzone;

            float speedReference = useSprintProfile ? sprintSpeedReference : walkSpeedReference;
            float normalizedSpeed = speedReference > movementDeadzone
                ? Mathf.InverseLerp(movementDeadzone, speedReference, speed)
                : 0f;

            float horizontalAmplitude = useSprintProfile ? sprintHorizontalAmplitude : walkHorizontalAmplitude;
            float verticalAmplitude = useSprintProfile ? sprintVerticalAmplitude : walkVerticalAmplitude;
            float frequency = useSprintProfile ? sprintFrequency : walkFrequency;

            Vector3 targetOffset = Vector3.zero;
            if (normalizedSpeed > 0f)
            {
                bobTimer += Time.deltaTime * frequency * Mathf.Lerp(0.85f, 1.2f, normalizedSpeed);
                float x = Mathf.Sin(bobTimer) * horizontalAmplitude;
                float y = Mathf.Cos(bobTimer * 2f) * verticalAmplitude;
                targetOffset = new Vector3(x, y, 0f) * normalizedSpeed;
            }

            float smooth = targetOffset.sqrMagnitude > 0f ? bobSmooth : returnSmooth;
            currentOffset = Vector3.Lerp(currentOffset, targetOffset, SmoothFactor(smooth));
            targetTransform.localPosition = baseLocalPosition + currentOffset;
        }

        private void OnDisable()
        {
            if (targetTransform != null)
                targetTransform.localPosition = baseLocalPosition;

            currentOffset = Vector3.zero;
        }

        private static float SmoothFactor(float speed)
        {
            return 1f - Mathf.Exp(-speed * Time.deltaTime);
        }
    }
}
