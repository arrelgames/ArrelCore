using UnityEngine;

namespace RLGames
{
    /// <summary>
    /// L0+L1 real spherical harmonics projection constants; must match <c>GiShIrradiance.hlsl</c>.
    /// </summary>
    public static class GiShBasis
    {
        public const float Y00 = 0.28209479177387814f;
        public const float Y1 = 0.4886025119029199f;

        /// <summary>Incoming radiance direction (unit) toward the surface; <paramref name="luminance"/> scales mono SH.</summary>
        public static void ProjectIncomingRadianceMono(ref Vector4 shLm, Vector3 wUnit, float luminance)
        {
            if (luminance <= 0f)
                return;
            shLm.x += luminance * Y00;
            shLm.y += luminance * Y1 * wUnit.y;
            shLm.z += luminance * Y1 * wUnit.z;
            shLm.w += luminance * Y1 * wUnit.x;
        }

        public static void ProjectIncomingRadianceRgb(
            ref Vector4 shR,
            ref Vector4 shG,
            ref Vector4 shB,
            Vector3 wUnit,
            Color rgb)
        {
            ProjectIncomingRadianceMono(ref shR, wUnit, rgb.r);
            ProjectIncomingRadianceMono(ref shG, wUnit, rgb.g);
            ProjectIncomingRadianceMono(ref shB, wUnit, rgb.b);
        }

        public static void ProjectIsotropicMono(ref Vector4 shLm, float luminance)
        {
            if (luminance <= 0f)
                return;
            shLm.x += luminance * Y00;
        }

        /// <summary>L0-only isotropic term per RGB channel (no angular variation).</summary>
        public static void ProjectIsotropicRgb(ref Vector4 shR, ref Vector4 shG, ref Vector4 shB, Color rgb)
        {
            if (rgb.r > 0f)
                shR.x += rgb.r * Y00;
            if (rgb.g > 0f)
                shG.x += rgb.g * Y00;
            if (rgb.b > 0f)
                shB.x += rgb.b * Y00;
        }
    }
}
