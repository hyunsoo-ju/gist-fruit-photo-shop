using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FruitGame
{
    // Ray tracing for the realistic look (Docs/DESIGN.md §4, "Ray tracing"). Unity offers no hardware ray tracing on this
    // Mac, so the rays are traced in software, on the GPU, through a 3D grid of the scene's blocks (RayTrace.compute):
    //  - the block grid: 5 cm cells (voxelSize) over the bounce grid's box (20 × 12 × 14 m, 400 × 240 × 280 cells),
    //    filled by drawing the scenery flattened along each axis (Hidden/FruitGame/Voxelize). Like the sky map, the
    //    still scenery is cached in its own grid, redrawn only when it changes; each frame the bricks what moved last
    //    frame touched are put back from it, and what moves is drawn again;
    //  - bounce light: the bounce grid's cells trace rays through it instead of summing every sunlit patch, so light no
    //    longer leaks through walls (Gather);
    //  - reflections: a mirror ray per pixel at half resolution, before the scene is drawn, from URP's depth and normals;
    //    the shaders use it in place of the reflection probe where the ray hit something (VoxelLight.hlsl).
    public partial class SceneLighting
    {
        [Header("Ray tracing")]
        [Tooltip("Ray traced reflections and bounce light (realistic look).")]
        [SerializeField] bool rayTracing = true;
        [Tooltip("Rays per bounce-grid cell, and how far they reach (metres).")]
        [SerializeField] int bounceRays = 64;
        [Tooltip("Reflections: how far a reflected ray goes (metres) and where it starts fading out.")]
        [SerializeField] float reflectionReach = 6f;
        [SerializeField] float reflectionFade = 4f;
        [Tooltip("Reflections: surfaces further from the camera than this (metres) keep the probe's reflection (fading from three quarters of it): behind the play area the background blur and the fog hide them.")]
        [SerializeField] float reflectionFarthest = 18f;
        [Tooltip("Reflections: resolution as a share of the screen's (1 = full).")]
        [SerializeField] float reflectionScale = 0.5f;
        [Tooltip("Size of a cell of the block grid the rays are traced through (metres). Smaller = finer reflections, more memory (5 cm: two grids of 107 MB) and slower rays.")]
        [SerializeField] float voxelSize = 0.05f;
        [Tooltip("Reflections: blur step in full-screen pixels (9 taps each way, only across the same surface): the blocks are glossy, not mirrors.")]
        [SerializeField] float reflectionBlurStep = 3f;
        [Tooltip("Reflections: share of each new frame when blending over frames (lower = steadier, slower to follow).")]
        [SerializeField] float reflectionNewShare = 0.15f;

        /// <summary>For comparisons in a test session: ray tracing off without leaving the realistic look.</summary>
        public static bool RayTracingPaused;
        /// <summary>For comparisons: the bounce grid built the old way (summed sunlit patches) while reflections stay on.</summary>
        public static bool BounceTracingPaused;

        const int VoxBrick = 4;
        /// <summary>Raster pixels per grid cell side when filling the grid (0 = 2 for cells of 8 cm and up, else 1, so
        /// small or thin things still land in it). Settable in test sessions.</summary>
        public static int VoxRaster;
        int RasterScale => VoxRaster > 0 ? Mathf.Min(VoxRaster, 2) : (voxCell >= 0.08f ? 2 : 1);
        float voxCell = 0.1f;

        static readonly int VoxBaseMapId = Shader.PropertyToID("_VoxBaseMap");
        static readonly int VoxBaseMapStId = Shader.PropertyToID("_VoxBaseMap_ST");
        static readonly int VoxColorId = Shader.PropertyToID("_VoxColor");
        static readonly int VoxMinId = Shader.PropertyToID("_VoxMin");
        static readonly int VoxDimsId = Shader.PropertyToID("_VoxDims");
        static readonly int RtReflectionsId = Shader.PropertyToID("_RtReflections");
        static readonly int RtParamsId = Shader.PropertyToID("_RtReflectionParams");

        struct VoxSurface { public Texture texture; public Vector4 st; public Color colour; }

        ComputeShader rtCompute;
        Material voxelizeMaterial;
        RenderTexture voxStatic, voxLive, voxBricks, voxRaster;
        Vector3Int voxDims;
        CommandBuffer voxBuffer;
        bool voxStaticDirty = true, voxReady;
        Matrix4x4 fluxMatrix;
        RtReflectionPass reflectionPass;
        RtResetPass resetPass;
        readonly Vector4[] ambientCube = new Vector4[6];
        readonly Color[] cubeColours = new Color[6];
        static readonly Vector3[] CubeDirections = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        readonly int[] kernels = new int[3];

        bool RayTracingOn => rayTracing && !RayTracingPaused && voxLive != null && SystemInfo.supportsComputeShaders;

        void MakeRayTracing()
        {
            if (!rayTracing || !SystemInfo.supportsComputeShaders) return;
            rtCompute ??= Resources.Load<ComputeShader>("RayTracing/RayTrace");
            var voxelize = Resources.Load<Shader>("RayTracing/Voxelize");
            if (rtCompute == null || voxelize == null) return;
            if (voxLive == null || voxStatic == null || voxBricks == null || voxRaster == null || voxelizeMaterial == null)
            {
                FreeRayTracing();
                var size = BounceHigh - BounceLow;
                voxCell = Mathf.Clamp(voxelSize, 0.04f, 0.2f);
                // Whole bricks along every side.
                int Cells(float m) => Mathf.CeilToInt(m / voxCell / VoxBrick) * VoxBrick;
                voxDims = new Vector3Int(Cells(size.x), Cells(size.y), Cells(size.z));
                voxStatic = Grid("Ray Tracing Still Blocks", voxDims, GraphicsFormat.R8G8B8A8_UNorm);
                voxLive = Grid("Ray Tracing Blocks", voxDims, GraphicsFormat.R8G8B8A8_UNorm);
                voxBricks = Grid("Ray Tracing Bricks", voxDims / VoxBrick, GraphicsFormat.R32_UInt);
                voxRaster = new RenderTexture(voxDims.x * 2, Mathf.Max(voxDims.y, voxDims.z) * 2, 0, GraphicsFormat.R8_UNorm)
                { name = "Ray Tracing Raster", hideFlags = HideFlags.DontSave };
                voxRaster.Create();
                voxelizeMaterial = new Material(voxelize) { name = "Voxelize", hideFlags = HideFlags.DontSave };
                voxStaticDirty = true;
                voxReady = false;
            }
            kernels[0] = rtCompute.FindKernel("Reflect");
            kernels[1] = rtCompute.FindKernel("Blur");
            kernels[2] = rtCompute.FindKernel("Temporal");
            reflectionPass ??= new RtReflectionPass(this);
            resetPass ??= new RtResetPass();
            RenderPipelineManager.beginCameraRendering -= EnqueueReflections;
            RenderPipelineManager.beginCameraRendering += EnqueueReflections;
        }

        static RenderTexture Grid(string name, Vector3Int dims, GraphicsFormat format)
        {
            var rt = new RenderTexture(dims.x, dims.y, 0, format)
            {
                name = name, hideFlags = HideFlags.DontSave, dimension = TextureDimension.Tex3D, volumeDepth = dims.z,
                enableRandomWrite = true, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        void StopRayTracing()
        {
            RenderPipelineManager.beginCameraRendering -= EnqueueReflections;
            Shader.SetGlobalVector(RtParamsId, Vector4.zero);
            voxReady = false;
        }

        void FreeRayTracing()
        {
            StopRayTracing();
            voxBuffer?.Release(); voxBuffer = null;
            Free(voxStatic); Free(voxLive); Free(voxBricks); Free(voxRaster); Free(voxelizeMaterial);
            voxStatic = voxLive = voxBricks = voxRaster = null; voxelizeMaterial = null;
            reflectionPass?.Dispose();
            reflectionPass = null;
            voxStaticDirty = true;
            voxInStatic.Clear(); voxPending.Clear();
        }

        VoxSurface[] Surfaces(SkyCaster c)
        {
            if (c.surfaces != null) return c.surfaces;
            var mats = c.renderer.sharedMaterials;
            var list = new VoxSurface[c.submeshes];
            for (int s = 0; s < c.submeshes; s++)
            {
                var m = s < mats.Length ? mats[s] : null;
                var surf = new VoxSurface { texture = Texture2D.whiteTexture, st = new Vector4(1, 1, 0, 0), colour = Color.white };
                if (m != null)
                {
                    if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                    {
                        surf.texture = m.GetTexture("_BaseMap");
                        var sc = m.GetTextureScale("_BaseMap"); var of = m.GetTextureOffset("_BaseMap");
                        surf.st = new Vector4(sc.x, sc.y, of.x, of.y);
                    }
                    if (m.HasProperty("_BaseColor")) surf.colour = m.GetColor("_BaseColor").linear;
                    else if (m.HasProperty("_Color")) surf.colour = m.GetColor("_Color").linear;
                }
                list[s] = surf;
            }
            return c.surfaces = list;
        }

        /// <summary>What's drawn into the still grid, and where it was then.</summary>
        readonly Dictionary<Renderer, (Matrix4x4 matrix, Bounds bounds)> voxInStatic = new Dictionary<Renderer, (Matrix4x4, Bounds)>();
        readonly List<Renderer> voxGone = new List<Renderer>();
        readonly List<Bounds> voxErase = new List<Bounds>();
        /// <summary>Still things not in the still grid yet: frames they've stayed put (they're added after a while).</summary>
        readonly Dictionary<Renderer, int> voxPending = new Dictionary<Renderer, int>();
        readonly List<SkyCaster> voxDraw = new List<SkyCaster>(), voxAdd = new List<SkyCaster>();
        readonly HashSet<Renderer> voxRedraw = new HashSet<Renderer>();
        /// <summary>How often the still grid has been redrawn whole (test sessions check it stays rare).</summary>
        public static int StillGridRedraws;
        /// <summary>How often part of the still grid has been erased and redrawn, and what set off the last (test sessions).</summary>
        public static int StillGridErases;
        public static string LastEraseCause = "";
        const int VoxSettleFrames = 30;
        const float SmallestMover = 0.25f;
        Bounds voxBounds;

        /// <summary>
        /// Fills the block grid for this frame (after RenderSkyOcclusion, which decides what moves). The still scenery
        /// lives in its own grid: things that turn up are added to it once they've stayed put for half a second, and it's
        /// redrawn whole only when something in it moves or goes (about 5 ms). The live grid is the still one plus what
        /// moves: drawing marks every brick it touches, and the next frame copies just those bricks back from the still
        /// grid before drawing what moves again (copying the whole grid each frame would take about 1.5 ms at 5 cm).
        /// </summary>
        void VoxelizeScene()
        {
            if (!RayTracingOn || voxelizeMaterial == null) { voxReady = false; return; }
            voxBounds = new Bounds((BounceLow + BounceHigh) * 0.5f, BounceHigh - BounceLow);
            // Still things that moved, went or were hidden since they were drawn: their part of the still grid is erased
            // and the still things around it drawn again (a whole redraw takes about 8 ms at 5 cm).
            voxGone.Clear(); voxErase.Clear();
            if (!voxStaticDirty)
                foreach (var kv in voxInStatic)
                    if (kv.Key == null || !Shown(kv.Key) || skyMovers.Contains(kv.Key) || kv.Key.localToWorldMatrix != kv.Value.matrix)
                    {
                        voxGone.Add(kv.Key);
                        voxErase.Add(kv.Value.bounds);
                        LastEraseCause = kv.Key == null ? "destroyed" : $"{kv.Key.name} ({(!Shown(kv.Key) ? "hidden" : skyMovers.Contains(kv.Key) ? "moving" : "moved")})";
                    }
            foreach (var r in voxGone) voxInStatic.Remove(r);
            if (voxErase.Count > 0) StillGridErases++;

            var cb = voxBuffer ??= new CommandBuffer { name = "Ray Tracing Blocks" };
            cb.Clear();
            cb.SetGlobalVector(VoxMinId, new Vector4(BounceLow.x, BounceLow.y, BounceLow.z, voxCell));
            cb.SetComputeVectorParam(rtCompute, VoxDimsId, new Vector4(voxDims.x, voxDims.y, voxDims.z, 0));
            voxDraw.Clear(); voxAdd.Clear();
            bool full = voxStaticDirty;
            if (full)
            {
                StillGridRedraws++;
                voxInStatic.Clear(); voxPending.Clear();
                voxStaticDirty = false;
            }
            foreach (var c in skyCasters)
            {
                if (c.renderer == null || !Shown(c.renderer) || voxInStatic.ContainsKey(c.renderer)) continue;
                if (!c.renderer.bounds.Intersects(voxBounds)) continue;
                if (c.moves)
                {
                    // Drifting leaves, cloud puffs and butterflies are smaller than a few cells: they'd hardly show in a
                    // reflection, and each costs three draws a frame.
                    var size = c.renderer.bounds.size;
                    if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) >= SmallestMover) voxDraw.Add(c);
                    continue;
                }
                voxPending.TryGetValue(c.renderer, out int frames);
                if (full || frames >= VoxSettleFrames)
                {
                    voxPending.Remove(c.renderer);
                    voxAdd.Add(c);
                    voxInStatic[c.renderer] = (c.renderer.localToWorldMatrix, c.renderer.bounds);
                }
                else
                {
                    voxPending[c.renderer] = frames + 1;
                    voxDraw.Add(c);
                }
            }
            var b = voxDims / VoxBrick;
            int gx = (b.x + 3) / 4, gy = (b.y + 3) / 4, gz = (b.z + 3) / 4;
            if (full)
            {
                // Everything still, drawn afresh; then the live grid and the bricks rebuilt from it.
                int clear = rtCompute.FindKernel("Clear");
                cb.SetComputeTextureParam(rtCompute, clear, "_VoxClear", voxStatic);
                cb.DispatchCompute(rtCompute, clear, (voxDims.x + 7) / 8, (voxDims.y + 7) / 8, (voxDims.z + 7) / 8);
                if (voxAdd.Count > 0) DrawBlocks(cb, voxStatic, voxAdd);
                int rebuild = rtCompute.FindKernel("Rebuild");
                cb.SetComputeTextureParam(rtCompute, rebuild, "_VoxStill", voxStatic);
                cb.SetComputeTextureParam(rtCompute, rebuild, "_VoxLiveOut", voxLive);
                cb.SetComputeTextureParam(rtCompute, rebuild, "_BricksOut", voxBricks);
                cb.DispatchCompute(rtCompute, rebuild, gx, gy, gz);
            }
            else
            {
                if (voxErase.Count > 0)
                {
                    // Erase each gone thing's box (its bricks are marked, so they're copied over below), then draw the
                    // still things that overlap it again.
                    int erase = rtCompute.FindKernel("EraseBox");
                    cb.SetComputeTextureParam(rtCompute, erase, "_VoxClear", voxStatic);
                    cb.SetComputeTextureParam(rtCompute, erase, "_BricksOut", voxBricks);
                    var origin = BounceLow;
                    foreach (var bounds in voxErase)
                    {
                        var lo = Vector3Int.Max(Vector3Int.zero, Vector3Int.FloorToInt((bounds.min - origin) / voxCell) - Vector3Int.one);
                        var hi = Vector3Int.Min(voxDims, Vector3Int.CeilToInt((bounds.max - origin) / voxCell) + Vector3Int.one);
                        var n = hi - lo;
                        if (n.x <= 0 || n.y <= 0 || n.z <= 0) continue;
                        cb.SetComputeVectorParam(rtCompute, "_BoxMin", new Vector4(lo.x, lo.y, lo.z, 0));
                        cb.SetComputeVectorParam(rtCompute, "_BoxSize", new Vector4(n.x, n.y, n.z, 0));
                        cb.DispatchCompute(rtCompute, erase, (n.x + 7) / 8, (n.y + 7) / 8, (n.z + 7) / 8);
                    }
                    foreach (var kv in voxInStatic)
                    {
                        if (kv.Key == null) continue;
                        foreach (var bounds in voxErase)
                            if (kv.Value.bounds.Intersects(bounds)) { voxRedraw.Add(kv.Key); break; }
                    }
                    foreach (var c in skyCasters)
                        if (c.renderer != null && voxRedraw.Contains(c.renderer) && !voxAdd.Contains(c)) voxAdd.Add(c);
                    voxRedraw.Clear();
                }
                // Newly settled things go into the still grid (marking their bricks, so they're copied over below);
                // the bricks what moved touched last frame are put back as the still grid has them.
                if (voxAdd.Count > 0) DrawBlocks(cb, voxStatic, voxAdd);
                int restore = rtCompute.FindKernel("Restore");
                cb.SetComputeTextureParam(rtCompute, restore, "_VoxStill", voxStatic);
                cb.SetComputeTextureParam(rtCompute, restore, "_VoxLiveOut", voxLive);
                cb.SetComputeTextureParam(rtCompute, restore, "_BricksOut", voxBricks);
                cb.DispatchCompute(rtCompute, restore, gx, gy, gz);
            }
            if (voxDraw.Count > 0) DrawBlocks(cb, voxLive, voxDraw);
            Graphics.ExecuteCommandBuffer(cb);
            voxReady = true;
        }

        /// <summary>Draws a list of things into a grid, flattened along x, y and z in turn.</summary>
        void DrawBlocks(CommandBuffer cb, RenderTexture grid, List<SkyCaster> list)
        {
            cb.SetRenderTarget(voxRaster);
            cb.SetRandomWriteTarget(1, grid);
            cb.SetRandomWriteTarget(2, voxBricks);
            int raster = RasterScale;
            for (int axis = 0; axis < 3; axis++)
            {
                int w = (axis == 0 ? voxDims.y : voxDims.x) * raster;
                int h = (axis == 2 ? voxDims.y : voxDims.z) * raster;
                cb.SetViewport(new Rect(0, 0, w, h));
                cb.SetGlobalVector(VoxDimsId, new Vector4(voxDims.x, voxDims.y, voxDims.z, axis));
                foreach (var c in list)
                {
                    var surfaces = Surfaces(c);
                    for (int s = 0; s < c.submeshes; s++)
                    {
                        cb.SetGlobalTexture(VoxBaseMapId, surfaces[s].texture);
                        cb.SetGlobalVector(VoxBaseMapStId, surfaces[s].st);
                        cb.SetGlobalColor(VoxColorId, surfaces[s].colour);
                        cb.DrawRenderer(c.renderer, voxelizeMaterial, s, 0);
                    }
                }
            }
            cb.ClearRandomWriteTargets();
        }

        /// <summary>The lighting a ray's hit needs (sun's-eye map, sky, bounce, underlight), for both kernels.</summary>
        void BindHitLighting(CommandBuffer cb, int kernel)
        {
            var cs = rtCompute;
            cb.SetComputeTextureParam(cs, kernel, "_Vox", voxLive);
            cb.SetComputeTextureParam(cs, kernel, "_Bricks", voxBricks);
            cb.SetComputeVectorParam(cs, "_VoxMin", new Vector4(BounceLow.x, BounceLow.y, BounceLow.z, voxCell));
            cb.SetComputeVectorParam(cs, "_VoxDims", new Vector4(voxDims.x, voxDims.y, voxDims.z, 0));
            cb.SetComputeTextureParam(cs, kernel, "_Flux", fluxColour);
            cb.SetComputeTextureParam(cs, kernel, "_FluxPosition", fluxPosition);
            cb.SetComputeTextureParam(cs, kernel, "_FluxNormal", fluxNormal);
            cb.SetComputeMatrixParam(cs, "_FluxMatrix", fluxMatrix);
            var toSun = -sun.transform.forward;
            cb.SetComputeVectorParam(cs, "_ToSun", toSun);
            var light = sun.color.linear * sun.intensity;
            cb.SetComputeVectorParam(cs, "_SunLight", new Vector4(light.r, light.g, light.b, 0));
        }

        /// <summary>Builds layers of a bounce grid by tracing rays (instead of BounceVolume.compute's Gather).</summary>
        bool TraceBounce(RenderTexture volume, int first, int count)
        {
            if (!RayTracingOn || BounceTracingPaused || !voxReady || fluxColour == null) return false;
            var cb = voxBuffer ??= new CommandBuffer { name = "Ray Tracing Blocks" };
            cb.Clear();
            int k = rtCompute.FindKernel("Bounce");
            BindHitLighting(cb, k);
            cb.SetComputeTextureParam(rtCompute, k, "_Volume", volume);
            cb.SetComputeVectorParam(rtCompute, "_Dims", new Vector4(bounceDims.x, bounceDims.y, bounceDims.z, bounceRays));
            cb.SetComputeVectorParam(rtCompute, "_Min", new Vector4(BounceLow.x, BounceLow.y, BounceLow.z, BounceCell));
            cb.SetComputeVectorParam(rtCompute, "_Shape", new Vector4(bounceRadius, count, 0, first));
            cb.DispatchCompute(rtCompute, k, (bounceDims.x * bounceDims.y * count + 3) / 4, 1, 1); // 4 cells a group
            Graphics.ExecuteCommandBuffer(cb);
            return true;
        }

        void EnqueueReflections(ScriptableRenderContext context, Camera camera)
        {
            if (camera != cam || !RayTracingOn || !voxReady || fluxColour == null) return;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            data.scriptableRenderer.EnqueuePass(reflectionPass);
            data.scriptableRenderer.EnqueuePass(resetPass);
        }

        /// <summary>Everything the reflection kernel reads besides the frame's depth and normals.</summary>
        void BindReflections(CommandBuffer cb, int k)
        {
            BindHitLighting(cb, k);
            RenderSettings.ambientProbe.Evaluate(CubeDirections, cubeColours);
            for (int i = 0; i < 6; i++) ambientCube[i] = new Vector4(Mathf.Max(0, cubeColours[i].r), Mathf.Max(0, cubeColours[i].g), Mathf.Max(0, cubeColours[i].b), 0);
            cb.SetComputeVectorArrayParam(rtCompute, "_Ambient", ambientCube);
            bool sky = skyDepth != null;
            cb.SetComputeTextureParam(rtCompute, k, "_SkyMap", sky ? skyDepth : (Texture)Texture2D.whiteTexture);
            cb.SetComputeMatrixParam(rtCompute, "_SkyMatrix", skyProj * skyView);
            cb.SetComputeVectorParam(rtCompute, "_SkyOrigin", skyOrigin);
            cb.SetComputeVectorParam(rtCompute, "_SkyForward", new Vector4(skyForward.x, skyForward.y, skyForward.z, 1f / SkyDepthRange));
            cb.SetComputeVectorParam(rtCompute, "_SkyParams", sky ? new Vector4(1f, skyShadowSoftness / (2f * SkySize * SkyAspect), 0.08f / SkyDepthRange, skyShadowFloor) : Vector4.zero);
            cb.SetComputeTextureParam(rtCompute, k, "_BounceTex", bounceVolumes != null ? bounceVolumes[bounceTo] : voxBricks);
            cb.SetComputeVectorParam(rtCompute, "_BounceMin", new Vector4(BounceLow.x, BounceLow.y, BounceLow.z, 1f / BounceCell));
            cb.SetComputeVectorParam(rtCompute, "_BounceDims", bounceVolumes != null ? new Vector4(bounceDims.x, bounceDims.y, bounceDims.z, bounceStrength) : Vector4.zero);
            cb.SetComputeVectorParam(rtCompute, "_UnderLight", (Vector4)(underLight.linear * underLightIntensity));
            cb.SetComputeVectorParam(rtCompute, "_ReflShape", new Vector4(reflectionReach, reflectionFade, reflectionFarthest, ReflectionDebug));
        }

        /// <summary>Test sessions: 1 writes each pixel's rebuilt world position instead of its reflection.</summary>
        public static float ReflectionDebug;

        /// <summary>
        /// Traces the reflections after URP's depth-and-normals prepass, before the scene is drawn, into a half-size
        /// texture the scene's shaders read (_RtReflections, with _RtReflectionParams.x = 1 while it's valid).
        /// </summary>
        sealed class RtReflectionPass : ScriptableRenderPass
        {
            readonly SceneLighting owner;

            /// <summary>One camera size's textures: the traced reflections, a scratch one, and two histories (ping-pong).</summary>
            sealed class Targets
            {
                public RenderTexture raw, scratch;
                public readonly RenderTexture[] history = new RenderTexture[2];
                public readonly RTHandle[] handles = new RTHandle[4];
                public int current, frame = -10;
                public Matrix4x4 viewProj;
            }

            readonly Dictionary<Vector2Int, Targets> sizes = new Dictionary<Vector2Int, Targets>();
            RenderTexture last;

            /// <summary>The last frame's reflections, as the scene used them (kept, so a test session can read them back).</summary>
            public RenderTexture Target => last;

            public RtReflectionPass(SceneLighting owner)
            {
                this.owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public void Dispose()
            {
                foreach (var t in sizes.Values)
                {
                    foreach (var h in t.handles) h?.Release();
                    foreach (var rt in new[] { t.raw, t.scratch, t.history[0], t.history[1] })
                        if (rt != null) { rt.Release(); Free(rt); }
                }
                sizes.Clear();
                last = null;
            }

            static RenderTexture Make(string name, int w, int h)
            {
                var rt = new RenderTexture(w, h, 0, GraphicsFormat.R16G16B16A16_SFloat)
                {
                    name = name, hideFlags = HideFlags.DontSave, enableRandomWrite = true,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                };
                rt.Create();
                return rt;
            }

            Targets For(int w, int h)
            {
                var key = new Vector2Int(w, h);
                if (sizes.TryGetValue(key, out var t)) return t;
                t = new Targets
                {
                    raw = Make("Ray Traced Reflections", w, h),
                    scratch = Make("Ray Traced Reflections Blur", w, h),
                };
                t.history[0] = Make("Ray Traced Reflections History 0", w, h);
                t.history[1] = Make("Ray Traced Reflections History 1", w, h);
                t.handles[0] = RTHandles.Alloc(t.raw);
                t.handles[1] = RTHandles.Alloc(t.scratch);
                t.handles[2] = RTHandles.Alloc(t.history[0]);
                t.handles[3] = RTHandles.Alloc(t.history[1]);
                return sizes[key] = t;
            }

            sealed class PassData
            {
                public SceneLighting owner;
                public TextureHandle depth, normals, raw, scratch, history, output;
                public Vector4 size, origin, rx, ry, rz, z, temporal;
                public Matrix4x4 prevViewProj;
                public bool debug;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                var depth = resources.cameraDepthTexture;
                var normals = resources.cameraNormalsTexture;
                if (!depth.IsValid() || !normals.IsValid()) return;
                var depthDesc = renderGraph.GetTextureDesc(depth);
                int dw = depthDesc.width, dh = depthDesc.height;
                float scale = Mathf.Clamp(owner.reflectionScale, 0.25f, 1f);
                int w = Mathf.Max(1, Mathf.RoundToInt(dw * scale)), h = Mathf.Max(1, Mathf.RoundToInt(dh * scale));
                var t = For(w, h);

                var camera = cameraData.camera;
                var tr = camera.transform;
                float tanY = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * dw / dh;
                // Row 0 of the depth texture is the top of the picture where textures start at the top (Metal).
                bool topFirst = SystemInfo.graphicsUVStartsAtTop;
                // The history is only good if it's from the frame just before (the photo camera renders now and then).
                bool fresh = t.frame == Time.frameCount - 1 || t.frame == Time.frameCount;
                int prev = t.current, next = 1 - t.current;

                using var builder = renderGraph.AddUnsafePass<PassData>("Ray Traced Reflections", out var data);
                data.owner = owner;
                data.depth = depth;
                data.normals = normals;
                data.raw = renderGraph.ImportTexture(t.handles[0]);
                data.scratch = renderGraph.ImportTexture(t.handles[1]);
                data.history = renderGraph.ImportTexture(t.handles[2 + prev]);
                data.output = renderGraph.ImportTexture(t.handles[2 + next]);
                data.size = new Vector4(w, h, dw, dh);
                data.origin = tr.position;
                data.rx = tr.right * tanX;
                data.ry = tr.up * (tanY * (topFirst ? -1f : 1f));
                data.rz = tr.forward;
                data.z = new Vector4(camera.nearClipPlane, camera.farClipPlane, SystemInfo.usesReversedZBuffer ? 1f : 0f, 0f);
                data.temporal = new Vector4(owner.reflectionNewShare, fresh ? 1f : 0f, topFirst ? 1f : 0f, 0f);
                data.prevViewProj = t.viewProj;
                data.debug = ReflectionDebug > 0.5f;
                t.viewProj = camera.projectionMatrix * camera.worldToCameraMatrix;
                t.frame = Time.frameCount;
                t.current = next;
                last = data.debug ? t.raw : t.history[next];

                builder.UseTexture(data.depth);
                builder.UseTexture(data.normals);
                builder.UseTexture(data.raw, AccessFlags.ReadWrite);
                builder.UseTexture(data.scratch, AccessFlags.ReadWrite);
                builder.UseTexture(data.history);
                builder.UseTexture(data.output, AccessFlags.Write);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData d, UnsafeGraphContext ctx) =>
                {
                    var cs = d.owner.rtCompute;
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    var ks = d.owner.kernels;
                    int reflect = ks[0], blur = ks[1], temporal = ks[2];
                    d.owner.BindReflections(cmd, reflect);
                    foreach (int k in ks)
                    {
                        cmd.SetComputeTextureParam(cs, k, "_Depth", d.depth);
                        cmd.SetComputeTextureParam(cs, k, "_Normals", d.normals);
                    }
                    cmd.SetComputeVectorParam(cs, "_ReflSize", d.size);
                    cmd.SetComputeVectorParam(cs, "_RayOrigin", d.origin);
                    cmd.SetComputeVectorParam(cs, "_RayX", d.rx);
                    cmd.SetComputeVectorParam(cs, "_RayY", d.ry);
                    cmd.SetComputeVectorParam(cs, "_RayZ", d.rz);
                    cmd.SetComputeVectorParam(cs, "_ZParams", d.z);
                    int gx = ((int)d.size.x + 7) / 8, gy = ((int)d.size.y + 7) / 8;
                    cmd.SetComputeTextureParam(cs, reflect, "_ReflOut", d.raw);
                    cmd.DispatchCompute(cs, reflect, gx, gy, 1);
                    if (d.debug)
                    {
                        cmd.SetGlobalVector(RtParamsId, Vector4.zero);
                        return;
                    }
                    // Blurred across, then down (back into the raw texture), then blended with the last frames.
                    float step = d.owner.reflectionBlurStep * d.size.x / d.size.z;
                    cmd.SetComputeVectorParam(cs, "_BlurStep", new Vector4(step, 0, 0, 0));
                    cmd.SetComputeTextureParam(cs, blur, "_SmoothIn", d.raw);
                    cmd.SetComputeTextureParam(cs, blur, "_SmoothOut", d.scratch);
                    cmd.DispatchCompute(cs, blur, gx, gy, 1);
                    cmd.SetComputeVectorParam(cs, "_BlurStep", new Vector4(0, step, 0, 0));
                    cmd.SetComputeTextureParam(cs, blur, "_SmoothIn", d.scratch);
                    cmd.SetComputeTextureParam(cs, blur, "_SmoothOut", d.raw);
                    cmd.DispatchCompute(cs, blur, gx, gy, 1);
                    cmd.SetComputeMatrixParam(cs, "_PrevViewProj", d.prevViewProj);
                    cmd.SetComputeVectorParam(cs, "_Temporal", d.temporal);
                    cmd.SetComputeTextureParam(cs, temporal, "_SmoothIn", d.raw);
                    cmd.SetComputeTextureParam(cs, temporal, "_History", d.history);
                    cmd.SetComputeTextureParam(cs, temporal, "_SmoothOut", d.output);
                    cmd.DispatchCompute(cs, temporal, gx, gy, 1);
                    cmd.SetGlobalTexture(RtReflectionsId, d.output);
                    cmd.SetGlobalVector(RtParamsId, new Vector4(1f, 0f, 0f, 0f));
                });
            }
        }

        /// <summary>After the scene: reflections off again, so no other camera (probe, staff screen) reads them.</summary>
        sealed class RtResetPass : ScriptableRenderPass
        {
            public RtResetPass() { renderPassEvent = RenderPassEvent.AfterRenderingTransparents; }

            sealed class PassData { }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddUnsafePass<PassData>("Ray Traced Reflections Off", out _);
                builder.AllowGlobalStateModification(true);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (PassData d, UnsafeGraphContext ctx) => ctx.cmd.SetGlobalVector(RtParamsId, Vector4.zero));
            }
        }
    }
}
