// Volumetric sun beams for the realistic look (Docs/DESIGN.md §4, SceneLighting). A quad just past the camera's near
// plane; for each pixel it marches from the camera to the surface behind (the depth texture), sampling the sun's
// shadow map and its dapple cookie, so shafts of light appear wherever the sun gets past the leaves and crates, and
// dark wedges where they block it. Henyey-Greenstein scattering makes the air glow most when looking toward the sun.
// _Contrast keeps the shafts and fades the even glow between them (see frag).
Shader "FruitGame/Volumetric Light"
{
    Properties
    {
        _Tint("Tint", Color) = (1, 0.8, 0.58, 1)
        _Density("Density", Range(0, 0.5)) = 0.05
        _MaxDistance("Max Distance", Float) = 28
        _Anisotropy("Forward Scattering", Range(0, 0.95)) = 0.6
        _Steps("Steps", Range(8, 64)) = 32
        _Contrast("Shaft Contrast", Range(0, 4)) = 0
        _LeafLight("Light Through Leaves", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" }

        Pass
        {
            Name "Beams"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _LIGHT_COOKIES

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Density;
                float _MaxDistance;
                half _Anisotropy;
                float _Steps;
                half _Contrast;
                half _LeafLight;
            CBUFFER_END

            // The sun's shadow at p, as the exact average of the 4 × 4 block of shadow-map texels it falls in (four
            // bilinear comparison taps at the block's texel corners). The canopy's leaves let light through as an
            // ordered 4 × 4 pattern of holes in the shadow map (CanopyShadow.shader); read one texel at a time, every
            // hole counted as a gap and the god rays lit the whole screen. One block, not VoxelLight's blend of four:
            // the jittered steps along each ray hide the blocks.
            half SunShadow(float3 p)
            {
                #if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    return 1.0h;
                #else
                    float4 sc = TransformWorldToShadowCoord(p);
                    float2 corner = floor(sc.xy * _MainLightShadowmapSize.zw * 0.25) * 4.0 + 1.0;
                    float2 inv = _MainLightShadowmapSize.xy;
                    half s = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(0, 0)) * inv, sc.z))
                           + SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(2, 0)) * inv, sc.z))
                           + SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(0, 2)) * inv, sc.z))
                           + SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(2, 2)) * inv, sc.z));
                    s = LerpWhiteTo(s * 0.25h, GetMainLightShadowParams().x);
                    return BEYOND_SHADOW_FAR(sc) ? 1.0h : s;
                #endif
            }

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float raw = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 end = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float3 start = _WorldSpaceCameraPos;
                float3 ray = end - start;
                float len = min(length(ray), _MaxDistance);
                float3 dir = ray / max(length(ray), 1e-4);

                int steps = (int)_Steps;
                float stepLen = len / steps;
                // Jitter the start per pixel so the steps don't show as bands.
                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0);
                Light sun = GetMainLight();
                // Each step's light is split in two: the shafts, where the sun comes through a gap in the canopy, and
                // the even glow, the light the leaves let through (_LeafLight; the shadow map's holes blur to that share
                // under the leaves) plus the open air before the canopy's shade begins.
                half t = _LeafLight;
                float shafts = 0, glow = 0;
                [loop] for (int i = 0; i < steps; i++)
                {
                    float3 p = start + dir * ((i + jitter) * stepLen);
                    half lit = SunShadow(p);
                    #if defined(_LIGHT_COOKIES)
                        lit *= SampleMainLightCookie(p).r;
                    #endif
                    half gap = saturate((lit - t) / max(1.0h - t, 1e-3h));
                    shafts += gap;
                    glow += lit - gap * (1.0h - t);
                }
                // Contrast (staff panel): a ray that crosses a shaft gathers far more gap light than one through the
                // part-shade around it, so the shaft light is weighted by the ray's share of gap steps against a shaft's
                // (ShaftShare), to the power of twice the contrast, and the even glow is turned down, 1 / (1 + contrast²):
                // the shafts keep their light and turning the god rays up no longer brightens the whole scene. Ordinary
                // rays come out a fifth to a third gap light (the near air and scattered gaps), so a shaft is half.
                // 0 = every ray's light as gathered.
                const float ShaftShare = 0.5;
                float share = shafts / steps;
                // (Kept off zero: pow(0, 0) is NaN on the GPU, which bloom spread into a white blowout.)
                float shaftWeight = pow(max(saturate(share / ShaftShare), 1e-4), 2.0 * _Contrast);
                float glowWeight = 1.0 / (1.0 + _Contrast * _Contrast);
                float acc = (shafts * (1.0 - t) * shaftWeight + glow * glowWeight) * stepLen * _Density;

                // Henyey-Greenstein: strongest looking toward the sun.
                float g = _Anisotropy;
                float c = dot(dir, sun.direction);
                float phase = (1 - g * g) / (4 * PI * pow(max(1 + g * g - 2 * g * c, 1e-3), 1.5));
                return half4(sun.color * _Tint.rgb * acc * phase * 4 * PI * 0.25, 0);
            }
            ENDHLSL
        }
    }
}
