using UnityEngine;
using UnityEngine.InputSystem;

namespace RLGames
{
    public class PlayerBrain : BrainBase
    {
        [Header("Mouse - Sensitivity")]
        [Tooltip("Base look sensitivity when using mouse. Higher = faster rotation per pixel delta.")]
        [SerializeField] private float mouseSensitivity = 0.1f;
        [Tooltip("Mouse delta (pixels) is divided by this and clamped to [-1,1] so big moves = full turn, small = proportional.")]
        [SerializeField] private float mouseDeltaScale = 100f;

        [Header("Mouse - Curves & Deadzone")]
        [Tooltip("When enabled, deadzone and response curve are applied to mouse input.")]
        [SerializeField] private bool useMouseCurvesAndDeadzone = false;
        [Tooltip("Mouse input magnitude below this is treated as zero. Keep very small for mouse (0.01-0.02).")]
        [SerializeField] private float mouseDeadZoneInner = 0.02f;
        [Tooltip("Exponent for mouse response curve. 1.0 = linear (recommended for mouse). Slight values like 1.1 add a tiny soft center.")]
        [SerializeField] private float mouseLookCurveExponent = 1.0f;

        [Header("Mouse - Smoothing")]
        [Tooltip("When enabled, mouse look is smoothed over time.")]
        [SerializeField] private bool useMouseSmoothing = false;
        [Tooltip("Mouse hipfire smoothing strength (0=very smooth, 1=no smoothing). Higher = more responsive.")]
        [SerializeField] private float mouseHipfireSmoothingStrength = 0.6f;
        [Tooltip("Mouse ADS smoothing strength. Lower = steadier aim down sights.")]
        [SerializeField] private float mouseAdsSmoothingStrength = 0.3f;
        [Tooltip("When raw look delta magnitude exceeds this, smoothing uses max alpha for snappier fast flicks.")]
        [SerializeField] private float mouseAdaptiveSmoothingRefMagnitude = 1.5f;
        [Tooltip("Max smoothing alpha used for large mouse moves. Higher = faster catch-up on fast flicks.")]
        [SerializeField] private float mouseAdaptiveSmoothingMaxAlpha = 0.9f;

        [Header("Controller - Sensitivity")]
        [Tooltip("Base look sensitivity when using gamepad. Stick values are rate-based and multiplied by Time.deltaTime.")]
        [SerializeField] private float controllerSensitivity = 120f;

        [Header("Controller - Curves & Deadzone")]
        [Tooltip("When enabled, deadzone and response curve are applied to controller stick input.")]
        [SerializeField] private bool useControllerCurvesAndDeadzone = true;
        [Tooltip("Stick magnitude below this is treated as zero. Essential for preventing stick drift (0.05-0.15).")]
        [SerializeField] private float controllerDeadZoneInner = 0.1f;
        [Tooltip("Stick magnitude above this is treated as full. Rescales the range between inner and outer.")]
        [SerializeField] private float controllerDeadZoneOuter = 0.95f;
        [Tooltip("Exponent for controller response curve. >1 gives soft center, stronger response at edges (1.5-2.5 recommended).")]
        [SerializeField] private float controllerLookCurveExponent = 2.0f;

        [Header("Controller - Smoothing")]
        [Tooltip("When enabled, controller look is smoothed over time.")]
        [SerializeField] private bool useControllerSmoothing = true;
        [Tooltip("Controller hipfire smoothing strength (0=very smooth, 1=no smoothing).")]
        [SerializeField] private float controllerHipfireSmoothingStrength = 0.3f;
        [Tooltip("Controller ADS smoothing strength. Lower = steadier aim down sights.")]
        [SerializeField] private float controllerAdsSmoothingStrength = 0.15f;

        [Header("Shared - Per-Axis")]
        [Tooltip("Multiplier for horizontal (yaw) rotation. Lets you make turning left/right faster or slower than vertical.")]
        [SerializeField] private float horizontalMultiplier = 1.0f;
        [Tooltip("Multiplier for vertical (pitch) rotation. Often slightly lower than horizontal for finer elevation control.")]
        [SerializeField] private float verticalMultiplier = 0.8f;

