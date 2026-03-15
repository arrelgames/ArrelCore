using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Self-contained cosmetic weapon controller. Derives sway from observing
    /// parent-transform rotation changes and character velocity -- no brain
    /// coupling required. Works identically for player and AI units.
    /// </summary>
    public class WeaponMeshController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The weapon model transform to animate. Defaults to this transform if unset.")]
        [SerializeField] private Transform weaponMesh;
        [Tooltip("Empty transform at the hipfire rest position.")]
        [SerializeField] private Transform hipfirePosition;
        [Tooltip("Empty transform at the ADS (aim down sights) position.")]
        [SerializeField] private Transform adsPosition;

        [Header("Sway - Rotation")]
        [Tooltip("How strongly camera rotation translates into weapon positional sway.")]
        [SerializeField] private float swayAmount = 0.04f;
        [Tooltip("How fast the weapon catches up to the sway target. Higher = snappier.")]
        [SerializeField] private float swaySmooth = 10f;
        [Tooltip("Sway multiplier while aiming down sights (< 1 reduces sway).")]
        [SerializeField] private float adsSwayMultiplier = 0.4f;

        [Header("Sway - Rotation Tilt")]
        [Tooltip("How strongly camera rotation causes the weapon to tilt (degrees).")]
        [SerializeField] private float swayTiltAmount = 3f;

        [Header("Sway - Bounds")]
        [Tooltip("Max positional sway offset magnitude (meters). Weapon is hard-clamped to this radius.")]
        [SerializeField] private float maxSwayPosition = 0.03f;
        [Tooltip("Max rotational sway tilt (degrees). Weapon tilt is hard-clamped to this.")]
        [SerializeField] private float maxSwayRotation = 5f;
        [Tooltip("Curve that reduces sway accumulation as the weapon approaches its bounds. X = 0 (at limit) to 1 (at rest). Y = multiplier on new sway.")]
        [SerializeField] private AnimationCurve swayFalloff = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Bob")]
        [Tooltip("Positional bob amplitude when moving.")]
        [SerializeField] private float bobAmount = 0.03f;
        [Tooltip("Bob oscillation speed.")]
        [SerializeField] private float bobSpeed = 7f;
        [Tooltip("Bob multiplier while aiming down sights.")]
        [SerializeField] private float adsBobMultiplier = 0.25f;

        [Header("Breathing")]
        [Tooltip("Subtle idle breathing amplitude.")]
        [SerializeField] private float breathAmount = 0.005f;
        [Tooltip("Breathing oscillation speed.")]
        [SerializeField] private float breathSpeed = 1.5f;

        [Header("Recoil")]
        [Tooltip("Default positional kick applied per shot.")]
        [SerializeField] private Vector3 recoilKick = new Vector3(-0.05f, 0.03f, 0f);
        [Tooltip("Default rotational kick applied per shot (degrees).")]
        [SerializeField] private Vector3 recoilRotKick = new Vector3(-5f, 0f, 0f);
        [Tooltip("Per-axis random variation added to each recoil kick.")]
        [SerializeField] private Vector3 recoilRandomRange = new Vector3(0.02f, 0.02f, 0f);
        [Tooltip("How fast recoil decays back to zero. Higher = faster recovery.")]
        [SerializeField] private float recoilReturnSpeed = 8f;

        [Header("ADS Transition")]
        [Tooltip("How fast the weapon lerps between hipfire and ADS positions.")]
        [SerializeField] private float adsLerpSpeed = 12f;

        // Auto-discovered references
        private Unit unit;
        private CharacterController characterController;

        // Frame-to-frame tracking
        private Quaternion lastParentRotation;
        private Vector3 lastRootPosition;

        // Accumulated offsets
        private Vector3 swayPositionOffset;
        private Vector2 swayRotationOffset;
        private Vector3 bobOffset;
        private Vector3 breathOffset;
        private Vector3 recoilPosition;
        private Vector3 recoilRotation;
        private Vector3 recoilVelocityPos;
        private Vector3 recoilVelocityRot;

        private float bobTimer;
        private float breathTimer;
        private bool isADS;

        private void Awake()
        {
            if (weaponMesh == null)
                weaponMesh = transform;

            unit = GetComponentInParent<Unit>();
            if (unit == null)
                Debug.LogWarning("[WeaponMeshController] No Unit found in parents; ADS detection disabled.", this);

            characterController = GetComponentInParent<CharacterController>();
        }

        private void Start()
        {
            if (transform.parent != null)
                lastParentRotation = transform.parent.rotation;

            lastRootPosition = transform.root.position;
        }

        private void LateUpdate()
        {
            DetectState();
            Vector2 rotDelta = DetectRotationDelta();
            Vector3 moveDelta = DetectMovementDelta();

            HandleSway(rotDelta);
            HandleBob(moveDelta);
            HandleBreathing();
            RecoverRecoil();
            UpdateTransform();
        }

        #region Public API

        /// <summary>Apply cosmetic recoil using built-in default kick values with random variation.</summary>
        public void ApplyRecoil()
        {
            Vector3 randomPos = new Vector3(
                Random.Range(-recoilRandomRange.x, recoilRandomRange.x),
                Random.Range(-recoilRandomRange.y, recoilRandomRange.y),
                Random.Range(-recoilRandomRange.z, recoilRandomRange.z));

            recoilPosition += recoilKick + randomPos;
            recoilRotation += recoilRotKick;
        }

        /// <summary>Apply cosmetic recoil with explicit kick values.</summary>
        public void ApplyRecoil(Vector3 positionKick, Vector3 rotationKick)
        {
            recoilPosition += positionKick;
            recoilRotation += rotationKick;
        }

        #endregion

        #region Detection

        private void DetectState()
        {
            isADS = unit != null && unit.command.Aim;
        }

        private Vector2 DetectRotationDelta()
        {
            if (transform.parent == null)
                return Vector2.zero;

            Quaternion currentRot = transform.parent.rotation;
            Quaternion delta = Quaternion.Inverse(lastParentRotation) * currentRot;
            lastParentRotation = currentRot;

            float yaw = NormalizeAngle(delta.eulerAngles.y);
            float pitch = -NormalizeAngle(delta.eulerAngles.x);

            return new Vector2(yaw, pitch);
        }

        private Vector3 DetectMovementDelta()
        {
            if (characterController != null)
            {
                Vector3 vel = characterController.velocity;
                vel.y = 0f;
                return transform.InverseTransformDirection(vel);
            }

            Vector3 rootPos = transform.root.position;
            Vector3 worldDelta = (rootPos - lastRootPosition) / Time.deltaTime;
            lastRootPosition = rootPos;
            worldDelta.y = 0f;
            return transform.InverseTransformDirection(worldDelta);
        }

        #endregion

        #region Processing

        private void HandleSway(Vector2 rotDelta)
        {
            float adsMult = isADS ? adsSwayMultiplier : 1f;

            // Soft-limit: reduce accumulation as we approach bounds
            float posRatio = maxSwayPosition > 0f
                ? 1f - swayPositionOffset.magnitude / maxSwayPosition
                : 0f;
            float rotRatio = maxSwayRotation > 0f
                ? 1f - swayRotationOffset.magnitude / maxSwayRotation
                : 0f;
            float posFalloff = swayFalloff.Evaluate(Mathf.Clamp01(posRatio));
            float rotFalloff = swayFalloff.Evaluate(Mathf.Clamp01(rotRatio));

            // Position sway target
            Vector3 posTarget = new Vector3(-rotDelta.x, -rotDelta.y, 0f)
                * swayAmount * adsMult * posFalloff;

            // Rotation sway accumulation
            Vector2 rotAccum = new Vector2(rotDelta.x, rotDelta.y)
                * swayTiltAmount * adsMult * rotFalloff;

            float t = SmoothFactor(swaySmooth);

            swayPositionOffset = Vector3.Lerp(swayPositionOffset, posTarget, t);
            swayRotationOffset = Vector2.Lerp(swayRotationOffset, rotAccum, t);

            // Hard clamp
            swayPositionOffset = Vector3.ClampMagnitude(swayPositionOffset, maxSwayPosition);
            swayRotationOffset = Vector2.ClampMagnitude(swayRotationOffset, maxSwayRotation);
        }

        private void HandleBob(Vector3 localVelocity)
        {
            float speed = new Vector2(localVelocity.x, localVelocity.z).magnitude;

            if (speed > 0.1f)
            {
                bobTimer += Time.deltaTime * bobSpeed;
                float moveDir = Mathf.Sign(localVelocity.x);

                float x = Mathf.Sin(bobTimer) * bobAmount * moveDir;
                float y = Mathf.Cos(bobTimer * 2f) * bobAmount;

                float adsMult = isADS ? adsBobMultiplier : 1f;
                bobOffset = new Vector3(x, y, 0f) * adsMult;
            }
            else
            {
                bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, SmoothFactor(5f));
            }
        }

        private void HandleBreathing()
        {
            breathTimer += Time.deltaTime * breathSpeed;

            float y = Mathf.Sin(breathTimer) * breathAmount;
            float x = Mathf.Cos(breathTimer * 0.5f) * breathAmount;

            breathOffset = new Vector3(x, y, 0f);
        }

        private void RecoverRecoil()
        {
            recoilPosition = Vector3.SmoothDamp(
                recoilPosition, Vector3.zero,
                ref recoilVelocityPos, 1f / recoilReturnSpeed);

            recoilRotation = Vector3.SmoothDamp(
                recoilRotation, Vector3.zero,
                ref recoilVelocityRot, 1f / recoilReturnSpeed);
        }

        private void UpdateTransform()
        {
            Vector3 basePos = isADS ? adsPosition.localPosition : hipfirePosition.localPosition;
            Quaternion baseRot = isADS ? adsPosition.localRotation : hipfirePosition.localRotation;

            Vector3 finalPos = basePos
                + swayPositionOffset
                + bobOffset
                + breathOffset
                + recoilPosition;

            Quaternion swayTilt = Quaternion.Euler(-swayRotationOffset.y, swayRotationOffset.x, 0f);
            Quaternion finalRot = baseRot * swayTilt * Quaternion.Euler(recoilRotation);

            float t = SmoothFactor(adsLerpSpeed);
            weaponMesh.localPosition = Vector3.Lerp(weaponMesh.localPosition, finalPos, t);
            weaponMesh.localRotation = Quaternion.Slerp(weaponMesh.localRotation, finalRot, t);
        }

        #endregion

        #region Utility

        private static float SmoothFactor(float speed)
        {
            return 1f - Mathf.Exp(-speed * Time.deltaTime);
        }

        private static float NormalizeAngle(float angle)
        {
            return Mathf.Repeat(angle + 180f, 360f) - 180f;
        }

        #endregion
    }
}
