// Live falling fruit: simple lit (main light + shadows + ambient) plus a fresnel rim highlight,
// so the fruit in play pops against the hazed background pile (Docs/DESIGN.md §15).
Shader "FruitGame/Fruit Rim"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _RimColor("Rim Color", Color) = (1, 0.98, 0.92, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength("Rim Strength", Range(0, 2)) = 0.7
        _CookieStrength("Sun Dapple", Range(0, 1)) = 1
        _SkyScale("Sky light and reflections (realistic look)", Range(0, 1)) = 1
        _RealisticRim("Rim in the realistic look (regular fruit far less than golden ones)", Range(0, 1)) = 1
        _GlossScale("Gloss (realistic look; regular fruit are less glossy than golden ones)", Range(0, 1)) = 1
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
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "VoxelLight.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half _CookieStrength;
                half _GlossScale;
                half _SkyScale;
                half _RealisticRim;
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
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float3 n = normalize(input.normalWS);

                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half ndl = saturate(dot(n, light.direction));
                half cookie = VoxelCookie(input.positionWS, _CookieStrength);
                half3 diffuse = light.color * (ndl * light.shadowAttenuation * light.distanceAttenuation * cookie);
                // Backlit: glossy like the scenery (VoxelLight.hlsl); classic: flat-lit.
                half3 color = _VoxelBacklight > 0.5h ? VoxelLightGloss(albedo.rgb, n, input.positionWS, 0.0h, _CookieStrength, GetNormalizedScreenSpaceUV(input.positionCS), _GlossScale, _SkyScale)
                                                     : albedo.rgb * (diffuse + SampleSH(n));

                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower) * _RimStrength;
                // Backlit: the rim takes the sun's warm colour and is strongest on the side facing the sun, so the
                // fruit catch the light from behind; classic: the cream rim all round.
                half sunSide = saturate(dot(n, light.direction) + 0.35h);
                half3 rimColor = lerp(_RimColor.rgb, light.color * 1.15h, _VoxelBacklight);
                rim *= lerp(1.0h, sunSide * 1.6h * _RealisticRim, _VoxelBacklight);
                color += rimColor * rim;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Shadows and depth come from URP Lit, so the fruit still casts shadows and shows up in the
        // depth texture that depth of field uses.
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
}
