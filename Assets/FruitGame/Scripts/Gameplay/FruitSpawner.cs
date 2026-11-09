using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Spawns the fruit (Docs/DESIGN.md §5–§7). Normally one at a time: the next appears after the current one is caught
    /// or missed, plus a gap that shrinks over the round. Except in Easy, fruit also drop on a beat
    /// (GameConfig.DropIntervalAt) that starts at that same pace and quickens evenly, so later in the round the next fruit
    /// drops before the last one lands (never more than two in the air). Every round has the same number of golden fruit
    /// and bombs (goldenPerRound, bombsPerRound), each at a random time in its own even share of the round, so they're
    /// spread out but not predictable; bombs only after bombAfterSeconds. The assist fades over the round. Easy (Difficulty.Easy) keeps one at a time, steady gaps, full assist and
    /// no bombs.
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
        [Tooltip("Voxel bomb visual (Bake Props), with a \"Fuse Tip\" child for the burning fuse.")]
        [SerializeField] GameObject bombPrefab;

        /// <summary>Raised when the current fruit is caught or missed.</summary>
        public event Action<FallingFruit, FallingFruit.Outcome> FruitResolved;
        public event Action<FallingFruit> FruitSpawned;
        public event Action<FallingFruit> FruitHitBasket;

        /// <summary>Round progress 0..1, used for the difficulty ramp.</summary>
        public Func<float> Progress { get; set; } = () => 0f;
        /// <summary>Seconds into the round, for when bombs may start.</summary>
        public Func<float> Elapsed { get; set; } = () => 0f;

        public IReadOnlyList<FruitType> FruitTypes => fruitTypes;
        /// <summary>The fruit in the air (the attract screen's pilot reads it).</summary>
        public IReadOnlyList<FallingFruit> Active => active;
        public bool Spawning { get; private set; }
        public bool HasActiveFruit
        {
            get
            {
                foreach (var f in active) if (f != null && !f.Resolved) return true;
                return false;
            }
        }

        readonly List<FallingFruit> active = new List<FallingFruit>();
        float nextSpawnTime = -1f;
        PhysicsMaterial fruitMaterial;
        readonly Dictionary<FruitType, Material> materials = new Dictionary<FruitType, Material>();

        public void Begin()
        {
            Spawning = true;
            nextSpawnTime = Time.time;
            lastSpawnTime = -1f;
            float end = config.roundDuration - config.specialsEndBefore;
            Schedule(goldenAt, config.goldenPerRound, 0f, end);
            Schedule(bombAt, bombPrefab == null ? 0 : config.BombsPerRound, config.bombAfterSeconds, end);
        }

        readonly List<float> goldenAt = new List<float>();
        readonly List<float> bombAt = new List<float>();

        /// <summary>`count` times between `from` and `to`: one in the middle part of each equal slot, in order.</summary>
        static void Schedule(List<float> times, int count, float from, float to)
        {
            times.Clear();
            if (count <= 0 || to <= from) return;
            float slot = (to - from) / count;
            for (int i = 0; i < count; i++)
                times.Add(from + slot * (i + UnityEngine.Random.Range(0.2f, 0.8f)));
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
            active.Clear();
        }

        float lastSpawnTime = -1f;

        void Update()
        {
            active.RemoveAll(f => f == null || f.Resolved);
            float progress = Mathf.Clamp01(Progress());
            if (assist != null) assist.roundLevel = config.AssistLevelAt(progress);
            if (!Spawning) return;

            float beat = config.DropIntervalAt(progress);
            bool afterLanding = active.Count == 0 && nextSpawnTime >= 0f && Time.time >= nextSpawnTime;
            // On the beat (later in the round): the next drops on time, even while the last is still falling.
            bool onBeat = beat > 0f && active.Count < config.MaxFruitInAir && lastSpawnTime >= 0f && Time.time >= lastSpawnTime + beat;
            if (afterLanding || onBeat)
            {
                nextSpawnTime = -1f;
                Spawn();
            }
        }

        /// <summary>A drop x at least spawnAwayFromBasket from the basket (growing over the round; uniform in Easy).</summary>
        float SpawnX(float progress)
        {
            float range = config.spawnXRange;
            if (Difficulty.Gentle || basket == null) return UnityEngine.Random.Range(-range, range);
            float away = Mathf.Lerp(config.spawnAwayFromBasket.x, config.spawnAwayFromBasket.y, progress);
            float bx = basket.X;
            // The allowed stretches either side of the basket, picked in proportion to their length.
            float leftLen = Mathf.Max(0f, (bx - away) - (-range));
            float rightLen = Mathf.Max(0f, range - (bx + away));
            if (leftLen + rightLen <= 0.01f) return bx < 0f ? range : -range;
            float r = UnityEngine.Random.value * (leftLen + rightLen);
            return r < leftLen ? -range + r : bx + away + (r - leftLen);
        }

        FallingFruit.Kind PickKind()
        {
            // The next spawn after a scheduled time takes it (a bomb first if both are due; the golden fruit follows).
            float t = Elapsed();
            if (bombAt.Count > 0 && t >= bombAt[0]) { bombAt.RemoveAt(0); return FallingFruit.Kind.Bomb; }
            if (goldenAt.Count > 0 && t >= goldenAt[0]) { goldenAt.RemoveAt(0); return FallingFruit.Kind.Golden; }
            return FallingFruit.Kind.Fruit;
        }

        void Spawn()
        {
            var kind = PickKind();
            var type = kind == FallingFruit.Kind.Bomb ? null : PickType();
            float progress = Mathf.Clamp01(Progress());
            float fall = config.FallSpeedAt(progress) * (kind == FallingFruit.Kind.Golden ? config.goldenSpeedFactor : 1f);
            float cap = config.DriftCapAt(progress);

            var go = new GameObject(kind == FallingFruit.Kind.Bomb ? "Bomb" : type != null ? $"Fruit_{type.displayName}" : "Fruit");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(SpawnX(progress), config.spawnY, 0f);
            go.transform.rotation = UnityEngine.Random.rotation;
            if (kind == FallingFruit.Kind.Bomb)
            {
                var model = Instantiate(bombPrefab, go.transform);
                NormaliseSize(model.transform, config.fruitSize);
                go.AddComponent<BombFuse>();
            }
            else BuildVisual(go.transform, type);
            // Step a whole game pixel at a time in the pixel render (Docs/DESIGN.md §4).
            foreach (Transform model in go.transform) model.gameObject.AddComponent<PixelSnap>().renderScale = config.renderScale;
            if (liveMaterial != null && kind != FallingFruit.Kind.Bomb)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var live = FruitType.WithAtlas(liveMaterial, type != null ? type.atlas : null);
                    if (kind == FallingFruit.Kind.Golden) live = FruitType.Golden(liveMaterial);
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = live;
                    r.sharedMaterials = mats;
                }
            if (kind == FallingFruit.Kind.Golden) go.AddComponent<GoldenSparkle>().radius = config.fruitSize * 0.45f;

            var fruit = go.AddComponent<FallingFruit>();
            fruit.Init(type, kind, config, basket, assist, fall, UnityEngine.Random.Range(-cap, cap), cap, FruitMaterial());
            fruit.OnResolved += HandleResolved;
            fruit.OnHitBasket += HandleHitBasket;
            active.Add(fruit);
            lastSpawnTime = Time.time;
            FruitSpawned?.Invoke(fruit);
        }

        void HandleResolved(FallingFruit fruit, FallingFruit.Outcome outcome)
        {
            fruit.OnResolved -= HandleResolved;
            fruit.OnHitBasket -= HandleHitBasket;
            active.Remove(fruit);
            // The next fruit follows once nothing is in the air (late in the round, it may already have dropped).
            if (Spawning && active.Count == 0) nextSpawnTime = Time.time + config.NextFruitDelayAt(Mathf.Clamp01(Progress()));
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
