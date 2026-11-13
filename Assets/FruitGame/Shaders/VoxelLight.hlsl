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
half4 _VoxelUnderLight;    // realistic: cool light reflected up from below (rgb x intensity; 0 = off)
half4 _VoxelHazeColor;     // realistic: the colour distance fades toward (a = 1 to use it instead of each material's)

// Realistic look: sky shadows. SceneLighting draws each surface's distance along the sky's direction into
// _SkyOcclusionMap (Hidden/FruitGame/Sky Depth); a point further along than what's stored there is under something,
// so the inside of a tree, the gaps between crates and the undersides of things get little of the sky's (diffuse)
// light while open tops get all of it.
TEXTURE2D_FLOAT(_SkyOcclusionMap);
SAMPLER(sampler_SkyOcclusionMap);
float4x4 _SkyOcclusionMatrix;   // world -> the sky view's clip space
float4 _SkyOrigin;              // xyz: the sky view's position
float4 _SkyForward;             // xyz: its direction, w: 1 / depth range
float4 _SkyShadowShape;         // x: how much darker partly covered places get (power, 1 = linear), y/z: from/to (m from
                                // the camera) both ease off, so the distant scenery keeps its clarity, w: how much of the
                                // fill lights (bounce, underlight) fully covered places keep
float4 _SkyOcclusionParams;     // x: on, y: blur radius (uv), z: bias (in stored units), w: darkest it gets

static const float2 SkyTaps[12] =
{
    float2(0.0, 0.0), float2(0.53, 0.21), float2(-0.37, 0.48), float2(-0.52, -0.33), float2(0.27, -0.61),
    float2(0.84, -0.18), float2(0.12, 0.92), float2(-0.88, 0.14), float2(-0.29, -0.93), float2(0.66, 0.64),
    float2(-0.64, 0.71), float2(0.91, 0.38)
};

// Realistic look: bounce light. SceneLighting fills a coarse grid over the scene (BounceVolume.compute) with the
// sunlight bounced off everything the sun reaches, from six directions per cell; a surface reads it smoothly at its
// position and normal and adds it, tinted by its own colour. One bounce, world space, no noise.
TEXTURE3D(_BounceVolume);
SAMPLER(sampler_BounceVolume);
TEXTURE3D(_BounceVolumeNext); // the newer grid, faded in over _BounceBlend
half _BounceBlend;
float4 _BounceVolumeMin;    // xyz: the grid's lowest corner, w: 1 / cell size
float4 _BounceVolumeDims;   // xyz: cells, w: strength (0 = off)

// Realistic look: ray traced reflections (SceneLighting.RayTracing.cs, RayTrace.compute). Traced before the scene is
// drawn, one mirror ray per pixel through a 3D grid of the scene's blocks, then blurred along each surface and blended
// over frames: rgb = what the ray saw, lit, times a = how much it counts (0 where it hit nothing, so the reflection
// probe stays). Premultiplied, so it filters cleanly. _RtReflectionParams.x is 1 only while the main camera draws the scene.
TEXTURE2D(_RtReflections);
float4 _RtReflectionParams;

// Swaps the probe's reflection for the traced one where the ray found something, through the same specular term URP
// gives the probe (Fresnel and roughness), so it's exactly as strong as the reflection it replaces.
half3 RayTracedReflection(InputData input, SurfaceData surface, half skyScale)
{
    if (_RtReflectionParams.x < 0.5) return 0;
    half4 rt = SAMPLE_TEXTURE2D_LOD(_RtReflections, sampler_LinearClamp, input.normalizedScreenSpaceUV, 0);
    // Only glossy surfaces take it: on rough ones (regular fruit, 0.12 of the gloss) a sharp reflection is wrong, and the
    // probe's blurred one stays.
    rt *= smoothstep(0.45h, 0.7h, surface.smoothness);
    if (rt.a <= 0.001h) return 0;
    BRDFData brdf;
    half alpha = 1;
    InitializeBRDFData(surface.albedo, surface.metallic, surface.specular, surface.smoothness, alpha, brdf);
    half3 r = reflect(-input.viewDirectionWS, input.normalWS);
    half fresnel = Pow4(1.0h - saturate(dot(input.normalWS, input.viewDirectionWS)));
    half3 probe = GlossyEnvironmentReflection(r, input.positionWS, brdf.perceptualRoughness, 1.0h, input.normalizedScreenSpaceUV);
    return EnvironmentBRDFSpecular(brdf, fresnel) * (rt.rgb * skyScale - probe * surface.occlusion * rt.a);
}

