using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The start-screen title 「지스트의 과일 사진가게」 in 3D blocks (Docs/DESIGN.md §11). Each title line is a 1-bit glyph
    /// bitmap (Art/Title, rendered from Neo둥근모 at 16 px, 1 pixel = 1 block) extruded in the sticker style: cream
    /// letters, a pink outline one block further back, and a dark shadow block behind, offset down-right. The voxel camera
    /// icon sits beside the first line. The title is cut into strips and slices that drop in like fruit and stack into
    /// the letters whenever the start screen appears; then it bobs gently and a band of light sweeps across now and then.
    /// It's built at runtime from the bitmaps, so changing the text only needs new bitmaps.
    /// </summary>
    public class VoxelTitle : MonoBehaviour
    {
        [SerializeField] GameStateMachine game;
        [SerializeField] Material material;
        [Tooltip("Title lines, top to bottom (readable 1-bit bitmaps; dark = letter).")]
        [SerializeField] Texture2D[] lines;
        [SerializeField] Texture2D subtitle;
        [SerializeField] bool showIcon = true;
        [Tooltip("A colour pixel logo (readable; alpha = shape, 1 pixel = 1 block) placed at the start of the first line.")]
        [SerializeField] Texture2D logo;
        [Tooltip("Outline around the logo (its own colours stay).")]
        [SerializeField] Color32 logoOutline = new Color32(0xFF, 0xF8, 0xEC, 0xFF);

        [Header("Shape")]
        [SerializeField] float block = 0.034f;
        [SerializeField] float subtitleScale = 0.5f;
        [Tooltip("Extrusion in blocks: about the stroke's cross-section (letter plus the outline ring on each side).")]
        [SerializeField] int depth = 4;
        [Tooltip("Blocks between lines.")]
        [SerializeField] int lineGap = 2;
        [SerializeField] Color32 face = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
        [SerializeField] Color32 outline = new Color32(0xC2, 0x18, 0x5B, 0xFF);
        [SerializeField] Color32 shadow = new Color32(0x59, 0x0A, 0x2E, 0xFF);

        [Header("Motion")]
        [SerializeField] float dropHeight = 4.5f;
        [SerializeField] float gravity = 22f;
        [SerializeField] float stripStagger = 0.07f;
        [SerializeField] float rowStagger = 0.05f;
        [SerializeField] float bob = 0.03f;
        [Tooltip("Gentle wiggle, degrees around x and y, so the depth reads.")]
        [SerializeField] Vector2 wiggle = new Vector2(4f, 9f);
        [Tooltip("Gentle scale pulse (fraction).")]
        [SerializeField] float pulse = 0.025f;
        [SerializeField] float sweepEvery = 4f;
        [SerializeField] float sweepTime = 1.1f;

        class Piece
        {
            public Transform transform;
            public Renderer renderer;
            public Vector3 home;
            public float delay;
        }

        readonly List<Piece> pieces = new List<Piece>();
        Material instance;
        Transform root;
        Vector3 rootHome;
        bool shown;
        float shownAt;
        float width;
        static readonly int SweepX = Shader.PropertyToID("_SweepX");

        public bool Built => root != null;

        void Awake()
        {
            Build();
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (instance != null) Destroy(instance);
            foreach (var p in pieces)
                if (p.transform != null) Destroy(p.transform.GetComponent<MeshFilter>().sharedMesh);
        }

        void Update()
        {
            bool want = game == null || game.State == GameState.Boot;
            if (want != shown)
            {
                SetVisible(want);
                if (want) shownAt = Time.time;
            }
            if (!shown) return;

            float t = Time.time - shownAt;
            float last = 0f;
            foreach (var p in pieces)
            {
                float local = t - p.delay;
                last = Mathf.Max(last, p.delay);
                p.renderer.enabled = local > 0f;
                p.transform.localPosition = p.home + Vector3.up * Height(local);
            }
            // Once everything has landed: a gentle bob, and a light sweep every few seconds.
            float settled = t - last - FallTime() - 0.3f;
            root.localPosition = rootHome + Vector3.up * (settled > 0f ? Mathf.Sin(settled * 1.6f) * bob : 0f);
            // Always: a slight angular wiggle and scale pulse, so it clearly reads as 3D blocks.
            root.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.1f) * wiggle.x, Mathf.Sin(t * 0.8f + 1f) * wiggle.y, 0f);
            root.localScale = Vector3.one * (1f + Mathf.Sin(t * 1.9f) * pulse);
            float phase = settled > 0f ? settled % sweepEvery : -1f;
            float sweep = phase >= 0f && phase < sweepTime
                ? Mathf.Lerp(-width * 0.5f - 1f, width * 0.5f + 1f, phase / sweepTime) + transform.position.x
                : -1000f;
            instance.SetFloat(SweepX, sweep);
        }

        float FallTime() => Mathf.Sqrt(2f * dropHeight / gravity);

        /// <summary>Height above home at time t after a piece's drop starts: free fall, then one small bounce.</summary>
        float Height(float t)
        {
            if (t <= 0f) return dropHeight;
            float fall = FallTime();
            if (t < fall) return dropHeight - 0.5f * gravity * t * t;
            float b = t - fall;
            const float bounceTime = 0.22f;
            return b < bounceTime ? Mathf.Sin(b / bounceTime * Mathf.PI) * 0.06f : 0f;
        }

        void SetVisible(bool on)
        {
            shown = on;
            if (root != null) root.gameObject.SetActive(on);
        }

        /// <summary>Builds (or rebuilds) the blocks from the bitmaps. Public so editor tools can build it for exports.</summary>
        public void Build()
        {
            if (root != null) DestroyImmediate(root.gameObject);
            pieces.Clear();
            instance = material != null ? new Material(material) : null;
            root = new GameObject("Blocks").transform;
            root.SetParent(transform, false);
            rootHome = root.localPosition;

            // Rows from the top: each title line (the first one led by the logo and followed by the icon), then the
            // subtitle smaller. Parts on a line sit side by side, centred vertically on the tallest.
            float y = 0f;
            float maxWidth = 0f;
            int strip = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] == null) continue;
                var parts = new List<(Grid grid, float size, float z)>();
                if (i == 0 && logo != null) parts.Add((Sticker(logo, true), block, 0f));
                var text = Sticker(lines[i], false);
                parts.Add((text, block, 0f));
                if (i == 0 && showIcon)
                {
                    var icon = CameraIcon();
                    float iconBlock = text.size.y * block / icon.size.y;
                    parts.Add((icon, iconBlock, -iconBlock));
                }
                float gap = 2 * block;
                float lineW = -gap, lineH = 0f;
                foreach (var p in parts) { lineW += p.grid.size.x * p.size + gap; lineH = Mathf.Max(lineH, p.grid.size.y * p.size); }
                float x = -lineW * 0.5f;
                foreach (var p in parts)
                {
                    float ph = p.grid.size.y * p.size;
                    AddPieces(p.grid, p.size, new Vector3(x, y - lineH + (lineH - ph) * 0.5f, p.z), ref strip);
                    x += p.grid.size.x * p.size + gap;
                }
                maxWidth = Mathf.Max(maxWidth, lineW);
                y -= lineH + lineGap * block;
            }
            if (subtitle != null)
            {
                float sb = block * subtitleScale;
                var grid = Sticker(subtitle, false);
                y -= lineGap * block;
                AddPieces(grid, sb, new Vector3(-grid.size.x * sb * 0.5f, y - grid.size.y * sb, 0f), ref strip);
                maxWidth = Mathf.Max(maxWidth, grid.size.x * sb);
            }
            width = maxWidth;

            // Pivot the whole title about its middle, so the wiggle and pulse are centred.
            var centre = new Vector3(0f, y * 0.5f, 0f);
            foreach (var p in pieces)
            {
                p.home -= centre;
                p.transform.localPosition = p.home;
            }
            rootHome = centre;
            root.localPosition = centre;
        }

        /// <summary>A coloured block grid and its size.</summary>
        struct Grid
        {
            public Color32[,,] cells;
            public Vector3Int size;
        }

        /// <summary>
        /// Extrudes a bitmap in the sticker style: face, outline set back one block, shadow behind. A glyph bitmap
        /// (dark = letter) gets the cream face and pink outline; a colour logo (alpha = shape) keeps its own colours and
        /// gets the logo outline.
        /// </summary>
        Grid Sticker(Texture2D bitmap, bool colourLogo)
        {
            var cells = BlockMesh.Sticker(
                (x, y) => colourLogo ? bitmap.GetPixel(x, y).a > 0.5f : bitmap.GetPixel(x, y).grayscale < 0.5f,
                (x, y) => colourLogo ? (Color32)bitmap.GetPixel(x, y) : face,
                bitmap.width, bitmap.height, depth, colourLogo ? logoOutline : outline, shadow, out var size);
            return new Grid { cells = cells, size = size };
        }

        /// <summary>
        /// A small voxel camera whose lens is an orange slice: pink body, dark front plate, white flash, shutter button.
        /// </summary>
        public static Color32[,,] CameraIconCells(out Vector3Int size)
        {
            size = new Vector3Int(18, 13, 6);
            var c = new Color32[size.x, size.y, size.z];
            var body = new Color32(0xC2, 0x18, 0x5B, 0xFF);
            var bodyDark = new Color32(0x8E, 0x10, 0x42, 0xFF);
            var plate = new Color32(0x33, 0x41, 0x4F, 0xFF);
            var white = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
            var peel = new Color32(0xF2, 0x8C, 0x28, 0xFF);
            var flesh = new Color32(0xFF, 0xB3, 0x47, 0xFF);
            var pith = new Color32(0xFF, 0xE9, 0xC4, 0xFF);
            // Body: rows 0–9, rounded corners, 4 deep (z 2–5).
            for (int y = 0; y < 10; y++)
            for (int x = 1; x < 17; x++)
            {
                bool corner = (y == 0 || y == 9) && (x == 1 || x == 16);
                if (corner) continue;
                for (int z = 2; z < 6; z++) c[x, y, z] = y == 0 ? bodyDark : body;
            }
            // Viewfinder hump and shutter button on top.
            for (int x = 4; x < 9; x++) for (int z = 3; z < 6; z++) { c[x, 10, z] = body; c[x, 11, z] = x == 4 || x == 8 ? body : bodyDark; }
            for (int x = 12; x < 15; x++) for (int z = 3; z < 5; z++) c[x, 10, z] = white;
            // Front plate strip and flash.
            for (int x = 2; x < 16; x++) c[x, 8, 1] = plate;
            c[13, 8, 1] = white; c[14, 8, 1] = white;
            // Lens: an orange slice, radius 4.5, sticking out 2 blocks (z 0–1).
            var centre = new Vector2(9f, 4.5f);
            for (int y = 0; y < 10; y++)
            for (int x = 3; x < 16; x++)
            {
                var d = new Vector2(x + 0.5f, y + 0.5f) - centre;
                float r = d.magnitude;
                if (r > 4.6f) continue;
                Color32 col;
                if (r > 3.7f) col = peel;
                else if (r > 3.0f) col = pith;
                else
                {
                    // Six segments separated by pith lines, with a pith centre.
                    float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 360f;
                    bool spoke = (ang % 60f) < 12f && r > 0.8f;
                    col = r < 0.9f || spoke ? pith : flesh;
                }
                c[x, y, 0] = col;
                c[x, y, 1] = r > 3.7f ? peel : plate;
            }
            return c;
        }

        static Grid CameraIcon()
        {
            var cells = CameraIconCells(out var size);
            return new Grid { cells = cells, size = size };
        }

        /// <summary>Cuts a grid into 8-column strips × 4-row slices, each its own object for the drop-in.</summary>
        void AddPieces(Grid grid, float size, Vector3 origin, ref int strip)
        {
            const int StripW = 8, SliceH = 4;
            int strips = Mathf.CeilToInt(grid.size.x / (float)StripW);
            int slices = Mathf.CeilToInt(grid.size.y / (float)SliceH);
            for (int s = 0; s < strips; s++)
            {
                for (int r = 0; r < slices; r++)
                {
                    int x0 = s * StripW, y0 = r * SliceH;
                    var cells = grid.cells;
                    var dims = grid.size;
                    var mesh = BlockMesh.Build((x, y, z) =>
                    {
                        int gx = x0 + x, gy = y0 + y;
                        return gx < dims.x && gy < dims.y ? cells[gx, gy, z] : default;
                    }, new Vector3Int(StripW, SliceH, dims.z), size, Vector3.zero, $"Title_{strip + s}_{r}");
                    if (mesh.vertexCount == 0) { DestroyImmediate(mesh); continue; }
                    var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
                    go.layer = gameObject.layer;
                    go.transform.SetParent(root, false);
                    var home = origin + new Vector3(x0 * size, y0 * size, 0f);
                    go.transform.localPosition = home;
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var rend = go.GetComponent<MeshRenderer>();
                    rend.sharedMaterial = instance;
                    rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    pieces.Add(new Piece { transform = go.transform, renderer = rend, home = home, delay = (strip + s) * stripStagger + r * rowStagger });
                }
            }
            strip += strips;
        }
    }
}
