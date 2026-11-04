using UnityEngine;
using UnityEngine.Rendering;

namespace FruitGame
{
    /// <summary>
    /// The two scenery bands that frame the play column (Docs/DESIGN.md §4), built at start from block grids so they
    /// cover the tallest and widest views:
    /// - <b>Canopy</b>: a leafy band of round clumps across the top, in front of the play plane, with a scalloped lower
    ///   edge and a few fruit blocks. Fruit spawn behind it, so in portrait (where the view reaches above the spawn height) they drop out of
    ///   the leaves; in landscape only its lowest leaves show at the top edge.
    /// - <b>Ground bank</b>: grass over soil across the bottom, just below the basket, so missed fruit disappear behind it
    ///   in portrait, where the view reaches below the miss line. In landscape it's a thin strip behind the bushes.
    /// </summary>
    public class SceneryBands : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("Lit vertex-colour material (FruitGame/Voxel Title with no sweep).")]
        [SerializeField] Material material;
        [SerializeField] float halfWidth = 6.5f;
        [Header("Canopy")]
        [SerializeField] float canopyBottom = 2.95f;
        [SerializeField] float canopyTop = 5.4f;
        [SerializeField] Vector2 canopyZ = new Vector2(-0.7f, -0.2f);
        [Header("Ground bank")]
        [SerializeField] float groundTop = -2.95f;
        [SerializeField] float groundBottom = -5.4f;
        [SerializeField] Vector2 groundZ = new Vector2(-0.5f, 1.0f);

        static readonly Color32[] Leaves =
        {
            new Color32(0x69, 0xAC, 0x43, 0xFF), new Color32(0x43, 0xAC, 0x4A, 0xFF), new Color32(0x79, 0xBC, 0x12, 0xFF),
            new Color32(0x47, 0x83, 0x51, 0xFF),
        };
        static readonly Color32[] Fruit =
        {
            new Color32(0xDB, 0x3B, 0x2E, 0xFF), new Color32(0xF2, 0x8C, 0x26, 0xFF), new Color32(0xF2, 0xC2, 0x30, 0xFF),
        };
        static readonly Color32 Grass = new Color32(0x68, 0xAB, 0x42, 0xFF);
        static readonly Color32 GrassDeep = new Color32(0x51, 0x9A, 0x30, 0xFF);
        static readonly Color32 Soil = new Color32(0xA8, 0x6E, 0x44, 0xFF);
        static readonly Color32 SoilDark = new Color32(0x8C, 0x58, 0x34, 0xFF);
        static readonly Color32 Stone = new Color32(0xA8, 0xA3, 0x9A, 0xFF);

        void Awake()
        {
            float b = config.worldBlockSize;
            Add("Canopy", BuildCanopy(b), new Vector3(-halfWidth, canopyBottom - b, canopyZ.x), ShadowCastingMode.Off);
            Add("Ground Bank", BuildGround(b), new Vector3(-halfWidth, groundBottom, groundZ.x), ShadowCastingMode.On);
        }

        void Add(string name, Mesh mesh, Vector3 origin, ShadowCastingMode shadows)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.position = origin;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = shadows;
        }

        static int Hash(int x, int y, int z, int s) => Mathf.Abs((x * 73856093) ^ (y * 19349663) ^ (z * 83492791) ^ s);

        /// <summary>Rows of round leaf clumps (three tiers, the lowest giving a scalloped lower edge), each clump in one
        /// of four greens, lit from the upper left like the scene and shaded underneath, at slightly different depths so
        /// they overlap; a few fruit blocks sit on the lit side of the front clumps. Solid leaves above the top tier.</summary>
        Mesh BuildCanopy(float b)
        {
            var size = new Vector3Int(Mathf.CeilToInt(halfWidth * 2f / b), Mathf.CeilToInt((canopyTop - canopyBottom) / b) + 3,
                Mathf.CeilToInt((canopyZ.y - canopyZ.x) / b));
            var rng = new System.Random(3);
            var clumps = new System.Collections.Generic.List<(float x, float y, float r, int z, Color32 c)>();
            int[] tiers = { 9, 19, 29 };
            for (int t = 0; t < tiers.Length; t++)
                for (float x = (t % 2) * 7f - 4f; x < size.x + 10; x += 12f + rng.Next(5))
                    clumps.Add((x + rng.Next(-2, 3), tiers[t] + rng.Next(-2, 3), 7.5f + rng.Next(4), rng.Next(3), Leaves[rng.Next(Leaves.Length)]));
            int solidFrom = tiers[tiers.Length - 1];
            var light = new Vector2(-0.6f, 0.8f);
            var dark = new Color32(0x20, 0x40, 0x20, 0xFF);
            var bright = new Color32(0xE8, 0xF5, 0xB0, 0xFF);
            Color32 Cell(int x, int y, int z)
            {
                // The first clump covering this cell decides its colour (lower tiers come first: they're in front).
                foreach (var k in clumps)
                {
                    if (z < k.z) continue;
                    var d = new Vector2(x - k.x, y - k.y) / k.r;
                    if (d.sqrMagnitude > 1f) continue;
                    float lit = Vector2.Dot(d, light);
                    if (z == k.z && lit > 0.35f && Hash(x, y, z, 5) % 47 == 0) return Fruit[Hash(x, y, 1, 9) % Fruit.Length];
                    return lit > 0.45f ? Color32.Lerp(k.c, bright, 0.22f) : lit < -0.3f ? Color32.Lerp(k.c, dark, 0.3f) : k.c;
                }
                return y >= solidFrom && z >= 2 ? Color32.Lerp(Leaves[0], dark, 0.25f) : default;
            }
            return BlockMesh.Build(Cell, size, b, Vector3.zero, "Canopy");
        }

        /// <summary>Two blocks of grass with a few tufts on top, then soil with darker speckles and the odd stone.</summary>
        Mesh BuildGround(float b)
        {
            var size = new Vector3Int(Mathf.CeilToInt(halfWidth * 2f / b), Mathf.CeilToInt((groundTop - groundBottom) / b) + 2,
                Mathf.CeilToInt((groundZ.y - groundZ.x) / b));
            int surface = Mathf.CeilToInt((groundTop - groundBottom) / b) - 1;
            Color32 Cell(int x, int y, int z)
            {
                int top = surface + (Hash(x, 0, z, 3) % 9 == 0 ? 1 : 0);
                if (y > top) return default;
                if (y >= top - 1) return Hash(x, y, z, 4) % 4 == 0 ? GrassDeep : Grass;
                int h = Hash(x, y, z, 6);
                if (h % 97 == 0) return Stone;
                return h % 5 == 0 ? SoilDark : Soil;
            }
            return BlockMesh.Build(Cell, size, b, Vector3.zero, "Ground Bank");
        }
    }
}
