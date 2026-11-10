using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Shows the player cut out of the webcam image on the L3 quad (Docs/DESIGN.md §13): webcam colour × a smoothed mask.
    /// Smoothing = a small blur plus a temporal blend with the previous mask, so edges are soft and don't flicker.
    /// The quad is hidden until both the camera and the mask are live.
    /// </summary>
    public class PersonCutout : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] WebcamSource source;
        [SerializeField] PersonSegmenter segmenter;
        [SerializeField] Renderer quad;
        [SerializeField] Shader smoothShader;
        [Tooltip("Places the beauty filter's cheek blush from the tracked face box.")]
        [SerializeField] FaceTracker tracker;
        [Tooltip("478-point face mesh for reshaping and precise makeup (photo booth only).")]
        [SerializeField] FaceMesh mesh;
        [Tooltip("Hidden/FruitGame/Beauty Prep: mean / variance pre-passes for the beauty filter.")]
        [SerializeField] Shader beautyPrepShader;
        [Tooltip("GPUPixel's whitening LUT chain baked into two 512×512 LUTs (skin, skin + light).")]
        [SerializeField] Texture2D lutSkin;
        [SerializeField] Texture2D lutLight;
        [Tooltip("GPUPixel's high-pass delta for the variance map.")]
        [SerializeField] float highPassDelta = 14f;

        [Tooltip("Weight of the newest mask each frame (lower = steadier but laggier edges).")]
        [SerializeField, Range(0.05f, 1f)] float temporalBlend = 0.6f;
        [SerializeField, Range(0f, 4f)] float blurSpread = 1.5f;
        [Tooltip("Mask range mapped to transparent → opaque at the edge.")]
        [SerializeField] Vector2 edge = new Vector2(0.4f, 0.65f);

        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int PrevTexId = Shader.PropertyToID("_PrevTex");
        static readonly int BlendId = Shader.PropertyToID("_Blend");
        static readonly int SpreadId = Shader.PropertyToID("_Spread");
        static readonly int EdgeLowId = Shader.PropertyToID("_EdgeLow");
        static readonly int EdgeHighId = Shader.PropertyToID("_EdgeHigh");
        static readonly int BeautyId = Shader.PropertyToID("_Beauty");
        static readonly int CheekLId = Shader.PropertyToID("_CheekL");
        static readonly int CheekRId = Shader.PropertyToID("_CheekR");
        static readonly int CheekRadiusId = Shader.PropertyToID("_CheekRadius");
        static readonly int MouthId = Shader.PropertyToID("_Mouth");
        static readonly int MouthRadiusId = Shader.PropertyToID("_MouthRadius");
        static readonly int MeanTexId = Shader.PropertyToID("_MeanTex");
        static readonly int VarTexId = Shader.PropertyToID("_VarTex");
        static readonly int SpacingId = Shader.PropertyToID("_Spacing");
        static readonly int DeltaId = Shader.PropertyToID("_Delta");

        [Tooltip("How quickly the makeup and reshape follow the face (1/s; higher = snappier, lower = smoother). 30 ≈ 33 ms behind the face mesh.")]
        [SerializeField] float makeupFollow = 30f;

        static readonly int BlurAlphaId = Shader.PropertyToID("_BlurAlpha");
        static readonly int BlushStrengthId = Shader.PropertyToID("_BlushStrength");
        static readonly int BlushColorId = Shader.PropertyToID("_BlushColor");
        static readonly int LipStrengthId = Shader.PropertyToID("_LipStrength");
        static readonly int LipColorId = Shader.PropertyToID("_LipColor");

        /// <summary>Where the makeup and reshape go, in webcam-output UV: eased for the live view, exact for a photo.</summary>
        class Makeup
        {
            public Vector2 cheekL, cheekR, mouth;
            public float cheekRadius, mouthRadius, slimRadius;
            public Vector3 eyeA, eyeB;
            public readonly Vector4[] slim = new Vector4[4];

            public void LerpTo(Makeup to, float t)
            {
                cheekL = Vector2.Lerp(cheekL, to.cheekL, t);
                cheekR = Vector2.Lerp(cheekR, to.cheekR, t);
                mouth = Vector2.Lerp(mouth, to.mouth, t);
                cheekRadius = Mathf.Lerp(cheekRadius, to.cheekRadius, t);
                mouthRadius = Mathf.Lerp(mouthRadius, to.mouthRadius, t);
                slimRadius = Mathf.Lerp(slimRadius, to.slimRadius, t);
                eyeA = Vector3.Lerp(eyeA, to.eyeA, t);
                eyeB = Vector3.Lerp(eyeB, to.eyeB, t);
                for (int i = 0; i < 4; i++) slim[i] = Vector4.Lerp(slim[i], to.slim[i], t);
            }
        }

        Material prepMaterial;
        readonly Makeup shown = new Makeup(), target = new Makeup(), still = new Makeup();
        bool shownHasMesh;
        Color baseBlush, baseLip;
        RenderTexture heldFrame, heldMask;
        static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int OutlineTexelId = Shader.PropertyToID("_OutlineTexel");
        static readonly int FrameAspectId = Shader.PropertyToID("_FrameAspect");
        static readonly int EdgeFadeId = Shader.PropertyToID("_EdgeFade");
        static readonly int BigEyeId = Shader.PropertyToID("_BigEye");
        static readonly int SlimId = Shader.PropertyToID("_Slim");
        static readonly int EyeAId = Shader.PropertyToID("_EyeA");
        static readonly int EyeBId = Shader.PropertyToID("_EyeB");
        static readonly int SlimRadiusId = Shader.PropertyToID("_SlimRadius");
        static readonly int[] SlimIds = { Shader.PropertyToID("_Slim0"), Shader.PropertyToID("_Slim1"), Shader.PropertyToID("_Slim2"), Shader.PropertyToID("_Slim3") };
        bool makeupPrimed;
        RenderTexture meanRt, varRt, tempRt;

        Material smoothMaterial;
        Material quadMaterial;
        RenderTexture current;
        RenderTexture previous;
        bool primed;

        public RenderTexture SmoothedMask => current;
        /// <summary>Beauty filter strength for the player (0 = off). Set by the photo booth.</summary>
        public float BeautyStrength { get; set; }
        /// <summary>Eye enlargement (0 = off). Set by the photo booth; needs the face mesh.</summary>
        public float BigEyeStrength { get; set; }
        /// <summary>V-line face slimming (0 = off). Set by the photo booth; needs the face mesh.</summary>
        public float SlimStrength { get; set; }

        void Awake()
        {
            smoothMaterial = new Material(smoothShader);
            quadMaterial = new Material(quad.sharedMaterial);
            // The material's colours are the base tones; GameConfig's coolness turns them warmer or cooler.
            baseBlush = quadMaterial.GetColor(BlushColorId);
            baseLip = quadMaterial.GetColor(LipColorId);
            quad.sharedMaterial = quadMaterial;
            quad.enabled = false;
        }

        void OnDestroy()
        {
            Destroy(smoothMaterial);
            Destroy(quadMaterial);
            if (prepMaterial != null) Destroy(prepMaterial);
            Release(meanRt);
            Release(varRt);
            Release(tempRt);
            Release(current);
            Release(previous);
            Release(heldFrame);
            Release(heldMask);
        }

        void LateUpdate()
        {
            bool live = source != null && source.HasFrames && segmenter != null && segmenter.HasMask;
            if (quad.enabled != live) quad.enabled = live;
            if (!live) { primed = false; return; }

            var mask = segmenter.Mask;
            Ensure(ref current, mask);
            Ensure(ref previous, mask);

            // First frame: start from the raw mask so the cut-out doesn't fade in from nothing.
            smoothMaterial.SetTexture(PrevTexId, primed ? current : mask);
            smoothMaterial.SetFloat(BlendId, primed ? temporalBlend : 1f);
            smoothMaterial.SetFloat(SpreadId, blurSpread);
            Graphics.Blit(mask, previous, smoothMaterial);
            (current, previous) = (previous, current);
            primed = true;

            quadMaterial.SetTexture(MainTexId, source.Output);
            quadMaterial.SetTexture(MaskTexId, current);
            quadMaterial.SetFloat(EdgeLowId, edge.x);
            quadMaterial.SetFloat(EdgeHighId, edge.y);
            // Pixel-style outline around the player (Docs/DESIGN.md §4); the front layers draw over it where they overlap.
            quadMaterial.SetFloat(OutlineWidthId, config != null ? config.playerOutline : 0f);
            // The quad spans the view's height: one screen pixel is 1 / reference height of it.
            float h = ScreenLayout.Reference.y;
            quadMaterial.SetVector(OutlineTexelId, new Vector4(1f / (h * CameraFrame.Aspect), 1f / h, 0f, 0f));
            quadMaterial.SetFloat(FrameAspectId, CameraFrame.Aspect);
            quadMaterial.SetFloat(EdgeFadeId, CameraFrame.EdgeFade);
            if (config != null) quadMaterial.SetColor(OutlineColorId, config.playerOutlineColor);
            UpdateBeauty();
        }

        /// <summary>
        /// Feeds the beauty filter (ported from GPUPixel): mean and variance maps of the frame at half resolution,
        /// the LUTs, and makeup positions from BlazeFace's keypoints. Cheeks sit below each eye, toward the outside;
        /// the lips sit on the mouth keypoint. Without a face the makeup simply stays off.
        /// </summary>
        void UpdateBeauty()
        {
            quadMaterial.SetFloat(BeautyId, BeautyStrength);
            if (BeautyStrength <= 0f || source == null || source.Output == null)
            {
                if (mesh != null) mesh.Active = false;
                return;
            }

            ApplySettings();
            RunBeautyPrep(source.Output);
            quadMaterial.SetTexture(MeanTexId, meanRt);
            quadMaterial.SetTexture(VarTexId, varRt);
            quadMaterial.SetTexture("_LutSkin", lutSkin);
            quadMaterial.SetTexture("_LutLight", lutLight);

            if (mesh != null) mesh.Active = true;
            bool useMesh = mesh != null && mesh.HasMesh;
            bool face = tracker != null && tracker.HasFace;
            if (!face && !useMesh)
            {
                makeupPrimed = false;
                quadMaterial.SetFloat(CheekRadiusId, 0.0001f);
                quadMaterial.SetFloat(MouthRadiusId, 0.0001f);
                quadMaterial.SetFloat(BigEyeId, 0f);
                quadMaterial.SetFloat(SlimId, 0f);
                return;
            }

            // Targets for this frame: from the 478-point face mesh when available, else BlazeFace's six keypoints.
            if (useMesh) FromMesh(mesh.Points, target);
            else FromKeypoints(tracker.Keypoints, target);

            // Ease toward the targets so makeup and reshape glide with the face instead of jumping per detection.
            float t = makeupPrimed ? 1f - Mathf.Exp(-makeupFollow * Time.unscaledDeltaTime) : 1f;
            makeupPrimed = true;
            shown.LerpTo(target, t);
            shownHasMesh = useMesh;
            Push(shown, useMesh);
        }

        /// <summary>The staff-adjustable beauty settings (GameConfig): smoothing, and blush and lip tint strength and tone.</summary>
        void ApplySettings()
        {
            if (config == null) return;
            quadMaterial.SetFloat(BlurAlphaId, config.skinSmoothing);
            quadMaterial.SetFloat(BlushStrengthId, config.blushStrength);
            quadMaterial.SetColor(BlushColorId, Tone(baseBlush, config.blushCoolness));
            quadMaterial.SetFloat(LipStrengthId, config.lipStrength);
            quadMaterial.SetColor(LipColorId, Tone(baseLip, config.lipCoolness));
        }

        /// <summary>Coolness 0.5 keeps the base colour; lower turns its hue toward coral/peach, higher toward lilac/berry.</summary>
        static Color Tone(Color baseColor, float coolness)
        {
            Color.RGBToHSV(baseColor, out float h, out float sat, out float v);
            h = Mathf.Repeat(h + (0.5f - coolness) * 2f * (25f / 360f), 1f);
            var c = Color.HSVToRGB(h, sat, v);
            c.a = baseColor.a;
            return c;
        }

        static void FromMesh(Vector2[] p, Makeup m)
        {
            m.eyeA = (p[33] + p[133]) * 0.5f;
            m.eyeB = (p[263] + p[362]) * 0.5f;
            float eyeGap = AspectDistance(m.eyeA, m.eyeB);
            float eyeRadius = Mathf.Max(AspectDistance(p[33], p[133]), AspectDistance(p[263], p[362])) * 0.95f;
            m.eyeA.z = eyeRadius;
            m.eyeB.z = eyeRadius;
            m.cheekL = p[50];
            m.cheekR = p[280];
            m.cheekRadius = eyeGap * 0.33f;
            m.mouth = (p[13] + p[14]) * 0.5f;
            m.mouthRadius = AspectDistance(p[61], p[291]) * 0.6f;
            // V-line: pull the lower jaw (both sides, two points each) toward a point between chin and nose.
            Vector2 pullTarget = Vector2.Lerp(p[152], p[1], 0.55f);
            int[] jaw = { 172, 136, 397, 365 };
            for (int i = 0; i < 4; i++)
            {
                Vector2 start = p[jaw[i]];
                Vector2 pull = (pullTarget - start) * 0.22f;
                m.slim[i] = new Vector4(start.x, start.y, pull.x, pull.y);
            }
            m.slimRadius = eyeGap * 0.85f;
        }

        static void FromKeypoints(Vector2[] k, Makeup m)
        {
            Vector2 rightEye = k[0], leftEye = k[1], nose = k[2];
            float eyeGap = Vector2.Distance(rightEye, leftEye);
            Vector2 down = nose - (rightEye + leftEye) * 0.5f;
            Vector2 centre = (rightEye + leftEye) * 0.5f;
            Vector2 CheekUnder(Vector2 eye) => eye + down * 1.05f + (eye - centre).normalized * eyeGap * 0.12f;
            m.cheekL = CheekUnder(leftEye);
            m.cheekR = CheekUnder(rightEye);
            m.cheekRadius = eyeGap * 0.38f;
            m.mouth = k[3];
            m.mouthRadius = eyeGap * 0.42f;
            m.eyeA = new Vector3(rightEye.x, rightEye.y, 0f);
            m.eyeB = new Vector3(leftEye.x, leftEye.y, 0f);
            for (int i = 0; i < 4; i++) m.slim[i] = Vector4.zero;
            m.slimRadius = eyeGap * 0.85f;
        }

        void Push(Makeup m, bool hasMesh)
        {
            quadMaterial.SetVector(CheekLId, m.cheekL);
            quadMaterial.SetVector(CheekRId, m.cheekR);
            quadMaterial.SetFloat(CheekRadiusId, m.cheekRadius);
            quadMaterial.SetVector(MouthId, m.mouth);
            quadMaterial.SetFloat(MouthRadiusId, m.mouthRadius);
            // Reshape needs the mesh; without it the face keeps its shape.
            quadMaterial.SetFloat(BigEyeId, hasMesh ? BigEyeStrength : 0f);
            quadMaterial.SetFloat(SlimId, hasMesh ? SlimStrength : 0f);
            quadMaterial.SetVector(EyeAId, m.eyeA);
            quadMaterial.SetVector(EyeBId, m.eyeB);
            quadMaterial.SetFloat(SlimRadiusId, m.slimRadius);
            for (int i = 0; i < 4; i++) quadMaterial.SetVector(SlimIds[i], m.slim[i]);
        }

        // ---- Photos: the makeup placed on the exact frame ------------------------------------------------------------

        /// <summary>The face mesh, for measuring a photo's held frame.</summary>
        public FaceMesh Mesh => mesh;
        /// <summary>The camera frame kept by HoldFrame.</summary>
        public RenderTexture HeldFrame => heldFrame;
        /// <summary>The cut-out mask used for the photo (the held live mask, or the photo's own matte once given).</summary>
        public RenderTexture HeldMask => heldMask;
        /// <summary>The background removal, for a photo's own matte.</summary>
        public PersonSegmenter Segmenter => segmenter;

        /// <summary>
        /// Keeps a copy of this moment's camera frame and cut-out mask for a photo (the live view carries on). False when
        /// the player isn't showing.
        /// </summary>
        public bool HoldFrame()
        {
            if (!quad.enabled || source == null || source.Output == null || current == null) return false;
            Copy(source.Output, ref heldFrame, "Photo Frame Held");
            Copy(current, ref heldMask, "Photo Mask Held");
            return true;
        }

        /// <summary>
        /// For rendering a photo: shows the held frame instead of the live one, cut out with `matte` (that frame's own
        /// background removal, softened like the live mask but with no blend from earlier frames) or else the mask held
        /// with it, and with the makeup and reshape placed exactly from `meshPoints` measured on it (no easing), or as
        /// they are on screen when null. EndPhotoFrame puts the live view back; call it right after the render.
        /// </summary>
        public void BeginPhotoFrame(Vector2[] meshPoints, Texture matte = null)
        {
            if (heldFrame == null) return;
            if (matte != null && heldMask != null)
            {
                smoothMaterial.SetTexture(PrevTexId, matte);
                smoothMaterial.SetFloat(BlendId, 1f);
                smoothMaterial.SetFloat(SpreadId, blurSpread);
                Graphics.Blit(matte, heldMask, smoothMaterial);
            }
            quadMaterial.SetTexture(MainTexId, heldFrame);
            quadMaterial.SetTexture(MaskTexId, heldMask);
            if (BeautyStrength <= 0f) return;
            RunBeautyPrep(heldFrame);
            if (meshPoints == null) return;
            FromMesh(meshPoints, still);
            Push(still, true);
        }

        public void EndPhotoFrame()
        {
            if (source != null && source.Output != null) quadMaterial.SetTexture(MainTexId, source.Output);
            if (current != null) quadMaterial.SetTexture(MaskTexId, current);
            if (BeautyStrength <= 0f || source == null || source.Output == null) return;
            RunBeautyPrep(source.Output);
            if (makeupPrimed) Push(shown, shownHasMesh);
        }

        static void Copy(RenderTexture from, ref RenderTexture to, string name)
        {
            if (to == null || to.width != from.width || to.height != from.height || to.format != from.format)
            {
                Release(to);
                to = new RenderTexture(from.width, from.height, 0, from.format) { name = name, filterMode = from.filterMode, wrapMode = from.wrapMode };
                to.Create();
            }
            Graphics.CopyTexture(from, to);
        }

        /// <summary>Distance in "u" units on the frame (a v step is 1 / aspect of a u step).</summary>
        static float AspectDistance(Vector2 a, Vector2 b) =>
            Vector2.Distance(new Vector2(a.x, a.y / CameraFrame.Aspect), new Vector2(b.x, b.y / CameraFrame.Aspect));

        /// <summary>GPUPixel's box blur (radius 4, spaced taps) for the mean, and the blurred squared high-pass.</summary>
        void RunBeautyPrep(RenderTexture frame)
        {
            if (prepMaterial == null) prepMaterial = new Material(beautyPrepShader);
            int w = Mathf.Max(8, frame.width / 2), h = Mathf.Max(8, frame.height / 2);
            EnsureLinear(ref meanRt, w, h, "Beauty Mean");
            EnsureLinear(ref varRt, w, h, "Beauty Variance");
            EnsureLinear(ref tempRt, w, h, "Beauty Temp");

            // Tap spacing is in the source's texels: the horizontal pass reads the full frame and the vertical one the
            // half-size copy, so the horizontal spacing is scaled up to reach as far (the blur was half as wide as tall).
            prepMaterial.SetFloat(SpacingId, Width * frame.width / (float)w);
            Graphics.Blit(frame, tempRt, prepMaterial, 0);   // gamma + horizontal
            prepMaterial.SetFloat(SpacingId, Width);
            Graphics.Blit(tempRt, meanRt, prepMaterial, 2);  // vertical → mean

            prepMaterial.SetTexture(MeanTexId, meanRt);
            prepMaterial.SetFloat(DeltaId, highPassDelta);
            prepMaterial.SetFloat(SpacingId, Width);
            Graphics.Blit(frame, varRt, prepMaterial, 3);    // squared difference
            Graphics.Blit(varRt, tempRt, prepMaterial, 1);   // horizontal
            Graphics.Blit(tempRt, varRt, prepMaterial, 2);   // vertical → variance
        }

        /// <summary>Skin smoothing width (GameConfig.skinSmoothWidth): the mean blur's tap spacing, in texels.</summary>
        float Width => config != null ? config.skinSmoothWidth : 3f;

        static void EnsureLinear(ref RenderTexture rt, int w, int h, string name)
        {
            if (rt != null && rt.width == w && rt.height == h) return;
            Release(rt);
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
        }

        static void Ensure(ref RenderTexture rt, RenderTexture like)
        {
            if (rt != null && rt.width == like.width && rt.height == like.height) return;
            Release(rt);
            rt = new RenderTexture(like.width, like.height, 0, RenderTextureFormat.R8) { name = "Smoothed Mask", filterMode = FilterMode.Bilinear };
            rt.Create();
        }

        static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
        }
    }
}
