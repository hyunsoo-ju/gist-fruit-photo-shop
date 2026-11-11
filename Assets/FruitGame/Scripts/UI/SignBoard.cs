using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// A UI panel as a wooden signboard in the scene (PhysicalUI, Docs/DESIGN.md §15): plank boards in a darker frame,
    /// hanging on two ropes from above and swaying a little, the panel's text raised on it in cream voxel letters with a
    /// dark edge (like the button labels). It sits where the panel is on screen and at its size, just behind the buttons
    /// on it. While it shows, the 2D panel and its text are hidden (their alpha, so taps and layout carry on); pictures
    /// on the panel (the captured photos) stay 2D on top.
    /// </summary>
    public class SignBoard : MonoBehaviour
    {
        /// <summary>Canvas units per board block (a button block is 4).</summary>
        const float BoardBlock = 8f;
        // The twig ring stands TwigRise blocks proud of the planks; the planks are PlankDepth thick behind it.
        const int TwigRise = 2, PlankDepth = 2, Thickness = TwigRise + PlankDepth, PlankHeight = 7, Corner = 9;
        const float BoardGrit = 0.12f;
        const int RopeWidth = 2, RopeLength = 600;

        // Authored dark: block colours go into the lighting as they are, and the midday sun washed the first, lighter
        // browns out to cream, so the cream letters didn't read on them.
        static readonly Color32 WoodA = new Color32(0x5E, 0x3A, 0x1C, 0xFF);
        static readonly Color32 WoodB = new Color32(0x52, 0x32, 0x18, 0xFF);
        static readonly Color32 WoodC = new Color32(0x68, 0x42, 0x22, 0xFF);
        static readonly Color32 BarkA = new Color32(0x3A, 0x26, 0x14, 0xFF);
        static readonly Color32 BarkB = new Color32(0x2A, 0x1A, 0x0C, 0xFF);
        static readonly Color32 BarkLight = new Color32(0x7A, 0x5A, 0x36, 0xFF);
        static readonly Color32 Seam = new Color32(0x24, 0x14, 0x08, 0xFF);
        static readonly Color32 RopeA = new Color32(0x8A, 0x72, 0x4A, 0xFF);
        static readonly Color32 RopeB = new Color32(0x6E, 0x58, 0x36, 0xFF);

        RectTransform rect;
        Image image;
        Transform root, sway, boardT;
        Mesh boardMesh, ropeMesh;
        Vector2Int builtBlocks;
        readonly List<Part> texts = new List<Part>();
        float phase;

        class Part
        {
            public TMP_Text text;
            public Transform transform;
            public Mesh mesh;
            public string built;
            public Vector2 builtSize;
        }

        void Awake()
        {
            rect = (RectTransform)transform;
            image = GetComponent<Image>();
            phase = Random.value * 10f;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            if (boardMesh != null) Destroy(boardMesh);
            if (ropeMesh != null) Destroy(ropeMesh);
            foreach (var p in texts) if (p.mesh != null) Destroy(p.mesh);
        }

        void OnDisable()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            var ui = PhysicalUI.Instance;
            bool on = PhysicalUI.On && ui.SignMaterial != null && PhysicalUI.GroupAlpha(transform) > 0.4f;
            ShowFlat(!on);
            if (!on)
            {
                if (root != null) root.gameObject.SetActive(false);
                return;
            }
            if (root == null) Build(ui);
            root.gameObject.SetActive(true);
            Rebuild(ui);
            ui.Place(root, rect, PhysicalUI.Distance + PhysicalUI.BoardBehind);
            // Hanging from the ropes' tops: a slow swing of under a degree, so it reads as hanging, not as a moving target.
            float t = Time.time + phase;
            sway.localRotation = Quaternion.Euler(0f, 0f, 0.6f * Mathf.Sin(t * 0.7f) + 0.25f * Mathf.Sin(t * 1.9f));
        }

        /// <summary>The 2D panel and its texts: hidden (alpha 0) while the board shows, back otherwise.</summary>
        void ShowFlat(bool flat)
        {
            float a = flat ? 1f : 0f;
            if (image != null) image.canvasRenderer.SetAlpha(a);
            foreach (var t in GetComponentsInChildren<TMP_Text>())
                if (t.GetComponentInParent<KeycapButton>() == null) t.canvasRenderer.SetAlpha(a);
        }

        void Build(PhysicalUI ui)
        {
            root = new GameObject(name + " (sign)").transform;
            root.gameObject.layer = ui.Layer;
            sway = Child(root, "Sway");
            boardT = Child(sway, "Board");
            var ropes = Child(sway, "Ropes");
            AddRenderer(boardT.gameObject, ui);
            AddRenderer(ropes.gameObject, ui);
        }

        Transform Child(Transform parent, string childName)
        {
            var t = new GameObject(childName).transform;
            t.gameObject.layer = root.gameObject.layer;
            t.SetParent(parent, false);
            return t;
        }

        static void AddRenderer(GameObject go, PhysicalUI ui)
        {
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ui.SignMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }

        /// <summary>Rebuilds the board when its size changes and each text when it changes.</summary>
        void Rebuild(PhysicalUI ui)
        {
            var size = rect.rect.size;
            var blocks = new Vector2Int(Mathf.Max(8, Mathf.RoundToInt(size.x / BoardBlock)), Mathf.Max(8, Mathf.RoundToInt(size.y / BoardBlock)));
            if (blocks != builtBlocks)
            {
                builtBlocks = blocks;
                if (boardMesh != null) Destroy(boardMesh);
                if (ropeMesh != null) Destroy(ropeMesh);
                boardMesh = Board(blocks.x, blocks.y);
                ropeMesh = Ropes(blocks.x, blocks.y);
                boardT.GetComponent<MeshFilter>().sharedMesh = boardMesh;
                sway.parent.Find("Sway/Ropes").GetComponent<MeshFilter>().sharedMesh = ropeMesh;
                // Swing from the ropes' tops, far above the board.
                float top = size.y * 0.5f + RopeLength * BoardBlock;
                sway.localPosition = new Vector3(0f, top, 0f);
                boardT.localPosition = new Vector3(0f, -top, 0f);
                sway.Find("Ropes").localPosition = new Vector3(0f, -top, 0f);
                boardT.localScale = sway.Find("Ropes").localScale = Vector3.one * BoardBlock;
            }

            // The panel's own texts (not the buttons' labels), raised on the board's face.
            var current = new List<TMP_Text>();
            foreach (var t in GetComponentsInChildren<TMP_Text>())
                if (t.GetComponentInParent<KeycapButton>() == null && t.isActiveAndEnabled) current.Add(t);
            texts.RemoveAll(p =>
            {
                if (current.Contains(p.text)) return false;
                if (p.transform != null) Destroy(p.transform.gameObject);
                if (p.mesh != null) Destroy(p.mesh);
                return true;
            });
            foreach (var t in current)
            {
                var part = texts.Find(p => p.text == t);
                if (part == null)
                {
                    part = new Part { text = t, transform = Child(boardT.parent, t.name) };
                    AddRenderer(part.transform.gameObject, ui);
                    texts.Add(part);
                }
                string s = t.text;
                var tr = t.rectTransform;
                if (s != part.built || tr.rect.size != part.builtSize)
                {
                    part.built = s;
                    part.builtSize = tr.rect.size;
                    if (part.mesh != null) Destroy(part.mesh);
                    float glyph = Mathf.Max(1f, t.fontSize / 16f);
                    var bits = VoxelButtonKit.Label(t.font, s, Mathf.Max(8, Mathf.RoundToInt(tr.rect.width / glyph)), 24);
                    part.mesh = bits != null ? Letters(bits) : null;
                    part.transform.GetComponent<MeshFilter>().sharedMesh = part.mesh;
                    part.transform.localScale = Vector3.one * glyph;
                }
                // Where the text sits on the panel, by its alignment within its own box.
                float glyphSize = part.transform.localScale.x;
                var local = (Vector2)rect.InverseTransformPoint(tr.TransformPoint(tr.rect.center)) - rect.rect.center;
                var mesh = part.mesh;
                if (mesh != null)
                {
                    var b = mesh.bounds.size * glyphSize;
                    var box = tr.rect.size;
                    var h = t.horizontalAlignment;
                    var v = t.verticalAlignment;
                    float x = h == HorizontalAlignmentOptions.Left ? -box.x * 0.5f + b.x * 0.5f : h == HorizontalAlignmentOptions.Right ? box.x * 0.5f - b.x * 0.5f : 0f;
                    float y = v == VerticalAlignmentOptions.Top ? box.y * 0.5f - b.y * 0.5f : v == VerticalAlignmentOptions.Bottom ? -box.y * 0.5f + b.y * 0.5f : 0f;
                    local += new Vector2(x, y);
                }
                // On the board's face (toward the camera is −z).
                part.transform.localPosition = new Vector3(local.x, local.y - sway.localPosition.y, -PlankDepth * BoardBlock);
            }
        }

        /// <summary>
        /// A rounded board of planks with a ring of twigs round its edge; toward the camera is −z. The twigs are uneven:
        /// four to six blocks thick by turns, in bark tones, with the odd knot standing out, so the edge reads as
        /// bundled branches rather than a sawn frame. Every block gets a little grit.
        /// </summary>
        static Mesh Board(int w, int h)
        {
            return BlockMesh.Build((x, y, z) =>
            {
                if (!VoxelButtonKit.Inside(x, y, w, h, Corner)) return default;
                // How far round the edge this block is (for the twigs' thickness and knots).
                int along = x + y * 3;
                int strand = 4 + (int)(Hash(along / 6) % 3); // 4 to 6 blocks thick, changing every few blocks
                bool twig = !VoxelButtonKit.Inside(x - strand, y - strand, w - strand * 2, h - strand * 2, Mathf.Max(1, Corner - strand));
                if (twig)
                {
                    bool knot = Hash(along / 2 + 991) % 23 == 0;
                    if (z < (knot ? 0 : 1) && !knot) return default; // the ring is TwigRise proud, knots a block more
                    // Bark: streaks along the twig, a lighter strand now and then.
                    uint b = Hash(along / 4 + (x + y) % 2 * 17);
                    var bark = b % 3 == 0 ? BarkLight : b % 2 == 0 ? BarkA : BarkB;
                    return VoxelButtonKit.Grit(z <= 1 ? bark : VoxelButtonKit.Shade(bark, 0.8f), x, y, z, BoardGrit);
                }
                if (z < TwigRise) return default; // the planks sit behind the twig ring
                int plank = y / PlankHeight;
                Color32 c;
                if (y % PlankHeight == PlankHeight - 1) c = Seam;
                else
                {
                    var wood = plank % 3 == 0 ? WoodA : plank % 3 == 1 ? WoodB : WoodC;
                    // Grain: a slightly different tone every few blocks along a plank.
                    c = (x * 7 + plank * 13 + (x / 5) * 3) % 9 == 0 ? VoxelButtonKit.Shade(wood, 0.9f) : wood;
                }
                if (z > TwigRise) c = VoxelButtonKit.Shade(c, 0.8f);
                return VoxelButtonKit.Grit(c, x, y, z, BoardGrit);
            }, new Vector3Int(w, h, Thickness), 1f, new Vector3(-w * 0.5f, -h * 0.5f, -Thickness), "Sign Board");
        }

        static uint Hash(int v)
        {
            uint h = (uint)v * 0x9E3779B1;
            h ^= h >> 15; h *= 0x85EBCA6B; h ^= h >> 13;
            return h;
        }

        /// <summary>Two twisted ropes from the board's top, a fifth in from each side, up out of view.</summary>
        static Mesh Ropes(int w, int h)
        {
            int left = Mathf.RoundToInt(w * 0.2f), right = w - left - RopeWidth;
            int height = RopeLength;
            return BlockMesh.Build((x, y, z) =>
            {
                bool inLeft = x >= left && x < left + RopeWidth, inRight = x >= right && x < right + RopeWidth;
                if (!inLeft && !inRight) return default;
                // A twist: the light and dark strands swap every few blocks.
                return ((y / 3) + x + z) % 2 == 0 ? RopeA : RopeB;
            }, new Vector3Int(w, height, RopeWidth), 1f, new Vector3(-w * 0.5f, h * 0.5f, -Thickness + 0.5f), "Sign Ropes");
        }

        /// <summary>Raised cream letters with a dark edge round them, like the button labels.</summary>
        static Mesh Letters(bool[,] bits)
        {
            int w = bits.GetLength(0) + 2, h = bits.GetLength(1) + 2;
            bool Ink(int x, int y) => x >= 1 && y >= 1 && x - 1 < bits.GetLength(0) && y - 1 < bits.GetLength(1) && bits[x - 1, y - 1];
            bool EdgeAt(int x, int y)
            {
                if (Ink(x, y)) return false;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Ink(x + dx, y + dy)) return true;
                return false;
            }
            const int raise = 3;
            return BlockMesh.Build((x, y, z) =>
            {
                int level = raise - 1 - z; // z = 0 is the top, toward the camera
                if (Ink(x, y)) return level == raise - 1 ? VoxelButtonKit.LabelCream : VoxelButtonKit.Shade(VoxelButtonKit.LabelCream, 0.78f);
                if (EdgeAt(x, y) && level < 1) return VoxelButtonKit.Edge;
                return default;
            }, new Vector3Int(w, h, raise), 1f, new Vector3(-w * 0.5f, -h * 0.5f, -raise), "Sign Text");
        }
    }
}
