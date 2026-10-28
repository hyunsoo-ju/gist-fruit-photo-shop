using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Spawns exactly one fruit at a time. The next fruit only appears after the current one
    /// is caught or missed, plus GameConfig.nextFruitDelay.
    /// </summary>
    public class FruitSpawner : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BasketController basket;
        [SerializeField] BasketAssist assist;
        [SerializeField] List<FruitType> fruitTypes = new List<FruitType>();
        [Tooltip("Lit material used for greybox spheres; tinted per fruit type.")]
        [SerializeField] Material greyboxMaterial;
        [Tooltip("Material for fruit in play (rim-lit so they pop against the hazed pile). Leave empty to keep the model's own.")]
        [SerializeField] Material liveMaterial;

        /// <summary>Raised when the current fruit is caught or missed.</summary>
        public event Action<FallingFruit, FallingFruit.Outcome> FruitResolved;
        public event Action<FallingFruit> FruitSpawned;
        public event Action<FallingFruit> FruitHitBasket;

        /// <summary>Round progress 0..1, used for the difficulty ramp.</summary>
        public Func<float> Progress { get; set; } = () => 0f;

        public IReadOnlyList<FruitType> FruitTypes => fruitTypes;
        public bool Spawning { get; private set; }
        public bool HasActiveFruit => current != null && !current.Resolved;

        FallingFruit current;
        float nextSpawnTime = -1f;
        PhysicsMaterial fruitMaterial;
        readonly Dictionary<FruitType, Material> materials = new Dictionary<FruitType, Material>();

        public void Begin()
        {
            Spawning = true;
            nextSpawnTime = Time.time;
        }

        /// <summary>Stops new spawns. A fruit already in the air is allowed to finish.</summary>
        public void StopSpawning()
        {
            Spawning = false;
            nextSpawnTime = -1f;
        }

        public void Clear()
        {
            StopSpawning();
            foreach (var f in GetComponentsInChildren<FallingFruit>())
                Destroy(f.gameObject);
            current = null;
        }

        void Update()
        {
            if (Spawning && !HasActiveFruit && nextSpawnTime >= 0f && Time.time >= nextSpawnTime)
            {
                nextSpawnTime = -1f;
                Spawn();
            }
        }

        void Spawn()
        {
            var type = PickType();
            float progress = Mathf.Clamp01(Progress());
            float fall = config.FallSpeedAt(progress);
            float cap = config.DriftCapAt(progress);

            var go = new GameObject(type != null ? $"Fruit_{type.displayName}" : "Fruit");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(
                UnityEngine.Random.Range(-config.spawnXRange, config.spawnXRange), config.spawnY, 0f);
            go.transform.rotation = UnityEngine.Random.rotation;
            BuildVisual(go.transform, type);
            // Step a whole game pixel at a time in the pixel render (Docs/DESIGN.md §3).
            foreach (Transform model in go.transform) model.gameObject.AddComponent<PixelSnap>().renderScale = config.renderScale;
            if (liveMaterial != null)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var live = FruitType.WithAtlas(liveMaterial, type != null ? type.atlas : null);
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = live;
                    r.sharedMaterials = mats;
                }

            current = go.AddComponent<FallingFruit>();
            current.Init(type, config, basket, assist, fall, UnityEngine.Random.Range(-cap, cap), cap, FruitMaterial());
            current.OnResolved += HandleResolved;
            current.OnHitBasket += HandleHitBasket;
            FruitSpawned?.Invoke(current);
        }

        void HandleResolved(FallingFruit fruit, FallingFruit.Outcome outcome)
        {
            fruit.OnResolved -= HandleResolved;
            fruit.OnHitBasket -= HandleHitBasket;
            if (Spawning) nextSpawnTime = Time.time + config.nextFruitDelay;
            FruitResolved?.Invoke(fruit, outcome);
        }

        void HandleHitBasket(FallingFruit fruit) => FruitHitBasket?.Invoke(fruit);

        PhysicsMaterial FruitMaterial()
        {
            if (fruitMaterial == null)
                fruitMaterial = new PhysicsMaterial("Fruit")
                {
                    bounciness = config.fruitBounciness,
                    dynamicFriction = config.fruitFriction,
                    staticFriction = config.fruitFriction,
                    bounceCombine = PhysicsMaterialCombine.Maximum
                };
            return fruitMaterial;
        }

        FruitType PickType()
        {
            float total = 0f;
            foreach (var t in fruitTypes) if (t != null) total += t.spawnWeight;
            if (total <= 0f) return fruitTypes.Count > 0 ? fruitTypes[0] : null;

            float r = UnityEngine.Random.value * total;
            foreach (var t in fruitTypes)
            {
                if (t == null) continue;
                r -= t.spawnWeight;
                if (r <= 0f) return t;
            }
            return fruitTypes[fruitTypes.Count - 1];
        }

        /// <summary>Adds a fruit's model (or a greybox sphere) under root, sized for its type. Also used by the pile.</summary>
        public void BuildVisual(Transform root, FruitType type)
        {
            float size = config.fruitSize * (type != null ? type.sizeScale : 1f);

            if (type != null && type.visualPrefabs != null && type.visualPrefabs.Length > 0)
            {
                var prefab = type.visualPrefabs[UnityEngine.Random.Range(0, type.visualPrefabs.Length)];
                if (prefab != null)
                {
                    var model = Instantiate(prefab, root);
                    foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
                    NormaliseSize(model.transform, size);
                    return;
                }
            }

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(root, false);
            sphere.transform.localScale = Vector3.one * size;
            if (type != null && type.colliderShape == FruitColliderShape.Capsule)
                sphere.transform.localScale = new Vector3(size * 1.3f, size * 0.8f, size * 0.8f);
            sphere.GetComponent<Renderer>().sharedMaterial = MaterialFor(type);
        }

        /// <summary>Scales and centres a model so its largest extent equals size.</summary>
        static void NormaliseSize(Transform model, float size)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var b = WorldBounds(renderers);
            float extent = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (extent <= 1e-5f) return;
            model.localScale *= size / extent;
            model.position += model.parent.position - WorldBounds(renderers).center;
        }

        static Bounds WorldBounds(Renderer[] renderers)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        Material MaterialFor(FruitType type)
        {
            if (greyboxMaterial == null) return null;
            if (type == null) return greyboxMaterial;
            if (!materials.TryGetValue(type, out var m))
            {
                m = new Material(greyboxMaterial) { name = $"Greybox_{type.displayName}" };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", type.greyboxColor);
                else m.color = type.greyboxColor;
                materials[type] = m;
            }
            return m;
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) Destroy(m);
            if (fruitMaterial != null) Destroy(fruitMaterial);
        }
    }
}
