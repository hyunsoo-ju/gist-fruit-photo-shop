// Sun's-eye view of the scenery for the bounce light (SceneLighting, BounceVolume.compute): drawn by a command buffer
// from the sun's direction, it stores for each texel the sunlight the surface sends back out (its colour times the
// sun's angle on it), its position and its normal. Whatever the sun sees first is sunlit; the canopy is drawn black,
// so the leaves above block it.
Shader "Hidden/FruitGame/Sun Flux"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FluxBaseMap);
            SAMPLER(sampler_FluxBaseMap);
            float4 _FluxBaseMap_ST;
            float4 _FluxColor;
            float4 _FluxSunDir; // xyz: toward the sun

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };
            struct Targets { float4 flux : SV_Target0; float4 position : SV_Target1; float4 normal : SV_Target2; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.ws);
                o.n = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv * _FluxBaseMap_ST.xy + _FluxBaseMap_ST.zw;
                return o;
            }

            Targets frag(Varyings input, bool front : SV_IsFrontFace)
            {
                float3 n = normalize(input.n) * (front ? 1 : -1);
                float3 albedo = SAMPLE_TEXTURE2D_LOD(_FluxBaseMap, sampler_FluxBaseMap, input.uv, 0).rgb * _FluxColor.rgb;
                Targets t;
                t.flux = float4(albedo * saturate(dot(n, _FluxSunDir.xyz)), 1);
                t.position = float4(input.ws, 1);
                t.normal = float4(n, 1);
                return t;
            }
            ENDHLSL
        }
    }
}
