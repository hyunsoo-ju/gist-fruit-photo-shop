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
    /// on the panel (the captured photos) stay 2D on top. Raised plates (SignPlate: the 랭킹's rows) stand on the planks
    /// with their texts on them, in each text's own colour (the 랭킹's black names, gold, silver and bronze places)
    /// without the dark edge. High contrast: a pure black board in a white frame, with white letters
    /// and grey ropes, unlit; plates are white with black letters.
    /// </summary>
    public class SignBoard : MonoBehaviour
    {
        /// <summary>Canvas units per board block (a button block is 4).</summary>
        const float BoardBlock = 8f;
        // The twig ring stands TwigRise blocks proud of the planks; the planks are PlankDepth thick behind it.
        const int TwigRise = 2, PlankDepth = 2, Thickness = TwigRise + PlankDepth, PlankHeight = 7, Corner = 9;
        const float BoardGrit = 0.12f;
        const int RopeWidth = 2, RopeLength = 600;
        const int PlateDepth = 2, PlateCorner = 3;
        const float PlateGrit = 0.06f;

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
        static readonly Color32 ContrastBoard = new Color32(0x00, 0x00, 0x00, 0xFF);
        static readonly Color32 ContrastFrame = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Color32 ContrastRope = new Color32(0x9A, 0x9A, 0x9A, 0xFF);

        /// <summary>How many board gaps further back than the usual (PhysicalUI.BoardBehind) this board hangs: the 랭킹
        /// is one further, so in portrait, where it hangs below the results, its ropes pass behind the results board.</summary>
        public float extraBehind;

        RectTransform rect;
        Image image;
        Transform root, sway, boardT;
        Mesh boardMesh, ropeMesh;
        Vector2Int builtBlocks;
        bool builtContrast;
        Material usedMaterial;
        bool usedShadows;
        readonly List<Part> texts = new List<Part>();
        readonly List<PlatePart> plates = new List<PlatePart>();
        float phase;

        class Part
        {
            public TMP_Text text;
            public Transform transform;
            public Mesh mesh;
            public string built;
            public Vector2 builtSize;
            public Color32 builtInk;
        }

        class PlatePart
        {
            public SignPlate plate;
            public Transform transform;
            public Mesh mesh;
            public Vector2Int builtBlocks;
            public Color32 builtColour;
            public bool builtContrast;
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
            foreach (var p in plates) if (p.mesh != null) Destroy(p.mesh);
        }

        void OnDisable()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            var ui = PhysicalUI.Instance;
            var material = PhysicalUI.On ? ui.BoardMaterial : null;
            bool on = material != null && PhysicalUI.GroupAlpha(transform) > 0.4f;
            ShowFlat(!on);
            if (!on)
            {
                if (root != null) root.gameObject.SetActive(false);
                return;
            }
            if (root == null) Build(ui);
            root.gameObject.SetActive(true);
            Rebuild(ui);
            UseMaterial(material);
            ui.Place(root, rect, PhysicalUI.Distance + PhysicalUI.BoardBehind * (1f + extraBehind));
            // Hanging from the ropes' tops: a slow swing of under a degree, so it reads as hanging, not as a moving target.
            float t = Time.time + phase;
            sway.localRotation = Quaternion.Euler(0f, 0f, 0.6f * Mathf.Sin(t * 0.7f) + 0.25f * Mathf.Sin(t * 1.9f));
        }

        /// <summary>The 2D panel and its texts: hidden (alpha 0) while the board shows, back otherwise.</summary>
        void ShowFlat(bool flat)
        {
            float a = flat ? 1f : 0f;
            if (image != null) image.canvasRenderer.SetAlpha(a);
            foreach (var plate in GetComponentsInChildren<SignPlate>())
                if (plate.TryGetComponent(out Image plateImage)) plateImage.canvasRenderer.SetAlpha(a);
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

        void AddRenderer(GameObject go, PhysicalUI ui)
        {
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            usedMaterial = null; // UseMaterial sets it up
        }

        /// <summary>The look or high contrast can change at any time: the board follows (material and shadows).</summary>
        void UseMaterial(Material material)
        {
            bool shadows = PhysicalUI.Shadows;
            if (material == usedMaterial && shadows == usedShadows) return;
            usedMaterial = material;
            usedShadows = shadows;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.sharedMaterial = material;
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = shadows;
            }
        }

        /// <summary>Rebuilds the board when its size changes and each text when it changes.</summary>
        void Rebuild(PhysicalUI ui)
        {
            var size = rect.rect.size;
            var blocks = new Vector2Int(Mathf.Max(8, Mathf.RoundToInt(size.x / BoardBlock)), Mathf.Max(8, Mathf.RoundToInt(size.y / BoardBlock)));
            bool contrast = HighContrast.On;
            if (contrast != builtContrast)
            {
                // Everything rebuilds in the other palette.
                builtContrast = contrast;
                builtBlocks = default;
                foreach (var p in texts) p.built = null;
            }
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

            RebuildPlates(ui, contrast);

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
                bool onPlate = t.GetComponentInParent<SignPlate>() != null;
                var ink = TextInk(t, onPlate, contrast);
                if (s != part.built || tr.rect.size != part.builtSize || !ink.Equals(part.builtInk))
                {
                    part.built = s;
                    part.builtSize = tr.rect.size;
                    part.builtInk = ink;
                    if (part.mesh != null) Destroy(part.mesh);
                    float glyph = Mathf.Max(1f, t.fontSize / 16f);
                    var bits = VoxelButtonKit.Label(t.font, s, Mathf.Max(8, Mathf.RoundToInt(tr.rect.width / glyph)), 24);
                    part.mesh = bits != null ? Letters(bits, ink) : null;
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
                // On the board's face (toward the camera is −z), or on its plate.
                float face = (PlankDepth + (onPlate ? PlateDepth : 0)) * BoardBlock;
                part.transform.localPosition = new Vector3(local.x, local.y - sway.localPosition.y, -face);
            }
        }

        /// <summary>A text's letter colour: the usual cream on the board (whatever colour the flat panel gives it); on a
        /// plate, the text's own colour (high contrast: black).</summary>
        static Color32 TextInk(TMP_Text t, bool onPlate, bool contrast)
        {
            if (!onPlate) return VoxelButtonKit.LabelCream;
            if (contrast) return new Color32(0, 0, 0, 0xFF);
            var c = t.color;
            return new Color(c.r, c.g, c.b, 1f);
        }

        /// <summary>The raised plates (SignPlate) under some of the text, each a rounded block on the planks.</summary>
        void RebuildPlates(PhysicalUI ui, bool contrast)
        {
            var current = new List<SignPlate>();
            foreach (var p in GetComponentsInChildren<SignPlate>())
                if (p.isActiveAndEnabled) current.Add(p);
            plates.RemoveAll(p =>
            {
                if (current.Contains(p.plate)) return false;
                if (p.transform != null) Destroy(p.transform.gameObject);
                if (p.mesh != null) Destroy(p.mesh);
                return true;
            });
            foreach (var plate in current)
            {
                var part = plates.Find(p => p.plate == plate);
                if (part == null)
                {
                    part = new PlatePart { plate = plate, transform = Child(boardT.parent, plate.name + " (plate)") };
                    AddRenderer(part.transform.gameObject, ui);
                    part.transform.GetComponent<MeshRenderer>().sharedMaterial = usedMaterial;
                    plates.Add(part);
                }
                var pr = (RectTransform)plate.transform;
                var blocks = new Vector2Int(Mathf.Max(4, Mathf.RoundToInt(pr.rect.width / BoardBlock)), Mathf.Max(4, Mathf.RoundToInt(pr.rect.height / BoardBlock)));
                var colour = contrast ? ContrastFrame : plate.colour;
                if (blocks != part.builtBlocks || !colour.Equals(part.builtColour) || contrast != part.builtContrast)
                {
                    part.builtBlocks = blocks;
                    part.builtColour = colour;
                    part.builtContrast = contrast;
                    if (part.mesh != null) Destroy(part.mesh);
                    part.mesh = Plate(blocks.x, blocks.y, colour, contrast);
                    part.transform.GetComponent<MeshFilter>().sharedMesh = part.mesh;
                    part.transform.localScale = Vector3.one * BoardBlock;
                }
                var local = (Vector2)rect.InverseTransformPoint(pr.TransformPoint(pr.rect.center)) - rect.rect.center;
                part.transform.localPosition = new Vector3(local.x, local.y - sway.localPosition.y, -PlankDepth * BoardBlock);
            }
        }

        static Mesh Plate(int w, int h, Color32 colour, bool contrast)
        {
            return BlockMesh.Build((x, y, z) =>
            {
                if (!VoxelButtonKit.Inside(x, y, w, h, PlateCorner)) return default;
                if (contrast) return colour;
                var c = z > 0 ? VoxelButtonKit.Shade(colour, 0.82f) : colour;
                return VoxelButtonKit.Grit(c, x, y, z, PlateGrit);
            }, new Vector3Int(w, h, PlateDepth), 1f, new Vector3(-w * 0.5f, -h * 0.5f, -PlateDepth), "Sign Plate");
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
                bool contrast = HighContrast.On;
                // How far round the edge this block is (for the twigs' thickness and knots).
                int along = x + y * 3;
                int strand = 4 + (int)(Hash(along / 6) % 3); // 4 to 6 blocks thick, changing every few blocks
                bool twig = !VoxelButtonKit.Inside(x - strand, y - strand, w - strand * 2, h - strand * 2, Mathf.Max(1, Corner - strand));
                if (twig)
                {
                    bool knot = Hash(along / 2 + 991) % 23 == 0;
                    if (z < (knot ? 0 : 1) && !knot) return default; // the ring is TwigRise proud, knots a block more
                    if (contrast) return ContrastFrame;
                    // Bark: streaks along the twig, a lighter strand now and then.
                    uint b = Hash(along / 4 + (x + y) % 2 * 17);
                    var bark = b % 3 == 0 ? BarkLight : b % 2 == 0 ? BarkA : BarkB;
                    return VoxelButtonKit.Grit(z <= 1 ? bark : VoxelButtonKit.Shade(bark, 0.8f), x, y, z, BoardGrit);
                }
                if (z < TwigRise) return default; // the planks sit behind the twig ring
                if (contrast) return ContrastBoard;
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
                if (HighContrast.On) return ContrastRope;
                // A twist: the light and dark strands swap every few blocks.
                return ((y / 3) + x + z) % 2 == 0 ? RopeA : RopeB;
            }, new Vector3Int(w, height, RopeWidth), 1f, new Vector3(-w * 0.5f, h * 0.5f, -Thickness + 0.5f), "Sign Ropes");
        }

        /// <summary>Raised cream letters with a dark edge round them, like the button labels.</summary>
        static Mesh Letters(bool[,] bits, Color32 ink)
        {
            bool cream = ink.Equals(VoxelButtonKit.LabelCream);
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
                if (Ink(x, y)) return level == raise - 1 ? ink : VoxelButtonKit.Shade(ink, 0.78f);
                if (cream && EdgeAt(x, y) && level < 1) return VoxelButtonKit.Edge;
                return default;
            }, new Vector3Int(w, h, raise), 1f, new Vector3(-w * 0.5f, -h * 0.5f, -raise), "Sign Text");
        }
    }
}
