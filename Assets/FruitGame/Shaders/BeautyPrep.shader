// Pre-passes for the photo-booth beauty filter, ported from GPUPixel (Apache-2.0, see Art/Beauty/NOTICE.txt):
// a wide box blur of the webcam frame ("mean") and a box-blurred squared high-pass ("variance"). Everything is
// stored in gamma space (as GPUPixel works) in linear-format targets. Used with Graphics.Blit.
//   Pass 0  ToGammaBlurH : gamma(src), horizontal box blur
//   Pass 1  BlurH        : horizontal box blur (already gamma)
//   Pass 2  BlurV        : vertical box blur
//   Pass 3  Difference   : min(((gamma(src) - mean) * _Delta)^2, 1)
Shader "Hidden/FruitGame/Beauty Prep"
{
    Properties
    {
        _MainTex("Source", 2D) = "black" {}
        _MeanTex("Mean", 2D) = "black" {}
        _Spacing("Tap Spacing (texels)", Float) = 2
        _Delta("High-pass Delta", Float) = 14
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        sampler2D _MeanTex;
        float _Spacing;
        float _Delta;

        // 9-tap box blur (radius 4) along dir, as GPUPixel's BoxBlur with radius 4.
        float3 Box(float2 uv, float2 dir, bool toGamma)
        {
            float3 sum = 0;
            [unroll] for (int i = -4; i <= 4; i++)
            {
                float3 c = tex2D(_MainTex, uv + dir * _MainTex_TexelSize.xy * _Spacing * i).rgb;
                sum += toGamma ? LinearToGammaSpace(c) : c;
            }
            return sum / 9.0;
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target { return float4(Box(i.uv, float2(1, 0), true), 1); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target { return float4(Box(i.uv, float2(1, 0), false), 1); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target { return float4(Box(i.uv, float2(0, 1), false), 1); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img i) : SV_Target
            {
                float3 c = LinearToGammaSpace(tex2D(_MainTex, i.uv).rgb);
                float3 m = tex2D(_MeanTex, i.uv).rgb;
                float3 d = (c - m) * _Delta;
                return float4(min(d * d, 1.0), 1);
            }
            ENDCG
        }
    }
}
