using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// CPU-side SH upload mode for grid GI. Tier C uses one mono SH volume (bound to all three SH texture globals).
    /// Tier A uses three volumes (R/G/B SH coefficients in RGBA each).
    /// </summary>
    public enum GiShGridMode
    {
        Off = 0,
        TierC_Mono = 1,
        TierA_PerChannelRgb = 2
    }

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

        /// <summary>1 = apply N·(-L) visibility to sampled GI; 0 = legacy omni sampling. Set by <see cref="GiManagerDirectional"/>.</summary>
        public const string DirectionalActive = "_GiDirectionalActive";

        /// <summary>World-space direction the GI light travels (same convention as <see cref="GiSource"/> Directional).</summary>
        public const string SunDirWorld = "_GiSunDirWorld";

        /// <summary>Optional: include <c>Assets/RLCore/Shaders/GiDirectionalScale.hlsl</c> in a Shader Graph Custom Function named <c>GiDirectionalScale_float</c>, wired after the final GI multiply and before the subgraph output.</summary>
        public const string DirectionalScaleIncludePath = "Assets/RLCore/Shaders/GiDirectionalScale.hlsl";

        public const string VolumeSh0 = "_GiVolumeSH0";
        public const string VolumeSh1 = "_GiVolumeSH1";
        public const string VolumeSh2 = "_GiVolumeSH2";
        /// <summary>1 = apply SH directional combine in <c>SGF_GI_Sample</c>; 0 = L0-only.</summary>
        public const string UseSH = "_GiUseSH";
        /// <summary>1 = Tier C (mono SH0); 2 = Tier A (SH0/1/2 per channel).</summary>
        public const string ShTier = "_GiShTier";
        public const string ShCombineIncludePath = "Assets/RLCore/Shaders/GiShIrradiance.hlsl";

        private static Texture3D sNeutralVolume1;

        /// <summary>1×1×1 RGBAHalf black volume for binding when SH is disabled or unused.</summary>
        public static Texture3D NeutralVolume1x1
        {
            get
            {
                if (sNeutralVolume1 == null)
                {
                    sNeutralVolume1 = new Texture3D(1, 1, 1, TextureFormat.RGBAHalf, false)
                    {
                        name = "GiNeutralVolume1",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Point
                    };
                    sNeutralVolume1.SetPixel(0, 0, 0, Color.clear);
                    sNeutralVolume1.Apply(false, false);
                }

                return sNeutralVolume1;
            }
        }

        /// <summary>Disables SH sampling in materials that declare the SH properties (e.g. when using <see cref="GiManagerDirectional"/> only).</summary>
        public static void BindNeutralShGlobals()
        {
            Texture3D z = NeutralVolume1x1;
            Shader.SetGlobalTexture(VolumeSh0, z);
            Shader.SetGlobalTexture(VolumeSh1, z);
            Shader.SetGlobalTexture(VolumeSh2, z);
            Shader.SetGlobalFloat(UseSH, 0f);
            Shader.SetGlobalFloat(ShTier, 0f);
        }

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
