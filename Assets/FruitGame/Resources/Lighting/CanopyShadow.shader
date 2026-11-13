// The realistic look's hidden leaf canopy (SceneLighting, Docs/DESIGN.md §4): it only casts the sun's shadow. Its leaves
// let `_Transmit` of the sun through (staff panel: light through leaves) by leaving holes in the shadow map, which the
// soft-shadow filter blurs into an even part-shade. The holes are an ordered 4 × 4 (Bayer) pattern, the finest even
// spread a shadow map allows: every 4 × 4 block of texels has the same share open, so the filter leaves almost no
// pattern (random noise left clumps that showed as grain on the background trees). They are placed by shadow-map
// texel, the same for all three leaf layers, so the share that gets through is `_Transmit` however many layers a ray
// crosses. Only the canopy does this: the crates', barrels' and trees' own shadows stay solid.
Shader "FruitGame/Canopy Shadow"
{
    Properties
    {
        _Transmit("Light Through Leaves", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            CBUFFER_START(UnityPerMaterial)
                half _Transmit;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // A hole wherever this texel's threshold falls under the share let through.
                static const float Bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                uint2 p = (uint2)input.positionCS.xy & 3u;
                clip((Bayer[p.y * 4 + p.x] + 0.5) / 16.0 - _Transmit);
                return 0;
            }
            ENDHLSL
        }
    }
}
