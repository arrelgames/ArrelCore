using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// Shared GI shader global names and UVW mapping helper for debugging.
    /// </summary>
    public static class GiShaderGlobals
    {
        public const string VolumeTexture = "_GiVolume";
        public const string Intensity = "_GiIntensity";
        public const string GridMinXZ = "_GiGridMinXZ";
        public const string GridMaxXZ = "_GiGridMaxXZ";
        public const string GridSizeXZ = "_GiGridSizeXZ";
        public const string VolumeSize = "_GiVolumeSize";
        public const string VolumeParams = "_GiVolumeParams";

        /// <summary>
        /// CPU-side mirror of shader UVW mapping.
        /// x = worldX * volumeParams.x + volumeParams.z
        /// z = worldZ * volumeParams.y + volumeParams.w
        /// y = 0.5 (current mid-slice packing in GiManager).
        /// </summary>
        public static Vector3 WorldToVolumeUVW(Vector3 worldPos, Vector4 volumeParams)
        {
            float u = worldPos.x * volumeParams.x + volumeParams.z;
            float w = worldPos.z * volumeParams.y + volumeParams.w;
            return new Vector3(u, 0.5f, w);
        }
    }
}
