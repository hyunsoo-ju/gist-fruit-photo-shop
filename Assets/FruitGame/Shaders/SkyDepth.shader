// Sky shadow map for the realistic look (SceneLighting, VoxelLight.hlsl): drawn by a command buffer from the sky's
// direction, it stores each surface's distance along that direction (0 nearest the sky, 1 at the far end of the range),
// so a point further than the stored value is under something and sees less of the sky.
Shader "Hidden/FruitGame/Sky Depth"
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

            float4 _SkyOrigin;   // xyz: the sky camera's position
            float4 _SkyForward;  // xyz: its forward direction, w: 1 / depth range

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float t : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.t = dot(ws - _SkyOrigin.xyz, _SkyForward.xyz) * _SkyForward.w;
                return o;
            }

            float4 frag(Varyings input) : SV_Target { return input.t; }
            ENDHLSL
        }
    }
}
