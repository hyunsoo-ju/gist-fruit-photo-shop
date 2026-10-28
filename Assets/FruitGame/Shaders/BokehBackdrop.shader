// Displays the blurred room on the L5 backdrop quad (Docs/DESIGN.md §9): un-premultiplies the blur (rgb / a), then a soft
// grade (a little desaturation, brightening and a tint toward the scene's haze blue). Shows a plain fallback colour
// until the camera is live.
Shader "FruitGame/Bokeh Backdrop"
{
    Properties
    {
        _MainTex("Blurred (premultiplied)", 2D) = "black" {}
        _Live("Live", Float) = 0
        _FallbackColor("Fallback Color", Color) = (0.725, 0.827, 0.867, 1)
        _Desaturate("Desaturate", Range(0, 1)) = 0.15
        _Brighten("Brighten", Range(0.5, 2)) = 1.08
        _Tint("Tint", Color) = (0.663, 0.776, 0.867, 1)
        _TintAmount("Tint Amount", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Live;
                half4 _FallbackColor;
                half _Desaturate;
                half _Brighten;
                half4 _Tint;
                half _TintAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw; // the illustration backdrop is cropped to 3:4
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (_Live < 0.5h) return half4(_FallbackColor.rgb, 1);
                half4 s = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                // Where everything nearby was person (alpha ~0), fall back to the haze colour.
                half3 rgb = s.a > 0.02h ? s.rgb / s.a : _FallbackColor.rgb;
                half luma = dot(rgb, half3(0.299h, 0.587h, 0.114h));
                rgb = lerp(rgb, luma.xxx, _Desaturate) * _Brighten;
                rgb = lerp(rgb, _Tint.rgb, _TintAmount);
                return half4(saturate(rgb), 1);
            }
            ENDHLSL
        }
    }
}
