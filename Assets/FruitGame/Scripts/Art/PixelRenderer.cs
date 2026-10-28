using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FruitGame
{
    /// <summary>
    /// The pixel render (Docs/DESIGN.md §3). Two helper cameras draw the game layers at renderScale × 1080 × 1440 into
    /// textures with hard pixels: one for everything behind the player (layers Back and Pile: backdrop, far orchard,
    /// pile) and one for everything in front (basket, falling fruit, foreground plants, on a transparent background).
    /// This camera then draws, at full resolution: the back layer on a far quad, the sharp player (layer Person), and
    /// the front layer on a near quad. The player stays crisp for the beauty filter and the photo, and the front layer
    /// covers the player's outline wherever the basket or plants overlap.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class PixelRenderer : MonoBehaviour
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
        static readonly Vector2Int Reference = new Vector2Int(1080, 1440);

        Camera main;
        Camera backCam, frontCam;
        RenderTexture backRt, frontRt;
        Transform backQuad, frontQuad;
        Material backMat, frontMat;
        int originalMask;

        public bool Active => pixelRender && backCam != null;

        void Awake()
        {
            main = GetComponent<Camera>();
            originalMask = main.cullingMask;
            if (!pixelRender) return;

            var size = Vector2Int.RoundToInt((Vector2)Reference * config.renderScale);
            backRt = MakeTarget("Pixel Back", size);
            frontRt = MakeTarget("Pixel Front", size);

            int back = Mask(BackLayer) | Mask("Pile");
            int person = Mask(PersonLayer);
            int composite = Mask(CompositeLayer);
            int ui = Mask("UI") | Mask("HUD3D");
            backCam = MakeCamera("Pixel Back Camera", back, main.backgroundColor, backRt);
            frontCam = MakeCamera("Pixel Front Camera", originalMask & ~(back | person | composite | ui), Color.clear, frontRt);
            main.cullingMask = person | composite;

            backMat = MakeMaterial(backRt, BlendMode.One, BlendMode.Zero, (int)RenderQueue.Geometry - 100);
            frontMat = MakeMaterial(frontRt, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, (int)RenderQueue.Transparent);
            backQuad = MakeQuad("Pixel Back Layer", backMat);
            frontQuad = MakeQuad("Pixel Front Layer", frontMat);
            FitQuads();
        }

        void OnDestroy()
        {
            if (backRt != null) { backRt.Release(); Destroy(backRt); }
            if (frontRt != null) { frontRt.Release(); Destroy(frontRt); }
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

        /// <summary>Sizes the composite quads to exactly fill this camera's view at their distances.</summary>
        void FitQuads()
        {
            float h = 2f * Mathf.Tan(main.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = (float)Reference.x / Reference.y;
            Place(backQuad, BackDistance);
            Place(frontQuad, FrontDistance);

            void Place(Transform quad, float d)
            {
                quad.localPosition = new Vector3(0f, 0f, d);
                quad.localRotation = Quaternion.identity;
                quad.localScale = new Vector3(h * d * aspect, h * d, 1f);
            }
            // The helpers see exactly what this camera sees at the reference 3:4 aspect.
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

        Camera MakeCamera(string name, int mask, Color background, RenderTexture target)
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
            cam.targetTexture = target;
            cam.depth = main.depth - 1f;
            var data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            return cam;
        }

        Material MakeMaterial(RenderTexture tex, BlendMode src, BlendMode dst, int queue)
        {
            var m = new Material(compositeShader) { renderQueue = queue };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            return m;
        }

        Transform MakeQuad(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.layer = LayerMask.NameToLayer(CompositeLayer);
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }
    }
}
