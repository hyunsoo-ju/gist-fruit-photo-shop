// Distant layers in the pixel style (Docs/DESIGN.md §3): simple lit plus an ordered (Bayer 4x4) dither toward a fade colour.
// The fade is quantised into _Levels steps and dithered between neighbouring steps on a screen grid of _DitherPixel px.
Shader "FruitGame/Dither Fade"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _FadeColor("Fade Color", Color) = (0.663, 0.776, 0.867, 1)
        _Fade("Fade", Range(0, 1)) = 0.6
        _Levels("Levels", Range(1, 8)) = 4
        _DitherPixel("Dither Pixel Size", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _FadeColor;
                half _Fade;
                half _Levels;
                float _DitherPixel;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            half4 frag(Varyings input) : SV_Target
            {
                // Mip 0 only: the atlases are palettes of small swatches, and lower mips blend neighbouring swatches.
                half4 albedo = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv, 0) * _BaseColor;
                float3 n = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate(dot(n, light.direction));
                half3 color = albedo.rgb * (light.color * ndl * light.shadowAttenuation + SampleSH(n));

                uint2 cellPos = (uint2)floor(input.positionCS.xy / max(_DitherPixel, 1));
                float threshold = (bayer[(cellPos.y % 4) * 4 + (cellPos.x % 4)] + 0.5) / 16.0;
                float f = _Fade * _Levels;
                float level = floor(f) + step(threshold, frac(f));
                color = lerp(color, _FadeColor.rgb, saturate(level / _Levels));
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
}
