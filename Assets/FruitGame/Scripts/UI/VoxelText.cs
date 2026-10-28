using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Shows a HUD TextMeshPro text (score, timer) as 3D sticker-style block digits (Docs/DESIGN.md §11). It mirrors the
    /// text, visibility and scale of the TMP object it sits on (so GameHUD's pop and pulse animations carry over), hides
    /// the TMP itself, and draws through the HUD camera, a perspective overlay camera on its own layer where one world
    /// unit is one screen pixel at its PixelDistance. One glyph pixel is one block, at the TMP's font size
    /// (fontSize / 16 canvas units), exactly like the pixel font. Each digit wiggles and pulses gently so the depth
    /// reads; with bumpOnIncrease, the digits jump toward the camera and swell for a moment when the number goes up.
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class VoxelText : MonoBehaviour
    {
        [SerializeField] Camera hudCamera;
        [SerializeField] Material material;
        [Tooltip("Glyph strip: one 8-pixel-wide cell per character of charset (dark = ink).")]
        [SerializeField] Texture2D glyphs;
        [SerializeField] string charset = "0123456789:";
        [SerializeField] int cellWidth = 8;
        [Tooltip("Extrusion in blocks: about the stroke's cross-section (1 block of letter plus the outline ring on each side).")]
        [SerializeField] int depth = 4;
        [SerializeField] Color32 face = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
        [SerializeField] Color32 outline = new Color32(0xC2, 0x18, 0x5B, 0xFF);
        [SerializeField] Color32 shadow = new Color32(0x59, 0x0A, 0x2E, 0xFF);

        [Header("Motion")]
        [Tooltip("Gentle wiggle, degrees around x and y.")]
        [SerializeField] Vector2 wiggle = new Vector2(6f, 12f);
        [Tooltip("Gentle scale pulse (fraction).")]
        [SerializeField] float pulse = 0.04f;
        [SerializeField] bool bumpOnIncrease;
        [Tooltip("How far the digits jump straight toward the camera, as a fraction of their distance.")]
        [SerializeField] float bumpDepth = 0.18f;
        [SerializeField] float bumpScale = 0.25f;
        [SerializeField] float bumpTime = 0.3f;

        TMP_Text text;
        Canvas canvas;
        HudCamera hud;
        Transform root;
        Material instance;
        readonly Dictionary<char, (Mesh mesh, int width)> meshes = new Dictionary<char, (Mesh, int)>();
        readonly List<Transform> chars = new List<Transform>();
        readonly List<Vector3> homes = new List<Vector3>();
        string shown;
        float bumpStart = -10f;
        float width;
        int glyphHeight;

        void Awake()
        {
            text = GetComponent<TMP_Text>();
            canvas = GetComponentInParent<Canvas>().rootCanvas;
            hud = hudCamera.GetComponent<HudCamera>();
            text.enabled = false;
            instance = new Material(material);
            root = new GameObject(name + " (voxel)").transform;
            root.gameObject.layer = hudCamera.gameObject.layer;
            glyphHeight = glyphs.height + 3;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            foreach (var m in meshes.Values) Destroy(m.mesh);
            Destroy(instance);
        }

        void OnEnable() { if (root != null) root.gameObject.SetActive(true); }
        void OnDisable() { if (root != null) root.gameObject.SetActive(false); }

        void LateUpdate()
        {
            if (text.text != shown)
            {
                if (bumpOnIncrease && int.TryParse(text.text, out int now) && int.TryParse(shown, out int before) && now > before)
                    bumpStart = Time.unscaledTime;
                shown = text.text;
                Layout();
            }
            Place();
        }

        /// <summary>One child per character, each centred on its own cell (so it wiggles and pulses about its middle).</summary>
        void Layout()
        {
            foreach (var c in chars) Destroy(c.gameObject);
            chars.Clear();
            homes.Clear();
            float x = 0f;
            foreach (char ch in shown)
            {
                if (!Glyph(ch, out var mesh, out int w)) { x += cellWidth; continue; }
                var go = new GameObject(ch.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
                go.layer = root.gameObject.layer;
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(x + (w + 3) * 0.5f, glyphHeight * 0.5f, 0f);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = instance;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                chars.Add(go.transform);
                homes.Add(go.transform.localPosition);
                // A full sticker cell (glyph + outline ring + shadow) per character, so neighbours never overlap.
                x += w + 3;
            }
            width = x;
        }

        /// <summary>Puts the digits where the TMP text would be, aligned like it (top-left or top-right), at its scale.</summary>
        void Place()
        {
            var rt = text.rectTransform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners); // overlay canvas: screen pixels
            float scale = canvas.scaleFactor * rt.lossyScale.y / canvas.transform.lossyScale.y;
            float block = text.fontSize / 16f * scale;
            bool right = text.horizontalAlignment == HorizontalAlignmentOptions.Right;
            // The text hangs from the rect's top edge, left- or right-aligned (edge already includes the HUD's scale).
            var top = Vector3.Lerp(corners[1], corners[2], right ? 1f : 0f);
            var edge = hud.ScreenToWorld(top);
            root.localScale = Vector3.one * block;
            root.position = edge + new Vector3(right ? -width * block : 0f, -glyphHeight * block, 0f);

            // Score bump: a quick jump toward the camera and a swell, easing back.
            float u = (Time.unscaledTime - bumpStart) / bumpTime;
            float b = u >= 0f && u < 1f ? Mathf.Sin(u * Mathf.PI) * (1f - u * 0.3f) : 0f;
            float bumpDistance = b * bumpDepth * hud.PixelDistance; // world units
            var cameraPos = hudCamera.transform.position;

            float t = Time.unscaledTime;
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                // Straight toward the camera, so the digit grows in place on screen instead of sliding outward.
                var home = root.TransformPoint(homes[i]);
                var offset = (cameraPos - home).normalized * bumpDistance;
                c.localPosition = homes[i] + root.InverseTransformVector(offset);
                c.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f + i * 0.9f) * wiggle.x, Mathf.Sin(t * 1.7f + i * 0.6f) * wiggle.y, 0f);
                c.localScale = Vector3.one * (1f + Mathf.Sin(t * 2.2f + i * 0.8f) * pulse + b * bumpScale);
            }
        }

        bool Glyph(char ch, out Mesh mesh, out int w)
        {
            if (meshes.TryGetValue(ch, out var cached)) { mesh = cached.mesh; w = cached.width; return true; }
            mesh = null; w = 0;
            int index = charset.IndexOf(ch);
            if (index < 0) return false;
            int x0 = index * cellWidth;
            int cw = Mathf.Min(cellWidth, glyphs.width - x0);
            // Trim empty columns so characters sit snugly.
            int first = cw, last = -1;
            for (int x = 0; x < cw; x++)
                for (int y = 0; y < glyphs.height; y++)
                    if (glyphs.GetPixel(x0 + x, y).grayscale < 0.5f) { first = Mathf.Min(first, x); last = Mathf.Max(last, x); }
            if (last < 0) return false;
            w = last - first + 1;
            int fx = x0 + first;
            var cells = BlockMesh.Sticker((x, y) => glyphs.GetPixel(fx + x, y).grayscale < 0.5f, (x, y) => face,
                w, glyphs.height, depth, outline, shadow, out var size);
            // Centred on the character so it wiggles and pulses about its middle.
            mesh = BlockMesh.Build((x, y, z) => cells[x, y, z], size, 1f, -(Vector3)size * 0.5f, "Glyph_" + ch);
            meshes[ch] = (mesh, w);
            return true;
        }
    }
}
