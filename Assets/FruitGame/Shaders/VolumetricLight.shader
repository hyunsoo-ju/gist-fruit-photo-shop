// Volumetric sun beams for the realistic look (Docs/DESIGN.md §4, SceneLighting). A quad just past the camera's near
// plane; for each pixel it marches from the camera to the surface behind (the depth texture), sampling the sun's
// shadow map and its dapple cookie, so shafts of light appear wherever the sun gets past the leaves and crates, and
// dark wedges where they block it. Henyey-Greenstein scattering makes the air glow most when looking toward the sun.
Shader "FruitGame/Volumetric Light"
{
    Properties
    {
        _Tint("Tint", Color) = (1, 0.8, 0.58, 1)
        _Density("Density", Range(0, 0.5)) = 0.05
        _MaxDistance("Max Distance", Float) = 28
        _Anisotropy("Forward Scattering", Range(0, 0.95)) = 0.6
        _Steps("Steps", Range(8, 64)) = 32
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
            CBUFFER_END

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
                float acc = 0;
                [loop] for (int i = 0; i < steps; i++)
                {
                    float3 p = start + dir * ((i + jitter) * stepLen);
                    half lit = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));
                    #if defined(_LIGHT_COOKIES)
                        lit *= SampleMainLightCookie(p).r;
                    #endif
                    acc += lit;
                }
                acc *= stepLen * _Density;

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
