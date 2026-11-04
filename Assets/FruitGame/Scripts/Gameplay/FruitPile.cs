using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The background pile (Docs/DESIGN.md §12). Each caught fruit's copy drops from above into a narrow invisible box
    /// behind the person: one fruit deep, so it stacks like a 2D heap but tumbles and looks 3D. It sits on a raised
    /// platform of crates, so the heap shows above the player's shoulders.
    /// Anti-glitch measures: simple colliders, continuous collision, gentle depenetration, spawning only into
    /// free space, extra solver iterations, freezing settled fruit, a cap on the count, and its own physics layer.
    /// </summary>
    public class FruitPile : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] FruitSpawner spawner;
        [SerializeField] AudioFeedback audioFeedback;
        [Tooltip("Hazed fruit material that pushes the pile into the background (same haze as the distant trees). " +
                 "Replaces every material on pile fruit; leave empty to keep the normal look.")]
        [SerializeField] Material hazeMaterial;
        [Tooltip("Full-colour fruit material used while the photo booth is open (the pile shows in full colour).")]
        [SerializeField] Material photoMaterial;
        [SerializeField] float landSoundCooldown = 0.1f;
        [SerializeField] float shrinkTime = 0.3f;

        [Header("Crate heap")]
        [Tooltip("Crate for the heap under the pile (the crate pack's Box, as on the walls). Empty = the pile sits on the ground.")]
        [SerializeField] GameObject crate;
        [Tooltip("Barrel mixed in with the crates (the crate pack's barrel).")]
        [SerializeField] GameObject barrel;
        [Tooltip("Height of the heap above the ground: about 2.1 m lifts the pile just above the shoulders in the usual framing.")]
        [SerializeField] float heapHeight = 2.1f;
        [Tooltip("About one in this many pieces is a barrel.")]
        [SerializeField, Min(1)] int barrelEvery = 3;
        [Tooltip("Each piece is scaled by a random factor in this range.")]
        [SerializeField] Vector2 heapSize = new Vector2(0.8f, 1.15f);
        [Tooltip("Pieces lean by up to this many degrees, so the heap looks jumbled.")]
        [SerializeField] float heapTilt = 12f;
        [Tooltip("Each piece is darkened by a random brightness in this range (with a slight warm or cool shift) so the heap sits back behind the player.")]
        [SerializeField] Vector2 crateBrightness = new Vector2(0.62f, 0.85f);
        [SerializeField] int crateSeed = 11;

        const string LayerName = "Pile";

        readonly List<PileFruit> fruits = new List<PileFruit>();
        // The crate heap's renderers with their own (full-colour) materials, and the hazed copies used during play.
        readonly List<(MeshRenderer renderer, Material full, Material haze)> heapRenderers = new List<(MeshRenderer, Material, Material)>();
        readonly Dictionary<Material, Material> heapHaze = new Dictionary<Material, Material>();
        PhysicsMaterial material;
        int layer = -1;
        int mask = Physics.DefaultRaycastLayers;
        float depth;
        float floorY;
        float lastLandSound = -1f;

        public int Count => fruits.Count;

        void Awake()
        {
            // Dithered background look (Docs/DESIGN.md §4): strength from GameConfig, on a copy so the asset isn't edited.
            if (hazeMaterial != null && hazeMaterial.HasProperty("_Fade"))
            {
                hazeMaterial = new Material(hazeMaterial);
                hazeMaterial.SetFloat("_Fade", config.pileDither);
            }
            layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0)
            {
                // The pile only ever touches itself and its own walls.
                for (int i = 0; i < 32; i++)
                    Physics.IgnoreLayerCollision(layer, i, i != layer);
                mask = 1 << layer;
            }

            material = new PhysicsMaterial("Pile")
            {
                bounciness = 0.05f,
                dynamicFriction = 0.8f,
                staticFriction = 0.8f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            floorY = BuildPlatform();
            BuildContainer();
        }

        /// <summary>
        /// A messy heap of crates and barrels across the back (Docs/DESIGN.md §12), like the side walls: columns of pieces,
        /// each its own size, colour and lean, stacked to about `heapHeight`, so the caught fruit heaps up above the
        /// player's shoulders instead of behind the torso. Drawn on the Back layer (behind the player); each piece also gets
        /// a box collider on the pile's layer, so fruit settle on the uneven tops. Returns the height of a backup floor just
        /// under the heap's top, which stops fruit dropping down the gaps between pieces.
        /// </summary>
        float BuildPlatform()
        {
            if (crate == null || heapHeight <= 0f) return config.pileFloorY;
            int back = LayerMask.NameToLayer(PixelRenderer.BackLayer);
            var rng = new System.Random(crateSeed);
            float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            var root = new GameObject("Crate Heap").transform;
            root.SetParent(transform, false);
            var block = new MaterialPropertyBlock();
            float top = config.pileFloorY + heapHeight;
            float x = -config.pileHalfWidth - 0.15f;
            while (x < config.pileHalfWidth + 0.15f)
            {
                // A column: pieces stacked until they reach the heap's top, the last one allowed to poke above it.
                float y = config.pileFloorY;
                float columnWidth = 0f;
                while (y < top - 0.2f)
                {
                    bool isBarrel = barrel != null && rng.Next(barrelEvery) == 0;
                    var prefab = isBarrel ? barrel : crate;
                    var size = prefab.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                    float scale = Rand(heapSize.x, heapSize.y) * 0.7f / Mathf.Max(size.x, size.z);
                    var piece = Instantiate(prefab, root);
                    piece.name = isBarrel ? "Heap Barrel" : "Heap Crate";
                    var tilt = Quaternion.Euler(Rand(-heapTilt, heapTilt) * 0.5f, 0f, Rand(-heapTilt, heapTilt));
                    piece.transform.localRotation = tilt * Quaternion.Euler(0f, isBarrel ? rng.Next(360) : rng.Next(4) * 90f + Rand(-8f, 8f), 0f);
                    piece.transform.localScale = Vector3.one * scale;
                    float h = size.y * scale, w = Mathf.Max(size.x, size.z) * scale;
                    // Sink each piece a little into the one below, so the leaning stack doesn't float.
                    piece.transform.localPosition = new Vector3(x + w * 0.5f + Rand(-0.08f, 0.08f), y + h * 0.5f - 0.04f, Rand(-0.12f, 0.12f));
                    y += h * Rand(0.86f, 0.95f);
                    columnWidth = Mathf.Max(columnWidth, w);

                    float v = Rand(crateBrightness.x, crateBrightness.y);
                    float warm = Rand(-0.06f, 0.06f);
                    var tint = new Color(v * (1f + warm), v, v * (1f - warm), 1f);
                    foreach (var rend in piece.GetComponentsInChildren<MeshRenderer>())
                    {
                        // Hazed toward the sky like the pile fruit (the same dither material, with the props' palette),
                        // back to full colour in the photo (SetPhotoMode).
                        var full = rend.sharedMaterial;
                        var haze = HeapHaze(full);
                        heapRenderers.Add((rend, full, haze));
                        rend.sharedMaterial = haze;
                        if (back >= 0) rend.gameObject.layer = back;
                        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        rend.GetPropertyBlock(block);
                        block.SetColor("_BaseColor", tint);
                        rend.SetPropertyBlock(block);
                    }
                    // The collider lives on a child on the pile's layer (the piece itself renders on Back).
                    var col = new GameObject("Collider");
                    col.transform.SetParent(piece.transform, false);
                    if (layer >= 0) col.layer = layer;
                    var box = col.AddComponent<BoxCollider>();
                    var bounds = prefab.GetComponent<MeshFilter>().sharedMesh.bounds;
                    box.center = bounds.center;
                    box.size = bounds.size;
                    box.sharedMaterial = material;
                }
                // Columns overlap a little and vary in spacing, so the front isn't a neat grid.
                x += columnWidth * Rand(0.78f, 0.92f);
            }
            return top - 0.35f;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            foreach (var haze in heapHaze.Values) Destroy(haze);
        }

        void BuildContainer()
        {
            float maxScale = 1f;
            if (spawner != null)
                foreach (var type in spawner.FruitTypes)
                    if (type != null) maxScale = Mathf.Max(maxScale, type.sizeScale);
            depth = config.fruitSize * maxScale * config.pileDepthFactor;

            float w = config.pileHalfWidth;
            float wallH = config.pileSpawnY - floorY + 2f;
            float wallY = floorY + wallH * 0.5f;
            const float t = 0.5f;

            Box("Floor", new Vector3(0f, floorY - t * 0.5f, 0f), new Vector3(w * 2f + 2f * t, t, depth + 2f * t));
            Box("Wall L", new Vector3(-w - t * 0.5f, wallY, 0f), new Vector3(t, wallH, depth + 2f * t));
            Box("Wall R", new Vector3(w + t * 0.5f, wallY, 0f), new Vector3(t, wallH, depth + 2f * t));
            Box("Wall Front", new Vector3(0f, wallY, -(depth + t) * 0.5f), new Vector3(w * 2f, wallH, t));
            Box("Wall Back", new Vector3(0f, wallY, (depth + t) * 0.5f), new Vector3(w * 2f, wallH, t));
        }

        void Box(string name, Vector3 localPos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            if (layer >= 0) go.layer = layer;
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.sharedMaterial = material;
        }

        /// <summary>Drops a copy of a caught fruit onto the pile.</summary>
        /// <param name="golden">A golden catch: the pile copy is gold too.</param>
        public void Add(FruitType type, bool golden = false)
        {
            if (fruits.Count >= config.pileMax) RemoveOldest();

            var go = new GameObject(type != null ? $"Pile_{type.displayName}" : "Pile_Fruit");
            go.transform.SetParent(transform, false);
            if (layer >= 0) go.layer = layer;
            spawner.BuildVisual(go.transform, type);
            if (layer >= 0)
                foreach (var child in go.GetComponentsInChildren<Transform>()) child.gameObject.layer = layer;
            MakeBackground(go, type, golden);

            float radius = FitCollider(go);
            go.transform.position = FreeSpawnPoint(radius);
            go.transform.rotation = Random.rotation;

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxDepenetrationVelocity = 1.5f;
            body.solverIterations = 10;
            body.solverVelocityIterations = 4;
            body.angularDamping = 0.3f;
            body.linearVelocity = new Vector3(0f, -2f, 0f);
            body.angularVelocity = Random.onUnitSphere * 3f;

            var fruit = go.AddComponent<PileFruit>();
            fruit.Init(config.pileFreezeSpeed, config.pileFreezeTime);
            fruit.Type = type;
            fruit.Golden = golden;
            fruit.Landed += HandleLanded;
            fruits.Add(fruit);
        }

        /// <summary>
        /// Background look: hazed material, and no shadows either way, so pile fruit sit flat in the distance
        /// and never read as a falling fruit.
        /// </summary>
        void MakeBackground(GameObject go, FruitType type, bool golden)
        {
            var haze = golden ? FruitType.Golden(hazeMaterial) : FruitType.WithAtlas(hazeMaterial, type != null ? type.atlasFar : null);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (haze == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = haze;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Photo booth: show the pile in full colour (true) or back to the hazed background look.</summary>
        /// <summary>The pile's haze material with a prop material's texture (one copy per prop material).</summary>
        Material HeapHaze(Material full)
        {
            if (full == null || hazeMaterial == null) return full;
            if (heapHaze.TryGetValue(full, out var haze)) return haze;
            haze = new Material(hazeMaterial) { name = full.name + " (Pile Haze)" };
            if (full.HasProperty("_BaseMap")) haze.SetTexture("_BaseMap", full.GetTexture("_BaseMap"));
            heapHaze[full] = haze;
            return haze;
        }

        public void SetPhotoMode(bool on)
        {
            foreach (var (renderer, full, haze) in heapRenderers)
                if (renderer != null) renderer.sharedMaterial = on ? full : haze;
            if ((on ? photoMaterial : hazeMaterial) == null) return;
            foreach (var f in fruits)
            {
                if (f == null) continue;
                var type = f.Type;
                var mat = f.Golden ? FruitType.Golden(on ? photoMaterial : hazeMaterial)
                    : on ? FruitType.WithAtlas(photoMaterial, type != null ? type.atlas : null)
                         : FruitType.WithAtlas(hazeMaterial, type != null ? type.atlasFar : null);
                foreach (var r in f.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
            }
        }

        public void Clear()
        {
            foreach (var f in fruits)
                if (f != null) Destroy(f.gameObject);
            fruits.Clear();
        }

        void RemoveOldest()
        {
            fruits.RemoveAll(f => f == null);
            if (fruits.Count == 0) return;
            var oldest = fruits[0];
            fruits.RemoveAt(0);
            oldest.ShrinkAway(shrinkTime);
            // Anything resting on it must be able to settle into the gap.
            foreach (var f in fruits)
                if (f != null && f.Frozen) f.Wake();
        }

        /// <summary>
        /// Fits a sphere, or a capsule along the longest axis for elongated fruit (banana, pear, pineapple),
        /// to the visual. Returns the radius to use for spawn clearance.
        /// </summary>
        float FitCollider(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            Bounds b;
            if (renderers.Length == 0) b = new Bounds(go.transform.position, Vector3.one * config.fruitSize);
            else
            {
                b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
            }
            var size = b.size;
            var center = go.transform.InverseTransformPoint(b.center);
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float shortest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float middle = size.x + size.y + size.z - longest - shortest;

            if (longest > middle * 1.3f)
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.direction = longest == size.x ? 0 : longest == size.y ? 1 : 2;
                capsule.radius = middle * 0.5f * 0.95f;
                capsule.height = longest * 0.95f;
                capsule.center = center;
                capsule.sharedMaterial = material;
                return longest * 0.5f;
            }

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = longest * 0.5f * 0.9f;
            sphere.center = center;
            sphere.sharedMaterial = material;
            return sphere.radius;
        }

        Vector3 FreeSpawnPoint(float radius)
        {
            float range = Mathf.Max(0f, config.pileHalfWidth - radius);
            var origin = transform.position;
            Vector3 p = origin;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                p = new Vector3(origin.x + Random.Range(-range, range), config.pileSpawnY, origin.z);
                if (!Physics.CheckSphere(p, radius, mask, QueryTriggerInteraction.Ignore)) return p;
            }
            // Everything near the top is busy: drop it from higher up instead.
            p.y += radius * 3f;
            return p;
        }

        void HandleLanded(PileFruit fruit)
        {
            fruit.Landed -= HandleLanded;
            if (audioFeedback == null || Time.time - lastLandSound < landSoundCooldown) return;
            lastLandSound = Time.time;
            audioFeedback.PlayPileLand();
        }
    }
}
