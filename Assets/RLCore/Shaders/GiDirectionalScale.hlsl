#ifndef GI_DIRECTIONAL_SCALE_INCLUDED
#define GI_DIRECTIONAL_SCALE_INCLUDED

// Used by Shader Graph Custom Function nodes (optional) or manual includes.
void GiDirectionalScale_float(float3 Normal, float3 SunDir, float Active, float3 In, out float3 Out)
{
    float3 n = normalize(Normal);
    float3 s = normalize(SunDir);
    float ndl = saturate(dot(n, -s));
    Out = In * lerp(1.0, ndl, saturate(Active));
}

#endif
