using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FruitGame
{
    public enum LightingStyle { Classic, Realistic }

    /// <summary>
    /// The scene's look (Docs/DESIGN.md §4, "Lighting"), switched from the staff panel (Display page). Classic is the
    /// game as it was: flat-lit blocks in the half-resolution pixel render. Realistic is a deliberately overdone,
    /// "AAA" take on the same scene:
    ///  - the camera draws the 3D scene itself at full resolution (no pixel layers, no pixel snapping, no dither haze);
    ///  - a low warm sun from behind and to the left, real-time soft shadows at full strength, its dapple cookie;
    ///  - a procedural sky that lights the scene (ambient from the sky) and a live reflection probe of the scene;
    ///  - every block shaded physically as glossy plastic (VoxelLight.hlsl; URP Lit scenery gets glossy copies), with
    ///    screen-space ambient occlusion, light glowing through leaves, and distance fog;
    ///  - volumetric sun beams marched through the shadow map (VolumetricLight.shader);
    ///  - post-processing: ACES tonemapping, bloom, slight depth of field, grading, vignette and a little grain;
    ///  - warm rims on falling fruit and a warm edge on the player's sun side.
    /// The title, buttons and digits keep their classic light either way. Everything is put back for Classic.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class SceneLighting : MonoBehaviour, IEditPreview
    {
        [SerializeField] GameConfig config;
        [SerializeField] Light sun;
        [SerializeField] Shader volumetricShader;
        [SerializeField] Shader skyShader;

        [Header("Sun")]
        [Tooltip("Low, from behind and to the left: every block's left side and top catch it, the faces toward the camera fall into shade.")]
        [SerializeField] Vector3 sunAngles = new Vector3(24f, 128f, 0f);
        [SerializeField] Color sunColour = new Color(1f, 0.8f, 0.58f);
        [SerializeField] float sunIntensity = 3.6f;

        [Header("Materials")]
        [Tooltip("Glossy plastic: sharp highlights and clear reflections.")]
        [SerializeField] float smoothness = 0.78f;
        [SerializeField] Color glowTint = new Color(1f, 1f, 0.4f);
        [Tooltip("Warm edge on the player's sun side (0 = off: on a real camera image it read as a glowing outline).")]
        [SerializeField] float playerRim = 0f;

        [Header("Sky and air")]
        [SerializeField] Color skyTint = new Color(0.45f, 0.55f, 0.7f);
        [SerializeField] float skyExposure = 1.25f;
        [Tooltip("Sky light on the shaded faces (1 = as the sky gives it): a little more keeps the fruit and the basket readable.")]
        [SerializeField] float skyLight = 1.3f;
        [SerializeField] Color fogColour = new Color(0.66f, 0.72f, 0.8f);
        [SerializeField] float fogDensity = 0.02f;
        [Tooltip("Volumetric beams: density of the air the sun lights.")]
        [SerializeField] float beamDensity = 0.05f;

        [Header("Dapple")]
        [SerializeField] float dappleTile = 7f;
        [SerializeField] Vector2 dappleDrift = new Vector2(0.06f, 0.02f);

        [Header("Camera")]
        [Tooltip("Depth of field: where it's sharpest (the play plane, from the camera) and how soft the rest gets.")]
        [SerializeField] float focusDistance = 12f;
        [SerializeField] float aperture = 7f;

        // The classic look, as saved in the scene.
        bool captured;
        Quaternion classicRotation;
        Color classicColour;
        float classicIntensity, classicShadowStrength, classicAmbientIntensity;
        LightShadows classicShadows;
        AmbientMode classicMode;
        Color classicSky, classicEquator, classicGround;
        SphericalHarmonicsL2 classicProbe;
        Material classicSkybox;
        Light classicSun;
        DefaultReflectionMode classicReflectMode;
        Texture classicReflection;
        bool classicFog;
        FogMode classicFogMode;
        Color classicFogColour;
        float classicFogDensity;
        Vector2 classicCookieSize, classicCookieOffset;
        CameraOverrideOption classicDepthOption;
        bool classicHdr;
        AntialiasingMode classicAa;
        AntialiasingQuality classicAaQuality;

        LightingStyle? applied;
        UniversalAdditionalLightData sunData;
        UniversalAdditionalCameraData camData;
        Camera cam;
        PixelRenderer pixel;
        Texture2D cookie;
        Material skybox, beamsMaterial;
        GameObject beams, probeObject, volumeObject;
        ReflectionProbe probe;
        VolumeProfile profile;
        Vector2 dappleOffset;
        float probeTimer;
        readonly Dictionary<Renderer, Material[]> glossed = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<Material, Material> glossCopies = new Dictionary<Material, Material>();

        static readonly int Backlight = Shader.PropertyToID("_VoxelBacklight");
        static readonly int Smoothness = Shader.PropertyToID("_VoxelSmoothness");
        static readonly int FadeScale = Shader.PropertyToID("_VoxelFadeScale");
        static readonly int GlowTint = Shader.PropertyToID("_VoxelGlowTint");
        static readonly int GlowDepth = Shader.PropertyToID("_VoxelGlowDepth");
        static readonly int UILightDir = Shader.PropertyToID("_UILightDir");
        static readonly int UILightColor = Shader.PropertyToID("_UILightColor");
        static readonly int UISky = Shader.PropertyToID("_UIAmbientSky");
        static readonly int UIEquator = Shader.PropertyToID("_UIAmbientEquator");
        static readonly int UIGround = Shader.PropertyToID("_UIAmbientGround");
        static readonly int PlayerRim = Shader.PropertyToID("_PlayerRim");
        static readonly int PlayerRimColor = Shader.PropertyToID("_PlayerRimColor");
        static readonly int PlayerRimDir = Shader.PropertyToID("_PlayerRimDir");

        LightingStyle Wanted => config != null ? config.lightingStyle : LightingStyle.Classic;

        void Awake() => Capture();

        void Capture()
        {
            if (captured || sun == null || applied == LightingStyle.Realistic) return;
            captured = true;
            classicRotation = sun.transform.rotation;
            classicColour = sun.color;
            classicIntensity = sun.intensity;
            classicShadowStrength = sun.shadowStrength;
            classicShadows = sun.shadows;
            classicMode = RenderSettings.ambientMode;
            classicAmbientIntensity = RenderSettings.ambientIntensity;
            classicSky = RenderSettings.ambientSkyColor;
            classicEquator = RenderSettings.ambientEquatorColor;
            classicGround = RenderSettings.ambientGroundColor;
            classicProbe = RenderSettings.ambientProbe;
            classicSkybox = RenderSettings.skybox;
            classicSun = RenderSettings.sun;
            classicReflectMode = RenderSettings.defaultReflectionMode;
            classicReflection = RenderSettings.customReflectionTexture;
            classicFog = RenderSettings.fog;
            classicFogMode = RenderSettings.fogMode;
            classicFogColour = RenderSettings.fogColor;
            classicFogDensity = RenderSettings.fogDensity;
            sunData = sun.GetComponent<UniversalAdditionalLightData>();
            if (sunData != null) { classicCookieSize = sunData.lightCookieSize; classicCookieOffset = sunData.lightCookieOffset; }
            cam = Camera.main;
            if (cam != null)
            {
                pixel = cam.GetComponent<PixelRenderer>();
                camData = cam.GetComponent<UniversalAdditionalCameraData>();
                classicHdr = cam.allowHDR;
                if (camData != null)
                {
                    classicDepthOption = camData.requiresDepthOption;
                    classicAa = camData.antialiasing;
                    classicAaQuality = camData.antialiasingQuality;
                }
            }
        }

        void Update()
        {
            if (sun == null) return;
            if (applied != Wanted) Apply(Wanted);
            if (applied != LightingStyle.Realistic) return;
            if (Application.isPlaying && sunData != null)
            {
                dappleOffset += dappleDrift * Time.deltaTime;
                sunData.lightCookieOffset = dappleOffset;
            }
            // The reflections follow the scene (the pile grows) every 2 s: each refresh renders the scene six times.
            probeTimer -= Time.unscaledDeltaTime;
            if (probe != null && probeTimer <= 0f) { probe.RenderProbe(); probeTimer = 2f; }
            FitBeams();
        }

        void Apply(LightingStyle style)
        {
            Capture();
            applied = style;
            if (style == LightingStyle.Classic) { Restore(); return; }

            // Full-resolution 3D render.
            if (pixel != null) pixel.SetDirect(true);
            PixelSnap.Enabled = false;
            if (cam != null)
            {
                cam.allowHDR = true;
                if (camData != null)
                {
                    camData.requiresDepthTexture = true;
                    camData.renderPostProcessing = true;
                    camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    camData.antialiasingQuality = AntialiasingQuality.High;
                }
            }

            // Sun.
            sun.transform.rotation = Quaternion.Euler(sunAngles);
            sun.color = sunColour;
            sun.intensity = sunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            if (cookie == null) cookie = MakeDapple();
            sun.cookie = cookie;
            if (sunData != null) { sunData.lightCookieSize = new Vector2(dappleTile, dappleTile); sunData.lightCookieOffset = dappleOffset; }

            // Sky: lights the scene and is reflected in it.
            if (skybox == null && skyShader != null)
            {
                skybox = new Material(skyShader) { name = "Realistic Sky", hideFlags = HideFlags.DontSave };
                skybox.SetFloat("_SunDisk", 2f);
                skybox.SetFloat("_SunSize", 0.05f);
                skybox.SetFloat("_SunSizeConvergence", 6f);
                skybox.SetFloat("_AtmosphereThickness", 1.15f);
                skybox.SetColor("_SkyTint", skyTint);
                skybox.SetColor("_GroundColor", new Color(0.32f, 0.3f, 0.24f));
                skybox.SetFloat("_Exposure", skyExposure);
            }
            RenderSettings.skybox = skybox;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = skyLight;
            DynamicGI.UpdateEnvironment();
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            MakeProbe();

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColour;
            RenderSettings.fogDensity = fogDensity;

            // Play only: swapping materials on prefab instances in the Editor records scene overrides, even when put back.
            if (Application.isPlaying) Gloss();
            Shader.SetGlobalFloat(Backlight, 1f);
            Shader.SetGlobalFloat(Smoothness, smoothness);
            Shader.SetGlobalFloat(FadeScale, 0.0001f); // fog does the distance now
            Shader.SetGlobalColor(GlowTint, glowTint);
            Shader.SetGlobalFloat(GlowDepth, 0.45f);
            // The title, buttons and digits keep the classic light.
            var classicDir = -(classicRotation * Vector3.forward);
            Shader.SetGlobalVector(UILightDir, new Vector4(classicDir.x, classicDir.y, classicDir.z, 1f));
            Shader.SetGlobalColor(UILightColor, classicColour * classicIntensity);
            Shader.SetGlobalColor(UISky, classicSky);
            Shader.SetGlobalColor(UIEquator, classicEquator);
            Shader.SetGlobalColor(UIGround, classicGround);
            Shader.SetGlobalFloat(PlayerRim, playerRim);
            Shader.SetGlobalColor(PlayerRimColor, sunColour);
            Shader.SetGlobalVector(PlayerRimDir, new Vector4(-0.55f, 0.83f, 7f, 0f));

            MakeVolume();
            MakeBeams();
        }

        void Restore()
        {
            if (!captured || sun == null) return;
            if (pixel != null) pixel.SetDirect(false);
            PixelSnap.Enabled = true;
            if (cam != null)
            {
                cam.allowHDR = classicHdr;
                if (camData != null)
                {
                    camData.requiresDepthOption = classicDepthOption;
                    camData.antialiasing = classicAa;
                    camData.antialiasingQuality = classicAaQuality;
                }
            }
            sun.transform.rotation = classicRotation;
            sun.color = classicColour;
            sun.intensity = classicIntensity;
            sun.shadows = classicShadows;
            sun.shadowStrength = classicShadowStrength;
            sun.cookie = null;
            if (sunData != null) { sunData.lightCookieSize = classicCookieSize; sunData.lightCookieOffset = classicCookieOffset; }
            RenderSettings.skybox = classicSkybox;
            RenderSettings.sun = classicSun;
            RenderSettings.ambientMode = classicMode;
            RenderSettings.ambientIntensity = classicAmbientIntensity;
            RenderSettings.ambientSkyColor = classicSky;
            RenderSettings.ambientEquatorColor = classicEquator;
            RenderSettings.ambientGroundColor = classicGround;
            RenderSettings.ambientProbe = classicProbe;
            RenderSettings.defaultReflectionMode = classicReflectMode;
            RenderSettings.customReflectionTexture = classicReflection;
            RenderSettings.fog = classicFog;
            RenderSettings.fogMode = classicFogMode;
            RenderSettings.fogColor = classicFogColour;
            RenderSettings.fogDensity = classicFogDensity;
            Ungloss();
            Shader.SetGlobalFloat(Backlight, 0f);
            Shader.SetGlobalFloat(FadeScale, 0f);
            Shader.SetGlobalVector(UILightDir, Vector4.zero);
            Shader.SetGlobalFloat(PlayerRim, 0f);
            if (beams != null) beams.SetActive(false);
            if (probeObject != null) probeObject.SetActive(false);
            if (volumeObject != null) volumeObject.SetActive(false);
        }

        // ---- Pieces ---------------------------------------------------------------------------------------------------

        void MakeProbe()
        {
            if (probeObject == null)
            {
                probeObject = new GameObject("Realistic Reflections");
                probe = probeObject.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                probe.resolution = 128;
                probe.hdr = true;
                probe.boxProjection = true;
                probe.size = new Vector3(40f, 24f, 30f);
                probe.importance = 10;
                probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                int skip = 0;
                foreach (var l in new[] { "UI", "HUD3D", PixelRenderer.PersonLayer, PixelRenderer.CompositeLayer })
                {
                    int i = LayerMask.NameToLayer(l);
                    if (i >= 0) skip |= 1 << i;
                }
                probe.cullingMask = ~skip;
            }
            probeObject.transform.position = new Vector3(0f, 1f, 2f);
            probeObject.SetActive(true);
            probe.RenderProbe();
            probeTimer = 2f;
        }

        void MakeVolume()
        {
            if (volumeObject == null)
            {
                volumeObject = new GameObject("Realistic Post");
                var v = volumeObject.AddComponent<Volume>();
                v.isGlobal = true;
                v.priority = 100f;
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.DontSave;
                var tm = profile.Add<Tonemapping>(true); tm.mode.value = TonemappingMode.ACES;
                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.value = 0.9f; bloom.threshold.value = 0.95f; bloom.scatter.value = 0.75f;
                bloom.tint.value = new Color(1f, 0.9f, 0.78f);
                var dof = profile.Add<DepthOfField>(true);
                dof.mode.value = DepthOfFieldMode.Bokeh;
                dof.focusDistance.value = focusDistance;
                dof.aperture.value = aperture;
                dof.focalLength.value = 70f;
                var grade = profile.Add<ColorAdjustments>(true);
                grade.postExposure.value = 0.15f; grade.contrast.value = 18f; grade.saturation.value = 12f;
                var wb = profile.Add<WhiteBalance>(true); wb.temperature.value = 8f;
                var smh = profile.Add<ShadowsMidtonesHighlights>(true);
                smh.shadows.value = new Vector4(0.92f, 0.98f, 1.08f, -0.04f);
                smh.highlights.value = new Vector4(1.06f, 1f, 0.92f, 0f);
                var vig = profile.Add<Vignette>(true); vig.intensity.value = 0.3f; vig.smoothness.value = 0.45f;
                var grain = profile.Add<FilmGrain>(true); grain.type.value = FilmGrainLookup.Thin1; grain.intensity.value = 0.12f;
                var ca = profile.Add<ChromaticAberration>(true); ca.intensity.value = 0.06f;
                v.sharedProfile = profile;
            }
            volumeObject.SetActive(true);
        }

        void MakeBeams()
        {
            if (volumetricShader == null || cam == null) return;
            if (beams == null)
            {
                beams = GameObject.CreatePrimitive(PrimitiveType.Quad);
                beams.name = "Volumetric Sun";
                var col = beams.GetComponent<Collider>();
                if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
                beams.transform.SetParent(cam.transform, false);
                beamsMaterial = new Material(volumetricShader) { name = "Volumetric Sun", hideFlags = HideFlags.DontSave };
                var r = beams.GetComponent<MeshRenderer>();
                r.sharedMaterial = beamsMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            beamsMaterial.SetFloat("_Density", beamDensity);
            beamsMaterial.SetFloat("_Steps", 20f); // jittered, so 20 samples a ray look as smooth as 32
            beamsMaterial.SetColor("_Tint", sunColour);
            beams.SetActive(true);
            FitBeams();
        }

        /// <summary>The beams' quad covers the view just past the near plane (the shader marches the whole ray).</summary>
        void FitBeams()
        {
            if (beams == null || cam == null) return;
            float d = cam.nearClipPlane + 0.05f;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
            beams.transform.localPosition = new Vector3(0f, 0f, d);
            beams.transform.localRotation = Quaternion.identity;
            beams.transform.localScale = new Vector3(h * Mathf.Max(cam.aspect, 2f), h, 1f);
        }

        /// <summary>Swaps every URP Lit material in the scene for a glossy plastic copy (put back by Ungloss).</summary>
        void Gloss()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (glossed.ContainsKey(r)) continue;
                var mats = r.sharedMaterials;
                bool any = false;
                var swapped = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    swapped[i] = m;
                    if (m == null || m.shader != lit) continue;
                    if (!glossCopies.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (plastic)", hideFlags = HideFlags.DontSave };
                        copy.SetFloat("_Smoothness", smoothness);
                        copy.SetFloat("_Metallic", 0f);
                        copy.SetFloat("_EnvironmentReflections", 1f);
                        copy.SetFloat("_SpecularHighlights", 1f);
                        copy.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                        copy.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
                        glossCopies[m] = copy;
                    }
                    swapped[i] = copy;
                    any = true;
                }
                if (!any) continue;
                glossed[r] = mats;
                r.sharedMaterials = swapped;
            }
        }

        void Ungloss()
        {
            foreach (var kv in glossed) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
            glossed.Clear();
            foreach (var c in glossCopies.Values) Free(c);
            glossCopies.Clear();
        }

        /// <summary>Light through gaps in the leaves and clouds overhead: soft blotches, filtered smooth.</summary>
        static Texture2D MakeDapple()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true, true)
            {
                name = "Sun Dapple", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave,
            };
            var rng = new System.Random(7);
            float[,] Grid(int cells)
            {
                var g = new float[cells, cells];
                for (int y = 0; y < cells; y++) for (int x = 0; x < cells; x++) g[x, y] = (float)rng.NextDouble();
                return g;
            }
            float Sample(float[,] g, int cells, float u, float v)
            {
                float x = u * cells, y = v * cells;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = Mathf.SmoothStep(0f, 1f, x - x0), fy = Mathf.SmoothStep(0f, 1f, y - y0);
                float G(int i, int j) => g[((i % cells) + cells) % cells, ((j % cells) + cells) % cells];
                return Mathf.Lerp(Mathf.Lerp(G(x0, y0), G(x0 + 1, y0), fx), Mathf.Lerp(G(x0, y0 + 1), G(x0 + 1, y0 + 1), fx), fy);
            }
            var coarse = Grid(5);
            var fine = Grid(16);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float n = 0.6f * Sample(coarse, 5, u, v) + 0.4f * Sample(fine, 16, u, v);
                    float level = Mathf.Lerp(0.45f, 1f, Mathf.SmoothStep(0.36f, 0.52f, n));
                    byte b = (byte)Mathf.RoundToInt(level * 255f);
                    px[y * N + x] = new Color32(b, b, b, b);
                }
            tex.SetPixels32(px);
            tex.Apply(true, false);
            return tex;
        }

        void OnDestroy() => TearDown();

        void TearDown()
        {
            if (applied == LightingStyle.Realistic) Restore();
            applied = null;
            Shader.SetGlobalFloat(Backlight, 0f);
            Shader.SetGlobalFloat(FadeScale, 0f);
            Shader.SetGlobalVector(UILightDir, Vector4.zero);
            Shader.SetGlobalFloat(PlayerRim, 0f);
            Free(beams); Free(beamsMaterial); Free(cookie); Free(skybox); Free(probeObject); Free(volumeObject); Free(profile);
            beams = null; beamsMaterial = null; cookie = null; skybox = null; probeObject = null; probe = null; volumeObject = null; profile = null;
        }

        static void Free(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ---- Edit-mode preview (IEditPreview) -------------------------------------------------------------------------

        public int PreviewOrder => 10; // after the pixel render is built, so it can switch it to direct

        public void BuildPreview()
        {
            captured = false;
            Capture();
            Apply(Wanted);
        }

        public void EndPreview()
        {
            TearDown();
            captured = false;
        }
    }
}
