#ifndef RGI_UPSCALE
#define RGI_UPSCALE

	// Copyright 2022-2026 Kronnect - All Rights Reserved.

    TEXTURE2D_X(_InputRTGI);
    TEXTURE2D_X(_NFO_RT);

    #include "RadiantGI_Probing.hlsl"

    #if _VIRTUAL_EMITTERS

        #define MAX_EMITTERS 32

        CBUFFER_START(RadiantGIEmittersBuffer)
            float4 _EmittersBoxMin[MAX_EMITTERS];
            float4 _EmittersBoxMax[MAX_EMITTERS];
            float3 _EmittersPositions[MAX_EMITTERS];
            half3 _EmittersColors[MAX_EMITTERS];
            int _EmittersCount;
        CBUFFER_END

        half3 GetVirtualEmitters(float3 wpos, half3 norm) {
            half3 sum = 0;
            for (int k=0;k<_EmittersCount;k++) {
                float4 boxMin = _EmittersBoxMin[k];
                float4 boxMax = _EmittersBoxMax[k];
                if (all(wpos >= boxMin.xyz) && all(wpos <= boxMax.xyz)) {
                    float3 emitterPos = _EmittersPositions[k];
                    float3 surfaceToEmitter = normalize(emitterPos - wpos);
                    half normAtten = max(0, dot(surfaceToEmitter, norm));

                    float distSqr = dot2(emitterPos - wpos);
                    float lightAtten = rcp(distSqr);
                    half factor = half(distSqr * boxMin.w);
                    half smoothFactor = saturate(half(1.0) - factor * factor);
                    smoothFactor = smoothFactor * smoothFactor;
                    half distAtten = lightAtten * smoothFactor;

                    half w = normAtten * distAtten;
                    sum = max(sum, w * _EmittersColors[k]);
                }
            }
            return sum;
        }


    #endif

    #define TEST_DEPTH(lowestDiff, nearestColor, depthDiff, color) if (depthDiff < lowestDiff) { lowestDiff = depthDiff; nearestColor = color; }

    half4 GetIndirect(float2 uv, float depth) {        
        half4 nearestColor = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv);

        half depthM = nearestColor.w;
        half diff = abs(depth - depthM);

        UNITY_BRANCH
        if (diff > 0.00001) {
            float m = 0.5;

            float2 uvN = uv + float2(0, _MainTex_TexelSize.y * m );
            float2 uvS = uv - float2(0, _MainTex_TexelSize.y * m);
            float2 uvE = uv + float2(_MainTex_TexelSize.x * m, 0);
            float2 uvW = uv - float2(_MainTex_TexelSize.x * m, 0);

            half4 colorN = SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, uvN, 0);
            half4 colorS = SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, uvS, 0);
            half4 colorE = SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, uvE, 0);
            half4 colorW = SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, uvW, 0);

            half4 depths = half4(colorN.w, colorS.w, colorE.w, colorW.w);
            half4 dDiff = abs(depths - depth.xxxx);

            half lowestDiff = diff;
            TEST_DEPTH(lowestDiff, nearestColor, dDiff.x, colorN);
            TEST_DEPTH(lowestDiff, nearestColor, dDiff.y, colorS);
            TEST_DEPTH(lowestDiff, nearestColor, dDiff.z, colorE);
            TEST_DEPTH(lowestDiff, nearestColor, dDiff.w, colorW);

        }
        return nearestColor;
    }

	half4 FragUpscale (VaryingsRGI input): SV_Target {

        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

        float rawDepth = GetRawDepth(uv);
        if (IsSkyBox(rawDepth)) return 0; // exclude skybox
        float depth = RawToLinearEyeDepth(rawDepth);

        float4 res = GetIndirect(uv, depth);
        return res;
	}


	half4 FragCompose (VaryingsRGI i) : SV_Target {
        UNITY_SETUP_INSTANCE_ID(i);
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float2 uv     = UnityStereoTransformScreenSpaceTex(i.uv);

        #if defined(DEBUG_GI)
            half4 input = half4(0, 0, 0, 0);
        #else
            half4 input = SAMPLE_TEXTURE2D_X_LOD(_InputRTGI, sampler_PointClamp, uv, 0);
        #endif
        
        float rawDepth = GetRawDepth(uv);
        if (IsSkyBox(rawDepth)) return input; // exclude skybox

        float depth = RawToLinearEyeDepth(rawDepth);

        // limit to volume bounds
        float3 wpos = GetWorldPosition(uv, rawDepth);
        if (IsOutsideBounds(wpos)) return input;

   	    half3 indirect = GetIndirect(uv, depth).rgb * INDIRECT_INTENSITY;
        
        half3 norm = GetWorldNormal(uv);

        // add virtual emitters
        #if _VIRTUAL_EMITTERS
            indirect += GetVirtualEmitters(wpos, norm);
        #endif

        // max brightness
        half lumaIndirect = GetLuma(indirect);
        indirect *= saturate(LUMA_MAX / (lumaIndirect + 0.001));

        float3 cameraPosition = GetCameraPositionWS();
        half3 toCamera = normalize(cameraPosition - wpos);
        half ndot = abs(dot(norm, toCamera));

        half surfaceOcclusion = 1;
        half3 ambientToSubtract = half3(0, 0, 0);
            
        // Calculate ambient to subtract
        half3 ambient = SampleAmbientLighting(wpos, norm, uv * SOURCE_SIZE);

        #if _FORWARD
            half4 pixel = max(0, SAMPLE_TEXTURE2D_X(_InputRTGI, sampler_LinearClamp, uv));
            half3 hue = normalize(pixel.rgb + 0.01);
            indirect = indirect * hue;
            ambientToSubtract = ambient * hue;
        #elif _FORWARD_AND_DEFERRED
            half occlusion;
            half3 brdfDiffuse;
            GetGBufferDiffuse(uv, brdfDiffuse, occlusion);
            if (all(brdfDiffuse == 0)) {
                half4 pixel = max(0, SAMPLE_TEXTURE2D_X_LOD(_InputRTGI, sampler_LinearClamp, uv, 0));
                half3 hue = normalize(pixel.rgb + 0.01);
                indirect = indirect * hue;
                ambientToSubtract = ambient * hue;
            } else {
                surfaceOcclusion = occlusion;
                indirect = indirect * brdfDiffuse;
                ambientToSubtract = ambient * brdfDiffuse;
            }
        #else
            half3 brdfDiffuse;
            GetGBufferDiffuse(uv, brdfDiffuse, surfaceOcclusion);
            indirect = indirect * brdfDiffuse;
            ambientToSubtract = ambient * brdfDiffuse;
        #endif

        half giAtten = lerp(1, surfaceOcclusion, OCCLUSION_INTENSITY);
        indirect *= giAtten;
        ambientToSubtract *= giAtten;
        
        #if _SCREEN_SPACE_OCCLUSION
            half aoterm = GetScreenSpaceAmbientOcclusion(uv).indirectAmbientOcclusion;
            half aotermInfluence = aoterm * AO_INFLUENCE + ONE_MINUS_AO_INFLUENCE;
            ambientToSubtract *= aotermInfluence;
            indirect *= aotermInfluence;
        #endif

        // reduce fog effect by enhancing normal mapping
        half normalAtten = lerp(1.0, ndot, NORMALS_INFLUENCE);
        indirect *= normalAtten;
        ambientToSubtract *= normalAtten;

        // optional GI weight
        half giLuma = GetLuma(indirect.rgb);
        input.rgb *= rcp(1.0 + giLuma * GI_WEIGHT);

        // saturate
        indirect = lerp(giLuma, indirect, COLOR_SATURATION);

        // attenuates near to camera
        half nearAtten = min(1.0, depth * NEAR_CAMERA_ATTENUATION);
        indirect *= nearAtten;
        ambientToSubtract *= nearAtten;

        // subtract ambient to input image
        ambientToSubtract *= UNITY_AMBIENT_INTENSITY;
        input.rgb = max(input.rgb * SOURCE_BRIGHTNESS - ambientToSubtract, half3(0, 0, 0));

        // add GI to input image        
        input.rgb = input.rgb + indirect;

        #if _USES_NEAR_FIELD_OBSCURANCE
            half nfo = SAMPLE_TEXTURE2D_X(_NFO_RT, sampler_LinearClamp, uv).r;
            input.rgb = lerp(input.rgb, input.rgb * NEAR_FIELD_OBSCURANCE_TINT, saturate(nfo));
        #endif

        // dithering to reduce banding (optional)
        // half dither = frac(52.9829189 * frac(dot(i.positionCS.xy, half2(0.06711056, 0.00583715)))) / 255.0;
        // input.rgb += dither;

        return input;
	}


#endif // RGI_UPSCALE