using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitGame
{
    /// <summary>
    /// Two grassy hills in the far layer (Docs/DESIGN.md §4, L4b), one each side, that the far orchard's side trees
    /// stand on. Each rises from the ground beside the open middle (where the pile and the room behind the player stay
    /// visible) to a broad top running off the screen edge. Built at start from block grids at the far block size: a
    /// light grass top, then grass that darkens gradually with depth below the surface (with faint bands, barely darker
    /// than the grass around them), flowers and the odd stone, each dithered toward the sky like the rest of the far
    /// layer. OrchardBuilder plants the side trees on SurfaceY.
    /// </summary>
    public class FarHills : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] Shader ditherShader;
        [SerializeField] Vector2 depth = new Vector2(4.9f, 6.7f);
        [SerializeField] float outerX = 9f;

        public const float Ground = -3.6f;
        const float Foot = 3.6f, Shoulder = 5.0f, Crest = 6.5f;
        static readonly Color Sky = new Color(0.663f, 0.776f, 0.867f);

        /// <summary>Hill top height at |x| (world metres): flat ground in the middle, a smooth rise from the foot to the
        /// shoulder, then a gentle swell to the crest and a slight fall toward the screen edge.</summary>
        public static float SurfaceY(float absX)
        {
            if (absX <= Foot) return Ground;
            if (absX <= Shoulder) return Mathf.Lerp(Ground, -0.9f, Mathf.SmoothStep(0f, 1f, (absX - Foot) / (Shoulder - Foot)));
            if (absX <= Crest) return Mathf.Lerp(-0.9f, -0.25f, Mathf.SmoothStep(0f, 1f, (absX - Shoulder) / (Crest - Shoulder)));
            return Mathf.Lerp(-0.25f, -0.6f, (absX - Crest) / 3f);
        }

        static readonly Color Top = new Color32(0x62, 0x96, 0x3C, 0xFF);
        static readonly Color GrassLight = new Color32(0x52, 0x88, 0x38, 0xFF);
        static readonly Color GrassDeep = new Color32(0x34, 0x62, 0x2A, 0xFF);
        static readonly Color Flower = new Color32(0xF2, 0x8C, 0xB2, 0xFF);
        static readonly Color Stone = new Color32(0xB4, 0xAE, 0xA4, 0xFF);
        const int Levels = 6;              // grass shades from just under the top to the deepest
        const float LevelDepth = 0.45f;    // metres below the surface per shade
        const float BandShade = 0.95f;     // bands are only slightly darker than their level

        /// <summary>Colour slots: top, flower, stone, then a grass and a band colour per level.</summary>
        static Color[] BuildPalette()
        {
            var colors = new List<Color> { Top, Flower, Stone };
            for (int i = 0; i < Levels; i++)
            {
                var grass = Color.Lerp(GrassLight, GrassDeep, i / (Levels - 1f));
                colors.Add(grass);
                colors.Add(new Color(grass.r * BandShade, grass.g * BandShade, grass.b * BandShade, 1f));
            }
            return colors.ToArray();
        }

        void Awake()
        {
            float b = config.farBlockSize;
            var size = new Vector3Int(Mathf.CeilToInt((outerX - Foot + 0.5f) / b), Mathf.CeilToInt((0.2f - Ground) / b), Mathf.CeilToInt((depth.y - depth.x) / b));
            var palette = BuildPalette();
            var materials = new Material[palette.Length];
            for (int i = 0; i < palette.Length; i++)
            {
                var color = palette[i];
                var m = new Material(ditherShader) { name = "Far Hill " + i };
                m.SetColor("_BaseColor", color);
                m.SetColor("_FadeColor", Sky);
                m.SetFloat("_Fade", config.farDither);
                m.SetFloat("_Levels", 4f);
                m.SetFloat("_DitherPixel", 1f);
                materials[i] = m;
            }

            foreach (float side in new[] { -1f, 1f })
            {
                float x0 = Foot - 0.5f;
                // Palette slot of a block, or −1 for air.
                int Cell(int x, int y, int z)
                {
                    // Distance from the middle: the left hill's grid runs inward, the right one's outward.
                    float ax = x0 + ((side > 0 ? x : size.x - 1 - x) + 0.5f) * b;
                    // The front edge wanders a little, so the hill isn't a slab.
                    float frontZ = Mathf.PerlinNoise(ax * 0.7f, side * 3.3f) * 0.35f;
                    if ((z + 0.5f) * b < frontZ) return -1;
                    float top = SurfaceY(ax) + (Mathf.PerlinNoise(ax * 1.3f, 9f + side) - 0.5f) * 0.12f;
                    float wy = Ground + (y + 0.5f) * b;
                    if (wy > top) return -1;
                    int h = Mathf.Abs((x * 73856093) ^ (y * 19349663) ^ (z * 83492791) ^ (side > 0 ? 17 : 41));
                    if (wy > top - b) return h % 23 == 0 ? 1 : 0;
                    if (h % 29 == 0) return 1;
                    if (h % 71 == 0) return 2;
                    // Darker with depth below the surface, with a faint band at the top of each shade.
                    float below = top - wy;
                    int level = Mathf.Min(Levels - 1, Mathf.FloorToInt(below / LevelDepth));
                    bool band = below - level * LevelDepth < 0.1f && level > 0;
                    return 3 + level * 2 + (band ? 1 : 0);
                }

                var root = new GameObject(side < 0 ? "Hill Left" : "Hill Right").transform;
                root.SetParent(transform, false);
                root.position = new Vector3(side > 0 ? x0 : -(x0 + size.x * b), Ground, depth.x);
                for (int slot = 0; slot < palette.Length; slot++)
                {
                    var color = (Color32)palette[slot];
                    var mesh = BlockMesh.Build((x, y, z) => Cell(x, y, z) == slot ? color : default, size, b, Vector3.zero, $"Hill {slot}");
                    if (mesh.vertexCount == 0) continue;
                    var go = new GameObject($"Hill {slot}", typeof(MeshFilter), typeof(MeshRenderer));
                    go.layer = gameObject.layer;
                    go.transform.SetParent(root, false);
                    go.GetComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.GetComponent<MeshRenderer>();
                    r.sharedMaterial = materials[slot];
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }
        }
    }
}
