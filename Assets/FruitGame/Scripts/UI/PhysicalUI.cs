using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Physical UI (Docs/DESIGN.md §15): the voxel buttons and the panels stand in the 3D scene, in every look. The
    /// panels are wooden signboards hanging on ropes from above, their text raised in voxel letters (SignBoard). The UI
    /// keeps working as before underneath: the (invisible) 2D elements still take the taps, and each model is placed where
    /// its 2D element sits on screen, scaled to the same size. What stays 2D: the captured photos, the floating
    /// instructions, the final photo preview and the phone card (its Close button too). Full-screen dimmers become a dark
    /// veil just behind the signs, so they dim the scene but not the signs.
    /// Realistic look: just in front of the player, lit by the scene's sun, sky, leaf spots and beams and casting shadows.
    /// Classic look: in front of the pixel layers' near quad, drawn at full resolution by the game camera in the classic
    /// buttons' light, without shadows. High contrast (either look): unlit pure black and white, no shadows.
    /// </summary>
    public class PhysicalUI : MonoBehaviour
    {
        /// <summary>
        /// Metres from the camera to the buttons. Realistic: 9 (the player stands at about 14). Classic: 1.5, nearer
        /// than the pixel layers' front quad (2 m), so nothing in the game draws over them. A model scaled toward the
        /// camera looks the same on screen, so only the light and shadows differ.
        /// </summary>
        public static float Distance => realistic ? 9f : 1.5f;
        static float Scale => Distance / 9f;
        /// <summary>The signboards sit this far behind the buttons on them.</summary>
        public static float BoardBehind => 0.12f * Scale;
        /// <summary>A little room in front of a sign, so a button doesn't sink into it.</summary>
        public static float Clearance => 0.02f * Scale;
        /// <summary>The dimming veil sits this far behind the signs.</summary>
        static float VeilBehind => 0.6f * Scale;

        static PhysicalUI instance;
        static bool realistic;

        // Play mode without a domain reload keeps statics: start each run clean, or a second run kept the first run's
        // (destroyed) panels and the side panels stayed flat until the Editor restarted.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            instance = null;
            realistic = false;
        }

        /// <summary>Physical UI is showing: always in Play, once there's a game camera.</summary>
        public static bool On => Application.isPlaying && instance != null && instance.Camera != null;

        /// <summary>The realistic look is on (SceneLighting says).</summary>
        public static bool Realistic => realistic;

        /// <summary>The models cast and take shadows: only in the realistic look without high contrast.</summary>
        public static bool Shadows => realistic && !HighContrast.On;

        /// <summary>SceneLighting tells this when the realistic look goes on or off.</summary>
        public static void SetRealistic(bool on)
        {
            realistic = on;
            if (on) Ensure();
        }

        /// <summary>Makes the physical UI in Play if it isn't there yet (VoxelButtons calls it, for the classic look).</summary>
        public static void Ensure()
        {
            if (!Application.isPlaying || instance != null) return;
            var go = new GameObject("Physical UI"); // a scene object: gone when Play ends
            instance = go.AddComponent<PhysicalUI>();
        }

        public static PhysicalUI Instance => instance;

        /// <summary>The dimmer's shader (SceneLighting holds the reference, so builds include it).</summary>
        public static Shader VeilShader { get; set; }

        public Camera Camera => Camera.main;
        public int Layer { get; private set; }
        /// <summary>The buttons' material for the look and contrast now.</summary>
        public Material ButtonMaterial => HighContrast.On ? ContrastMaterial : realistic ? SceneMaterial : ClassicMaterial;
        /// <summary>The signboards' material for the look and contrast now.</summary>
        public Material BoardMaterial => HighContrast.On ? ContrastMaterial : realistic ? SignMaterial : ClassicMaterial;

        /// <summary>Classic look: the blocks' own material, in the classic buttons' light.</summary>
        Material ClassicMaterial
        {
            get
            {
                if (classicMaterial == null && VoxelButtons.Instance != null && VoxelButtons.Instance.BlockMaterial != null)
                    classicMaterial = new Material(VoxelButtons.Instance.BlockMaterial) { name = "Physical UI Classic", hideFlags = HideFlags.DontSave };
                return classicMaterial;
            }
        }

        /// <summary>High contrast: the block colours as they are (pure black and white), unlit.</summary>
        Material ContrastMaterial
        {
            get
            {
                if (contrastMaterial == null && VoxelButtons.Instance != null && VoxelButtons.Instance.BlockMaterial != null)
                {
                    contrastMaterial = new Material(VoxelButtons.Instance.BlockMaterial) { name = "Physical UI Contrast", hideFlags = HideFlags.DontSave };
                    contrastMaterial.SetFloat("_Unlit", 1f);
                }
                if (contrastMaterial != null) contrastMaterial.SetFloat("_UnlitTonemap", realistic ? 1f : 0f);
                return contrastMaterial;
            }
        }
        Material classicMaterial, contrastMaterial;

        /// <summary>The blocks' material, lit by the scene (made on first use: VoxelButtons may start after this).</summary>
        Material SceneMaterial
        {
            get
            {
                if (sceneMaterial == null && VoxelButtons.Instance != null && VoxelButtons.Instance.BlockMaterial != null)
                {
                    sceneMaterial = new Material(VoxelButtons.Instance.BlockMaterial) { name = "Physical UI", hideFlags = HideFlags.DontSave };
                    sceneMaterial.SetFloat("_SceneLit", 1f);
                    sceneMaterial.SetFloat("_StageLit", 0f);
                    ApplyGlow();
                }
                return sceneMaterial;
            }
        }
        Material sceneMaterial, signMaterial;

        /// <summary>The signboards' material: lit by the scene like the buttons, without their glow.</summary>
        Material SignMaterial
        {
            get
            {
                if (signMaterial == null && SceneMaterial != null)
                {
                    signMaterial = new Material(SceneMaterial) { name = "Physical UI Signs", hideFlags = HideFlags.DontSave };
                    signMaterial.SetColor("_SelfGlow", Color.clear);
                    signMaterial.SetVector("_FillBalance", new Vector4(1, 1, 1, 0));
                }
                return signMaterial;
            }
        }

        /// <summary>The buttons' own warm glow (SceneLighting sets it: colour times strength), for readability.</summary>
        public static Color ButtonGlow { get; set; }

        static readonly int UILightColor = Shader.PropertyToID("_UILightColor");
        static readonly int UIEquator = Shader.PropertyToID("_UIAmbientEquator");

        void ApplyGlow()
        {
            if (sceneMaterial == null) return;
            sceneMaterial.SetColor("_SelfGlow", ButtonGlow);
            // The scene's fill light (the blue sky, the cool underlight) made the buttons look cold: they take it at its
            // strength but in the colour of the classic UI light (its light on a front face plus its ambient), so they
            // keep the warmth they have in the classic look. The signs are scenery and keep the scene's colours.
            Vector4 sun = Shader.GetGlobalVector(UILightColor), amb = Shader.GetGlobalVector(UIEquator);
            var classic = new Vector3(sun.x * 0.5f + amb.x, sun.y * 0.5f + amb.y, sun.z * 0.5f + amb.z);
            float lum = 0.2126f * classic.x + 0.7152f * classic.y + 0.0722f * classic.z;
            sceneMaterial.SetVector("_FillBalance", lum > 1e-3f ? new Vector4(classic.x / lum, classic.y / lum, classic.z / lum, 1f) : new Vector4(1, 1, 1, 0));
        }
        Material veilMaterial;

        readonly List<SignBoard> boards = new List<SignBoard>();
        readonly List<(Image image, Transform veil)> dimmers = new List<(Image, Transform)>();

        void Awake()
        {
            // Ignore Raycast: drawn by the game camera, but left out of the sky and sun maps (SceneLighting), so the signs
            // don't put the play area in sky shadow.
            Layer = Mathf.Max(0, LayerMask.NameToLayer("Ignore Raycast"));
            var veilShader = VeilShader != null ? VeilShader : Shader.Find("Hidden/FruitGame/Veil");
            if (veilShader != null) veilMaterial = new Material(veilShader) { hideFlags = HideFlags.DontSave };
            Hook();
        }

        /// <summary>Finds the panels to make physical and the full-screen dimmers, by their place in the UI.</summary>
        void Hook()
        {
            Canvas ui = null;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.transform.Find("Frame") != null) ui = c;
            if (ui == null) return;
            var frame = ui.transform.Find("Frame");
            foreach (var path in new[] { "Game Over/Results", "Game Over/Ranking", "Side Panels/Left Panel", "Side Panels/Film Strip" })
            {
                var t = frame.Find(path);
                if (t == null || t.GetComponent<SignBoard>() != null) continue;
                var board = t.gameObject.AddComponent<SignBoard>();
                if (path == "Game Over/Ranking") board.extraBehind = 1f;
                boards.Add(board);
            }
            foreach (var path in new[] { "Pause Menu", "Photo Booth/Preview" })
            {
                var t = frame.Find(path);
                var image = t != null ? t.GetComponent<Image>() : null;
                if (image == null || veilMaterial == null) continue;
                var veil = GameObject.CreatePrimitive(PrimitiveType.Quad);
                veil.name = path + " (veil)";
                Destroy(veil.GetComponent<Collider>());
                veil.layer = Layer;
                var r = veil.GetComponent<MeshRenderer>();
                r.sharedMaterial = new Material(veilMaterial) { hideFlags = HideFlags.DontSave };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                veil.SetActive(false);
                dimmers.Add((image, veil.transform));
            }
        }

        /// <summary>The side panels are made by SidePanels in its Awake; hook them once they exist.</summary>
        void Start() { if (boards.Count < 3) { boards.Clear(); dimmers.Clear(); Hook(); } }

        void LateUpdate()
        {
            ApplyGlow();
            bool on = On;
            var cam = Camera;
            foreach (var (image, veil) in dimmers)
            {
                if (image == null || veil == null) continue;
                bool show = on && image.isActiveAndEnabled;
                if (on) image.canvasRenderer.SetAlpha(0f);
                else image.canvasRenderer.SetAlpha(1f);
                veil.gameObject.SetActive(show);
                if (!show) continue;
                // A quad filling the view just behind the signs, in the dimmer's colour.
                float d = Distance + BoardBehind + VeilBehind;
                float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.2f;
                veil.SetPositionAndRotation(cam.transform.position + cam.transform.forward * d, cam.transform.rotation);
                veil.localScale = new Vector3(h * cam.aspect * 1.2f, h, 1f);
                // A bit stronger than the 2D dimmer (the scene behind is bright HDR light, which a flat dimmer only half
                // covers once tone mapping squeezes it), but never above 0.7, so the game always shows through darker
                // rather than going black (the photo preview's 0.75 dimmer, times 1.5, blacked the scene out).
                var c = image.color;
                c.a = Mathf.Min(c.a * 1.5f, 0.7f) * GroupAlpha(image.transform);
                veil.GetComponent<MeshRenderer>().sharedMaterial.color = c;
            }
        }

        void OnDestroy()
        {
            foreach (var (_, veil) in dimmers) if (veil != null) Destroy(veil.gameObject);
            if (sceneMaterial != null) Destroy(sceneMaterial);
            if (signMaterial != null) Destroy(signMaterial);
            if (classicMaterial != null) Destroy(classicMaterial);
            if (contrastMaterial != null) Destroy(contrastMaterial);
            foreach (var (_, veil) in dimmers) if (veil != null) Destroy(veil.GetComponent<MeshRenderer>().sharedMaterial);
            if (veilMaterial != null) Destroy(veilMaterial);
            if (instance == this) instance = null;
        }

        // ---- Photos -----------------------------------------------------------------------------------------------

        readonly List<Renderer> hiddenForPhoto = new List<Renderer>();

        /// <summary>
        /// Hides the buttons, signs and dimmers (and so their shadows) while a photo is rendered, then shows them again.
        /// Picked by their materials, not their layer: the realistic look's sun beams share the layer (both stay out of
        /// the sky and sun maps), and leaving the layer out took the beams out of the photos too.
        /// </summary>
        public void HideForPhoto(bool hide)
        {
            foreach (var r in hiddenForPhoto) if (r != null) r.forceRenderingOff = false;
            hiddenForPhoto.Clear();
            if (!hide) return;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.layer != Layer || r.forceRenderingOff || !Owns(r.sharedMaterial)) continue;
                r.forceRenderingOff = true;
                hiddenForPhoto.Add(r);
            }
        }

        bool Owns(Material m)
        {
            if (m == null) return false;
            if (m == sceneMaterial || m == signMaterial || m == classicMaterial || m == contrastMaterial) return true;
            foreach (var (_, veil) in dimmers)
                if (veil != null && veil.GetComponent<MeshRenderer>().sharedMaterial == m) return true;
            return false;
        }

        // ---- Placement ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Puts a model where a 2D element sits on screen, `depth` metres from the camera, facing it. The model's local
        /// unit becomes one canvas unit of the overlay canvas, so a model built in canvas units matches the element's
        /// size on screen. Returns false if there's no camera.
        /// </summary>
        public bool Place(Transform model, RectTransform rect, float depth)
        {
            var cam = Camera;
            if (cam == null) return false;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners); // overlay canvas: world = screen pixels
            var centre = (corners[0] + corners[2]) * 0.5f;
            var ray = cam.ScreenPointToRay(new Vector3(centre.x, centre.y, 0f));
            // From the camera itself: the ray starts on the near plane, which put every model that much farther than
            // `depth` and so smaller on screen than its element (a fifth smaller at the classic look's 1.5 m).
            float along = depth / Mathf.Max(0.01f, Vector3.Dot(ray.direction, cam.transform.forward));
            model.SetPositionAndRotation(cam.transform.position + ray.direction * along, cam.transform.rotation);
            var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
            float metresPerPixel = 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, cam.pixelHeight);
            float k = metresPerPixel * canvas.scaleFactor;
            model.localScale = new Vector3(k, k, k);
            return true;
        }

        /// <summary>Metres per canvas unit for a model `depth` metres from the camera (what Place scales by).</summary>
        public float MetresPerCanvasUnit(RectTransform rect, float depth)
        {
            var cam = Camera;
            if (cam == null) return 0f;
            var canvas = rect.GetComponentInParent<Canvas>().rootCanvas;
            return 2f * depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, cam.pixelHeight) * canvas.scaleFactor;
        }

        /// <summary>The element's alpha from the canvas groups above it (1 if none).</summary>
        public static float GroupAlpha(Transform t)
        {
            float a = 1f;
            foreach (var g in t.GetComponentsInParent<CanvasGroup>())
            {
                a *= g.alpha;
                if (g.ignoreParentGroups) break;
            }
            return a;
        }

        /// <summary>Elements under the phone card stay 2D: the card is part of the final photo preview.</summary>
        public static bool KeptFlat(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name == "Phone Panel") return true;
            return false;
        }
    }
}
