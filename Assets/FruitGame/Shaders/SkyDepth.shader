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
            // Keeps the smallest distance whatever the drawing order (the same as a depth test, nearest the sky wins), so
            // SceneLighting can lay the cached still scenery down first and draw only what moves over it each frame.
            ZWrite Off
            ZTest Always
            Cull Off
            BlendOp Min
            Blend One One

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