half3 BounceLight(float3 positionWS, float3 n)
{
    if (_BounceVolumeDims.w <= 0) return 0;
    float3 c = (positionWS + n * 0.25 - _BounceVolumeMin.xyz) * _BounceVolumeMin.w; // in cells, nudged off the surface
    if (any(c < 0) || any(c > _BounceVolumeDims.xyz)) return 0;
    float D = _BounceVolumeDims.z;
    float z = clamp(c.z, 0.5, D - 0.5);
    float3 uvw = float3(c.xy / _BounceVolumeDims.xy, 0);
    float3 n2 = n * n;
    half3 e = 0;
    // The three faces of the ambient cube the normal leans toward, weighted by n² (they add up to 1), from the old
    // grid and the new one, crossfaded.
    float3 face = float3(n.x >= 0 ? 0 : 1, n.y >= 0 ? 2 : 3, n.z >= 0 ? 4 : 5);
    [unroll] for (int a = 0; a < 3; a++)
    {
        uvw.z = (z + face[a] * D) / (6 * D);
        half3 from = SAMPLE_TEXTURE3D_LOD(_BounceVolume, sampler_BounceVolume, uvw, 0).rgb;
        half3 to = SAMPLE_TEXTURE3D_LOD(_BounceVolumeNext, sampler_BounceVolume, uvw, 0).rgb;
        e += n2[a] * lerp(from, to, _BounceBlend);
    }
    return e * _BounceVolumeDims.w;
}

/// How much of the sky a point sees, 0..1 (1 when sky shadows are off).
half SkyVisibility(float3 positionWS, float3 n)
{
    if (_SkyOcclusionParams.x < 0.5) return 1.0h;
    float3 p = positionWS + n * 0.06;
    float4 c = mul(_SkyOcclusionMatrix, float4(p, 1.0));
    float2 uv = c.xy / c.w * 0.5 + 0.5;
    if (any(uv < 0.0) || any(uv > 1.0)) return 1.0h;
    float t = dot(p - _SkyOrigin.xyz, _SkyForward.xyz) * _SkyForward.w;
    half seen = 0;
    [unroll] for (int i = 0; i < 12; i++)
    {
        float d = SAMPLE_TEXTURE2D_LOD(_SkyOcclusionMap, sampler_SkyOcclusionMap, uv + SkyTaps[i] * _SkyOcclusionParams.y, 0).r;
        seen += t <= d + _SkyOcclusionParams.z ? 1.0h : 0.0h;
    }
    half v = seen / 12.0h;
    float dist = distance(positionWS, _WorldSpaceCameraPos);
    half power = lerp(max(_SkyShadowShape.x, 1.0), 1.0, saturate((dist - _SkyShadowShape.y) / max(_SkyShadowShape.z - _SkyShadowShape.y, 1e-3)));
    v = pow(v, power);
    return lerp((half)_SkyOcclusionParams.w, 1.0h, v);
}

// Realistic look: the sun's shadow, filtered so the canopy's light-through-leaves holes (CanopyShadow.shader: an
// ordered 4 × 4 pattern in the shadow map) read as an exact, even part-shade. URP's soft-shadow filter left a faint
// ripple of the pattern, which beat against the screen's pixels as moiré. Here each 4 × 4 block of texels (the
// pattern's own grid, from the atlas origin) is averaged exactly, four bilinear comparison taps at its texel corners,
// and the four blocks around the point are blended bilinearly: smooth edges about as soft as the pipeline's, and
// nothing of the pattern left. 16 taps.
half VoxelShadowBlock(float2 block, float z)
{
    float2 corner = block * 4.0 + 1.0;          // texel corners inside the block: +1 and +3
    float2 inv = _MainLightShadowmapSize.xy;
    return (SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(0, 0)) * inv, z))
          + SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(2, 0)) * inv, z))
          + SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(0, 2)) * inv, z))
          + SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3((corner + float2(2, 2)) * inv, z))) * 0.25h;
}

half VoxelSunShadow(float3 positionWS)
{
    #if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        return 1.0h;
    #else
        float4 sc = TransformWorldToShadowCoord(positionWS);
        float2 c = sc.xy * _MainLightShadowmapSize.zw * 0.25 - 0.5; // block coordinates, block centres on integers
        float2 c0 = floor(c);
        float2 f = c - c0;
        half s = lerp(lerp(VoxelShadowBlock(c0, sc.z), VoxelShadowBlock(c0 + float2(1, 0), sc.z), f.x),
                      lerp(VoxelShadowBlock(c0 + float2(0, 1), sc.z), VoxelShadowBlock(c0 + float2(1, 1), sc.z), f.x), f.y);
        s = LerpWhiteTo(s, GetMainLightShadowParams().x);
        s = lerp(s, 1.0h, GetMainLightShadowFade(positionWS));
        return BEYOND_SHADOW_FAR(sc) ? 1.0h : s;
    #endif
}

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
    half through = lerp(0.3h, 1.0h, VoxelSunShadow(probe)) * VoxelCookie(probe, cookieStrength);
    float3 v = normalize(GetWorldSpaceViewDir(positionWS));
    half back = saturate(dot(-light.direction, v));
    half ndl = saturate(dot(n, light.direction));
    half t = back * back * transmission * through * (1.0h - 0.6h * ndl);
    t = floor(t * 4.0h + 0.5h) * 0.25h; // a few flat steps, like the blocks
    return albedo * _VoxelGlowTint.rgb * light.color * t;
}

