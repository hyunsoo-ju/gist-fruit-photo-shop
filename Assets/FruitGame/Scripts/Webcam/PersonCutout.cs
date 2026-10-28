using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Shows the player cut out of the webcam image on the L3 quad (Docs/DESIGN.md §9): webcam colour × a smoothed mask.
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

        [Tooltip("How quickly the makeup follows the face (higher = snappier, lower = smoother).")]
        [SerializeField] float makeupFollow = 10f;

        Material prepMaterial;
        Vector2 cheekL, cheekR, mouthPos;
        float cheekRadius, mouthRadius, slimRadius;
        Vector3 eyeA, eyeB;
        readonly Vector4[] slim = new Vector4[4];
        static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
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
            // Pixel-style outline around the player (Docs/DESIGN.md §3); the front layers draw over it where they overlap.
            quadMaterial.SetFloat(OutlineWidthId, config != null ? config.playerOutline : 0f);
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
            Vector2 tCheekL, tCheekR, tMouth, tEyeA, tEyeB;
            float tCheekRadius, tMouthRadius, tEyeRadius, eyeGap;
            var slimTargets = new Vector4[4];
            if (useMesh)
            {
                var p = mesh.Points;
                tEyeA = (p[33] + p[133]) * 0.5f;
                tEyeB = (p[263] + p[362]) * 0.5f;
                eyeGap = AspectDistance(tEyeA, tEyeB);
                tEyeRadius = Mathf.Max(AspectDistance(p[33], p[133]), AspectDistance(p[263], p[362])) * 0.95f;
                tCheekL = p[50];
                tCheekR = p[280];
                tCheekRadius = eyeGap * 0.33f;
                tMouth = (p[13] + p[14]) * 0.5f;
                tMouthRadius = AspectDistance(p[61], p[291]) * 0.6f;
                // V-line: pull the lower jaw (both sides, two points each) toward a point between chin and nose.
                Vector2 pullTarget = Vector2.Lerp(p[152], p[1], 0.55f);
                int[] jaw = { 172, 136, 397, 365 };
                for (int i = 0; i < 4; i++)
                {
                    Vector2 start = p[jaw[i]];
                    slimTargets[i] = new Vector4(start.x, start.y, 0f, 0f);
                    Vector2 pull = (pullTarget - start) * 0.22f;
                    slimTargets[i].z = pull.x;
                    slimTargets[i].w = pull.y;
                }
            }
            else
            {
                var k = tracker.Keypoints;
                Vector2 rightEye = k[0], leftEye = k[1], nose = k[2];
                eyeGap = Vector2.Distance(rightEye, leftEye);
                Vector2 down = nose - (rightEye + leftEye) * 0.5f;
                Vector2 centre = (rightEye + leftEye) * 0.5f;
                Vector2 CheekUnder(Vector2 eye) => eye + down * 1.05f + (eye - centre).normalized * eyeGap * 0.12f;
                tCheekL = CheekUnder(leftEye);
                tCheekR = CheekUnder(rightEye);
                tCheekRadius = eyeGap * 0.38f;
                tMouth = k[3];
                tMouthRadius = eyeGap * 0.42f;
                tEyeA = rightEye; tEyeB = leftEye; tEyeRadius = 0f;
            }

            // Ease toward the targets so makeup and reshape glide with the face instead of jumping per detection.
            float t = makeupPrimed ? 1f - Mathf.Exp(-makeupFollow * Time.unscaledDeltaTime) : 1f;
            makeupPrimed = true;
            cheekL = Vector2.Lerp(cheekL, tCheekL, t);
            cheekR = Vector2.Lerp(cheekR, tCheekR, t);
            mouthPos = Vector2.Lerp(mouthPos, tMouth, t);
            cheekRadius = Mathf.Lerp(cheekRadius, tCheekRadius, t);
            mouthRadius = Mathf.Lerp(mouthRadius, tMouthRadius, t);
            eyeA = Vector3.Lerp(eyeA, new Vector3(tEyeA.x, tEyeA.y, tEyeRadius), t);
            eyeB = Vector3.Lerp(eyeB, new Vector3(tEyeB.x, tEyeB.y, tEyeRadius), t);
            for (int i = 0; i < 4; i++) slim[i] = Vector4.Lerp(slim[i], slimTargets[i], t);
            slimRadius = Mathf.Lerp(slimRadius, eyeGap * 0.85f, t);

            quadMaterial.SetVector(CheekLId, cheekL);
            quadMaterial.SetVector(CheekRId, cheekR);
            quadMaterial.SetFloat(CheekRadiusId, cheekRadius);
            quadMaterial.SetVector(MouthId, mouthPos);
            quadMaterial.SetFloat(MouthRadiusId, mouthRadius);

            // Reshape needs the mesh; without it the face keeps its shape.
            quadMaterial.SetFloat(BigEyeId, useMesh ? BigEyeStrength : 0f);
            quadMaterial.SetFloat(SlimId, useMesh ? SlimStrength : 0f);
            quadMaterial.SetVector(EyeAId, eyeA);
            quadMaterial.SetVector(EyeBId, eyeB);
            quadMaterial.SetFloat(SlimRadiusId, slimRadius);
            for (int i = 0; i < 4; i++) quadMaterial.SetVector(SlimIds[i], slim[i]);
        }

        /// <summary>Distance in "u" units on the 3:4 frame (a v step is 4/3 of a u step).</summary>
        static float AspectDistance(Vector2 a, Vector2 b) => Vector2.Distance(new Vector2(a.x, a.y * 4f / 3f), new Vector2(b.x, b.y * 4f / 3f));

        /// <summary>GPUPixel's box blur (radius 4, spaced taps) for the mean, and the blurred squared high-pass.</summary>
        void RunBeautyPrep(RenderTexture frame)
        {
            if (prepMaterial == null) prepMaterial = new Material(beautyPrepShader);
            int w = Mathf.Max(8, frame.width / 2), h = Mathf.Max(8, frame.height / 2);
            EnsureLinear(ref meanRt, w, h, "Beauty Mean");
            EnsureLinear(ref varRt, w, h, "Beauty Variance");
            EnsureLinear(ref tempRt, w, h, "Beauty Temp");

            prepMaterial.SetFloat(SpacingId, 2f);
            Graphics.Blit(frame, tempRt, prepMaterial, 0);   // gamma + horizontal
            Graphics.Blit(tempRt, meanRt, prepMaterial, 2);  // vertical → mean

            prepMaterial.SetTexture(MeanTexId, meanRt);
            prepMaterial.SetFloat(DeltaId, highPassDelta);
            Graphics.Blit(frame, varRt, prepMaterial, 3);    // squared difference
            Graphics.Blit(varRt, tempRt, prepMaterial, 1);   // horizontal
            Graphics.Blit(tempRt, varRt, prepMaterial, 2);   // vertical → variance
        }

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
