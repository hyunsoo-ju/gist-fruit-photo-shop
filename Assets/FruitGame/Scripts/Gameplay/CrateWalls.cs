using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitGame
{
    /// <summary>
    /// The visible side walls (Docs/DESIGN.md §3, §5), stacked from the voxelised crate pack (FruitGame > Voxels >
    /// Bake Props): crates (Box.fbx) and barrels (barrel.fbx) only. On each wall line (inner faces on the bounce planes at
    /// ±wallX) a column of crates with a barrel every few pieces rises from the ground bank into the canopy, turned at
    /// random so they don't look copied. Outside it (seen in landscape) shorter crate stacks step down toward the screen
    /// edge, each topped with a barrel with fruit heaped on its lid, and a barrel stands at the foot. Every piece gets its own size and a slightly different wood colour. When a fruit bounces off a wall, the piece it hit wobbles, a few wood chips fly and a knock plays.
    /// </summary>
    public class CrateWalls : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] GameObject crate;
        [SerializeField] GameObject barrel;
        [Tooltip("Fruit heaped on the barrels on top of the outer stacks (their voxel visuals, scaled down).")]
        [SerializeField] FruitType[] ornamentFruit;
        [SerializeField] AudioFeedback audioFeedback;
        [Tooltip("Lit vertex-colour material for the wood chips.")]
        [SerializeField] Material chipMaterial;
        [Tooltip("Height of the ground bank's top, where the stacks stand.")]
        [SerializeField] float bottom = -3.0f;
        [SerializeField] float top = 5.4f;
        [Tooltip("One in this many pieces of the wall column is a barrel.")]
        [SerializeField] int barrelEvery = 4;
        [SerializeField] float ornamentFruitSize = 0.3f;
        [Tooltip("Each piece is scaled by a random factor in this range, so the stacks look natural.")]
        [SerializeField] Vector2 sizeRange = new Vector2(0.82f, 1.12f);
        [Tooltip("Each piece's colour is multiplied by a random brightness in this range, with a slight warm or cool shift.")]
        [SerializeField] Vector2 brightnessRange = new Vector2(0.82f, 1.08f);
        [SerializeField] int seed = 7;

        static readonly Color32 Chip = new Color32(0xB8, 0x7D, 0x42, 0xFF);

        class Piece
        {
            public Transform transform;
            public Vector3 home;
            public Quaternion rotation;
            public float side, bottom, top, wobble;
        }

        class Flyer
        {
            public Transform transform;
            public Vector3 velocity;
            public float life;
        }

        readonly List<Piece> pieces = new List<Piece>();
        readonly List<Flyer> chips = new List<Flyer>();
        Mesh chipMesh;
        float lastSound;
        System.Random rng;

        void Awake()
        {
            rng = new System.Random(seed);
            chipMesh = BlockMesh.Build((x, y, z) => Chip, Vector3Int.one, 1f, -Vector3.one * 0.5f, "Chip");
            if (crate == null) return;
            foreach (float side in new[] { -1f, 1f })
            {
                BuildColumn(side);
                BuildOuterStacks(side);
            }
            FallingFruit.WallBounced += OnWallBounced;
        }

        void OnDestroy() => FallingFruit.WallBounced -= OnWallBounced;

        static Bounds MeshBounds(GameObject prefab) => prefab.GetComponent<MeshFilter>().sharedMesh.bounds;

        /// <summary>The wall itself: crates with a barrel every few pieces, each flush with the bounce plane.</summary>
        void BuildColumn(float side)
        {
            float y = bottom;
            int i = 0;
            while (y < top)
            {
                bool isBarrel = barrel != null && i % barrelEvery == barrelEvery - 1;
                var prefab = isBarrel ? barrel : crate;
                // Crates turn in quarter steps plus a few degrees, so neighbours read as separate crates; barrels spin
                // freely (they're round). A small outward nudge keeps the twisted corner off the bounce plane.
                float turn = isBarrel ? rng.Next(360) : rng.Next(4) * 90f + rng.Next(-4, 5);
                var piece = Place(prefab, side, y, turn, isBarrel ? 0f : 0.03f + (float)rng.NextDouble() * 0.04f);
                pieces.Add(piece);
                y = piece.top;
                i++;
            }
        }

        /// <summary>Landscape dressing outside the wall: stepped crate stacks, each topped with a barrel with fruit on its
        /// lid, and a barrel at the foot. Off-screen in portrait.</summary>
        void BuildOuterStacks(float side)
        {
            var size = MeshBounds(crate).size;
            float step = size.x * sizeRange.y + 0.04f;
            int[] heights = { 2, 1 };
            for (int s = 0; s < heights.Length; s++)
            {
                float y = bottom;
                for (int k = 0; k < heights[s]; k++)
                {
                    var p = Place(crate, side, y, rng.Next(4) * 90f, step * (s + 1) + (float)rng.NextDouble() * 0.08f);
                    y = p.top;
                }
                if (barrel != null)
                {
                    var b = Place(barrel, side, y, rng.Next(360), step * (s + 1) + 0.04f);
                    HeapFruit(b);
                }
            }
            if (barrel != null)
            {
                Place(barrel, side, bottom, side > 0 ? 180f : 0f, step * 3f);
            }
        }

        /// <summary>Instantiates a prop with its bottom at y and its inner side `outward` metres beyond the wall plane.</summary>
        Piece Place(GameObject prefab, float side, float y, float turn, float outward)
        {
            var go = Instantiate(prefab, transform);
            var rotation = Quaternion.Euler(0f, turn, 0f);
            go.transform.rotation = rotation;
            float scale = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble());
            go.transform.localScale = Vector3.one * scale;
            var b = MeshBounds(prefab);
            b = new Bounds(b.center * scale, b.size * scale);
            Tint(go);
            // Half-width along world x after the turn (a quarter turn swaps x and z).
            float halfX = Mathf.Abs((rotation * Vector3.right).x) * b.extents.x + Mathf.Abs((rotation * Vector3.forward).x) * b.extents.z;
            var centre = rotation * b.center;
            var home = new Vector3(side * (config.wallX + outward + halfX) - centre.x, y + b.extents.y - centre.y, -centre.z);
            go.transform.position = home;
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.On;
            return new Piece { transform = go.transform, home = home, rotation = rotation, side = side, bottom = y, top = y + b.size.y };
        }

        /// <summary>A random brightness with a slight warm (redder) or cool (greyer) shift, per piece.</summary>
        void Tint(GameObject go)
        {
            float v = Mathf.Lerp(brightnessRange.x, brightnessRange.y, (float)rng.NextDouble());
            float warm = ((float)rng.NextDouble() - 0.5f) * 0.12f;
            var tint = new Color(v * (1f + warm), v, v * (1f - warm), 1f);
            var block = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", tint);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>A few fruit heaped on a barrel's lid.</summary>
        void HeapFruit(Piece barrelPiece)
        {
            if (ornamentFruit == null || ornamentFruit.Length == 0) return;
            for (int k = 0; k < 3; k++)
            {
                var type = ornamentFruit[rng.Next(ornamentFruit.Length)];
                if (type == null || type.visualPrefabs == null || type.visualPrefabs.Length == 0) continue;
                var visual = type.visualPrefabs[rng.Next(type.visualPrefabs.Length)];
                var fruit = Instantiate(visual, barrelPiece.transform);
                fruit.transform.rotation = Quaternion.Euler(rng.Next(360), rng.Next(360), rng.Next(360));
                var mesh = fruit.GetComponentInChildren<MeshFilter>().sharedMesh;
                float across = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
                fruit.transform.localScale = Vector3.one * (ornamentFruitSize * type.sizeScale / Mathf.Max(1e-6f, across));
                float angle = k * 120f + rng.Next(40);
                var offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 0.09f;
                // Resting on the lid, the last one on top of the other two.
                float lift = ornamentFruitSize * type.sizeScale * 0.45f + (k == 2 ? ornamentFruitSize * 0.5f : 0f);
                fruit.transform.position = new Vector3(barrelPiece.transform.position.x, barrelPiece.top + lift, 0f) + (k == 2 ? Vector3.zero : offset);
            }
        }

        void OnWallBounced(Vector3 point, float speed)
        {
            Piece hit = null;
            foreach (var p in pieces)
                if (Mathf.Sign(point.x) == p.side && point.y >= p.bottom && point.y < p.top) { hit = p; break; }
            if (hit != null) hit.wobble = 1f;
            for (int i = 0; i < 4; i++) SpawnChip(point, -Mathf.Sign(point.x));
            if (audioFeedback != null && Time.time - lastSound > 0.1f)
            {
                audioFeedback.PlayWallBump();
                lastSound = Time.time;
            }
        }

        void SpawnChip(Vector3 point, float inward)
        {
            var go = new GameObject("Chip", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.transform.position = point + new Vector3(inward * 0.05f, Random.Range(-0.1f, 0.1f), -0.3f);
            go.transform.localScale = Vector3.one * config.fruitBlockSize * Random.Range(0.8f, 1.2f);
            go.GetComponent<MeshFilter>().sharedMesh = chipMesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = chipMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            chips.Add(new Flyer
            {
                transform = go.transform,
                velocity = new Vector3(inward * Random.Range(0.6f, 1.6f), Random.Range(0.8f, 2.2f), Random.Range(-0.4f, 0f)),
                life = Random.Range(0.35f, 0.55f),
            });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var p in pieces)
            {
                if (p.wobble <= 0f) continue;
                p.wobble = Mathf.Max(0f, p.wobble - dt / 0.35f);
                float w = p.wobble * p.wobble;
                // Knocked outward and tipped, settling back with a couple of swings.
                float swing = Mathf.Sin((1f - p.wobble) * Mathf.PI * 4f);
                p.transform.position = p.home + new Vector3(p.side * 0.05f * w, 0f, 0f);
                p.transform.rotation = Quaternion.Euler(0f, 0f, -p.side * 5f * w * swing) * p.rotation;
            }
            for (int i = chips.Count - 1; i >= 0; i--)
            {
                var chip = chips[i];
                chip.life -= dt;
                if (chip.life <= 0f)
                {
                    Destroy(chip.transform.gameObject);
                    chips.RemoveAt(i);
                    continue;
                }
                chip.velocity.y -= 9f * dt;
                chip.transform.position += chip.velocity * dt;
            }
        }
    }
}
