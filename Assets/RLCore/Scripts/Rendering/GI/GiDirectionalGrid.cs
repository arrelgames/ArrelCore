using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Thin façade over <see cref="GiGrid"/> for directional sun injection (used by <see cref="GiManagerDirectional"/>).
    /// </summary>
    public sealed class GiDirectionalGrid
    {
        private readonly GiGrid grid;

        public GiGrid Grid => grid;

        public GiDirectionalGrid(GiGrid grid)
        {
            this.grid = grid ?? throw new System.ArgumentNullException(nameof(grid));
        }

        public void ApplyDirectionalSun(
            Vector3 lightDirWorld,
            Color peakIrradiance,
            float maxDistanceWorld,
            bool respectOcclusion,
            bool preferVerticalColumnTransmittance,
            float verticalDownMinAbsY)
        {
            grid.ClearSourcesAndApplyDirectionalSun(
                lightDirWorld,
                peakIrradiance,
                maxDistanceWorld,
                respectOcclusion,
                preferVerticalColumnTransmittance,
                verticalDownMinAbsY);
        }
    }
}
