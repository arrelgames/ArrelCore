using UnityEngine;

/*
Player (Unit)
 └── Camera
      └── WeaponParent (empty)
           ├── WeaponMesh (your gun model)
           ├── HipfireTransform (empty, positioned for hipfire)
           └── ADSTransform (empty, positioned in front of camera)

*/


namespace RLGames
{
    public class WeaponMeshController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform weaponMesh;
        [SerializeField] private Transform hipfirePosition;
        [SerializeField] private Transform adsPosition;

        [Header("Sway Settings")]
        [SerializeField] private float swayAmount = 0.05f;
        [SerializeField] private float swaySmooth = 4f;
        [SerializeField] private float adsSwayMultiplier = 0.5f;

        [Header("Bob Settings")]
        [SerializeField] private float bobAmount = 0.05f;
        [SerializeField] private float bobSpeed = 8f;
        [SerializeField] private float adsBobMultiplier = 0.3f;

        [Header("Recoil Settings")]
        [SerializeField] private Vector3 recoilKick = new Vector3(-0.05f, 0.03f, 0f);
        [SerializeField] private Vector3 recoilRotation = new Vector3(-5f, 0f, 0f);
        [SerializeField] private Vector3 randomRecoilRange = new Vector3(0.02f, 0.02f, 0f); // max random offset
        [SerializeField] private float recoilRecoverSpeed = 8f;

        [Header("Lerp Settings")]
        [SerializeField] private float positionLerpSpeed = 10f;
        [SerializeField] private float rotationLerpSpeed = 10f;

        private Vector2 swayInput;
        private Vector2 moveInput;
        private bool isADS = false;

        private Vector3 swayOffset;
        private Vector3 bobOffset;
        private Vector3 recoilOffset;
        private Vector3 recoilRotOffset;

        private float bobTimer = 0f;

        private void Awake()
        {
            if (weaponMesh == null)
                weaponMesh = this.transform;
        }

        private void Update()
        {
            HandleSway();
            HandleBob();
            RecoverRecoil();
            UpdateWeaponTransform();
        }

        #region Public API
        public void SetADS(bool aiming)
        {
            isADS = aiming;
        }
        public void SetSwayInput(Vector2 lookInput) => swayInput = lookInput;
        public void SetMoveInput(Vector2 movementInput) => moveInput = movementInput;

        /// <summary>Call this from firing code to apply cosmetic recoil with randomness</summary>
        public void ApplyRecoil()
        {
            // Base recoil
            Vector3 recoilPos = recoilKick;
            Vector3 recoilRot = recoilRotation;

            // Add random variation
            Vector3 randomPos = new Vector3(
                Random.Range(-randomRecoilRange.x, randomRecoilRange.x),
                Random.Range(-randomRecoilRange.y, randomRecoilRange.y),
                Random.Range(-randomRecoilRange.z, randomRecoilRange.z)
            );

            recoilOffset += recoilPos + randomPos;
            recoilRotOffset += recoilRot;
        }
        #endregion

        #region Internal Logic
        private void HandleSway()
        {
            float swayMultiplier = isADS ? adsSwayMultiplier : 1f;
            Vector3 targetSway = new Vector3(-swayInput.x, -swayInput.y, 0f) * swayAmount * swayMultiplier;
            swayOffset = Vector3.Lerp(swayOffset, targetSway, Time.deltaTime * swaySmooth);
        }

        private void HandleBob()
        {
            float speed = moveInput.magnitude;
            if (speed > 0.01f)
            {
                bobTimer += Time.deltaTime * bobSpeed;
                float bobX = Mathf.Sin(bobTimer) * bobAmount * moveInput.x;
                float bobY = Mathf.Cos(bobTimer * 2f) * bobAmount * moveInput.y;
                float bobMultiplier = isADS ? adsBobMultiplier : 1f;
                bobOffset = new Vector3(bobX, bobY, 0f) * bobMultiplier;
            }
            else
            {
                bobOffset = Vector3.Lerp(bobOffset, Vector3.zero, Time.deltaTime * 5f);
            }
        }

        private void RecoverRecoil()
        {
            recoilOffset = Vector3.Lerp(recoilOffset, Vector3.zero, Time.deltaTime * recoilRecoverSpeed);
            recoilRotOffset = Vector3.Lerp(recoilRotOffset, Vector3.zero, Time.deltaTime * recoilRecoverSpeed);
        }

        private void UpdateWeaponTransform()
        {
            Vector3 targetPosition = isADS ? adsPosition.localPosition : hipfirePosition.localPosition;
            Quaternion targetRotation = isADS ? adsPosition.localRotation : hipfirePosition.localRotation;

            targetPosition += swayOffset + bobOffset + recoilOffset;
            targetRotation *= Quaternion.Euler(recoilRotOffset);

            weaponMesh.localPosition = Vector3.Lerp(weaponMesh.localPosition, targetPosition, Time.deltaTime * positionLerpSpeed);
            weaponMesh.localRotation = Quaternion.Slerp(weaponMesh.localRotation, targetRotation, Time.deltaTime * rotationLerpSpeed);
        }
        #endregion
    }
}