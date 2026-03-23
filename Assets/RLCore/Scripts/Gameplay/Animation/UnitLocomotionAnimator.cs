using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Drives an <see cref="Animator"/> from horizontal <see cref="CharacterController"/> speed.
    /// Expects a 1D blend tree parameter <see cref="locomotionParameter"/> with 0 = idle, 0.5 = walk, 1 = run (in-place clips; root motion off).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class UnitLocomotionAnimator : MonoBehaviour
    {
        public const float BlendIdle = 0f;
        public const float BlendWalk = 0.5f;
        public const float BlendRun = 1f;

        [Header("Target")]
        [Tooltip("Animator to drive (assign the mesh/rig Animator in the inspector).")]
        [SerializeField] private Animator animator;

        [Header("Animator")]
        [Tooltip("Blend tree float parameter name.")]
        [SerializeField] private string locomotionParameter = "Locomotion";
        [SerializeField] private bool applyRootMotion = false;

        [Header("Speed mapping")]
        [Tooltip("Horizontal speeds at or below this map toward idle (0).")]
        [SerializeField] private float idleSpeedMax = 0.05f;
        [Tooltip("Speed at full walk blend (0.5).")]
        [SerializeField] private float walkSpeedReference = 5f;
        [Tooltip("Speed at full run blend (1).")]
        [SerializeField] private float runSpeedReference = 8f;

        [Header("Smoothing")]
        [SerializeField] private float locomotionSmoothTime = 0.12f;

        [Header("Manual override")]
        [SerializeField] private bool useManualLocomotion;
        [SerializeField] [Range(0f, 1f)] private float manualLocomotionBlend;

        private CharacterController characterController;
        private int locomotionHash;
        private float locomotionCurrent;
        private float locomotionVelocity;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (animator != null)
                animator.applyRootMotion = applyRootMotion;
            else
                Debug.LogWarning(
                    $"[{nameof(UnitLocomotionAnimator)}] No Animator assigned on '{name}'.",
                    this);

            locomotionHash = Animator.StringToHash(locomotionParameter);
        }

        private void OnValidate()
        {
            if (animator != null)
                animator.applyRootMotion = applyRootMotion;
        }

        private void LateUpdate()
        {
            if (animator == null)
                return;

            float target;
            if (useManualLocomotion)
                target = manualLocomotionBlend;
            else
                target = SpeedToLocomotionBlend(HorizontalSpeed);

            if (locomotionSmoothTime > 0f)
                locomotionCurrent = Mathf.SmoothDamp(
                    locomotionCurrent,
                    target,
                    ref locomotionVelocity,
                    locomotionSmoothTime,
                    Mathf.Infinity,
                    Time.deltaTime);
            else
                locomotionCurrent = target;

            animator.SetFloat(locomotionHash, locomotionCurrent);
        }

        private float HorizontalSpeed
        {
            get
            {
                Vector3 v = characterController.velocity;
                v.y = 0f;
                return v.magnitude;
            }
        }

        private float SpeedToLocomotionBlend(float horizontalSpeed)
        {
            if (horizontalSpeed <= idleSpeedMax)
                return BlendIdle;

            if (horizontalSpeed <= walkSpeedReference)
            {
                float t = Mathf.InverseLerp(idleSpeedMax, walkSpeedReference, horizontalSpeed);
                return Mathf.Lerp(BlendIdle, BlendWalk, t);
            }

            float u = Mathf.InverseLerp(walkSpeedReference, runSpeedReference, horizontalSpeed);
            return Mathf.Lerp(BlendWalk, BlendRun, Mathf.Clamp01(u));
        }

        /// <summary>Maps to blend 0 (idle).</summary>
        public void Idle()
        {
            useManualLocomotion = true;
            manualLocomotionBlend = BlendIdle;
        }

        /// <summary>Maps to blend 0.5 (walk).</summary>
        public void Walk()
        {
            useManualLocomotion = true;
            manualLocomotionBlend = BlendWalk;
        }

        /// <summary>Maps to blend 1 (run).</summary>
        public void Run()
        {
            useManualLocomotion = true;
            manualLocomotionBlend = BlendRun;
        }

        /// <summary>Resume driving the blend from <see cref="CharacterController"/> velocity.</summary>
        public void UseAutomaticLocomotion()
        {
            useManualLocomotion = false;
        }

        /// <summary>Manual blend in [0, 1] for the 1D locomotion tree (idle / walk / run).</summary>
        public void SetManualLocomotionBlend(float blend01)
        {
            useManualLocomotion = true;
            manualLocomotionBlend = Mathf.Clamp01(blend01);
        }
    }
}
