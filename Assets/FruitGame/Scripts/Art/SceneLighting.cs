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
    ///  - a near-white midday sun from behind the player (the camera's side), 38° up, real-time soft shadows at full strength, coming
    ///    through a dense canopy of leaf blocks overhead (unseen, shadows only) so it falls in spots and shafts;
    ///  - a procedural sky that lights the scene (ambient from the sky) and a live reflection probe of the scene;
    ///  - every block shaded physically as glossy plastic (VoxelLight.hlsl; URP Lit scenery gets glossy copies), with
    ///    screen-space ambient occlusion, light glowing through leaves, and distance fog;
    ///  - volumetric sun beams marched through the shadow map (VolumetricLight.shader);
    ///  - post-processing: ACES tonemapping, bloom, slight depth of field, grading, and vignette (film grain was dropped: its tiled pattern showed as a grid);
    ///  - sunlit rims on falling fruit;
    ///  - software ray tracing through a voxel grid of the scene for reflections and the bounce light
    ///    (SceneLighting.RayTracing.cs).
    /// The title, buttons and digits keep their classic light either way. Everything is put back for Classic.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public partial class SceneLighting : MonoBehaviour, IEditPreview
    {
        [SerializeField] GameConfig config;
        [SerializeField] Light sun;
        [SerializeField] Shader volumetricShader;
        [SerializeField] Shader plasticShader;
        [SerializeField] Shader sunFluxShader;
        [SerializeField] ComputeShader bounceCompute;
        [SerializeField] Shader skyDepthShader;
        [Tooltip("The dimmer behind the physical UI's signs (PhysicalUI).")]
        [SerializeField] Shader veilShader;

        [Header("Sky shadows and bounce light")]
        [Tooltip("Where the sky's light comes from (pitch, yaw): overhead, so whatever is under a crown or an overhang is in sky shadow (tilted toward the camera, the faces we see all looked out from under the trees).")]
        [SerializeField] Vector2 skyAngles = new Vector2(89f, 0f);
        [Tooltip("How soft the sky shadows are (metres of blur) and how dark fully covered places get (0..1).")]
        [SerializeField] float skyShadowSoftness = 1.5f;
        [SerializeField] float skyShadowFloor = 0f;
        [Tooltip("How much darker sky shadows get (power on the share of sky a point sees; 1 = linear), and the distance from the camera (metres) over which that eases back, so the far scenery stays clear.")]
        [SerializeField] float skyShadowDepth = 2.5f;
        [SerializeField] Vector2 skyShadowDepthFade = new Vector2(18f, 26f);
        [Tooltip("Share of the fill lights (bounce, the cool underlight) that places fully under sky shadow keep.")]
        [SerializeField] float skyShadowFill = 0.4f;
        [Tooltip("Sunlight bouncing between objects: strength (1 = physically one bounce; pushed well past that for the look) and how far a sunlit surface's light reaches (metres).")]
        [SerializeField] float bounceStrength = 40f;
        [SerializeField] float bounceRadius = 6f;
        [SerializeField] Shader skyShader;

        [Header("Sun")]
        [Tooltip("From behind the player (the camera's side), 38° up and turned 10°: the sun lights the faces of the greenery toward the camera.")]
        [SerializeField] Vector3 sunAngles = new Vector3(38f, 10f, 0f);
        [SerializeField] Color sunColour = new Color(1f, 0.97f, 0.92f); // midday: near white
        [SerializeField] float sunIntensity = 3.6f;

        [Header("Materials")]
        [Tooltip("Glossy plastic: sharp highlights and clear reflections.")]
        [SerializeField] float smoothness = 0.78f;
        [SerializeField] Color glowTint = new Color(0.85f, 1f, 0.55f);
        [Tooltip("Warm edge on the player's sun side (0 = off: on a real camera image it read as a glowing outline).")]
        [SerializeField] float playerRim = 0f;

        [Header("Sky and air")]
        [SerializeField] Color skyTint = new Color(0.45f, 0.55f, 0.7f);
        [SerializeField] float skyExposure = 1.25f;
        [Tooltip("Sky light (the diffuse light, beside the direct sun) on the shaded faces, 1 = as the sky gives it: lower = darker shade.")]
        [SerializeField] float skyLight = 2.0f;
        [SerializeField] Color fogColour = new Color(0.78f, 0.75f, 0.66f);
        [Tooltip("Colour the fruit pile and its crates fade toward in this look (dark and warm, so they sit back).")]
        [SerializeField] Color hazeColour = new Color(0.36f, 0.33f, 0.27f);
        [SerializeField] float fogDensity = 0.02f;
        [Tooltip("Volumetric beams: density of the air the sun lights.")]
        [SerializeField] float beamDensity = 0.085f;
        [Tooltip("Cool diffuse light from below, as if reflected off the ground, on the undersides of the trees: colour and strength.")]
        [SerializeField] Color underLight = new Color(0.62f, 0.74f, 0.95f);
        [SerializeField] float underLightIntensity = 3f;

        [Header("Canopy the sun comes through")]
        [Tooltip("How far toward the sun the canopy sits from the play area (metres).")]
        [SerializeField] float canopyDistance = 10f;
        [Tooltip("Share of the canopy that's open to the sky: lower = fewer, narrower shafts.")]
        [SerializeField] float canopyOpen = 0.12f;
        [SerializeField] float canopySize = 46f;
        [Tooltip("Size of a leaf block (metres): smaller = finer-grained spots of light.")]
        [SerializeField] float leafBlock = 0.12f;
        [Tooltip("How far each layer of leaves sways (metres): the layers move separately, so the gaps open and close.")]
        [SerializeField] float canopySway = 0.35f;

        [Header("Camera")]
        [Tooltip("Background blur: distance from the camera where it starts and where it's full (the play plane is about 12 m), and how strong.")]
        [SerializeField] float backgroundBlurStart = 13f;
        [SerializeField] float backgroundBlurEnd = 22f;
        [SerializeField] float backgroundBlur = 1.2f;

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
        CameraOverrideOption classicDepthOption, classicColorOption;
        bool classicHdr;
        AntialiasingMode classicAa;
        AntialiasingQuality classicAaQuality;

        LightingStyle? applied;
        UniversalAdditionalLightData sunData;
        UniversalAdditionalCameraData camData;
        Camera cam;
        PixelRenderer pixel;
        GameObject canopy;
        Mesh[] canopyMeshes;
        Transform[] canopyLayers;
        Material canopyMaterial;
        Vector3 canopyHome;
        Material skybox, beamsMaterial;
        GameObject beams, probeObject, volumeObject;
        ReflectionProbe probe;
        VolumeProfile profile;
        float probeTimer;
        readonly Dictionary<Renderer, Material[]> glossed = new Dictionary<Renderer, Material[]>();
        readonly Dictionary<Material, Material> glossCopies = new Dictionary<Material, Material>();

        static readonly int Backlight = Shader.PropertyToID("_VoxelBacklight");
        static readonly int Smoothness = Shader.PropertyToID("_VoxelSmoothness");
        static readonly int FadeScale = Shader.PropertyToID("_VoxelFadeScale");
        static readonly int HazeColour = Shader.PropertyToID("_VoxelHazeColor");
        static readonly int UnderLight = Shader.PropertyToID("_VoxelUnderLight");
        static readonly int GlowTint = Shader.PropertyToID("_VoxelGlowTint");
        static readonly int GlowDepth = Shader.PropertyToID("_VoxelGlowDepth");
        static readonly int UILightDir = Shader.PropertyToID("_UILightDir");
        static readonly int UIStageLight = Shader.PropertyToID("_UIStageLight");
        static readonly int UIGlowTint = Shader.PropertyToID("_UIGlowTint");
        static readonly int UILightColor = Shader.PropertyToID("_UILightColor");
        static readonly int UISky = Shader.PropertyToID("_UIAmbientSky");
        static readonly int UIEquator = Shader.PropertyToID("_UIAmbientEquator");
        static readonly int UIGround = Shader.PropertyToID("_UIAmbientGround");
        static readonly int PlayerRim = Shader.PropertyToID("_PlayerRim");
        static readonly int PlayerRimColor = Shader.PropertyToID("_PlayerRimColor");
        static readonly int PlayerRimDir = Shader.PropertyToID("_PlayerRimDir");

        LightingStyle Wanted => config != null ? config.lightingStyle : LightingStyle.Classic;

        void Awake() => Capture();

        void OnEnable() => HighContrast.Changed += ShowButtonLight;
        void OnDisable() => HighContrast.Changed -= ShowButtonLight;

        // Realistic look: the buttons take the scene's sun and sky plus a glow of their own; with high contrast on (or
        // in the classic look) they keep their own fixed light, as before.
        bool stageLitButtons;
        [Header("Buttons in the realistic look")]
        [Tooltip("Buttons' own glow, and how much of the sun and the sky's light they take (high contrast turns all this off).")]
        [SerializeField] float buttonGlow = 0.12f;
        [Tooltip("Colour of the buttons' own glow (warm).")]
        [SerializeField] Color buttonGlowTint = new Color(1f, 0.72f, 0.45f);
        [SerializeField] float buttonSun = 0.18f;
        [SerializeField] float buttonSky = 0.35f;

        void ShowButtonLight()
        {
            Shader.SetGlobalVector(UIStageLight, stageLitButtons && !HighContrast.On ? new Vector4(1f, buttonGlow, buttonSun, buttonSky) : Vector4.zero);
            Shader.SetGlobalColor(UIGlowTint, buttonGlowTint.linear);
            PhysicalUI.ButtonGlow = buttonGlowTint.linear * buttonGlow;
        }

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
            if (sunData != null) { classicCookieSize = sunData.lightCookieSize; classicCookieOffset = sunData.lightCookieOffset; classicSoftQuality = sunData.softShadowQuality; }
            cam = Camera.main;
            if (cam != null)
            {
                pixel = cam.GetComponent<PixelRenderer>();
                camData = cam.GetComponent<UniversalAdditionalCameraData>();
                classicHdr = cam.allowHDR;
                if (camData != null)
                {
                    classicDepthOption = camData.requiresDepthOption;
                    classicColorOption = camData.requiresColorOption;
                    classicAa = camData.antialiasing;
                    classicAaQuality = camData.antialiasingQuality;
                }
            }
        }

        void Update()
        {
            if (sun == null) return;
            // Standby: the guest screen is covered and its cameras are off, so the lighting rests too (a change of look
            // made meanwhile is applied when the booth opens, behind the loading screen).
            if (Standby.Idle) return;
            if (applied != Wanted) Apply(Wanted);
            if (applied != LightingStyle.Realistic) return;
            // Each layer of leaves sways on its own, with gusts, so the gaps between them open and close and the
            // spots of light flicker and wander like sun through leaves in a breeze.
            if (Application.isPlaying && canopyLayers != null)
            {
                float t = Time.time;
                float gust = 0.6f + 0.4f * Mathf.PerlinNoise(t * 0.15f, 3.7f);
                for (int i = 0; i < canopyLayers.Length; i++)
                {
                    if (canopyLayers[i] == null) continue;
                    float a = canopySway * gust * (1f - 0.25f * i), ph = i * 2.1f;
                    canopyLayers[i].localPosition = new Vector3(
                        a * (Mathf.Sin(t * (0.9f + 0.37f * i) + ph) + 0.4f * Mathf.Sin(t * (2.3f + 0.6f * i) + ph * 1.7f)),
                        a * 0.6f * (Mathf.Sin(t * (0.7f + 0.29f * i) + ph * 0.6f) + 0.4f * Mathf.Sin(t * (1.9f + 0.5f * i) + ph)),
                        0f);
                }
            }
            // The reflections follow the scene (the pile grows) every 2 s: each refresh renders the scene six times.
            probeTimer -= Time.unscaledDeltaTime;
            if (probe != null && probeTimer <= 0f) { probe.RenderProbe(); GatherSkyCasters(); probeTimer = 2f; }
            using (PerfMeter.Measure(PerfMeter.Part.Lighting))
            {
                RenderSkyOcclusion();
                FollowRayTracingSwitch();
                VoxelizeScene();
                TickBounce();
                FitBeams();
            }
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
            // Cheaper soft-shadow filtering (0.45 ms a frame less than High, the same softness to the eye) and
            // half-resolution SSAO (0.6 ms less; it's a faint, blurred darkening anyway). Realistic only: the classic
            // look keeps the pipeline's own settings.
            if (sunData != null) sunData.softShadowQuality = SoftShadowQuality.Medium;
            SetSsaoDownsample(true);
            MakeCanopy();

            // Sky: lights the scene and is reflected in it.
            if (skybox == null && skyShader != null)
            {
                skybox = new Material(skyShader) { name = "Realistic Sky", hideFlags = HideFlags.DontSave };
                skybox.SetFloat("_SunDisk", 2f);
                skybox.SetFloat("_SunSize", 0.05f);
                skybox.SetFloat("_SunSizeConvergence", 6f);
                skybox.SetFloat("_AtmosphereThickness", 1.15f);
                skybox.SetColor("_SkyTint", skyTint);
                skybox.SetColor("_GroundColor", new Color(0.3f, 0.3f, 0.28f));
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
            Shader.SetGlobalVector(UnderLight, underLight.linear * underLightIntensity);
            Shader.SetGlobalFloat(FadeScale, 0.0001f); // fog does the distance now
            // The pile's haze (and any kept fade) takes a dark warm colour, not the classic sky blue, so the pile and its
            // crates sink back behind the play plane.
            Shader.SetGlobalVector(HazeColour, new Vector4(hazeColour.linear.r, hazeColour.linear.g, hazeColour.linear.b, 1f));
            Shader.SetGlobalColor(GlowTint, glowTint);
            Shader.SetGlobalFloat(GlowDepth, 0.45f);
            // The title, buttons and digits keep the classic light.
            var classicDir = -(classicRotation * Vector3.forward);
            Shader.SetGlobalVector(UILightDir, new Vector4(classicDir.x, classicDir.y, classicDir.z, 1f));
            stageLitButtons = true;
            ShowButtonLight();
            PhysicalUI.VeilShader = veilShader;
            PhysicalUI.SetRealistic(true);
            Shader.SetGlobalColor(UILightColor, classicColour * classicIntensity);
            Shader.SetGlobalColor(UISky, classicSky);
            Shader.SetGlobalColor(UIEquator, classicEquator);
            Shader.SetGlobalColor(UIGround, classicGround);
            Shader.SetGlobalFloat(PlayerRim, playerRim);
            Shader.SetGlobalColor(PlayerRimColor, sunColour);
            Shader.SetGlobalVector(PlayerRimDir, new Vector4(-0.55f, 0.83f, 7f, 0f));

            MakeVolume();
            MakeBeams();
            MakeBounce();
            MakeSkyOcclusion();
            MakeRayTracing();
        }

        SoftShadowQuality classicSoftQuality = SoftShadowQuality.UsePipelineSettings;
        bool? classicSsaoDownsample;

        /// <summary>
        /// Switches the renderer's SSAO between full and half resolution (the URP feature's own Downsample setting, which
        /// has no public setter), remembering the original so the classic look gets it back exactly.
        /// </summary>
        void SetSsaoDownsample(bool realistic)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", Any)?.GetValue(asset) as ScriptableRendererData[];
            if (list == null) return;
            foreach (var data in list)
            {
                if (data == null) continue;
                foreach (var feature in data.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    var settings = feature.GetType().GetField("m_Settings", Any)?.GetValue(feature);
                    var field = settings?.GetType().GetField("Downsample", Any);
                    if (field == null) continue;
                    if (realistic)
                    {
                        classicSsaoDownsample ??= (bool)field.GetValue(settings);
                        field.SetValue(settings, true);
                    }
                    else if (classicSsaoDownsample.HasValue) field.SetValue(settings, classicSsaoDownsample.Value);
                }
            }
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
                    camData.requiresColorOption = classicColorOption;
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
            if (sunData != null) { sunData.lightCookieSize = classicCookieSize; sunData.lightCookieOffset = classicCookieOffset; sunData.softShadowQuality = classicSoftQuality; }
            SetSsaoDownsample(false);
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
            Shader.SetGlobalVector(HazeColour, Vector4.zero);
            Shader.SetGlobalVector(UnderLight, Vector4.zero);
            Shader.SetGlobalVector(UILightDir, Vector4.zero);
            stageLitButtons = false;
            ShowButtonLight();
            PhysicalUI.SetRealistic(false);
            Shader.SetGlobalFloat(PlayerRim, 0f);
            if (beams != null) beams.SetActive(false);
            // The canopy only casts shadows: left on, it shaded the whole classic scene flat and dark after a switch back.
            if (canopy != null) canopy.SetActive(false);
            Shader.SetGlobalVector(BounceDims, Vector4.zero);
            Shader.SetGlobalVector(SkyParams, Vector4.zero);
            StopRayTracing();
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
                bloom.tint.value = Color.white;
                var dof = profile.Add<DepthOfField>(true);
                // Far-only blur: everything behind the play plane softens a little, the player, the falling fruit
                // and the foreground stay sharp (a Bokeh focus blurred the front too).
                dof.mode.value = DepthOfFieldMode.Gaussian;
                dof.gaussianStart.value = backgroundBlurStart;
                dof.gaussianEnd.value = backgroundBlurEnd;
                dof.gaussianMaxRadius.value = backgroundBlur;
                dof.highQualitySampling.value = true;
                var grade = profile.Add<ColorAdjustments>(true);
                grade.postExposure.value = 0.15f; grade.contrast.value = 18f; grade.saturation.value = 12f;
                var wb = profile.Add<WhiteBalance>(true); wb.temperature.value = 0f;
                var smh = profile.Add<ShadowsMidtonesHighlights>(true);
                smh.shadows.value = new Vector4(0.97f, 0.99f, 1.04f, -0.1f); // deeper darks
                smh.highlights.value = new Vector4(1f, 1f, 1f, 0f);
                var vig = profile.Add<Vignette>(true); vig.intensity.value = 0.3f; vig.smoothness.value = 0.45f;
                var ca = profile.Add<ChromaticAberration>(true); ca.intensity.value = 0.06f;
                v.sharedProfile = profile;
            }
            volumeObject.SetActive(true);
        }

        // ---- Sky shadows ----------------------------------------------------------------------------------------------

        static readonly int SkyMap = Shader.PropertyToID("_SkyOcclusionMap");
        static readonly int SkyMatrix = Shader.PropertyToID("_SkyOcclusionMatrix");
        static readonly int SkyParams = Shader.PropertyToID("_SkyOcclusionParams");
        static readonly int SkyShape = Shader.PropertyToID("_SkyShadowShape");
        static readonly int SkyOriginId = Shader.PropertyToID("_SkyOrigin");
        static readonly int SkyForwardId = Shader.PropertyToID("_SkyForward");
        const float SkySize = 16f, SkyAspect = 1.3f, SkyDepthRange = 80f;
        /// <summary>The full-screen quad (beams) sits on this layer, which the sky camera doesn't see.</summary>
        static int OverlayLayer => Mathf.Max(0, LayerMask.NameToLayer("Ignore Raycast"));

        RenderTexture skyDepth;
        Material skyDepthMaterial;
        Matrix4x4 skyView, skyProj;
        Vector3 skyOrigin, skyForward;

        /// <summary>
        /// The sky shadow map: every scenery renderer drawn from the sky's direction (orthographic, over the play area
        /// and the orchard) with Hidden/FruitGame/Sky Depth, by a command buffer, so it doesn't depend on URP's camera
        /// path (a URP camera rendering into a texture doesn't keep its depth). Still scenery is cached; see RenderSkyOcclusion.
        /// </summary>
        void MakeSkyOcclusion()
        {
            if (skyDepthShader == null) return;
            if (skyDepth == null)
            {
                skyDepth = new RenderTexture(1024, Mathf.RoundToInt(1024 / SkyAspect), 24, RenderTextureFormat.RFloat)
                {
                    name = "Sky Shadow Depth", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
                };
                skyDepth.Create();
                skyStatic = new RenderTexture(skyDepth.descriptor) { name = "Sky Shadow Depth (still)", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                skyStatic.Create();
                skyDepthMaterial = new Material(skyDepthShader) { name = "Sky Depth", hideFlags = HideFlags.DontSave };
            }
            var rot = Quaternion.Euler(skyAngles.x, skyAngles.y, 0f);
            skyForward = rot * Vector3.forward;
            skyOrigin = new Vector3(0f, 0f, 4f) - skyForward * (SkyDepthRange * 0.5f);
            // View matrix in Unity's camera convention (looking down −z).
            skyView = Matrix4x4.TRS(skyOrigin, rot, new Vector3(1f, 1f, -1f)).inverse;
            skyProj = Matrix4x4.Ortho(-SkySize * SkyAspect, SkySize * SkyAspect, -SkySize, SkySize, 0.1f, SkyDepthRange);
            RenderSkyOcclusion();
        }

        // What the sky map draws, gathered every 2 s (the pile grows); the map itself is redrawn every frame from it, so
        // anything that moves (fruit, swaying plants) moves its sky shadow smoothly. Redrawn only every 2 s, the
        // shadows on the greenery jumped at each redraw.
        sealed class SkyCaster
        {
            public Renderer renderer;
            public int submeshes;
            public Matrix4x4 matrix;   // where it was when the still map was drawn
            public bool shown;
            public bool moves;         // drawn every frame rather than into the still map
            public VoxSurface[] surfaces; // its look in the ray tracing grid (SceneLighting.RayTracing.cs)
        }

        readonly List<SkyCaster> skyCasters = new List<SkyCaster>();
        /// <summary>Renderers seen moving: from then on they're drawn every frame instead of into the still map.</summary>
        readonly HashSet<Renderer> skyMovers = new HashSet<Renderer>();
        /// <summary>The still scenery's part of the sky map, redrawn only when something in it changes.</summary>
        RenderTexture skyStatic;
        bool skyStaticDirty = true;

        static bool Shown(Renderer r) => r != null && r.enabled && r.gameObject.activeInHierarchy;

        void GatherSkyCasters()
        {
            skyCasters.Clear();
            int skip = 0;
            // The far orchard is on the Back layer with the camera backdrop: keep the layer, leave out the backdrop.
            foreach (var l in new[] { "UI", "HUD3D", PixelRenderer.PersonLayer, PixelRenderer.CompositeLayer, "Ignore Raycast" })
            {
                int i = LayerMask.NameToLayer(l);
                if (i >= 0) skip |= 1 << i;
            }
            var backdrop = FindAnyObjectByType<BokehBackdrop>();
            // The canopy band and ground bank (SceneryBands) sit off screen in landscape; the band would roof over the
            // whole play area and put everything equally in sky shadow, so they're left out.
            var bands = FindAnyObjectByType<SceneryBands>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if ((skip & (1 << r.gameObject.layer)) != 0) continue;
                if (bands != null && r.transform.IsChildOf(bands.transform)) continue;
                if (backdrop != null && r.transform.IsChildOf(backdrop.transform)) continue;
                if (!(r is MeshRenderer) || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                skyCasters.Add(new SkyCaster
                {
                    renderer = r, submeshes = mf.sharedMesh.subMeshCount, matrix = r.localToWorldMatrix,
                    shown = Shown(r), moves = skyMovers.Contains(r),
                });
            }
            skyStaticDirty = true;
        }

        CommandBuffer skyBuffer;

        void RenderSkyOcclusion()
        {
            if (skyDepth == null) return;
            if (skyCasters.Count == 0) GatherSkyCasters();
            Shader.SetGlobalVector(SkyOriginId, skyOrigin);
            Shader.SetGlobalVector(SkyForwardId, new Vector4(skyForward.x, skyForward.y, skyForward.z, 1f / SkyDepthRange));
            // Of the ~600 things in the map, ~350 never move but hold nearly all of its 1.6 million vertices; redrawing
            // them all every frame cost about 1.1 ms. So the still ones go into a cached map, redrawn only when one of them
            // moves (it then joins the moving ones), appears or disappears, and when the list is gathered again (every 2 s);
            // each frame the cache is copied in and only the moving ones are drawn over it.
            foreach (var c in skyCasters)
            {
                if (c.moves) continue;
                bool shown = Shown(c.renderer);
                if (shown != c.shown) { c.shown = shown; skyStaticDirty = true; }
                if (shown && c.renderer.localToWorldMatrix != c.matrix)
                {
                    c.moves = true;
                    skyMovers.Add(c.renderer);
                    skyStaticDirty = true;
                }
            }
            var cb = skyBuffer ??= new CommandBuffer { name = "Sky Shadow Map" };
            cb.Clear();
            if (skyStaticDirty)
            {
                cb.SetRenderTarget(skyStatic);
                cb.ClearRenderTarget(false, true, Color.white); // 1 = nothing in the way
                cb.SetViewProjectionMatrices(skyView, skyProj);
                foreach (var c in skyCasters)
                {
                    if (c.moves || !c.shown || c.renderer == null) continue;
                    c.matrix = c.renderer.localToWorldMatrix;
                    for (int s = 0; s < c.submeshes; s++) cb.DrawRenderer(c.renderer, skyDepthMaterial, s, 0);
                }
                skyStaticDirty = false;
            }
            cb.CopyTexture(skyStatic, skyDepth);
            cb.SetRenderTarget(skyDepth);
            cb.SetViewProjectionMatrices(skyView, skyProj);
            foreach (var c in skyCasters)
            {
                if (!c.moves || !Shown(c.renderer)) continue;
                for (int s = 0; s < c.submeshes; s++) cb.DrawRenderer(c.renderer, skyDepthMaterial, s, 0);
            }
            Graphics.ExecuteCommandBuffer(cb);
            Shader.SetGlobalTexture(SkyMap, skyDepth);
            Shader.SetGlobalMatrix(SkyMatrix, skyProj * skyView);
            float blurUv = skyShadowSoftness / (2f * SkySize * SkyAspect);
            Shader.SetGlobalVector(SkyParams, new Vector4(1f, blurUv, 0.08f / SkyDepthRange, skyShadowFloor));
            Shader.SetGlobalVector(SkyShape, new Vector4(skyShadowDepth, skyShadowDepthFade.x, skyShadowDepthFade.y, skyShadowFill));
        }

        // ---- Bounce light ----------------------------------------------------------------------------------------------

        // World space and deterministic, so nothing flickers or trails: the scenery is drawn from the sun into a small
        // map (Hidden/FruitGame/Sun Flux: what each sunlit texel sends back out, where it is, which way it faces), and
        // BounceVolume.compute adds up, for every cell of a 0.5 m grid over the visible scene, the light all those
        // patches send it from six directions. Shaders read the grid at their position and normal (VoxelLight.hlsl).
        // Kept current continuously (TickBounce): built a few slices a frame and crossfaded in, so it never steps.
        static readonly int BounceVolumeId = Shader.PropertyToID("_BounceVolume");
        static readonly int BounceVolumeNextId = Shader.PropertyToID("_BounceVolumeNext");
        static readonly int BounceBlendId = Shader.PropertyToID("_BounceBlend");
        static readonly int BounceMin = Shader.PropertyToID("_BounceVolumeMin");
        static readonly int BounceDims = Shader.PropertyToID("_BounceVolumeDims");
        static readonly Vector3 BounceLow = new Vector3(-10f, -5f, -3f), BounceHigh = new Vector3(10f, 7f, 11f);
        const float BounceCell = 0.5f, FluxHalfSize = 14f, FluxDepthRange = 80f;
        const int FluxSize = 128, FluxStep = 2;

        RenderTexture fluxColour, fluxPosition, fluxNormal, fluxDepth;
        // Three grids: the one fading out, the one fading in, and the next one being built a few slices a frame.
        RenderTexture[] bounceVolumes;
        int bounceFrom, bounceTo = 1, bounceBuild = 2, buildLayer = -1;
        float bounceBlend = 1f;
        const int LayersPerFrame = 2;
        const float BounceFade = 2f;
        Material sunFluxMaterial;
        Vector3Int bounceDims;

        void MakeBounce()
        {
            if (sunFluxShader == null || bounceCompute == null || !SystemInfo.supportsComputeShaders) return;
            // Each piece checked on its own: an Editor script reload can keep some of them and drop others.
            if (bounceVolumes == null || sunFluxMaterial == null || fluxColour == null || fluxDepth == null
                || System.Array.Exists(bounceVolumes, v => v == null))
            {
                if (bounceVolumes != null) foreach (var v in bounceVolumes) Free(v);
                Free(fluxColour); Free(fluxPosition); Free(fluxNormal); Free(fluxDepth); Free(sunFluxMaterial);
                fluxColour = FluxTarget("Sun Flux", RenderTextureFormat.ARGBHalf);
                fluxPosition = FluxTarget("Sun Flux Position", RenderTextureFormat.ARGBFloat);
                fluxNormal = FluxTarget("Sun Flux Normal", RenderTextureFormat.ARGBHalf);
                fluxDepth = new RenderTexture(FluxSize, FluxSize, 24, RenderTextureFormat.Depth) { name = "Sun Flux Depth", hideFlags = HideFlags.DontSave };
                fluxDepth.Create();
                sunFluxMaterial = new Material(sunFluxShader) { name = "Sun Flux", hideFlags = HideFlags.DontSave };
                var size = BounceHigh - BounceLow;
                bounceDims = new Vector3Int(Mathf.CeilToInt(size.x / BounceCell), Mathf.CeilToInt(size.y / BounceCell), Mathf.CeilToInt(size.z / BounceCell));
                bounceVolumes = new RenderTexture[3];
                for (int v = 0; v < 3; v++)
                {
                    bounceVolumes[v] = new RenderTexture(bounceDims.x, bounceDims.y, 0, RenderTextureFormat.ARGBHalf)
                    {
                        name = "Bounce Volume " + v, hideFlags = HideFlags.DontSave, dimension = TextureDimension.Tex3D,
                        volumeDepth = bounceDims.z * 6, enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    };
                    bounceVolumes[v].Create();
                }
            }
            // The first grid is built whole and shown at once; after that, TickBounce keeps it current.
            RenderFlux();
            CompactPatches();
            Gather(bounceVolumes[bounceTo], 0, bounceDims.z);
            Graphics.CopyTexture(bounceVolumes[bounceTo], bounceVolumes[bounceFrom]);
            buildLayer = -1;
            bounceBlend = 1f;
            ShowBounce();
        }

        /// <summary>
        /// Keeps the bounce current without a visible step: a new sun's-eye map, then the next grid built a couple of
        /// slices a frame into the spare buffer, then a 2 s crossfade from the shown grid to it, and again.
        /// </summary>
        void TickBounce()
        {
            if (bounceVolumes == null) return;
            if (buildLayer < 0)
            {
                RenderFlux();
                CompactPatches();
                buildLayer = 0;
            }
            else if (buildLayer < bounceDims.z)
            {
                int n = Mathf.Min(LayersPerFrame, bounceDims.z - buildLayer);
                Gather(bounceVolumes[bounceBuild], buildLayer, n);
                buildLayer += n;
            }
            else if (bounceBlend >= 1f)
            {
                int old = bounceFrom;
                bounceFrom = bounceTo; bounceTo = bounceBuild; bounceBuild = old;
                bounceBlend = 0f;
                buildLayer = -1;
            }
            bounceBlend = Mathf.Min(1f, bounceBlend + Time.unscaledDeltaTime / BounceFade);
            ShowBounce();
        }

        void ShowBounce()
        {
            Shader.SetGlobalTexture(BounceVolumeId, bounceVolumes[bounceFrom]);
            Shader.SetGlobalTexture(BounceVolumeNextId, bounceVolumes[bounceTo]);
            Shader.SetGlobalVector(BounceMin, new Vector4(BounceLow.x, BounceLow.y, BounceLow.z, 1f / BounceCell));
            Shader.SetGlobalVector(BounceDims, new Vector4(bounceDims.x, bounceDims.y, bounceDims.z, bounceStrength));
            Shader.SetGlobalFloat(BounceBlendId, Mathf.SmoothStep(0f, 1f, bounceBlend));
        }

        static RenderTexture FluxTarget(string name, RenderTextureFormat format)
        {
            var rt = new RenderTexture(FluxSize, FluxSize, 0, format)
            {
                name = name, hideFlags = HideFlags.DontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        static readonly int FluxBaseMap = Shader.PropertyToID("_FluxBaseMap");
        static readonly int FluxBaseMapST = Shader.PropertyToID("_FluxBaseMap_ST");
        static readonly int FluxColor = Shader.PropertyToID("_FluxColor");

        /// <summary>Draws the scenery from the sun into the flux map the grid is built from.</summary>
        void RenderFlux()
        {
            if (bounceVolumes == null || sun == null) return;
            var toSun = -sun.transform.forward;
            var centre = (BounceLow + BounceHigh) * 0.5f;
            var rot = Quaternion.LookRotation(-toSun, Vector3.up);
            var view = Matrix4x4.TRS(centre + toSun * (FluxDepthRange * 0.5f), rot, new Vector3(1f, 1f, -1f)).inverse;
            var proj = Matrix4x4.Ortho(-FluxHalfSize, FluxHalfSize, -FluxHalfSize, FluxHalfSize, 0.1f, FluxDepthRange);
            fluxMatrix = proj * view;

            int skip = 0;
            foreach (var l in new[] { "UI", "HUD3D", PixelRenderer.PersonLayer, PixelRenderer.CompositeLayer, "Ignore Raycast" })
            {
                int i = LayerMask.NameToLayer(l);
                if (i >= 0) skip |= 1 << i;
            }
            var backdrop = FindAnyObjectByType<BokehBackdrop>();
            var bands = FindAnyObjectByType<SceneryBands>();
            var cb = new CommandBuffer { name = "Sun Flux" };
            cb.SetRenderTarget(new RenderTargetIdentifier[] { fluxColour, fluxPosition, fluxNormal }, fluxDepth);
            cb.ClearRenderTarget(true, true, Color.clear);
            cb.SetViewProjectionMatrices(view, proj);
            cb.SetGlobalVector("_FluxSunDir", toSun);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || (skip & (1 << r.gameObject.layer)) != 0) continue;
                if (bands != null && r.transform.IsChildOf(bands.transform)) continue;
                if (backdrop != null && r.transform.IsChildOf(backdrop.transform)) continue;
                if (!(r is MeshRenderer)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // The canopy only blocks: drawn black, so the sun's spots on the scene come through its gaps.
                bool blocker = r.shadowCastingMode == ShadowCastingMode.ShadowsOnly;
                var mats = r.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var m = s < mats.Length ? mats[s] : null;
                    Texture tex = Texture2D.whiteTexture; Vector4 st = new Vector4(1, 1, 0, 0); Color c = Color.white;
                    if (m != null)
                    {
                        if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null)
                        {
                            tex = m.GetTexture("_BaseMap");
                            var sc = m.GetTextureScale("_BaseMap"); var of = m.GetTextureOffset("_BaseMap");
                            st = new Vector4(sc.x, sc.y, of.x, of.y);
                        }
                        if (m.HasProperty("_BaseColor")) c = m.GetColor("_BaseColor");
                        else if (m.HasProperty("_Color")) c = m.GetColor("_Color");
                    }
                    cb.SetGlobalTexture(FluxBaseMap, tex);
                    cb.SetGlobalVector(FluxBaseMapST, st);
                    cb.SetGlobalColor(FluxColor, blocker ? Color.black : c.linear);
                    cb.DrawRenderer(r, sunFluxMaterial, s, 0);
                }
            }
            Graphics.ExecuteCommandBuffer(cb);
            cb.Release();
        }

        ComputeBuffer patches, patchCount;

        /// <summary>Collects the sunlit patches of the current flux map into a list (after RenderFlux).</summary>
        void CompactPatches()
        {
            int side = FluxSize / FluxStep;
            if (patches == null)
            {
                patches = new ComputeBuffer(side * side, sizeof(float) * 9, ComputeBufferType.Append);
                patchCount = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
            }
            float patch = 2f * FluxHalfSize / side;
            var light = sun.color.linear * sun.intensity;
            int k = bounceCompute.FindKernel("Compact");
            patches.SetCounterValue(0);
            bounceCompute.SetTexture(k, "_Flux", fluxColour);
            bounceCompute.SetTexture(k, "_Position", fluxPosition);
            bounceCompute.SetTexture(k, "_Normal", fluxNormal);
            bounceCompute.SetBuffer(k, "_PatchesOut", patches);
            bounceCompute.SetVector("_Dims", new Vector4(bounceDims.x, bounceDims.y, bounceDims.z, side));
            bounceCompute.SetVector("_Sun", new Vector4(light.r, light.g, light.b, patch * patch));
            bounceCompute.SetVector("_Shape", new Vector4(bounceRadius, BounceCell, FluxStep, 0));
            bounceCompute.Dispatch(k, (side + 7) / 8, (side + 7) / 8, 1);
            ComputeBuffer.CopyCount(patches, patchCount, 0);
        }

        /// <summary>Fills layers [first, first + count) of a grid from the current patch list.</summary>
        void Gather(RenderTexture volume, int first, int count)
        {
            if (TraceBounce(volume, first, count)) return;
            if (patches == null) return;
            int k = bounceCompute.FindKernel("Gather");
            bounceCompute.SetBuffer(k, "_Patches", patches);
            bounceCompute.SetBuffer(k, "_PatchCount", patchCount);
            bounceCompute.SetTexture(k, "_Volume", volume);
            bounceCompute.SetVector("_Dims", new Vector4(bounceDims.x, bounceDims.y, bounceDims.z, FluxSize / FluxStep));
            bounceCompute.SetVector("_Min", new Vector4(BounceLow.x, BounceLow.y, BounceLow.z, BounceCell));
            bounceCompute.SetVector("_Shape", new Vector4(bounceRadius, BounceCell, FluxStep, first));
            bounceCompute.Dispatch(k, (bounceDims.x + 7) / 8, (bounceDims.y + 7) / 8, count);
        }

        void FitQuad(Transform quad, float pastNear)
        {
            float d = cam.nearClipPlane + pastNear;
            float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
            quad.localPosition = new Vector3(0f, 0f, d);
            quad.localRotation = Quaternion.identity;
            quad.localScale = new Vector3(h * Mathf.Max(cam.aspect, 2f), h, 1f);
        }

        void MakeBeams()
        {
            if (volumetricShader == null || cam == null) return;
            if (beams == null)
            {
                beams = GameObject.CreatePrimitive(PrimitiveType.Quad);
                beams.name = "Volumetric Sun";
                beams.layer = OverlayLayer;
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
            beamsMaterial.SetFloat("_Anisotropy", 0.35f); // the sun is behind the camera: less forward scattering, so the shafts still show
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
                        // The same lighting as every other block (sky shadows included), not URP Lit's.
                        copy = new Material(plasticShader != null ? plasticShader : m.shader) { name = m.name + " (plastic)", hideFlags = HideFlags.DontSave };
                        copy.SetTexture("_BaseMap", m.GetTexture("_BaseMap"));
                        copy.SetTextureScale("_BaseMap", m.GetTextureScale("_BaseMap"));
                        copy.SetTextureOffset("_BaseMap", m.GetTextureOffset("_BaseMap"));
                        copy.SetColor("_BaseColor", m.GetColor("_BaseColor"));
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

        /// <summary>
        /// A dense canopy of leaf blocks between the sun and the scene, facing the sun: three staggered layers of
        /// blocks with clustered gaps (two octaves of noise), so the gaps only partly line up and the light gets through
        /// in irregular spots and shafts. Shadows only: the camera never sees it, it just shapes the sunlight on the
        /// greenery and in the volumetric beams.
        /// </summary>
        void MakeCanopy()
        {
            var toSun = -(Quaternion.Euler(sunAngles) * Vector3.forward);
            canopyHome = new Vector3(0f, 1f, 3f) + toSun * canopyDistance;
            if (canopy == null)
            {
                canopy = new GameObject("Sun Canopy (shadows only)");
                canopyMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sun Canopy", hideFlags = HideFlags.DontSave };
                canopyMaterial.SetFloat("_Cull", 0f); // both sides cast
                canopyMeshes = BuildCanopyMeshes();
                canopyLayers = new Transform[canopyMeshes.Length];
                for (int i = 0; i < canopyMeshes.Length; i++)
                {
                    var layer = new GameObject("Leaves " + (i + 1), typeof(MeshFilter), typeof(MeshRenderer));
                    layer.transform.SetParent(canopy.transform, false);
                    layer.GetComponent<MeshFilter>().sharedMesh = canopyMeshes[i];
                    var mr = layer.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = canopyMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    mr.receiveShadows = false;
                    canopyLayers[i] = layer.transform;
                }
            }
            canopy.transform.SetPositionAndRotation(canopyHome, Quaternion.LookRotation(-toSun, Vector3.up));
            canopy.SetActive(true);
        }

        /// <summary>The three layers of leaves, one mesh each so they can sway separately.</summary>
        Mesh[] BuildCanopyMeshes()
        {
            int cells = Mathf.RoundToInt(canopySize / leafBlock);
            float half = canopySize * 0.5f;
            var rng = new System.Random(19);
            float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
            // Noise per layer and cell; each layer's gaps sit a little apart, so they only partly line up.
            var noise = new float[3, cells, cells];
            for (int layer = 0; layer < 3; layer++)
                for (int y = 0; y < cells; y++)
                    for (int x = 0; x < cells; x++)
                    {
                        float u = x * leafBlock, v = y * leafBlock, shift = layer * 0.37f;
                        // Clumps about 2 m across, led by the large scale so the gaps bunch into a few openings and the
                        // beams come in bundles (5 m clumps left the whole lower scene under one dense patch).
                        noise[layer, x, y] = 0.8f * Mathf.PerlinNoise(ox + u * 0.5f + shift, oy + v * 0.5f)
                                           + 0.2f * Mathf.PerlinNoise(ox + 40f + u * 2.2f, oy + 40f + v * 2.2f + shift);
                    }
            // The threshold that leaves canopyOpen of the whole canopy open to the sun (a cell is open only where all
            // three layers are), found by bisection: stacking three layers each "22 % open" left only 6 % open.
            float lo = 0f, hi = 1f;
            for (int it = 0; it < 20; it++)
            {
                float t = (lo + hi) * 0.5f;
                int open = 0;
                for (int y = 0; y < cells; y++)
                    for (int x = 0; x < cells; x++)
                        if (noise[0, x, y] > t && noise[1, x, y] > t && noise[2, x, y] > t) open++;
                if ((float)open / (cells * cells) > canopyOpen) lo = t; else hi = t;
            }
            float threshold = (lo + hi) * 0.5f;

            var meshes = new Mesh[3];
            for (int layer = 0; layer < 3; layer++)
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                // Each run of leaf blocks along a row is one quad (the shadow is the same, at a fraction of the
                // geometry: one quad per block cost about 8 ms a frame in the shadow pass at 0.12 m blocks).
                for (int y = 0; y < cells; y++)
                    for (int x = 0; x < cells; )
                    {
                        if (noise[layer, x, y] > threshold) { x++; continue; }
                        int start = x;
                        while (x < cells && noise[layer, x, y] <= threshold) x++;
                        float jitter = ((float)rng.NextDouble() - 0.5f) * leafBlock * 0.6f;
                        AddQuad(verts, tris, new Vector3(start * leafBlock - half, y * leafBlock - half, layer * 0.9f + jitter),
                            (x - start) * leafBlock, leafBlock);
                    }
                var mesh = new Mesh { name = "Sun Canopy " + (layer + 1), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                meshes[layer] = mesh;
            }
            return meshes;
        }

        static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 corner, float width, float height)
        {
            int i = verts.Count;
            verts.Add(corner);
            verts.Add(corner + new Vector3(width, 0f, 0f));
            verts.Add(corner + new Vector3(width, height, 0f));
            verts.Add(corner + new Vector3(0f, height, 0f));
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        void OnDestroy() => TearDown();

        void TearDown()
        {
            if (applied == LightingStyle.Realistic) Restore();
            applied = null;
            Shader.SetGlobalFloat(Backlight, 0f);
            Shader.SetGlobalFloat(FadeScale, 0f);
            Shader.SetGlobalVector(HazeColour, Vector4.zero);
            Shader.SetGlobalVector(UnderLight, Vector4.zero);
            Shader.SetGlobalVector(UILightDir, Vector4.zero);
            stageLitButtons = false;
            ShowButtonLight();
            PhysicalUI.SetRealistic(false);
            Shader.SetGlobalFloat(PlayerRim, 0f);
            Shader.SetGlobalVector(BounceDims, Vector4.zero);
            FreeRayTracing();
            Free(fluxColour); Free(fluxPosition); Free(fluxNormal); Free(fluxDepth); if (bounceVolumes != null) foreach (var v in bounceVolumes) Free(v);
            Free(sunFluxMaterial); bounceVolumes = null; buildLayer = -1;
            patches?.Release(); patchCount?.Release(); patches = patchCount = null;
            fluxColour = fluxPosition = fluxNormal = fluxDepth = null; sunFluxMaterial = null;
            skyBuffer?.Release(); skyBuffer = null; skyCasters.Clear(); skyMovers.Clear(); skyStaticDirty = true;
            Free(skyDepth); Free(skyStatic); Free(skyDepthMaterial);
            skyDepth = null; skyStatic = null; skyDepthMaterial = null;
            Shader.SetGlobalVector(SkyParams, Vector4.zero);
            Free(beams); Free(beamsMaterial); Free(skybox); Free(probeObject); Free(volumeObject); Free(profile);
            Free(canopy); Free(canopyMaterial);
            if (canopyMeshes != null) foreach (var m in canopyMeshes) Free(m);
            beams = null; beamsMaterial = null; skybox = null; probeObject = null; probe = null; volumeObject = null; profile = null;
            canopy = null; canopyMeshes = null; canopyLayers = null; canopyMaterial = null;
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
