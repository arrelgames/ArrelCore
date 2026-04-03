#ifndef GI_SH_IRRADIANCE_INCLUDED
#define GI_SH_IRRADIANCE_INCLUDED

// Real SH (L0+L1) basis matching CPU GiShBasis / Unity-style Y_lm for radiance coefficients.
#define GI_SH_Y00 0.28209479177387814
#define GI_SH_Y1  0.4886025119029199

float GiShEvalMonoRadiance(float3 n, float4 shLm)
{
    // shLm = (c0, c_{1,-1}, c_{1,0}, c_{1,+1}) with c_k accumulating Y_k(ω_in) * radiance.
    return shLm.x * GI_SH_Y00
        + GI_SH_Y1 * (shLm.y * n.y + shLm.z * n.z + shLm.w * n.x);
}

float GiShIsoTerm(float c0)
{
    return c0 * GI_SH_Y00;
}

// Tier C: modulate L0 RGB by normalized mono angular factor (direct SH vs propagated L0).
void GiShCombineLit_float(
    float3 NormalWS,
    float3 L0Rgb,
    float4 Sh0,
    float4 Sh1,
    float4 Sh2,
    float UseSH,
    float ShTier,
    out float3 Out)
{
    float w = saturate(UseSH);
    if (w < 1e-5)
    {
        Out = L0Rgb;
        return;
    }

    float3 n = normalize(NormalWS);
    float tier = ShTier + 0.0; // force float

    if (tier < 1.5)
    {
        float mag = abs(Sh0.x) + abs(Sh0.y) + abs(Sh0.z) + abs(Sh0.w);
        if (mag < 1e-8)
        {
            Out = L0Rgb;
            return;
        }
        float iso = GiShIsoTerm(Sh0.x);
        float ang = GiShEvalMonoRadiance(n, Sh0);
        float denom = max(iso, 1e-4);
        float f = saturate(max(ang, 0.0) / denom);
        f = min(f, 3.0);
        Out = L0Rgb * lerp(1.0, f, w);
        return;
    }

    // Tier A: per-channel angular factors (same reconstruction as mono, per channel).
    float3 fRgb = float3(1, 1, 1);
    float isoR = GiShIsoTerm(Sh0.x);
    float isoG = GiShIsoTerm(Sh1.x);
    float isoB = GiShIsoTerm(Sh2.x);
    if (isoR > 1e-6)
        fRgb.r = saturate(max(GiShEvalMonoRadiance(n, Sh0), 0.0) / max(isoR, 1e-4));
    if (isoG > 1e-6)
        fRgb.g = saturate(max(GiShEvalMonoRadiance(n, Sh1), 0.0) / max(isoG, 1e-4));
    if (isoB > 1e-6)
        fRgb.b = saturate(max(GiShEvalMonoRadiance(n, Sh2), 0.0) / max(isoB, 1e-4));
    fRgb = min(fRgb, 3.0);
    Out = L0Rgb * lerp(float3(1, 1, 1), fRgb, w);
}

void GiShCombineLit_half(
    half3 NormalWS,
    half3 L0Rgb,
    half4 Sh0,
    half4 Sh1,
    half4 Sh2,
    half UseSH,
    half ShTier,
    out half3 Out)
{
    float3 o;
    GiShCombineLit_float(
        float3(NormalWS),
        float3(L0Rgb),
        float4(Sh0),
        float4(Sh1),
        float4(Sh2),
        float(UseSH),
        float(ShTier),
        o);
    Out = half3(o);
}

#endif
