// The voxel title and icon (Docs/DESIGN.md §11): vertex-coloured blocks, lit like the scene (main light, shadows,
// ambient), plus a diagonal band of light that sweeps across in world space (_SweepX, animated by VoxelTitle).
Shader "FruitGame/Voxel Title"
{
    Properties
    {
        _SweepX("Sweep Position (world x)", Float) = -100
        _SweepWidth("Sweep Width", Float) = 0.35
        _SweepStrength("Sweep Strength", Range(0, 1)) = 0.45
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SweepX;
                float _SweepWidth;
                half _SweepStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                // Vertex colours are authored in sRGB.
                o.color = half4(SRGBToLinear(input.color.rgb), input.color.a);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate(dot(n, light.direction));
                half3 color = input.color.rgb * (light.color * ndl * light.shadowAttenuation + SampleSH(n));
                // A slanted band of light: brightest where (x + y/2) passes _SweepX.
                float d = abs(input.positionWS.x + input.positionWS.y * 0.5 - _SweepX);
                color += _SweepStrength * saturate(1.0 - d / _SweepWidth) * step(d, _SweepWidth);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
    }
}
