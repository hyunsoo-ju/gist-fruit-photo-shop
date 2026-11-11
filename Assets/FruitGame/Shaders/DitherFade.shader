// Distant layers in the pixel style (Docs/DESIGN.md §4): simple lit, faded smoothly toward a haze colour.
// (_Levels and _DitherPixel are left over from an ordered dither, now unused.)
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
        _Wind("Wind (metres of sway per metre of height, 0 = still)", Float) = 0
        _WindSpeed("Wind Speed", Float) = 1.6
        _Transmission("Light Through (leaves, grass, petals; 0 = opaque)", Range(0, 2)) = 0
        _CookieStrength("Sun Dapple (0 for clouds)", Range(0, 1)) = 1
        _SelfLit("Self-lit (clouds: keep their own colour whatever the light)", Range(0, 1)) = 0
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
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "VoxelLight.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _FadeColor;
                half _Fade;
                half _Levels;
                float _DitherPixel;
                float _Wind;
                float _WindSpeed;
                half _Transmission;
                half _CookieStrength;
                half _SelfLit;
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
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                if (_Wind > 0)
                {
                    // Sways more the higher above the plant's foot, in a slow wave across the scene with a quicker flutter.
                    float h = max(0, ws.y - GetObjectToWorldMatrix()._m13);
                    float t = _Time.y * _WindSpeed;
                    ws.x += _Wind * h * (sin(t + ws.x * 0.7 + ws.z * 1.1) + 0.5 * sin(t * 2.3 + ws.x * 2.3 + ws.y * 1.7));
                }
                o.positionCS = TransformWorldToHClip(ws);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.positionWS = ws;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Mip 0 only: the atlases are palettes of small swatches, and lower mips blend neighbouring swatches.
                half4 albedo = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv, 0) * _BaseColor;
                float3 n = normalize(input.normalWS);
                half3 color = VoxelLight(albedo.rgb, n, input.positionWS, _Transmission, _CookieStrength, GetNormalizedScreenSpaceUV(input.positionCS));
                // Clouds keep their own colour in either lighting style (a top face slightly brighter, for shape).
                color = lerp(color, albedo.rgb * (1.2h + 0.15h * saturate(n.y)), _SelfLit);

                // Backlit: less haze, so the distance keeps its light and shade. A smooth fade: the ordered dither
                // that used to step it left a fine grid over the distant trees.
                float f = _Fade * (_VoxelFadeScale > 0 ? _VoxelFadeScale : 1);
                color = lerp(color, _FadeColor.rgb, saturate(f));
                color = MixFog(color, input.fogFactor); // realistic look only (fog is off in the classic)
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
}
