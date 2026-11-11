// The photo card's fruit stickers (PhotoDecor): a fruit model drawn once, off-screen, in a fixed soft light from the
// upper left, so the stickers look the same whichever lighting look the game is in.
Shader "Hidden/FruitGame/Sticker"
{
    Properties
    {
        _BaseMap("Atlas", 2D) = "white" {}
        _BaseColor("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 n = normalize(i.normalWS);
                // Toward the light: up, left and toward the viewer (the camera looks along +z).
                half ndl = saturate(dot(n, normalize(float3(-0.45, 0.65, -0.6))));
                half3 light = half3(0.62, 0.62, 0.66) + half3(1.0, 0.96, 0.88) * ndl * 1.2;
                return half4(albedo * light, 1);
            }
            ENDHLSL
        }
    }
}
