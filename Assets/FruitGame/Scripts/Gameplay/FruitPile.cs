using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The background pile (Docs/DESIGN.md §8). Each caught fruit's copy drops from above into a narrow invisible box
    /// behind the person: one fruit deep, so it stacks like a 2D heap but tumbles and looks 3D.
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

        const string LayerName = "Pile";

        readonly List<PileFruit> fruits = new List<PileFruit>();
        PhysicsMaterial material;
        int layer = -1;
        int mask = Physics.DefaultRaycastLayers;
        float depth;
        float lastLandSound = -1f;

        public int Count => fruits.Count;

        void Awake()
        {
            // Dithered background look (Docs/DESIGN.md §3): strength from GameConfig, on a copy so the asset isn't edited.
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
            BuildContainer();
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        void BuildContainer()
        {
            float maxScale = 1f;
            if (spawner != null)
                foreach (var type in spawner.FruitTypes)
                    if (type != null) maxScale = Mathf.Max(maxScale, type.sizeScale);
            depth = config.fruitSize * maxScale * config.pileDepthFactor;

            float w = config.pileHalfWidth;
            float wallH = config.pileSpawnY - config.pileFloorY + 2f;
            float wallY = config.pileFloorY + wallH * 0.5f;
            const float t = 0.5f;

            Box("Floor", new Vector3(0f, config.pileFloorY - t * 0.5f, 0f), new Vector3(w * 2f + 2f * t, t, depth + 2f * t));
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
        public void Add(FruitType type)
        {
            if (fruits.Count >= config.pileMax) RemoveOldest();

            var go = new GameObject(type != null ? $"Pile_{type.displayName}" : "Pile_Fruit");
            go.transform.SetParent(transform, false);
            if (layer >= 0) go.layer = layer;
            spawner.BuildVisual(go.transform, type);
            if (layer >= 0)
                foreach (var child in go.GetComponentsInChildren<Transform>()) child.gameObject.layer = layer;
            MakeBackground(go, type);

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
            fruit.Landed += HandleLanded;
            fruits.Add(fruit);
        }

        /// <summary>
        /// Background look: hazed material, and no shadows either way, so pile fruit sit flat in the distance
        /// and never read as a falling fruit.
        /// </summary>
        void MakeBackground(GameObject go, FruitType type)
        {
            var haze = FruitType.WithAtlas(hazeMaterial, type != null ? type.atlasFar : null);
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
        public void SetPhotoMode(bool on)
        {
            if ((on ? photoMaterial : hazeMaterial) == null) return;
            foreach (var f in fruits)
            {
                if (f == null) continue;
                var type = f.Type;
                var mat = on ? FruitType.WithAtlas(photoMaterial, type != null ? type.atlas : null)
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
