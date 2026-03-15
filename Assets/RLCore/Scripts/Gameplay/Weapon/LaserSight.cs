using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Laser sight that casts a ray from a muzzle transform and
    /// renders a beam plus impact visual up to a maximum distance.
    /// Designed to live on a prefab that can be parented under a weapon muzzle.
    /// </summary>
    public class LaserSight : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private GameObject impactVfx;

        [Header("Settings")]
        [SerializeField] private float maxDistance = 20000f;
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private bool alwaysOn = true;

        private bool isEnabled = true;

        private void Awake()
        {
            if (muzzle == null)
            {
                muzzle = transform;
            }

            if (lineRenderer != null)
            {
                lineRenderer.positionCount = 2;
            }
        }

        private void OnEnable()
        {
            isEnabled = true;
            UpdateVisibility();
        }

        private void OnDisable()
        {
            if (lineRenderer != null)
            {
                lineRenderer.enabled = false;
            }

            if (impactVfx != null)
            {
                impactVfx.SetActive(false);
            }
        }

        private void Update()
        {
            if (!alwaysOn && !isEnabled)
                return;

            if (muzzle == null || lineRenderer == null)
                return;

            UpdateLaser();
        }

        /// <summary>
        /// Enable or disable the laser visuals at runtime.
        /// Does not disable this component itself.
        /// </summary>
        public void SetLaserEnabled(bool enabled)
        {
            isEnabled = enabled;
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            bool show = (alwaysOn || isEnabled);

            if (lineRenderer != null)
            {
                lineRenderer.enabled = show;
            }

            if (impactVfx != null)
            {
                impactVfx.SetActive(show);
            }
        }

        private void UpdateLaser()
        {
            Vector3 origin = muzzle.position;
            Vector3 direction = muzzle.forward;

            RaycastHit hit;
            Vector3 endPoint;
            bool hasHit = Physics.Raycast(origin, direction, out hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore);

            if (hasHit)
            {
                endPoint = hit.point;
            }
            else
            {
                endPoint = origin + direction * maxDistance;
            }

            // Update beam
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, origin);
            lineRenderer.SetPosition(1, endPoint);

            // Update impact VFX
            if (impactVfx != null)
            {
                if (hasHit)
                {
                    if (!impactVfx.activeSelf && (alwaysOn || isEnabled))
                        impactVfx.SetActive(true);

                    impactVfx.transform.position = hit.point;
                    impactVfx.transform.rotation = Quaternion.LookRotation(hit.normal);
                }
                else
                {
                    // Hide impact when nothing is hit, but keep beam visible
                    if (impactVfx.activeSelf)
                        impactVfx.SetActive(false);
                }
            }
        }
    }
}

