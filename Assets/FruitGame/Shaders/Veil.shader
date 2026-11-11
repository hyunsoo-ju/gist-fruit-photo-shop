// The dimmer behind the physical UI (PhysicalUI): a flat, see-through colour drawn after the scene, just behind the
// signboards, so a menu dims the game without dimming its own signs and buttons. Drawn after the sun beams, so they
// don't light the dimmed scene back up.
Shader "Hidden/FruitGame/Veil"
{
    Properties { _Color("Colour", Color) = (0, 0, 0, 0.5) }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+200" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            float4 vert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 frag() : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
