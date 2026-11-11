// Ray tracing for the realistic look (Docs/DESIGN.md §4, "Ray tracing"; SceneLighting.RayTracing.cs): fills the 3D
// block grid the rays are traced through. Every mesh is drawn three times, flattened along x, y and z in turn, at twice
// the grid's resolution; each fragment writes its surface into the grid cell it falls in: the colour (linear) and which
// way the surface faces (one of 26 directions, packed in alpha; alpha 0 = empty). Drawn from all three sides, every
// surface lands in the grid whichever way it faces, as a closed one-cell skin no ray can slip through.
Shader "Hidden/FruitGame/Voxelize"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_VoxBaseMap);
            SAMPLER(sampler_VoxBaseMap);
            float4 _VoxBaseMap_ST;
            float4 _VoxColor;
            float4 _VoxMin;   // xyz: the grid's lowest corner, w: cell size (m)
            float4 _VoxDims;  // xyz: cells, w: the axis it's flattened along (0 x, 1 y, 2 z)
            RWTexture3D<float4> _VoxWrite : register(u1);
            RWTexture3D<uint> _BrickWrite : register(u2); // marks the 4×4×4-cell brick as drawn into (2), see RayTrace.compute

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ws : TEXCOORD0; float3 n : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.ws = TransformObjectToWorld(input.positionOS.xyz);
                o.n = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv * _VoxBaseMap_ST.xy + _VoxBaseMap_ST.zw;
                // Flattened along one axis: the other two span the viewport. Which way up doesn't matter, since the
                // fragment finds its cell from its world position.
                float3 g = (o.ws - _VoxMin.xyz) / (_VoxDims.xyz * _VoxMin.w);
                float2 p = _VoxDims.w < 0.5 ? g.yz : (_VoxDims.w < 1.5 ? g.xz : g.xy);
                o.positionCS = float4(p * 2 - 1, 0.5, 1);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                int3 v = (int3)floor((input.ws - _VoxMin.xyz) / _VoxMin.w);
                if (any(v < 0) || any(v >= (int3)_VoxDims.xyz)) return 0;
                float3 albedo = SAMPLE_TEXTURE2D_LOD(_VoxBaseMap, sampler_VoxBaseMap, input.uv, 0).rgb * _VoxColor.rgb;
                float3 n = normalize(input.n);
                int3 q = (int3)(sign(n) * (abs(n) > 0.38));
                int dir = (q.x + 1) * 9 + (q.y + 1) * 3 + (q.z + 1);
                _VoxWrite[v] = float4(albedo, (dir + 1) / 255.0);
                _BrickWrite[(uint3)v / 4] = 2;
                return 0;
            }
            ENDHLSL
        }
    }
}
