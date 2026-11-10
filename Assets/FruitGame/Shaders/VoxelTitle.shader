// The voxel title and icon (Docs/DESIGN.md §15): vertex-coloured blocks, lit like the scene (main light, shadows,
// ambient), plus a diagonal band of light that sweeps across in world space (_SweepX, animated by VoxelTitle).
Shader "FruitGame/Voxel Title"
{
    Properties
    {
        _SweepX("Sweep Position (world x)", Float) = -100
        _SweepWidth("Sweep Width", Float) = 0.35
        _SweepStrength("Sweep Strength", Range(0, 1)) = 0.45
        [Toggle] _SceneLit("Lit by the scene (butterflies, flames); off = the title and buttons' own fixed light", Float) = 0
        _Transmission("Light Through (scene-lit only)", Range(0, 2)) = 0
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "VoxelLight.hlsl"

            // The title, buttons and digits keep the classic light whatever the scene's lighting style (SceneLighting
            // sets these; with none set they follow the scene's light as before).
            float4 _UILightDir;     // xyz toward the light, w = 1 when set
            half4 _UILightColor;
            half4 _UIAmbientSky, _UIAmbientEquator, _UIAmbientGround;

            CBUFFER_START(UnityPerMaterial)
                float _SweepX;
                float _SweepWidth;
                half _SweepStrength;
                half _SceneLit;
                half _Transmission;
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
                half3 color;
                if (_SceneLit > 0.5h || _UILightDir.w < 0.5)
                    color = VoxelLight(input.color.rgb, n, input.positionWS, _SceneLit > 0.5h ? _Transmission : 0.0h, 1.0h, GetNormalizedScreenSpaceUV(input.positionCS));
                else
                {
                    half ndl = saturate(dot(n, _UILightDir.xyz));
                    half3 ambient = n.y >= 0 ? lerp(_UIAmbientEquator.rgb, _UIAmbientSky.rgb, n.y) : lerp(_UIAmbientEquator.rgb, _UIAmbientGround.rgb, -n.y);
                    color = input.color.rgb * (_UILightColor.rgb * ndl + ambient);
                }
                // A slanted band of light: brightest where (x + y/2) passes _SweepX.
                float d = abs(input.positionWS.x + input.positionWS.y * 0.5 - _SweepX);
                color += _SweepStrength * saturate(1.0 - d / _SweepWidth) * step(d, _SweepWidth);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
}
