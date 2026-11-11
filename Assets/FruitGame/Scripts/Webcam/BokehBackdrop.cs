using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The L5 backdrop: the webcam room, heavily blurred into soft bokeh (Docs/DESIGN.md §13). The player is masked out
    /// before blurring, so the room fills in around them instead of their outline smearing behind the cut-out.
    /// Shows a plain fallback colour until the camera is live; works without a mask too (just blurs everything).
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class BokehBackdrop : MonoBehaviour
    {
        [SerializeField] WebcamSource source;
        [SerializeField] PersonSegmenter segmenter;
        [SerializeField] Shader blurShader;
        [Tooltip("Blur resolution as a fraction of the webcam output (lower = cheaper and blurrier).")]
        [SerializeField, Range(0.1f, 0.5f)] float downsample = 0.25f;
        [Tooltip("Kawase offsets; more and larger steps = a wider blur.")]
        [SerializeField] float[] passes = { 0.5f, 1.5f, 2.5f, 3.5f, 4.5f };
        [Tooltip("Off: show the plain, sharp camera image behind the scene (no blur, masking or colour grade).")]
        [SerializeField] bool bokeh = false;
        [Tooltip("Show a pixel-art illustration instead of the camera (Docs/DESIGN.md §4, L5).")]
        [SerializeField] bool useIllustration = false;
        [SerializeField] Texture2D illustration;

        public bool Bokeh { get => bokeh; set => bokeh = value; }
        public bool UseIllustration { get => useIllustration; set => useIllustration = value; }

        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int UseMaskId = Shader.PropertyToID("_UseMask");
        static readonly int OffsetId = Shader.PropertyToID("_Offset");
        static readonly int EdgeFadeId = Shader.PropertyToID("_EdgeFade");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int LiveId = Shader.PropertyToID("_Live");
        static readonly int DesaturateId = Shader.PropertyToID("_Desaturate");
        static readonly int BrightenId = Shader.PropertyToID("_Brighten");
        static readonly int TintAmountId = Shader.PropertyToID("_TintAmount");
        static readonly int MainTexStId = Shader.PropertyToID("_MainTex_ST");
        float gradeDesaturate, gradeBrighten, gradeTint;

        Material blurMaterial;
        Material displayMaterial;
        RenderTexture a, b;

        void Awake()
        {
            blurMaterial = new Material(blurShader);
            var r = GetComponent<Renderer>();
            displayMaterial = new Material(r.sharedMaterial);
            r.sharedMaterial = displayMaterial;
            displayMaterial.SetFloat(LiveId, 0f);
            gradeDesaturate = displayMaterial.GetFloat(DesaturateId);
            gradeBrighten = displayMaterial.GetFloat(BrightenId);
            gradeTint = displayMaterial.GetFloat(TintAmountId);
        }

        void OnDestroy()
        {
            Destroy(blurMaterial);
            Destroy(displayMaterial);
            Release(a);
            Release(b);
        }

        void LateUpdate()
        {
            if (useIllustration && illustration != null)
            {
                // Centre-crop the illustration to the view, ungraded.
                float aspect = (float)illustration.width / illustration.height, view = CameraFrame.Aspect;
                var st = aspect < view
                    ? new Vector4(1f, aspect / view, 0f, (1f - aspect / view) * 0.5f)
                    : new Vector4(view / aspect, 1f, (1f - view / aspect) * 0.5f, 0f);
                displayMaterial.SetVector(MainTexStId, st);
                displayMaterial.SetFloat(EdgeFadeId, CameraFrame.EdgeFade);
                displayMaterial.SetTexture(MainTexId, illustration);
                displayMaterial.SetFloat(LiveId, 1f);
                displayMaterial.SetFloat(DesaturateId, 0f);
                displayMaterial.SetFloat(BrightenId, 1f);
                displayMaterial.SetFloat(TintAmountId, 0f);
                return;
            }
            displayMaterial.SetVector(MainTexStId, new Vector4(1f, 1f, 0f, 0f));
            displayMaterial.SetFloat(EdgeFadeId, CameraFrame.EdgeFade);
            // With the camera off from the staff panel, the plain colour it shows instead (no player to mask out).
            bool off = source != null && source.Off;
            bool live = source != null && (source.HasFrames || off) && source.Output != null;
            displayMaterial.SetFloat(LiveId, live ? 1f : 0f);
            if (!live) return;

            Show(source.Output, !off && segmenter != null && segmenter.HasMask ? segmenter.Mask : null);
        }

        /// <summary>
        /// For rendering a photo: the backdrop from a held camera frame (and its matte, for the bokeh), so the room
        /// behind the player matches the frame the photo's cut-out comes from. Call ShowLive right after the render.
        /// </summary>
        public void ShowPhotoFrame(RenderTexture frame, Texture mask)
        {
            if (frame == null || (useIllustration && illustration != null)) return;
            Show(frame, mask);
        }

        public void ShowLive() => LateUpdate();

        void Show(RenderTexture src, Texture mask)
        {
            // Bokeh off: the raw camera frame (alpha is 1, so the display shader's divide is a no-op), ungraded.
            displayMaterial.SetFloat(DesaturateId, bokeh ? gradeDesaturate : 0f);
            displayMaterial.SetFloat(BrightenId, bokeh ? gradeBrighten : 1f);
            displayMaterial.SetFloat(TintAmountId, bokeh ? gradeTint : 0f);
            if (!bokeh)
            {
                displayMaterial.SetTexture(MainTexId, src);
                return;
            }

            int w = Mathf.Max(8, Mathf.RoundToInt(src.width * downsample));
            int h = Mathf.Max(8, Mathf.RoundToInt(src.height * downsample));
            Ensure(ref a, w, h);
            Ensure(ref b, w, h);

            bool hasMask = mask != null;
            blurMaterial.SetTexture(MaskTexId, hasMask ? mask : Texture2D.blackTexture);
            blurMaterial.SetFloat(UseMaskId, hasMask ? 1f : 0f);
            Graphics.Blit(src, a, blurMaterial, 0);

            foreach (float offset in passes)
            {
                blurMaterial.SetFloat(OffsetId, offset);
                Graphics.Blit(a, b, blurMaterial, 1);
                (a, b) = (b, a);
            }

            displayMaterial.SetTexture(MainTexId, a);
        }

        static void Ensure(ref RenderTexture rt, int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h) return;
            Release(rt);
            rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf) { name = "Bokeh", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
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
