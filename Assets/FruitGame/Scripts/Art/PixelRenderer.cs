using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FruitGame
{
    /// <summary>
    /// The pixel render (Docs/DESIGN.md §4). Two helper cameras draw the game layers at renderScale × the layout's reference size into
    /// textures with hard pixels: one for everything behind the player (layers Back and Pile: backdrop, far orchard,
    /// pile) and one for everything in front (basket, falling fruit, foreground plants, on a transparent background).
    /// This camera then draws, at full resolution: the back layer on a far quad, the sharp player (layer Person), and
    /// the front layer on a near quad. The player stays crisp for the beauty filter and the photo, and the front layer
    /// covers the player's outline wherever the basket or plants overlap.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PixelRenderer : MonoBehaviour, IEditPreview
    {
        [SerializeField] GameConfig config;
        [Tooltip("Off: this camera renders everything at full resolution as before (for comparison).")]
        [SerializeField] bool pixelRender = true;
        [SerializeField] Shader compositeShader;

        public const string BackLayer = "Back";
        public const string PersonLayer = "Person";
        public const string CompositeLayer = "PixelComposite";
        const float BackDistance = 45f;
        const float FrontDistance = 2f;

        Camera main;
        Camera backCam, frontCam;
        RenderTexture backRt, frontRt;
        Transform backQuad, frontQuad;
        Material backMat, frontMat;
        int originalMask;

        public bool Active => pixelRender && backCam != null && !direct;
        bool direct;

        /// <summary>
        /// Realistic look (SceneLighting): this camera draws the whole 3D scene itself at full resolution, so depth of
        /// field, reflections and volumetric light work; off again = the pixel layers, exactly as before.
        /// </summary>
        public void SetDirect(bool on)
        {
            if (main == null || backCam == null || direct == on) return;
            direct = on;
            backCam.enabled = frontCam.enabled = !on;
            backQuad.gameObject.SetActive(!on);
            frontQuad.gameObject.SetActive(!on);
            int composite = Mask(CompositeLayer), ui = Mask("UI") | Mask("HUD3D");
            main.cullingMask = on ? originalMask & ~(composite | ui) : Mask(PersonLayer) | composite;
        }

        void Awake()
        {
            main = GetComponent<Camera>();
            int back = Mask(BackLayer) | Mask("Pile");
            int person = Mask(PersonLayer);
            int composite = Mask(CompositeLayer);
            originalMask = main.cullingMask;
            // Already wired for the pixel layers (an edit-mode title preview that wasn't undone left it so): the camera's
            // own mask is the scene's, everything but UI. Taking the wired one as the original made the realistic look,
            // which restores it, draw nothing but the person.
            if (originalMask == (person | composite)) originalMask = ~Mask("UI");
            if (!pixelRender) return;

            int ui = Mask("UI") | Mask("HUD3D");
            backCam = MakeCamera("Pixel Back Camera", back, main.backgroundColor);
            frontCam = MakeCamera("Pixel Front Camera", originalMask & ~(back | person | composite | ui), Color.clear);
            main.cullingMask = person | composite;

            backMat = MakeMaterial(BlendMode.One, BlendMode.Zero, (int)RenderQueue.Geometry - 100);
            frontMat = MakeMaterial(BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, (int)RenderQueue.Transparent);
            backQuad = MakeQuad("Pixel Back Layer", backMat);
            frontQuad = MakeQuad("Pixel Front Layer", frontMat);
            MakeTargets();
            ScreenLayout.Changed += MakeTargets;
        }

        void OnDestroy()
        {
            ScreenLayout.Changed -= MakeTargets;
            ReleaseTargets();
            if (backMat != null) Destroy(backMat);
            if (frontMat != null) Destroy(frontMat);
        }

        void LateUpdate()
        {
            if (Active) FitQuads();
        }

        /// <summary>Renders both layers now (the photo booth calls this before an off-screen capture).</summary>
        public void RenderNow()
        {
            if (!Active) return;
            FitQuads();
            backCam.Render();
            frontCam.Render();
        }

        /// <summary>Game pixels across the view: renderScale × the layout's reference size.</summary>
        public static Vector2Int TargetSize(GameConfig config) =>
            Vector2Int.RoundToInt((Vector2)ScreenLayout.Reference * config.renderScale);

        /// <summary>(Re)creates both layer textures at the current layout's size.</summary>
        void MakeTargets()
        {
            ReleaseTargets();
            var size = TargetSize(config);
            backRt = MakeTarget("Pixel Back", size);
            frontRt = MakeTarget("Pixel Front", size);
            backCam.targetTexture = backRt;
            frontCam.targetTexture = frontRt;
            backMat.SetTexture("_MainTex", backRt);
            frontMat.SetTexture("_MainTex", frontRt);
            FitQuads();
        }

        void ReleaseTargets()
        {
            if (backCam != null) backCam.targetTexture = null;
            if (frontCam != null) frontCam.targetTexture = null;
            if (backRt != null) { backRt.Release(); Destroy(backRt); }
            if (frontRt != null) { frontRt.Release(); Destroy(frontRt); }
        }

        /// <summary>Sizes the composite quads to exactly fill this camera's view at their distances.</summary>
        void FitQuads()
        {
            float h = 2f * Mathf.Tan(main.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = ScreenLayout.Aspect;
            Place(backQuad, BackDistance);
            Place(frontQuad, FrontDistance);

            void Place(Transform quad, float d)
            {
                quad.localPosition = new Vector3(0f, 0f, d);
                quad.localRotation = Quaternion.identity;
                quad.localScale = new Vector3(h * d * aspect, h * d, 1f);
            }
            // The helpers see exactly what this camera sees at the layout's aspect.
            foreach (var cam in new[] { backCam, frontCam })
            {
                cam.fieldOfView = main.fieldOfView;
                cam.nearClipPlane = main.nearClipPlane;
                cam.farClipPlane = main.farClipPlane;
                cam.aspect = aspect;
            }
        }

        static int Mask(string layer)
        {
            int i = LayerMask.NameToLayer(layer);
            return i < 0 ? 0 : 1 << i;
        }

        static RenderTexture MakeTarget(string name, Vector2Int size)
        {
            var rt = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1
            };
            rt.Create();
            return rt;
        }

        Camera MakeCamera(string name, int mask, Color background)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.cullingMask = mask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.allowHDR = false; // keeps the front layer's alpha
            cam.allowMSAA = false;
            cam.depth = main.depth - 1f;
            var data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            return cam;
        }

        Material MakeMaterial(BlendMode src, BlendMode dst, int queue)
        {
            var m = new Material(compositeShader) { renderQueue = queue };
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            return m;
        }

        Transform MakeQuad(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); // edit-mode preview too
            go.layer = LayerMask.NameToLayer(CompositeLayer);
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // ---- Edit-mode preview (IEditPreview) -------------------------------------------------------------------------

        public int PreviewOrder => 9;
        public void BuildPreview() => Awake();

        public void EndPreview()
        {
            ScreenLayout.Changed -= MakeTargets;
            if (main != null) main.cullingMask = originalMask;
            if (backCam != null) backCam.targetTexture = null;
            if (frontCam != null) frontCam.targetTexture = null;
            if (backRt != null) { backRt.Release(); DestroyImmediate(backRt); }
            if (frontRt != null) { frontRt.Release(); DestroyImmediate(frontRt); }
            if (backMat != null) DestroyImmediate(backMat);
            if (frontMat != null) DestroyImmediate(frontMat);
            backRt = frontRt = null;
            backMat = frontMat = null;
            backCam = frontCam = null;
            backQuad = frontQuad = null;
        }
    }
}
