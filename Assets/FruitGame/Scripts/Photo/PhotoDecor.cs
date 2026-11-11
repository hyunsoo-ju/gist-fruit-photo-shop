using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The photo card's "orchard picnic" decoration (Docs/DESIGN.md §14): a soft peach-and-cream picnic check behind
    /// everything, plain cream plates with a pixel edge behind the title and the bottom band (so the logos and the score
    /// always sit on a clean background), ten fruit stickers on light and darker cream backings around the frame and the
    /// title, a star on the final shot, the photo's date in yellow pixel digits in its corner, two hearts beside the score
    /// and sparkles beside the title. Everything is drawn on the card's 6 px art grid, point-filtered.
    /// The stickers are the game's own fruit, rendered once per session off-screen in a fixed light (Hidden/FruitGame/
    /// Sticker), so the private fruit models never end up in the repository as images.
    /// </summary>
    public class PhotoDecor
    {
        const int W = 1200, H = 1800;   // the card, in canvas units (= print pixels)
        const int P = 6;                // one art pixel
        const int Check = 36;           // picnic check square
        const int BandEdge = 36;        // the bottom plate's outer edge is this far from both sides (the 3 mm trim)
        const int StickerLayer = 31;    // drawn on its own layer by its own camera, then destroyed at once

        static readonly Color32 Cream = new Color32(255, 248, 236, 255);
        static readonly Color32 CheckMid = new Color32(253, 232, 218, 255);
        static readonly Color32 CheckDark = new Color32(250, 214, 196, 255);
        static readonly Color32 PlateEdge = new Color32(236, 214, 182, 255);
        static readonly Color32 BackLight = Cream;
        static readonly Color32 BackDark = new Color32(238, 218, 184, 255);
        static readonly Color32 StickerShadow = new Color32(120, 80, 40, 70);
        static readonly Color DateColour = new Color32(255, 214, 40, 255);

        // Card positions below are in print pixels from the top-left (y down), like the mockup they came from.
        struct Sticker
        {
            public string fruit; public int height; public float angle; public int x, y; public bool dark;
            public Sticker(string fruit, int height, float angle, int x, int y, bool dark)
            { this.fruit = fruit; this.height = height; this.angle = angle; this.x = x; this.y = y; this.dark = dark; }
        }

        /// <summary>Ten stickers: the frame's corners, either side of the title, two down each side. None touches a
        /// photo, the title or the logos. height = the fruit's height in art pixels; angle in degrees, counter-clockwise.</summary>
        static readonly Sticker[] Stickers =
        {
            new Sticker("orange", 15, 12, 86, 210, false),
            new Sticker("pineapple", 18, -10, 1106, 198, true),
            new Sticker("lemon", 12, 12, 90, 1538, true),
            new Sticker("pear", 13, -12, 1110, 1538, false),
            new Sticker("melon", 12, 8, 320, 140, true),
            new Sticker("mango", 11, 14, 890, 130, false),
            new Sticker("mango", 10, -20, 60, 600, false),
            new Sticker("grapefruit", 10, 10, 58, 1150, true),
            new Sticker("banana", 7, -70, 1146, 620, true),
            new Sticker("orange", 10, -8, 1142, 1150, false),
        };

        static readonly string[] Star =
        {
            "....O....", "...OYO...", "OOOOYOOOO", "OYYYYYYYO", ".OYYWYYO.", "..OYYYO..", ".OYYOYYO.", ".OYO.OYO.", ".OO...OO.",
        };
        static readonly string[] Spark = { "..W..", "..W..", "WWYWW", "..W..", "..W.." };
        static readonly string[] SparkSmall = { ".W.", "WYW", ".W." };
        static readonly string[] Heart = { ".PP.PP.", "PWPPPPP", "PPPPPPP", ".PPPPP.", "..PPP..", "...P..." };
        static readonly Dictionary<char, Color32> Palette = new Dictionary<char, Color32>
        {
            { 'O', new Color32(184, 92, 16, 255) }, { 'Y', new Color32(255, 205, 60, 255) }, { 'W', new Color32(255, 246, 200, 255) },
            { 'P', new Color32(236, 92, 150, 255) }, { 'w', new Color32(255, 255, 255, 255) }, { 'y', new Color32(255, 226, 120, 255) },
            { 'H', new Color32(255, 220, 236, 255) },
        };

        // Rendered stickers (art pixels, y down), kept for the session: the fruit don't change.
        static readonly Dictionary<int, (Color32[] px, int w, int h)> stickerCache = new Dictionary<int, (Color32[], int, int)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => stickerCache.Clear();

        readonly RectTransform root;
        readonly Shader stickerShader;
        RawImage under, over, heartLeft, heartRight;
        TMP_Text date;
        Texture2D underTex, overTex, heartTex;

        public PhotoDecor(RectTransform root, Shader stickerShader)
        {
            this.root = root;
            this.stickerShader = stickerShader != null ? stickerShader : Shader.Find("Hidden/FruitGame/Sticker");
        }

        /// <summary>
        /// Lays the decoration over the card (call after the layout is final): the check and plates behind everything
        /// but the card's own background, the stickers, star and sparkles above everything, the date on the final shot,
        /// the hearts beside the score.
        /// </summary>
        public void Apply(RectTransform title, RectTransform finalCut, RectTransform score, RectTransform points, RectTransform leftLogo, RectTransform rightLogo, TMP_FontAsset font)
        {
            if (under == null) Create(font);
            var titleBox = InkBox(title);
            if (underTex == null) underTex = Upload(DrawUnder(titleBox));
            under.texture = underTex;
            if (overTex == null) overTex = Upload(DrawOver(titleBox, CardBox(finalCut)));
            over.texture = overTex;

            // The date, bottom-right on the final shot.
            var cut = CardBox(finalCut);
            date.text = System.DateTime.Now.ToString("yyyy.MM.dd");
            var dr = date.rectTransform;
            dr.anchoredPosition = new Vector2(cut.xMax - 18, H - (cut.yMax - 14));

            // Hearts either side of the score and its "점", when there's room before the logos; a long score leaves
            // none beside the "점", so then both go up level with the top of the number, the right one above the "점".
            var s = CardBox(score);
            var p = CardBox(points);
            const float gap = 34f, half = 18f, clear = 8f;
            float leftRoom = leftLogo != null ? CardBox(leftLogo).xMax : 0f;
            float rightRoom = rightLogo != null ? CardBox(rightLogo).xMin : W;
            bool beside = p.xMax + gap + half + clear <= rightRoom && s.xMin - gap - half - clear >= leftRoom;
            float y = beside ? H - s.center.y : H - (p.yMin - 2f);
            heartLeft.rectTransform.anchoredPosition = new Vector2(s.xMin - gap, y);
            heartRight.rectTransform.anchoredPosition = new Vector2(beside ? p.xMax + gap : p.center.x, y);
        }

        public void Destroy()
        {
            if (underTex != null) Object.Destroy(underTex);
            if (overTex != null) Object.Destroy(overTex);
            if (heartTex != null) Object.Destroy(heartTex);
        }

        // ---- Objects ---------------------------------------------------------------------------------------------------

        void Create(TMP_FontAsset font)
        {
            under = FullCard("Decor Under");
            under.transform.SetSiblingIndex(1); // just above the card's plain background
            over = FullCard("Decor Over");
            heartTex = Upload(Sprite(Heart, 1), Heart[0].Length, Heart.Length);
            heartLeft = Small("Heart (left)", heartTex, Heart[0].Length * 5, Heart.Length * 5);
            heartRight = Small("Heart (right)", heartTex, Heart[0].Length * 5, Heart.Length * 5);

            var go = new GameObject("Date", typeof(RectTransform));
            go.transform.SetParent(root, false);
            date = go.AddComponent<TextMeshProUGUI>();
            date.font = font;
            date.fontSize = 48;
            date.color = DateColour;
            date.alignment = TextAlignmentOptions.BottomRight;
            date.textWrappingMode = TextWrappingModes.NoWrap;
            date.raycastTarget = false;
            var r = date.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            r.pivot = new Vector2(1f, 0f);
            r.sizeDelta = new Vector2(400, 60);
        }

        RawImage FullCard(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            var img = go.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        RawImage Small(string name, Texture tex, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(w, h);
            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>A rect's box on the card in print pixels, y down.</summary>
        Rect CardBox(RectTransform t)
        {
            var c = new Vector3[4];
            t.GetWorldCorners(c);
            Vector2 a = (Vector2)root.InverseTransformPoint(c[0]) - root.rect.min;
            Vector2 b = (Vector2)root.InverseTransformPoint(c[2]) - root.rect.min;
            return Rect.MinMaxRect(a.x, H - b.y, b.x, H - a.y);
        }

        /// <summary>The title's drawn part: its image has a little clear margin inside its rect.</summary>
        Rect InkBox(RectTransform title)
        {
            var b = CardBox(title);
            return Rect.MinMaxRect(b.xMin + 5, b.yMin + 8, b.xMax - 5, b.yMax - 5);
        }

        // ---- Drawing ---------------------------------------------------------------------------------------------------

        /// <summary>The picnic check and the two plates.</summary>
        static Color32[] DrawUnder(Rect title)
        {
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                bool rowStripe = (y / Check) % 2 == 0;
                for (int x = 0; x < W; x++)
                {
                    bool colStripe = (x / Check) % 2 == 0;
                    px[y * W + x] = rowStripe && colStripe ? CheckDark : rowStripe || colStripe ? CheckMid : Cream;
                }
            }
            // Title plate: the title plus 18 px, out to the art grid.
            int x0 = Floor((int)title.xMin - 18), y0 = Floor((int)title.yMin - 18);
            int x1 = Ceil(Mathf.CeilToInt(title.xMax) + 18), y1 = Ceil(Mathf.CeilToInt(title.yMax) + 18);
            Plate(px, x0, y0, x1, y1);
            // Bottom plate: the same distance from both sides, from just above the logos to just below them.
            Plate(px, BandEdge + P, 1548, W - 1 - BandEdge - P, 1722);
            return px;
        }

        /// <summary>Stickers, the star on the final shot and the sparkles by the title.</summary>
        Color32[] DrawOver(Rect title, Rect finalCut)
        {
            var px = new Color32[W * H];
            var types = FruitTypes();
            for (int i = 0; i < Stickers.Length; i++)
            {
                var s = Stickers[i];
                if (!stickerCache.TryGetValue(i, out var art))
                {
                    if (!types.TryGetValue(s.fruit, out var type)) continue;
                    var fruit = RenderFruit(type);
                    if (fruit.px == null) continue;
                    art = MakeSticker(fruit.px, fruit.w, fruit.h, s.height, s.angle, s.dark ? BackDark : BackLight);
                    stickerCache[i] = art;
                }
                Put(px, art.px, art.w, art.h, P, s.x, s.y);
            }
            Put(px, Sprite(Star, 1), Star[0].Length, Star.Length, 8, (int)finalCut.xMax - 8, (int)finalCut.yMin + 8);
            var spark = Sprite(Spark, 1, true);
            var small = Sprite(SparkSmall, 1, true);
            Put(px, spark, 5, 5, P, (int)title.xMin - 50, (int)title.yMin + 40);
            Put(px, small, 3, 3, P, (int)title.xMin - 20, (int)title.yMax - 20);
            Put(px, spark, 5, 5, P, (int)title.xMax + 50, (int)title.yMin + 60);
            Put(px, small, 3, 3, P, (int)title.xMax + 24, (int)title.yMax - 10);
            return px;
        }

        static int Floor(int v) => v - ((v % P) + P) % P;
        static int Ceil(int v) => v + ((-v % P) + P) % P;

        /// <summary>A cream plate with a pixel edge and stepped corners (x0..x1, y0..y1 inclusive, the fill's extent).</summary>
        static void Plate(Color32[] px, int x0, int y0, int x1, int y1)
        {
            Stepped(px, x0 - P, y0 - P, x1 + P, y1 + P, PlateEdge);
            Stepped(px, x0, y0, x1, y1, Cream);
        }

        static void Stepped(Color32[] px, int a, int b, int c, int e, Color32 col)
        {
            Fill(px, a + 2 * P, b, c - 2 * P, e, col);
            Fill(px, a, b + 2 * P, c, e - 2 * P, col);
            Fill(px, a + P, b + P, c - P, e - P, col);
        }

        static void Fill(Color32[] px, int x0, int y0, int x1, int y1, Color32 col)
        {
            for (int y = Mathf.Max(0, y0); y <= Mathf.Min(H - 1, y1); y++)
                for (int x = Mathf.Max(0, x0); x <= Mathf.Min(W - 1, x1); x++)
                    px[y * W + x] = col;
        }

        /// <summary>Draws an image (y down) scaled by k, centred on (cx, cy), alpha over what's there.</summary>
        static void Put(Color32[] dst, Color32[] src, int w, int h, int k, int cx, int cy)
        {
            int ox = cx - w * k / 2, oy = cy - h * k / 2;
            for (int sy = 0; sy < h; sy++)
                for (int sx = 0; sx < w; sx++)
                {
                    var c = src[sy * w + sx];
                    if (c.a == 0) continue;
                    for (int dy = 0; dy < k; dy++)
                    {
                        int y = oy + sy * k + dy;
                        if (y < 0 || y >= H) continue;
                        for (int dx = 0; dx < k; dx++)
                        {
                            int x = ox + sx * k + dx;
                            if (x < 0 || x >= W) continue;
                            dst[y * W + x] = Over(c, dst[y * W + x]);
                        }
                    }
                }
        }

        static Color32 Over(Color32 a, Color32 b)
        {
            if (a.a == 255 || b.a == 0) return a;
            float fa = a.a / 255f, fb = b.a / 255f, o = fa + fb * (1f - fa);
            byte Mix(byte ca, byte cb) => (byte)Mathf.RoundToInt((ca * fa + cb * fb * (1f - fa)) / o);
            return new Color32(Mix(a.r, b.r), Mix(a.g, b.g), Mix(a.b, b.b), (byte)Mathf.RoundToInt(o * 255f));
        }

        static Color32[] Sprite(string[] rows, int k, bool sparkle = false)
        {
            int h = rows.Length, w = rows[0].Length;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    char ch = rows[y][x];
                    if (sparkle) ch = ch == 'W' ? 'w' : ch == 'Y' ? 'y' : ch;
                    if (rows == Heart && ch == 'W') ch = 'H';
                    if (Palette.TryGetValue(ch, out var c)) px[y * w + x] = c;
                }
            return px;
        }

        /// <summary>Uploads a card-sized image (y down) as a point-filtered texture.</summary>
        static Texture2D Upload(Color32[] px) => Upload(px, W, H);

        static Texture2D Upload(Color32[] px, int w, int h)
        {
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++) System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Photo Decor" };
            tex.SetPixels32(flipped);
            tex.Apply(false, true);
            return tex;
        }

        // ---- Stickers --------------------------------------------------------------------------------------------------

        static Dictionary<string, FruitType> FruitTypes()
        {
            var map = new Dictionary<string, FruitType>();
            var spawner = Object.FindFirstObjectByType<FruitSpawner>(FindObjectsInactive.Include);
            if (spawner != null)
                foreach (var t in spawner.FruitTypes)
                    if (t != null && t.visualPrefabs != null && t.visualPrefabs.Length > 0 && t.visualPrefabs[0] != null) map[t.id] = t;
            return map;
        }

        /// <summary>Renders a fruit's first model, slightly turned, on a clear background (256 px, y down).</summary>
        (Color32[] px, int w, int h) RenderFruit(FruitType type)
        {
            if (stickerShader == null) return (null, 0, 0);
            const int size = 256;
            var prefab = type.visualPrefabs[0];
            var holder = new GameObject("Sticker Fruit") { hideFlags = HideFlags.HideAndDontSave };
            var mats = new List<Material>();
            // Copy only the meshes (the prefab's own scripts must not run).
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null) continue;
                var part = new GameObject(mf.name) { hideFlags = HideFlags.HideAndDontSave, layer = StickerLayer };
                part.transform.SetParent(holder.transform, false);
                var local = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                part.transform.localPosition = local.GetColumn(3);
                part.transform.localRotation = local.rotation;
                part.transform.localScale = local.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = part.AddComponent<MeshRenderer>();
                var src = mr.sharedMaterial;
                var m = new Material(stickerShader) { hideFlags = HideFlags.HideAndDontSave };
                Texture tex = type.atlas != null ? type.atlas : src != null && src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
                if (tex != null) m.SetTexture("_BaseMap", tex);
                if (src != null && src.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap"));
                if (src != null && src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                mats.Add(m);
            }
            holder.transform.SetPositionAndRotation(new Vector3(0f, -1000f, 0f), Quaternion.Euler(-15f, 25f, 0f));
            var bounds = new Bounds(holder.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in holder.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }

            var camGo = new GameObject("Sticker Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.1f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << StickerLayer;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            camGo.transform.SetPositionAndRotation(bounds.center - Vector3.forward * 10f, Quaternion.identity);
            var rt = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            Color32[] px = null;
            if (any)
            {
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var read = new Texture2D(size, size, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                read.Apply(false);
                RenderTexture.active = prev;
                var up = read.GetPixels32();
                Object.Destroy(read);
                px = new Color32[up.Length];
                for (int y = 0; y < size; y++) System.Array.Copy(up, (size - 1 - y) * size, px, y * size, size);
            }
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(holder);
            foreach (var m in mats) Object.DestroyImmediate(m);
            return (px, size, size);
        }

        /// <summary>
        /// Turns a render into a pixel sticker: cropped, shrunk to `height` art pixels, turned, hard-edged, on a 2 px
        /// backing in `back` with a faint shadow one pixel down and right.
        /// </summary>
        static (Color32[] px, int w, int h) MakeSticker(Color32[] src, int sw, int sh, int height, float angle, Color32 back)
        {
            // Crop to what was drawn.
            int minX = sw, minY = sh, maxX = -1, maxY = -1;
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                    if (src[y * sw + x].a > 0) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            if (maxX < 0) return (new Color32[1], 1, 1);
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            int h = height, w = Mathf.Max(1, Mathf.RoundToInt(cw * (float)height / ch));

            // Shrink by averaging (premultiplied, so the clear edge doesn't darken the colours).
            var small = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int x0 = minX + x * cw / w, x1 = Mathf.Max(x0 + 1, minX + (x + 1) * cw / w);
                    int y0 = minY + y * ch / h, y1 = Mathf.Max(y0 + 1, minY + (y + 1) * ch / h);
                    float r = 0, g = 0, b = 0, a = 0; int n = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++)
                        {
                            var c = src[yy * sw + xx]; float ca = c.a / 255f;
                            r += c.r / 255f * ca; g += c.g / 255f * ca; b += c.b / 255f * ca; a += ca; n++;
                        }
                    small[y * w + x] = a > 0 ? new Color(r / a, g / a, b / a, a / n) : Color.clear;
                }

            // Turn (nearest pixel, grown to fit).
            float rad = angle * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            int rw = Mathf.CeilToInt(Mathf.Abs(w * cos) + Mathf.Abs(h * sin));
            int rh = Mathf.CeilToInt(Mathf.Abs(w * sin) + Mathf.Abs(h * cos));
            var turned = new Color[rw * rh];
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    float dx = x + 0.5f - rw * 0.5f, dy = y + 0.5f - rh * 0.5f;
                    // counter-clockwise on screen (y down): source = rotate back
                    float sx = dx * cos - dy * sin + w * 0.5f, sy = dx * sin + dy * cos + h * 0.5f;
                    int ix = Mathf.FloorToInt(sx), iy = Mathf.FloorToInt(sy);
                    turned[y * rw + x] = ix >= 0 && iy >= 0 && ix < w && iy < h ? small[iy * w + ix] : Color.clear;
                }

            // Hard edge, then the backing (2 px around) and its shadow.
            const int pad = 4, ring = 2;
            int ow = rw + 2 * pad, oh = rh + 2 * pad;
            var fruit = new bool[ow * oh];
            var col = new Color32[ow * oh];
            for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    var c = turned[y * rw + x];
                    if (c.a * 255f <= 110f) continue;
                    int i = (y + pad) * ow + x + pad;
                    fruit[i] = true;
                    col[i] = new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), 255);
                }
            var backing = new bool[ow * oh];
            for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    bool near = false;
                    for (int yy = Mathf.Max(0, y - ring); yy <= Mathf.Min(oh - 1, y + ring) && !near; yy++)
                        for (int xx = Mathf.Max(0, x - ring); xx <= Mathf.Min(ow - 1, x + ring); xx++)
                            if (fruit[yy * ow + xx]) { near = true; break; }
                    backing[y * ow + x] = near;
                }
            var outPx = new Color32[ow * oh];
            for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    int i = y * ow + x;
                    if (fruit[i]) outPx[i] = col[i];
                    else if (backing[i]) outPx[i] = back;
                    else if (x > 0 && y > 0 && backing[(y - 1) * ow + x - 1]) outPx[i] = StickerShadow;
                }
            return (outPx, ow, oh);
        }
    }
}
