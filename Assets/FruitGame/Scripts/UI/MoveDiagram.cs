using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// A small looping pixel-art picture of how to move (Docs/DESIGN.md §15, the setup flow): a head and shoulders with
    /// the basket underneath. **Slide**: the head (and a little of the body) moves side to side and the basket follows.
    /// **Turn**: the body stays still and the head turns (the face slides across it, an ear shows), and the basket
    /// follows. Drawn into a tiny point-filtered texture, in the UI's palette, with a dark outline like the keycaps.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class MoveDiagram : MonoBehaviour
    {
        public SteerBy mode = SteerBy.Slide;
        [SerializeField] float period = 2.4f;
        [SerializeField] float fps = 12f;

        const int W = 48, H = 40;
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Color32 Outline = new Color32(0x1E, 0x22, 0x33, 0xFF);
        static readonly Color32 Skin = new Color32(0xF2, 0xC2, 0x9B, 0xFF);
        static readonly Color32 SkinShade = new Color32(0xD9, 0xA0, 0x7A, 0xFF);
        static readonly Color32 Hair = new Color32(0x3A, 0x2C, 0x3A, 0xFF);
        static readonly Color32 Shirt = new Color32(0xC2, 0x18, 0x5B, 0xFF);
        static readonly Color32 ShirtShade = new Color32(0x8E, 0x12, 0x43, 0xFF);
        static readonly Color32 Basket = new Color32(0xD2, 0x60, 0x3E, 0xFF);
        static readonly Color32 BasketShade = new Color32(0x8E, 0x3B, 0x23, 0xFF);
        static readonly Color32 Arrow = new Color32(0xF2, 0x8C, 0x26, 0xFF);

        Texture2D tex;
        readonly Color32[] px = new Color32[W * H];
        float nextDraw;

        void Awake()
        {
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            GetComponent<RawImage>().texture = tex;
            Draw(0f);
        }

        void OnDestroy()
        {
            if (tex != null) Destroy(tex);
        }

        void Update()
        {
            if (Time.unscaledTime < nextDraw) return;
            nextDraw = Time.unscaledTime + 1f / fps;
            Draw(Time.unscaledTime);
        }

        void Draw(float time)
        {
            float s = Mathf.Sin(time / period * Mathf.PI * 2f);
            for (int i = 0; i < px.Length; i++) px[i] = Clear;

            bool slide = mode == SteerBy.Slide;
            int headX = 24 + (slide ? Mathf.RoundToInt(5f * s) : 0);
            int bodyX = 24 + (slide ? Mathf.RoundToInt(2f * s) : 0);
            int faceShift = slide ? 0 : Mathf.RoundToInt(3f * s);
            int basketX = 24 + Mathf.RoundToInt((slide ? 9f : 9f) * s);

            // Body: rounded shoulders, a darker side for shape.
            for (int y = 22; y <= 31; y++)
                for (int x = bodyX - 11; x <= bodyX + 11; x++)
                {
                    int dy = 22 + 3 - y;
                    int dx = Mathf.Abs(x - bodyX) - 8;
                    if (dy > 0 && dx > 0 && dx * dx + dy * dy > 10) continue; // round the shoulder corners
                    Set(x, y, x > bodyX + 6 ? ShirtShade : Shirt);
                }
            // Neck, leaning toward the head when sliding.
            for (int y = 19; y <= 22; y++)
            {
                int nx = Mathf.RoundToInt(Mathf.Lerp(headX, bodyX, (y - 19) / 3f));
                for (int x = nx - 2; x <= nx + 2; x++) Set(x, y, SkinShade);
            }
            // Head: a circle; hair over the top and, when turned, over the side it turns away from.
            const int hy = 12, r = 7;
            for (int y = hy - r; y <= hy + r; y++)
                for (int x = headX - r; x <= headX + r; x++)
                {
                    int dx = x - headX, dy = y - hy;
                    if (dx * dx + dy * dy > r * r + 2) continue;
                    bool hair = dy <= -3 || (faceShift != 0 && Mathf.Sign(dx) == -Mathf.Sign(faceShift) && Mathf.Abs(dx) >= r - Mathf.Abs(faceShift) && dy < 3);
                    Set(x, y, hair ? Hair : Skin);
                }
            // Ear on the side the face turns away from.
            if (faceShift != 0)
            {
                int ex = headX - (int)Mathf.Sign(faceShift) * (r - 1 - Mathf.Abs(faceShift) / 2);
                Set(ex, hy, SkinShade); Set(ex, hy + 1, SkinShade);
            }
            // Face: eyes and a smile, moved across the head when turning.
            int fx = headX + faceShift;
            Set(fx - 3, hy + 1, Outline); Set(fx + 3, hy + 1, Outline);
            Set(fx - 3, hy + 2, Outline); Set(fx + 3, hy + 2, Outline);
            Set(fx - 1, hy + 4, ShirtShade); Set(fx, hy + 4, ShirtShade); Set(fx + 1, hy + 4, ShirtShade);

            // Basket: a terracotta tub under the player, following the movement.
            for (int y = 33; y <= 38; y++)
            {
                int half = 7 - (y - 33) / 2;
                for (int x = basketX - half; x <= basketX + half; x++) Set(x, y, y == 33 || (x + y) % 3 == 0 ? BasketShade : Basket);
            }

            OutlineFigure();

            // Arrows on the side it's moving toward.
            if (Mathf.Abs(s) > 0.25f)
            {
                int dir = s > 0 ? 1 : -1;
                int ax = dir > 0 ? 44 : 3;
                for (int i = 0; i < 4; i++)
                    for (int y = hy - i; y <= hy + i; y++) Set(ax - dir * i, y, Arrow);
            }

            tex.SetPixels32(px);
            tex.Apply(false);
        }

        void OutlineFigure()
        {
            var copy = (Color32[])px.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (copy[Index(x, y)].a != 0) continue;
                    if (Filled(copy, x - 1, y) || Filled(copy, x + 1, y) || Filled(copy, x, y - 1) || Filled(copy, x, y + 1))
                        px[Index(x, y)] = Outline;
                }
        }

        static bool Filled(Color32[] a, int x, int y) => x >= 0 && x < W && y >= 0 && y < H && a[Index(x, y)].a != 0;

        // y counts down from the top (as drawn), the texture's rows go up.
        static int Index(int x, int y) => (H - 1 - y) * W + x;

        void Set(int x, int y, Color32 c)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            px[Index(x, y)] = c;
        }
    }
}
