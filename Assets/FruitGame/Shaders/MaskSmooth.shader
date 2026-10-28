// Smooths the person mask for the cut-out: a small 3×3 blur of the new mask (soft edges, no stair-steps),
// blended with the previous smoothed mask (temporal lerp, no flicker). Used with Graphics.Blit.
Shader "Hidden/FruitGame/Mask Smooth"
{
    Properties
    {
        _MainTex("Current Mask", 2D) = "black" {}
        _PrevTex("Previous Smoothed Mask", 2D) = "black" {}
        _Blend("Weight Of New Mask", Range(0, 1)) = 0.6
        _Spread("Blur Spread (texels)", Range(0, 4)) = 1.5
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            sampler2D _PrevTex;
            float _Blend;
            float _Spread;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 d = _MainTex_TexelSize.xy * _Spread;
                float m = 0;
                for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                        m += tex2D(_MainTex, i.uv + float2(x, y) * d).r;
                m /= 9.0;
                float prev = tex2D(_PrevTex, i.uv).r;
                float v = lerp(prev, m, _Blend);
                return fixed4(v, v, v, 1);
            }
            ENDCG
        }
    }
}