        [Header("Shared - ADS")]
        [Tooltip("Sensitivity multiplier when aiming down sights. <1 slows look for precision (e.g. 0.6 = 60% of base).")]
        [SerializeField] private float adsSensitivityMultiplier = 0.6f;

        [Header("Aim Assist (Controller Only)")]
        [Tooltip("When true, rotation is slowed near valid targets when using gamepad.")]
        [SerializeField] private bool enableAimAssist = false;
        [Tooltip("When true, aim assist only applies while aiming down sights.")]
        [SerializeField] private bool aimAssistOnlyWhileADS = true;
        [Tooltip("Strength of slowdown when reticle is on target (0=none, 1=full brake).")]
        [SerializeField] private float slowdownStrength = 0.5f;
        [Tooltip("Angular radius (degrees) from crosshair within which aim assist slowdown applies.")]
        [SerializeField] private float slowdownRadiusDegrees = 6f;
        [Tooltip("Max world distance for aim assist target consideration.")]
        [SerializeField] private float aimAssistMaxDistance = 50f;

        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool fireInput;
        private bool aimInput;
        private bool sprintInput;
        private bool jumpRequested;

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction fireAction;
        private InputAction aimAction;
        private InputAction sprintAction;
        private InputAction jumpAction;

        private bool usingGamepad;
        private Vector2 smoothedLookDelta;

        private void OnEnable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                Debug.LogWarning("[PlayerBrain] No PlayerInput found on GameObject; input will not be processed.", this);
                return;
            }

            var actions = playerInput.actions;
            if (actions == null)
            {
                Debug.LogWarning("[PlayerBrain] PlayerInput has no actions asset; input will not be processed.", this);
                return;
            }

            moveAction = actions["Move"];
            lookAction = actions["Look"];
            fireAction = actions["Attack"];
            aimAction = actions["Aim"];
            sprintAction = actions["Sprint"];
            jumpAction = actions["Jump"];

            usingGamepad = playerInput.currentControlScheme == "Gamepad";
            playerInput.onControlsChanged += OnControlsChanged;

            if (fireAction != null)
            {
                fireAction.started += OnFireStarted;
                fireAction.canceled += OnFireCanceled;
            }

            if (aimAction != null)
            {
                aimAction.started += OnAimStarted;
                aimAction.canceled += OnAimCanceled;
            }

            if (sprintAction != null)
            {
                sprintAction.started += OnSprintStarted;
                sprintAction.canceled += OnSprintCanceled;
            }

