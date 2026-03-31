using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Marks a light/emissive object as a GI source.
    /// Does not create actual Unity lights; instead, it injects irradiance into <see cref="GiGrid"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiSource : MonoBehaviour
    {
        [Tooltip("Peak irradiance contributed to nearby GI nodes.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color irradiance = Color.white;

        [Tooltip("World-space radius within which this source affects GI nodes.")]
        [Min(0.1f)]
        [SerializeField] private float radius = 5f;

        [Tooltip("If true, grid-based LOS is used so light does not pass through blocked tiles.")]
        [SerializeField] private bool respectOcclusion = true;

        [Tooltip("Optional per-source intensity multiplier, useful for animation.")]
        [SerializeField] private float intensityMultiplier = 1f;

        /// <summary>
        /// Called by <see cref="GiManager"/> once per propagation tick to contribute source energy.
        /// </summary>
        public void ApplyToGrid(GiGrid grid)
        {
            if (grid == null)
                return;

            Color contribution = irradiance * Mathf.Max(0f, intensityMultiplier);
            if (contribution.maxColorComponent <= 0f)
                return;

            grid.AddRadialSource(transform.position, radius, contribution, respectOcclusion);
        }

        public void SetIntensityMultiplier(float value)
        {
            intensityMultiplier = value;
        }
    }
}

