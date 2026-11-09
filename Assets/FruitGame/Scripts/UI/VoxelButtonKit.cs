using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Builds the voxel buttons' blocks (Docs/DESIGN.md §15, M6 1.3), one block per UI art pixel: a flat cream
    /// **border** ring with rounded corners that never moves, and the **cap** inside it (its own mesh, so a press can sink
    /// it): a rounded slab in the button's colour with a 1-block **ridge** two blocks in from its edge, and the label in
    /// cream blocks raised two blocks above it on a 1-block dark edge. Labels come straight from the pixel font's own
    /// atlas, one glyph pixel per block, so any Korean or English label works.
    /// Heights are counted in blocks up from the base; the meshes face −z (toward a camera looking along +z).
    /// </summary>
    public static class VoxelButtonKit
    {
        public const int BorderWidth = 3;
        public const int BorderHeight = 2;
        public const int CapHeight = 8;         // cap top: 6 blocks above the border
        public const int RidgeInset = 2;
        public const int RidgeHeight = 2;
        public const int LabelRaise = 4;
        public const int EdgeHeight = 2;
        /// <summary>Hover lifts the cap this many blocks.</summary>
        public const int HoverLift = 2;
        public const int CornerRadius = 4;

        // The border is a warm tan, darker than the old cream so it stands out from the light background.
        static readonly Color32 Tan = new Color32(0xB9, 0x8A, 0x5E, 0xFF);
        static readonly Color32 ContrastBorder = new Color32(0xFF, 0xD4, 0x00, 0xFF);
        public static Color32 Cream => HighContrast.On ? ContrastBorder : Tan;
        public static readonly Color32 LabelCream = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
        public static readonly Color32 Edge = new Color32(0x1E, 0x22, 0x33, 0xFF);

        /// <summary>Inside a w × h rounded rectangle (corner radius r)?</summary>
        static bool Inside(int x, int y, int w, int h, int r)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            int cx = x < r ? r - 1 : x >= w - r ? w - r : x;
            int cy = y < r ? r - 1 : y >= h - r ? h - r : y;
            if (cx == x || cy == y) return true;
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= (r - 0.5f) * (r - 0.5f) + 0.5f;
        }

        /// <summary>The border ring: the whole button's rounded rectangle minus the cap's hole, BorderHeight tall.</summary>
        public static Mesh Border(int w, int h)
        {
            int capW = w - BorderWidth * 2, capH = h - BorderWidth * 2;
            var size = new Vector3Int(w, h, BorderHeight);
            return BlockMesh.Build((x, y, z) =>
            {
                if (!Inside(x, y, w, h, CornerRadius)) return default;
                if (Inside(x - BorderWidth, y - BorderWidth, capW, capH, CornerRadius - 1)) return default;
                return z == 0 ? Cream : Shade(Cream, 0.82f); // z = 0 is the top layer; lower blocks shade the sides
            }, size, 1f, Origin(w, h, BorderHeight), "Button Border");
        }

        /// <summary>
        /// The cap with its ridge and label, sized to sit in the border's hole. Its base is at height 0 like the border's,
        /// so moving it along z by whole blocks lifts (hover) or sinks (press) it.
        /// </summary>
        public static Mesh Cap(int w, int h, Color32 colour, bool[,] label)
        {
            int capW = w - BorderWidth * 2, capH = h - BorderWidth * 2;
            var ridge = Shade(colour, 0.72f);
            int lw = label != null ? label.GetLength(0) : 0, lh = label != null ? label.GetLength(1) : 0;
            int lx0 = (capW - lw) / 2, ly0 = (capH - lh) / 2;
            bool Ink(int x, int y)
            {
                int gx = x - lx0, gy = y - ly0;
                return label != null && gx >= 0 && gy >= 0 && gx < lw && gy < lh && label[gx, gy];
            }
            bool EdgeAt(int x, int y)
            {
                if (Ink(x, y)) return false;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Ink(x + dx, y + dy)) return true;
                return false;
            }

            int heights = CapHeight + LabelRaise;
            var size = new Vector3Int(capW, capH, heights);
            return BlockMesh.Build((x, y, z) =>
            {
                int level = heights - 1 - z; // z = 0 is the top, toward the camera
                if (!Inside(x, y, capW, capH, CornerRadius - 1)) return default;
                // The top layer in the button's colour, the blocks under it darker so the cap's sides read.
                if (level < CapHeight) return level == CapHeight - 1 ? colour : Shade(colour, level >= CapHeight - 3 ? 0.8f : 0.62f);
                bool onRidge = Inside(x - RidgeInset, y - RidgeInset, capW - RidgeInset * 2, capH - RidgeInset * 2, CornerRadius - 2)
                               && !Inside(x - RidgeInset - 1, y - RidgeInset - 1, capW - RidgeInset * 2 - 2, capH - RidgeInset * 2 - 2, CornerRadius - 2);
                if (onRidge && level < CapHeight + RidgeHeight) return ridge;
                if (Ink(x, y) && level < CapHeight + LabelRaise) return level == CapHeight + LabelRaise - 1 ? LabelCream : Shade(LabelCream, 0.78f);
                if (EdgeAt(x, y) && level < CapHeight + EdgeHeight) return Edge;
                return default;
            }, size, 1f, Origin(capW, capH, heights), "Button Cap");
        }

        /// <summary>Centred on x and y, with height 0 at z = 0 and higher blocks toward −z.</summary>
        static Vector3 Origin(int w, int h, int heights) => new Vector3(-w * 0.5f, -h * 0.5f, -heights);

        public static Color32 Shade(Color32 c, float k) =>
            new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), c.a);

        // ---- Glyphs from the pixel font's atlas -------------------------------------------------------------------------

        static readonly Dictionary<Texture, Texture2D> readable = new Dictionary<Texture, Texture2D>();
        static readonly Regex Tags = new Regex("<[^>]*>");

        /// <summary>
        /// The label as a bitmap (true = ink), one glyph pixel per cell, lines stacked; null if it has no ink.
        /// The pixel font is a raster font sampled at its native size, so atlas pixels are glyph pixels.
        /// </summary>
        public static bool[,] Label(TMP_FontAsset font, string text, int maxWidth, int maxLines = 2)
        {
            if (font == null || string.IsNullOrEmpty(text)) return null;
            text = Tags.Replace(text, "").Replace("\\n", "\n");
            var lines = new List<string>(text.Split('\n'));
            // Too wide for the cap: break the line at the space nearest its middle (if two lines fit).
            for (int i = 0; i < lines.Count && maxLines > lines.Count; i++)
            {
                if (Width(font, lines[i]) <= maxWidth || !lines[i].Contains(" ")) continue;
                int mid = lines[i].Length / 2, best = -1;
                for (int k = 0; k < lines[i].Length; k++)
                    if (lines[i][k] == ' ' && (best < 0 || Mathf.Abs(k - mid) < Mathf.Abs(best - mid))) best = k;
                lines.Insert(i + 1, lines[i].Substring(best + 1));
                lines[i] = lines[i].Substring(0, best);
            }

            var face = font.faceInfo;
            int lineHeight = Mathf.RoundToInt(face.ascentLine - face.descentLine);
            int lineGap = 2;
            int width = 0;
            foreach (var l in lines) width = Mathf.Max(width, Width(font, l));
            int height = lines.Count * lineHeight + (lines.Count - 1) * lineGap;
            if (width <= 0 || height <= 0) return null;
            var bits = new bool[width, height];
            bool any = false;
            for (int li = 0; li < lines.Count; li++)
            {
                int lw = Width(font, lines[li]);
                float pen = (width - lw) * 0.5f;
                // Baseline of this line, counted up from the bottom of the bitmap.
                int baseline = height - li * (lineHeight + lineGap) - Mathf.RoundToInt(face.ascentLine);
                foreach (char ch in lines[li])
                {
                    if (!font.characterLookupTable.TryGetValue(ch, out var character)) continue;
                    var glyph = character.glyph;
                    var r = glyph.glyphRect;
                    var m = glyph.metrics;
                    var atlas = Readable(font.atlasTextures[Mathf.Clamp(glyph.atlasIndex, 0, font.atlasTextures.Length - 1)]);
                    int ox = Mathf.RoundToInt(pen + m.horizontalBearingX);
                    int oy = baseline + Mathf.RoundToInt(m.horizontalBearingY - m.height);
                    for (int y = 0; y < r.height; y++)
                    for (int x = 0; x < r.width; x++)
                    {
                        if (atlas.GetPixel(r.x + x, r.y + y).a < 0.5f) continue;
                        int bx = ox + x, by = oy + y;
                        if (bx < 0 || by < 0 || bx >= width || by >= height) continue;
                        bits[bx, by] = true;
                        any = true;
                    }
                    pen += m.horizontalAdvance;
                }
            }
            return any ? bits : null;
        }

        static int Width(TMP_FontAsset font, string line)
        {
            float w = 0f;
            foreach (char ch in line)
                if (font.characterLookupTable.TryGetValue(ch, out var c)) w += c.glyph.metrics.horizontalAdvance;
            return Mathf.CeilToInt(w);
        }

        /// <summary>A CPU-readable copy of a font atlas (the atlas itself isn't readable), made once.</summary>
        static Texture2D Readable(Texture2D atlas)
        {
            if (readable.TryGetValue(atlas, out var copy) && copy != null) return copy;
            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(atlas, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            copy = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            readable[atlas] = copy;
            return copy;
        }
    }
}
