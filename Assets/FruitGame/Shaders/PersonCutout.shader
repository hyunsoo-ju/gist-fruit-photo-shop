// The player cut out of the webcam image (L3): colour × soft mask edge. It draws after the regular opaque
// scene (queue Geometry+500) so it blends over the pile and backdrop, and it writes depth only where the person
// is, so depth of field keeps the player sharp while the layers behind stay soft (Docs/DESIGN.md §9).
// With _Beauty > 0 (photo booth, Docs/DESIGN.md §10) it also applies a SNOW/SODA-style beauty filter to the player only,
// ported from GPUPixel (Apache-2.0, see Art/Beauty/NOTICE.txt): guided skin smoothing, sharpening, LUT-based
// whitening, plus cheek blush and lip tint placed from BlazeFace's face points.
Shader "FruitGame/Person Cutout"
{
    Properties
    {
        _MainTex("Webcam", 2D) = "black" {}
        _MaskTex("Mask", 2D) = "black" {}
        _EdgeLow("Edge Low", Range(0, 1)) = 0.35
        _EdgeHigh("Edge High", Range(0, 1)) = 0.65
        _DepthCutoff("Depth Cutoff", Range(0, 1)) = 0.5
        _OutlineWidth("Outline (px at 1080 wide)", Float) = 0
        _OutlineColor("Outline Color", Color) = (0.118, 0.133, 0.2, 1)

        [Header(Beauty filter ported from GPUPixel)]
        _Beauty("Strength (0 = off)", Range(0, 1)) = 0
        _MeanTex("Mean (gamma)", 2D) = "black" {}
        _VarTex("Variance (gamma)", 2D) = "black" {}
        _LutSkin("LUT Skin (baked)", 2D) = "white" {}
        _LutLight("LUT Skin + Light (baked)", 2D) = "white" {}
        _BlurAlpha("Smoothing", Range(0, 1)) = 0.9
        _Sharpen("Sharpen", Range(0, 1)) = 0.3
        _Whiten("Whitening", Range(0, 1)) = 0.6
        _BlushColor("Blush Color", Color) = (1, 0.62, 0.68, 1)
        _BlushStrength("Blush Strength", Range(0, 1)) = 0.14
        _LipColor("Lip Color", Color) = (0.93, 0.25, 0.42, 1)
        _LipStrength("Lip Strength", Range(0, 1)) = 0.45
        _CheekL("Left Cheek (uv)", Vector) = (0.4, 0.5, 0, 0)
        _CheekR("Right Cheek (uv)", Vector) = (0.6, 0.5, 0, 0)
        _Mouth("Mouth (uv)", Vector) = (0.5, 0.4, 0, 0)
        _CheekRadius("Cheek Radius (u)", Range(0.005, 0.2)) = 0.05
        _MouthRadius("Mouth Radius (u)", Range(0.005, 0.2)) = 0.03

        [Header(Face reshape from the face mesh)]
        _BigEye("Eye Enlarge", Range(0, 1)) = 0
        _EyeA("Eye A (u, v, radius)", Vector) = (0, 0, 0, 0)
        _EyeB("Eye B (u, v, radius)", Vector) = (0, 0, 0, 0)
        _Slim("Face Slim", Range(0, 1)) = 0
        _SlimRadius("Slim Radius (u)", Range(0.005, 0.3)) = 0.05
        _Slim0("Slim 0 (start, pull)", Vector) = (0, 0, 0, 0)
        _Slim1("Slim 1 (start, pull)", Vector) = (0, 0, 0, 0)
        _Slim2("Slim 2 (start, pull)", Vector) = (0, 0, 0, 0)
        _Slim3("Slim 3 (start, pull)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "Geometry+500" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
        TEXTURE2D(_MeanTex); SAMPLER(sampler_MeanTex);
        TEXTURE2D(_VarTex); SAMPLER(sampler_VarTex);
        TEXTURE2D(_LutSkin); SAMPLER(sampler_LutSkin);
        TEXTURE2D(_LutLight); SAMPLER(sampler_LutLight);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half _EdgeLow;
            half _EdgeHigh;
            half _DepthCutoff;
            half _OutlineWidth;
            half4 _OutlineColor;
            float4 _MainTex_TexelSize;
            half _Beauty;
            half _BlurAlpha;
            half _Sharpen;
            half _Whiten;
            half4 _BlushColor;
            half _BlushStrength;
            half4 _LipColor;
            half _LipStrength;
            float4 _CheekL;
            float4 _CheekR;
            float4 _Mouth;
            half _CheekRadius;
            half _MouthRadius;
            half _BigEye;
            float4 _EyeA;
            float4 _EyeB;
            half _Slim;
            float _SlimRadius;
            float4 _Slim0;
            float4 _Slim1;
            float4 _Slim2;
            float4 _Slim3;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        Varyings vert(Attributes input)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            o.uv = input.uv;
            return o;
        }

        half MaskAt(float2 uv) { return SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).r; }

        // Soft skin-tone detector in YCbCr (gamma values), 0 = not skin, 1 = skin. Used to keep makeup off hair.
        half SkinWeight(half3 c)
        {
            half y = dot(c, half3(0.299h, 0.587h, 0.114h));
            half cb = (c.b - y) * 0.564h + 0.5h;
            half cr = (c.r - y) * 0.713h + 0.5h;
            half d = max(abs(cr - 0.60h) / 0.09h, abs(cb - 0.40h) / 0.11h);
            return saturate((1.0h - d) * 3.0h) * smoothstep(0.08h, 0.2h, y);
        }

        // Soft round spot; distances in "u" units (the frame is 3:4, so one v step is 4/3 of a u step).
        half Spot(float2 uv, float2 centre, half radius)
        {
            float2 d = (uv - centre) * float2(1.0, 4.0 / 3.0) / max(radius, 1e-4);
            return exp(-dot(d, d) * 1.5);
        }

        // Standard 8×8-tile 512×512 colour LUT (64 levels), v flipped for Unity.
        half3 Lut64(TEXTURE2D_PARAM(lut, samp), half3 c)
        {
            half blue = c.b * 63.0h;
            float2 q1, q2;
            q1.y = floor(floor(blue) / 8.0); q1.x = floor(blue) - q1.y * 8.0;
            q2.y = floor(ceil(blue) / 8.0);  q2.x = ceil(blue) - q2.y * 8.0;
            float2 p = 0.5 / 512.0 + (1.0 / 8.0 - 1.0 / 512.0) * c.rg;
            float2 p1 = q1 / 8.0 + p, p2 = q2 / 8.0 + p;
            p1.y = 1.0 - p1.y; p2.y = 1.0 - p2.y;
            return lerp(SAMPLE_TEXTURE2D(lut, samp, p1).rgb, SAMPLE_TEXTURE2D(lut, samp, p2).rgb, frac(blue));
        }

        // GPUPixel's beauty face filter + makeup, in gamma space like the original.
        // Distances in "u" units: the frame is 3:4, so one v step is 4/3 of a u step.
        float2 AspectU(float2 d) { return d * float2(1.0, 4.0 / 3.0); }

        // GPUPixel's curve warp: near `start`, sample from further along -pull, so the contour moves by +pull.
        float2 SlimWarp(float2 uv, float4 pt)
        {
            float r = length(AspectU(uv - pt.xy));
            float a = saturate(1.0 - r / max(_SlimRadius, 1e-4));
            return uv - pt.zw * a * a * _Slim;
        }

        // GPUPixel's enlarge-eye: inside the radius, sample closer to the centre (magnifies the eye).
        float2 EyeWarp(float2 uv, float4 eye)
        {
            float2 d = uv - eye.xy;
            float w = length(AspectU(d)) / max(eye.z, 1e-4);
            if (w >= 1.0) return uv;
            float k = saturate(1.0 - _BigEye * 0.35 * (1.0 - w * w));
            return eye.xy + d * k;
        }

        float2 Reshape(float2 uv)
        {
            if (_Slim > 0.001)
            {
                uv = SlimWarp(uv, _Slim0);
                uv = SlimWarp(uv, _Slim1);
                uv = SlimWarp(uv, _Slim2);
                uv = SlimWarp(uv, _Slim3);
            }
            if (_BigEye > 0.001)
            {
                uv = EyeWarp(uv, _EyeA);
                uv = EyeWarp(uv, _EyeB);
            }
            return uv;
        }

        half3 Beautify(float2 uv, half3 linearColor)
        {
            half3 I = LinearToSRGB(linearColor);
            half3 mean = SAMPLE_TEXTURE2D(_MeanTex, sampler_MeanTex, uv).rgb;
            half3 var = SAMPLE_TEXTURE2D(_VarTex, sampler_VarTex, uv).rgb;

            // 1. Guided smoothing: pull toward the local mean where variance is low (skin), keep edges.
            half p = saturate((min(I.r, mean.r - 0.1h) - 0.2h) * 4.0h);
            half meanVar = (var.r + var.g + var.b) / 3.0h;
            half k = saturate((1.0h - meanVar / (meanVar + 0.1h)) * p * _BlurAlpha);
            half3 color = lerp(I, mean, k);

            // 2. Sharpen with a 3×3 high-pass so eyes and brows stay crisp.
            float2 t = _MainTex_TexelSize.xy;
            half3 sum = 0.25h * I;
            sum += 0.125h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-t.x, 0)).rgb);
            sum += 0.125h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(t.x, 0)).rgb);
            sum += 0.125h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -t.y)).rgb);
            sum += 0.125h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, t.y)).rgb);
            sum += 0.0625h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + t).rgb);
            sum += 0.0625h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - t).rgb);
            sum += 0.0625h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-t.x, t.y)).rgb);
            sum += 0.0625h * LinearToSRGB(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(t.x, -t.y)).rgb);
            color += _Sharpen * (I - sum) * 2.0h;
            color = saturate(color);

            // 3. Whitening: GPUPixel's LUT chain (gray curve → origin → skin, then "light") baked offline into two
            //    standard 64-level LUTs (Art/Beauty/beauty_skin_lut.png, beauty_light_lut.png).
            if (_Whiten > 0.0h)
            {
                half3 skinned = Lut64(TEXTURE2D_ARGS(_LutSkin, sampler_LutSkin), color);
                half3 light = Lut64(TEXTURE2D_ARGS(_LutLight, sampler_LutLight), color);
                // Fade out on very dark pixels (hair, brows): the "light" LUT tints near-black slightly red.
                half lum = dot(color, half3(0.299h, 0.587h, 0.114h));
                color = lerp(color, lerp(skinned, light, _Whiten), _Whiten * smoothstep(0.1h, 0.3h, lum));
            }

            // 4. Makeup: rosy cheeks and tinted lips (soft-light style), only on skin / lip colours.
            half skin = SkinWeight(I);
            half blush = saturate(Spot(uv, _CheekL.xy, _CheekRadius) + Spot(uv, _CheekR.xy, _CheekRadius)) * skin * _BlushStrength;
            color = lerp(color, color * lerp(1.0h, _BlushColor.rgb * 1.15h, 1.0h), blush);
            half y = dot(I, half3(0.299h, 0.587h, 0.114h));
            half redness = saturate(((I.r - y) * 0.713h + 0.5h - 0.56h) * 8.0h);
            half lips = Spot(uv, _Mouth.xy, _MouthRadius) * redness * _LipStrength;
            color = lerp(color, color * _LipColor.rgb * 1.35h, lips);

            return SRGBToLinear(lerp(I, saturate(color), _Beauty));
        }
        ENDHLSL

        Pass
        {
            Name "Cutout"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                // Face reshape (photo booth only) warps where everything is sampled, so the person's outline
                // follows the slimmer jaw too.
                float2 uv = _Beauty > 0.001h ? Reshape(input.uv) : input.uv;
                half a = smoothstep(_EdgeLow, _EdgeHigh, MaskAt(uv));

                // Pixel-style outline: a hard ring _OutlineWidth px wide around the matte (16 taps on two rings).
                // The soft matte edge blends into the outline colour rather than the scene behind, so there's no
                // light fringe between the player and the outline.
                half ring = 0;
                if (_OutlineWidth > 0.0h)
                {
                    float2 r = _OutlineWidth * float2(1.0 / 1080.0, 1.0 / 1440.0);
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float ang = k * 0.785398;
                        float2 dir = float2(cos(ang), sin(ang));
                        // Nothing outside the frame counts, so no outline is drawn along the image edges.
                        float2 q1 = uv + dir * r, q2 = uv + dir * r * 0.5;
                        ring = max(ring, all(q1 == saturate(q1)) ? MaskAt(q1) : 0);
                        ring = max(ring, all(q2 == saturate(q2)) ? MaskAt(q2) : 0);
                    }
                    ring = step(_EdgeLow + (_EdgeHigh - _EdgeLow) * 0.5h, ring);
                }
                half cover = max(a, ring);
                clip(cover - 0.02h);   // write no depth where there's no person
                half3 rgb = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                if (_Beauty > 0.001h) rgb = Beautify(uv, rgb);
                if (ring > 0.0h) rgb = lerp(_OutlineColor.rgb, rgb, smoothstep(0.35h, 1.0h, a));
                return half4(rgb, cover);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            half fragDepth(Varyings input) : SV_Target
            {
                clip(MaskAt(input.uv) - _DepthCutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
