// Shared lighting for the voxel scenery (Docs/DESIGN.md §4, "Lighting"). Include after URP's Lighting.hlsl.
//
// Classic style: flat-lit blocks (main light with shadows, plus ambient), as the game always looked.
// Backlit style (SceneLighting sets _VoxelBacklight): physically based, like sunlit plastic bricks. URP's own PBR
// (UniversalFragmentPBR: GGX highlights, Fresnel, reflections of the sky cubemap SceneLighting provides, the sun's
// shadows and its dapple cookie) at _VoxelSmoothness, plus light through leaves: when the sun is behind a leaf, grass
// or petal block, the side facing the camera glows warm yellow-green. `transmission` per material (0 = opaque).
#ifndef FRUITGAME_VOXEL_LIGHT_INCLUDED
#define FRUITGAME_VOXEL_LIGHT_INCLUDED

// Globals set by SceneLighting (all zero in the classic style, or before it runs).
half _VoxelBacklight;      // 0 classic, 1 backlit
half _VoxelFadeScale;      // backlit: how much of the distance fade toward the sky colour is kept (0 when unset = all)
half _VoxelSmoothness;     // backlit: how glossy the blocks are (plastic bricks)
half4 _VoxelGlowTint;      // colour the glow through leaves takes on
float _VoxelGlowDepth;     // metres toward the sun at which the glow checks for shadow

half VoxelCookie(float3 positionWS, half cookieStrength)
{
#if defined(_LIGHT_COOKIES)
    return lerp(1.0h, (half)SampleMainLightCookie(positionWS).r, cookieStrength);
#else
    return 1.0h;
#endif
}

half3 VoxelGlow(half3 albedo, float3 n, float3 positionWS, half transmission, half cookieStrength)
{
    Light light = GetMainLight();
    // The face toward the camera is shadowed by its own block, so look for shadow a way toward the sun (about a
    // bush's depth); foliage is solid blocks, so other leaves only dim the glow, never kill it.
    float3 probe = positionWS + light.direction * _VoxelGlowDepth;
    half through = lerp(0.3h, 1.0h, GetMainLight(TransformWorldToShadowCoord(probe)).shadowAttenuation) * VoxelCookie(probe, cookieStrength);
    float3 v = normalize(GetWorldSpaceViewDir(positionWS));
    half back = saturate(dot(-light.direction, v));
    half ndl = saturate(dot(n, light.direction));
    half t = back * back * transmission * through * (1.0h - 0.6h * ndl);
    t = floor(t * 4.0h + 0.5h) * 0.25h; // a few flat steps, like the blocks
    return albedo * _VoxelGlowTint.rgb * light.color * t;
}

half3 VoxelLight(half3 albedo, float3 n, float3 positionWS, half transmission, half cookieStrength, float2 screenUV)
{
    if (_VoxelBacklight < 0.5h)
    {
        Light light = GetMainLight(TransformWorldToShadowCoord(positionWS));
        half ndl = saturate(dot(n, light.direction));
        return albedo * (light.color * (ndl * light.shadowAttenuation * VoxelCookie(positionWS, cookieStrength)) + SampleSH(n));
    }

    InputData input = (InputData)0;
    input.positionWS = positionWS;
    input.normalWS = n;
    input.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    input.shadowCoord = TransformWorldToShadowCoord(positionWS);
    input.bakedGI = SampleSH(n);
    input.shadowMask = half4(1, 1, 1, 1);
    input.normalizedScreenSpaceUV = screenUV; // screen-space ambient occlusion

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.metallic = 0;
    surface.specular = half3(0, 0, 0);
    // Leaves, grass and petals are a little less glossy than wood and fruit, so their colour survives the sky's sheen.
    surface.smoothness = transmission > 0.0h ? _VoxelSmoothness * 0.7h : _VoxelSmoothness;
    surface.occlusion = 1;
    surface.alpha = 1;

    half3 color = UniversalFragmentPBR(input, surface).rgb;
    if (transmission > 0.0h) color += VoxelGlow(albedo, n, positionWS, transmission, cookieStrength);
    return color;
}

#endif
