// TextMeshPro UI text for a crisp pixel font (Docs/DESIGN.md §11). The atlas is a point-filtered raster atlas sampled
// at the font's native 16 px, so one atlas texel is one glyph pixel at any font size that's a multiple of 16.
// The outline and drop shadow are built from whole glyph pixels: the glyph is dilated by _OutlineSize texels for the
// outline, and the outlined shape is shifted by _ShadowOffset texels for the shadow. TMP pads each glyph quad by
// _Padding + 1 texels, so keep _Padding >= _OutlineSize + max(|_ShadowOffset|) and the atlas padding above that.
Shader "FruitGame/Pixel Text"
{
    Properties
    {
        [PerRendererData] _MainTex("Font Atlas", 2D) = "white" {}
        _FaceColor("Face Color", Color) = (1, 1, 1, 1)
        _OutlineColor("Outline Color", Color) = (0.76, 0.094, 0.357, 1)
        [IntRange] _OutlineSize("Outline (glyph px)", Range(0, 2)) = 0
        _ShadowColor("Shadow Color", Color) = (0.35, 0.04, 0.18, 0)
        _ShadowOffset("Shadow Offset (glyph px)", Vector) = (1, -1, 0, 0)
        _Padding("Padding", Float) = 4

        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        _ClipRect("Clip Rect", Vector) = (-32767, -32767, 32767, 32767)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Lighting Off
        Cull Off
        ZTest [unity_GUIZTestMode]
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            fixed4 _FaceColor;
            fixed4 _OutlineColor;
            float _OutlineSize;
            fixed4 _ShadowColor;
            float4 _ShadowOffset;
            float4 _ClipRect;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord0 : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.texcoord0;
                return o;
            }

            // Glyph coverage at uv, grown by r whole texels (a square kernel, so outlines keep pixel-art corners).
            float Dilate(float2 uv, int r)
            {
                float a = 0;
                for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    if (abs(x) > r || abs(y) > r) continue;
                    a = max(a, tex2D(_MainTex, uv + float2(x, y) * _MainTex_TexelSize.xy).a);
                }
                return a;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                int r = (int)round(_OutlineSize);
                float face = tex2D(_MainTex, i.uv).a;
                float outline = r > 0 ? Dilate(i.uv, r) : face;
                float shadow = _ShadowColor.a > 0 ? Dilate(i.uv - _ShadowOffset.xy * _MainTex_TexelSize.xy, r) * _ShadowColor.a : 0;

                // Layer shadow, then outline, then face ("over" in straight alpha).
                float3 rgb = _ShadowColor.rgb;
                float a = shadow;
                float ol = outline * _OutlineColor.a;
                rgb = lerp(rgb, _OutlineColor.rgb, ol / max(ol + a * (1 - ol), 1e-4) * step(1e-4, ol));
                a = ol + a * (1 - ol);
                fixed4 faceColor = _FaceColor * fixed4(i.color.rgb, 1);
                float fa = face * faceColor.a;
                rgb = lerp(rgb, faceColor.rgb, fa / max(fa + a * (1 - fa), 1e-4) * step(1e-4, fa));
                a = fa + a * (1 - fa);

                // Vertex alpha fades the whole text, outline and shadow included.
                fixed4 c = fixed4(rgb, a * i.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