            if (jumpAction != null)
                jumpAction.started += OnJumpStarted;
        }

        private void OnDisable()
        {
            var playerInput = GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.onControlsChanged -= OnControlsChanged;
            }

            if (fireAction != null)
            {
                fireAction.started -= OnFireStarted;
                fireAction.canceled -= OnFireCanceled;
            }

            if (aimAction != null)
            {
                aimAction.started -= OnAimStarted;
                aimAction.canceled -= OnAimCanceled;
            }

            if (sprintAction != null)
            {
                sprintAction.started -= OnSprintStarted;
                sprintAction.canceled -= OnSprintCanceled;
            }

            if (jumpAction != null)
                jumpAction.started -= OnJumpStarted;
        }

        private void OnControlsChanged(PlayerInput pi)
        {
            usingGamepad = pi.currentControlScheme == "Gamepad";
        }

        private void OnFireStarted(InputAction.CallbackContext ctx) => fireInput = true;
        private void OnFireCanceled(InputAction.CallbackContext ctx) => fireInput = false;
        private void OnAimStarted(InputAction.CallbackContext ctx) => aimInput = true;
        private void OnAimCanceled(InputAction.CallbackContext ctx) => aimInput = false;
        private void OnSprintStarted(InputAction.CallbackContext ctx) => sprintInput = true;
        private void OnSprintCanceled(InputAction.CallbackContext ctx) => sprintInput = false;
        private void OnJumpStarted(InputAction.CallbackContext ctx) => jumpRequested = true;

        protected override void Think()
        {
            if (moveAction != null)
                moveInput = moveAction.ReadValue<Vector2>();
            else
                moveInput = Vector2.zero;

            if (lookAction != null)
                lookInput = lookAction.ReadValue<Vector2>();
            else
                lookInput = Vector2.zero;

            Vector2 processedLook = usingGamepad
                ? ProcessControllerLook(lookInput)
                : ProcessMouseLook(lookInput);

            command.Move = moveInput;
            command.Look = processedLook;
            command.Fire = fireInput;
            command.Aim = aimInput;
            command.Sprint = sprintInput;
            command.Jump = jumpRequested;
            jumpRequested = false;
        }

        private Vector2 ProcessMouseLook(Vector2 raw)
        {
            // Normalize pixel delta to [-1, 1]
            Vector2 v = new Vector2(
                Mathf.Clamp(raw.x / mouseDeltaScale, -1f, 1f),
                Mathf.Clamp(raw.y / mouseDeltaScale, -1f, 1f));

            if (useMouseCurvesAndDeadzone)
            {
                if (Mathf.Abs(v.x) < mouseDeadZoneInner) v.x = 0f;
                if (Mathf.Abs(v.y) < mouseDeadZoneInner) v.y = 0f;

                v = new Vector2(ApplyCurve(v.x, mouseLookCurveExponent),
                                ApplyCurve(v.y, mouseLookCurveExponent));
            }

            float yaw = v.x * mouseSensitivity * horizontalMultiplier;
            float pitch = v.y * mouseSensitivity * verticalMultiplier;

            if (aimInput)
            {
                yaw *= adsSensitivityMultiplier;
                pitch *= adsSensitivityMultiplier;
            }

            Vector2 delta = new Vector2(yaw, pitch);

            if (useMouseSmoothing)
            {
                float alpha = aimInput
                    ? mouseAdsSmoothingStrength
                    : Mathf.Lerp(mouseHipfireSmoothingStrength, mouseAdaptiveSmoothingMaxAlpha,
                        Mathf.Clamp01(delta.magnitude / mouseAdaptiveSmoothingRefMagnitude));
                smoothedLookDelta = Vector2.Lerp(smoothedLookDelta, delta, alpha);
                return smoothedLookDelta;
            }

            return delta;
        }

        private Vector2 ProcessControllerLook(Vector2 raw)
        {
            Vector2 v = raw;

            if (useControllerCurvesAndDeadzone)
            {
                v.x = RemapAxis(v.x, controllerDeadZoneInner, controllerDeadZoneOuter);
                v.y = RemapAxis(v.y, controllerDeadZoneInner, controllerDeadZoneOuter);

                v = new Vector2(ApplyCurve(v.x, controllerLookCurveExponent),
                                ApplyCurve(v.y, controllerLookCurveExponent));
            }

            float yaw = v.x * controllerSensitivity * horizontalMultiplier * Time.deltaTime;
            float pitch = v.y * controllerSensitivity * verticalMultiplier * Time.deltaTime;

            if (aimInput)
            {
                yaw *= adsSensitivityMultiplier;
                pitch *= adsSensitivityMultiplier;
            }

            Vector2 delta = new Vector2(yaw, pitch);

            // Aim assist slowdown (controller only)
            if (enableAimAssist && (!aimAssistOnlyWhileADS || aimInput))
            {
                float slowdown = ComputeAimAssistSlowdown();
                delta *= slowdown;
            }

            if (useControllerSmoothing)
            {
                float alpha = aimInput
                    ? controllerAdsSmoothingStrength
                    : controllerHipfireSmoothingStrength;
                smoothedLookDelta = Vector2.Lerp(smoothedLookDelta, delta, alpha);
                return smoothedLookDelta;
            }

            return delta;
        }

        private static float RemapAxis(float value, float inner, float outer)
        {
            float sign = Mathf.Sign(value);
            float mag = Mathf.Abs(value);

            if (mag <= inner) return 0f;
            if (mag >= outer) return sign;

            float t = (mag - inner) / (outer - inner);
            return sign * t;
        }

        private static float ApplyCurve(float value, float exponent)
        {
            float sign = Mathf.Sign(value);
            float mag = Mathf.Abs(value);
            return sign * Mathf.Pow(mag, exponent);
        }

        private float ComputeAimAssistSlowdown()
        {
            // Stub: return 1 (no slowdown). Fill in later with UnitManager target search.
            return 1f;
        }
    }
}