/// `glossScale` scales the blocks' smoothness for one material and `skyScale` how much of the sky's light and
/// reflections it takes (regular fruit get less of both than the rest, so they don't look wet or chalky).
// Recolours a fill light to its own brightness in `balance.rgb`, by `balance.a` (0 = as it is). The physical buttons use
// it to take the sky and the cool underlight at their strength but in the classic UI light's warmer colour.
half3 BalanceFill(half3 light, half4 balance)
{
    return lerp(light, Luminance(light) * balance.rgb, balance.a);
}

half3 VoxelLightBalanced(half3 albedo, float3 n, float3 positionWS, half transmission, half cookieStrength, float2 screenUV, half glossScale, half skyScale, half4 fillBalance)
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
    // Lit without the pipeline's shadow (a coordinate beyond the shadow map reads as lit); the sun's light is taken
    // back below by VoxelSunShadow's own filter.
    input.shadowCoord = float4(0, 0, -1, 1);
    input.bakedGI = BalanceFill(SampleSH(n), fillBalance);
    input.shadowMask = half4(1, 1, 1, 1);
    input.normalizedScreenSpaceUV = screenUV; // screen-space ambient occlusion

    SurfaceData surface = (SurfaceData)0;
    surface.albedo = albedo;
    surface.metallic = 0;
    surface.specular = half3(0, 0, 0);
    // Leaves, grass and petals are a little less glossy than wood and fruit, so their colour survives the sky's sheen.
    surface.smoothness = (transmission > 0.0h ? _VoxelSmoothness * 0.7h : _VoxelSmoothness) * glossScale;
    // Sky shadows darken the sky's light and its reflections (URP applies occlusion to both).
    surface.occlusion = SkyVisibility(positionWS, n) * skyScale;
    surface.alpha = 1;

    // Debug (SceneLighting "Show sky visibility"): white = open sky, black = covered.
    if (_SkyOcclusionParams.x > 1.5) return surface.occlusion.xxx;
    half3 color = UniversalFragmentPBR(input, surface).rgb;
    half sunShadow = VoxelSunShadow(positionWS);
    if (sunShadow < 0.999h)
    {
        // The direct sun, exactly as UniversalFragmentPBR added it (its cookie and the ambient occlusion on direct
        // light included), less by the shadow.
        SurfaceData brdfSurface = surface;
        BRDFData brdf = (BRDFData)0;
        InitializeBRDFData(brdfSurface, brdf);
        AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(input, surface);
        Light sun = GetMainLight(input, half4(1, 1, 1, 1), ao);
        color -= LightingPhysicallyBased(brdf, sun, n, input.viewDirectionWS) * (1.0h - sunShadow);
    }
    color += RayTracedReflection(input, surface, skyScale);
    if (transmission > 0.0h) color += VoxelGlow(albedo, n, positionWS, transmission, cookieStrength);
    // Sky shadows also dim the fill lights (bounce, the cool underlight), or they'd light the covered places back up;
    // less so in the distance, so the background stays clear.
    float farW = saturate((distance(positionWS, _WorldSpaceCameraPos) - _SkyShadowShape.y) / max(_SkyShadowShape.z - _SkyShadowShape.y, 1e-3));
    half fill = _SkyOcclusionParams.x > 0.5 ? lerp(lerp((half)_SkyShadowShape.w, 1.0h, farW), 1.0h, surface.occlusion / max(skyScale, 1e-3h)) : 1.0h;
    color += albedo * BounceLight(positionWS, n) * fill;
    // Cool light from below, as if reflected off the ground: strongest on faces pointing down (the undersides of the
    // crowns), wrapping well onto the sides (so the trunks are lit), only a trace on tops. No shadows: a soft fill.
    half under = saturate(-n.y * 0.45h + 0.6h);
    color += albedo * BalanceFill(_VoxelUnderLight.rgb, fillBalance) * under * fill;
    return color;
}

half3 VoxelLightGloss(half3 albedo, float3 n, float3 positionWS, half transmission, half cookieStrength, float2 screenUV, half glossScale, half skyScale)
{
    return VoxelLightBalanced(albedo, n, positionWS, transmission, cookieStrength, screenUV, glossScale, skyScale, half4(1, 1, 1, 0));
}

half3 VoxelLight(half3 albedo, float3 n, float3 positionWS, half transmission, half cookieStrength, float2 screenUV)
{
    return VoxelLightGloss(albedo, n, positionWS, transmission, cookieStrength, screenUV, 1.0h, 1.0h);
}

#endif
