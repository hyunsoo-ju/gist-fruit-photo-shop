// Blur passes for the bokeh backdrop (used with Graphics.Blit, Docs/DESIGN.md §9).
// Pass 0 (Prep): webcam colour weighted by "not person" → rgb·w, a = w. Blurring this premultiplied pair and
//                dividing by alpha afterwards averages only room pixels, so the player doesn't smear into the bokeh.
// Pass 1 (Kawase): one Kawase blur step at offset _Offset texels; repeat with growing offsets for a wide blur.
Shader "Hidden/FruitGame/Bokeh Blur"
{
    Properties
    {
        _MainTex("Source", 2D) = "black" {}
        _MaskTex("Person Mask", 2D) = "black" {}
        _UseMask("Use Mask", Float) = 1
        _Offset("Offset", Float) = 1
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "Prep"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _MaskTex;
            float _UseMask;

            float4 frag(v2f_img i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                // Treat the mask's soft edge as person too, so no outline of the player bleeds into the blur.
                float person = smoothstep(0.15, 0.45, tex2D(_MaskTex, i.uv).r) * _UseMask;
                float w = 1.0 - person;
                return float4(c * w, w);
            }
            ENDCG
        }

        Pass
        {
            Name "Kawase"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Offset;

            float4 frag(v2f_img i) : SV_Target
            {
                float2 o = _MainTex_TexelSize.xy * (_Offset + 0.5);
                float4 s = tex2D(_MainTex, i.uv + float2( o.x,  o.y));
                s += tex2D(_MainTex, i.uv + float2(-o.x,  o.y));
                s += tex2D(_MainTex, i.uv + float2( o.x, -o.y));
                s += tex2D(_MainTex, i.uv + float2(-o.x, -o.y));
                return s * 0.25;
            }
            ENDCG
        }
    }
}
