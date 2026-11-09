using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitGame
{
    /// <summary>
    /// Makes the orchard look natural and alive (Docs/DESIGN.md §4, M6 1.1 and 1.2), at start, over the baked scenery:
    /// darker greens, a band of extra shrubs in the far layer, wind in the leaves and grass with a quicker motion for the
    /// flowers (FruitGame/Dither Fade's _Wind), and a few butterflies and drifting leaves. Everything alive moves in the
    /// far layer, behind the player and the fruit, so it can't be mistaken for something to catch.
    /// </summary>
    public class LivingScenery : MonoBehaviour
    {
        [Tooltip("Lit vertex-colour material (FruitGame/Voxel Title with no sweep), for the butterflies and leaves.")]
        [SerializeField] Material blockMaterial;
        [SerializeField] Shader swayShader;
        [SerializeField] Color leafTint = new Color(0.62f, 0.72f, 0.52f);
        [SerializeField] Color grassTint = new Color(0.62f, 0.74f, 0.58f);
        [SerializeField] float leafWind = 0.016f;
        [SerializeField] float grassWind = 0.07f;
        [SerializeField] float flowerWind = 0.09f;
        [SerializeField] int extraShrubs = 28;
        [SerializeField] int butterflies = 5;
        [SerializeField] int driftingLeaves = 12;

        const float FarFade = 0.4f, FarLeafFade = 0.65f;

        class Butterfly { public Transform root, left, right; public Vector3 home; public float phase, speed; }
        class Leaf { public Transform transform; public Vector3 spin; public float speed, phase, x; }

        readonly Dictionary<(Material, bool), Material> swapped = new Dictionary<(Material, bool), Material>();
        readonly List<Butterfly> flying = new List<Butterfly>();
        readonly List<Leaf> leaves = new List<Leaf>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Material blocks;
        int farLayer;

        static readonly Color32[][] Wings =
        {
            new[] { new Color32(0xF2, 0x8C, 0xB2, 0xFF), new Color32(0xC2, 0x18, 0x5B, 0xFF) },
            new[] { new Color32(0xF2, 0xC2, 0x30, 0xFF), new Color32(0xE0, 0x82, 0x1C, 0xFF) },
            new[] { new Color32(0xF5, 0xF0, 0xE4, 0xFF), new Color32(0x8F, 0xA8, 0xD8, 0xFF) },
        };
        static readonly Color32 Body = new Color32(0x2A, 0x22, 0x20, 0xFF);
        static readonly Color32[] LeafColours = { new Color32(0x3F, 0x76, 0x2E, 0xFF), new Color32(0x6E, 0x8A, 0x2C, 0xFF), new Color32(0x2E, 0x6E, 0x38, 0xFF) };

        void Awake()
        {
            var foreground = GameObject.Find("L1_Foreground");
            var far = GameObject.Find("L4b_FarTrees");
            if (foreground == null || far == null || swayShader == null) return;
            farLayer = FirstRendererLayer(far.transform);
            blocks = new Material(blockMaterial);
            if (blocks.HasProperty("_SweepStrength")) blocks.SetFloat("_SweepStrength", 0f);

            AddShrubs(foreground.transform.Find("Shrubs"), far.transform);
            Recolour(foreground.transform, false);
            Recolour(far.transform, true);
            var rng = new System.Random(11);
            for (int i = 0; i < butterflies; i++) AddButterfly(i, rng);
            var leafMeshes = new Mesh[LeafColours.Length];
            for (int i = 0; i < leafMeshes.Length; i++) leafMeshes[i] = Solid(LeafColours[i], 3, 2);
            for (int i = 0; i < driftingLeaves; i++) AddLeaf(leafMeshes[i % leafMeshes.Length], rng);
        }

        static int FirstRendererLayer(Transform root)
        {
            var r = root.GetComponentInChildren<MeshRenderer>(true);
            return r != null ? r.gameObject.layer : root.gameObject.layer;
        }

        /// <summary>Copies of the bushes along both sides of the far layer, keeping the middle open for the pile.</summary>
        void AddShrubs(Transform shrubs, Transform far)
        {
            if (shrubs == null) return;
            var bushes = new List<Transform>();
            foreach (Transform t in shrubs)
                if (t.name.Contains("bush") || t.name.Contains("Bush")) bushes.Add(t);
            if (bushes.Count == 0) return;
            var holder = new GameObject("Extra Shrubs").transform;
            holder.SetParent(far, false);
            holder.position = Vector3.zero;
            var rng = new System.Random(7);
            for (int i = 0; i < extraShrubs; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float ax = 3.1f + (i / 2) * (5.6f / Mathf.Max(1f, extraShrubs / 2f)) + (float)rng.NextDouble() * 0.25f;
                var copy = Instantiate(bushes[i % bushes.Count].gameObject, holder);
                copy.name = "Shrub " + (i + 1);
                float scale = 1.5f + (float)rng.NextDouble() * 0.8f;
                copy.transform.localScale *= scale;
                copy.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                copy.transform.position = new Vector3(side * ax, FarHills.SurfaceY(ax) - 0.08f, 4.3f + (float)rng.NextDouble() * 0.9f);
                foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = farLayer;
            }
        }

        void Recolour(Transform root, bool far)
        {
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var swap = Swap(mats[i], far);
                    if (swap != mats[i]) { mats[i] = swap; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        Material Swap(Material m, bool far)
        {
            if (m == null) return null;
            bool isLeaf = m.name.Contains("Leaves");
            bool isGrass = m.name.Contains("grass");
            bool isFlower = m.name.StartsWith("Petal");
            bool dithered = m.shader == swayShader;
            // Foreground wood, soil and the like keep their own look; in the far layer the copied shrubs have to dither too.
            if (!isLeaf && !isGrass && !isFlower && (dithered || !far)) return m;
            if (swapped.TryGetValue((m, far), out var done)) return done;

            var copy = dithered ? new Material(m) : new Material(swayShader);
            copy.name = m.name + " (living)";
            if (!dithered)
            {
                copy.SetTexture("_BaseMap", m.GetTexture("_BaseMap"));
                copy.SetColor("_BaseColor", m.GetColor("_BaseColor"));
                copy.SetFloat("_Fade", far ? FarFade : 0f);
                copy.SetFloat("_Levels", far ? 4f : 1f);
                copy.SetColor("_FadeColor", new Color(0.663f, 0.776f, 0.867f));
            }
            var c = copy.GetColor("_BaseColor");
            if (isLeaf) copy.SetColor("_BaseColor", Natural(c, leafTint));
            else if (isGrass) copy.SetColor("_BaseColor", Natural(c, grassTint));
            // Less washed out toward the sky, so the far orchard reads as green.
            if (isLeaf && dithered) copy.SetFloat("_Fade", m.GetFloat("_Fade") * FarLeafFade);
            copy.SetFloat("_Wind", (isFlower ? flowerWind : isGrass ? grassWind : isLeaf ? leafWind : 0f) * (far ? 0.6f : 1f));
            copy.SetFloat("_WindSpeed", isFlower ? 3.4f : isGrass ? 2.2f : 1.5f);
            swapped[(m, far)] = copy;
            return copy;
        }

        /// <summary>Takes the neon out of a green (part way to grey), then darkens it.</summary>
        static Color Natural(Color c, Color tint)
        {
            float g = c.grayscale;
            return Color.Lerp(c, new Color(g, g, g, c.a), 0.3f) * tint;
        }

        Mesh Keep(Mesh m) { meshes.Add(m); return m; }

        Mesh Solid(Color32 colour, int w, int h)
        {
            Color32 Cell(int x, int y, int z) => colour;
            return Keep(BlockMesh.Build(Cell, new Vector3Int(w, h, 1), 0.03f, new Vector3(-w * 0.015f, -h * 0.015f, 0f), "Drifting Leaf"));
        }

        /// <summary>A wing: rounded blocks in the main colour with a border in the second, its root along x = 0.</summary>
        Mesh Wing(Color32[] colours)
        {
            const int W = 5, H = 7;
            Color32 Cell(int x, int y, int z)
            {
                float dx = (x + 0.5f) / W, dy = (y + 0.5f) / H - 0.5f;
                float reach = 1f - 2.2f * Mathf.Abs(dy) * (dy > 0f ? 0.6f : 1f);
                if (dx > reach) return default;
                return dx > reach - 0.3f ? colours[1] : colours[0];
            }
            return Keep(BlockMesh.Build(Cell, new Vector3Int(W, H, 1), 0.035f, new Vector3(0f, -H * 0.0175f, 0f), "Butterfly Wing"));
        }

        Transform Part(string partName, Transform parent, Mesh mesh)
        {
            var go = new GameObject(partName, typeof(MeshFilter), typeof(MeshRenderer));
            go.layer = farLayer;
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = blocks;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        void AddButterfly(int index, System.Random rng)
        {
            var root = new GameObject("Butterfly " + (index + 1)).transform;
            root.gameObject.layer = farLayer;
            root.SetParent(transform, false);
            var wing = Wing(Wings[index % Wings.Length]);
            var body = Solid(Body, 1, 7);
            Part("Body", root, body);
            var b = new Butterfly
            {
                root = root,
                left = Part("Left", root, wing),
                right = Part("Right", root, wing),
                home = new Vector3(((float)rng.NextDouble() * 2f - 1f) * 4.6f, -1.6f + (float)rng.NextDouble() * 3.2f, 4.1f + (float)rng.NextDouble() * 0.8f),
                phase = (float)rng.NextDouble() * 100f,
                speed = 0.32f + (float)rng.NextDouble() * 0.25f,
            };
            flying.Add(b);
        }

        void AddLeaf(Mesh mesh, System.Random rng)
        {
            var t = Part("Drifting Leaf", transform, mesh);
            leaves.Add(new Leaf
            {
                transform = t,
                x = ((float)rng.NextDouble() * 2f - 1f) * 6f,
                speed = 0.25f + (float)rng.NextDouble() * 0.25f,
                phase = (float)rng.NextDouble() * 100f,
                spin = new Vector3((float)rng.NextDouble() * 180f, (float)rng.NextDouble() * 180f, (float)rng.NextDouble() * 120f),
            });
            t.position = new Vector3(leaves[leaves.Count - 1].x, Top - (float)rng.NextDouble() * (Top - Bottom), 4.4f);
        }

        const float Top = 4.5f, Bottom = -3.8f;
        const float FlatPitch = 68f;

        void Update()
        {
            float t = Time.time;
            foreach (var b in flying)
            {
                float u = t * b.speed + b.phase;
                b.root.position = b.home + new Vector3(Mathf.Sin(u) * 1.8f + Mathf.Sin(u * 2.3f) * 0.5f, Mathf.Sin(u * 1.7f) * 0.7f + Mathf.Sin(u * 7f) * 0.09f, 0f);
                // The wings beat up and down from level, in bursts, gliding a little in between.
                float beat = Mathf.Sin(t * 16f + b.phase) * (0.65f + 0.35f * Mathf.Sin(u * 3f));
                float open = 12f + 34f * beat;
                b.right.localRotation = Quaternion.Euler(0f, open, 0f);
                b.left.localRotation = Quaternion.Euler(0f, 180f - open, 0f);
                // Lying flat, tipped a little toward the camera so the wings still show, and heading the way it's flying.
                float heading = Mathf.Cos(u) + Mathf.Cos(u * 2.3f) * 0.64f;
                b.root.rotation = Quaternion.Euler(FlatPitch, Mathf.Clamp(heading * 55f, -75f, 75f), heading * -8f);
            }
            float dt = Time.deltaTime;
            foreach (var l in leaves)
            {
                var p = l.transform.position;
                p.y -= l.speed * dt;
                if (p.y < Bottom) p.y = Top;
                p.x = l.x + Mathf.Sin(p.y * 1.4f + l.phase) * 0.45f;
                l.transform.position = p;
                l.transform.Rotate(l.spin * dt);
            }
        }

        void OnDestroy()
        {
            foreach (var m in swapped.Values) Destroy(m);
            foreach (var m in meshes) Destroy(m);
            if (blocks != null) Destroy(blocks);
        }
    }
}
