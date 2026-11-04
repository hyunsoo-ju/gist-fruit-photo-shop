using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FruitGame
{
    /// <summary>
    /// Little flying blocks for effects (Docs/DESIGN.md §15): the bomb's fuse sparks and explosion, and the golden
    /// fruit's sparkle trail. Each block flies, falls (or floats) and shrinks away. One instance in the scene holds the
    /// lit vertex-colour material; the static calls are no-ops without it.
    /// </summary>
    public class BlockFx : MonoBehaviour
    {
        [Tooltip("Lit vertex-colour material (FruitGame/Voxel Title with no sweep).")]
        [SerializeField] Material material;

        static BlockFx instance;

        class Block
        {
            public Transform transform;
            public Vector3 velocity, spin;
            public float life, age, size, gravity;
        }

        readonly List<Block> blocks = new List<Block>();
        readonly Stack<GameObject> pool = new Stack<GameObject>();
        readonly Dictionary<Color32, Mesh> meshes = new Dictionary<Color32, Mesh>();

        void Awake() => instance = this;
        void OnDestroy() { if (instance == this) instance = null; }

        /// <summary>One block from position, moving at velocity, shrinking to nothing over life seconds.</summary>
        public static void Spark(Vector3 position, Color32 color, Vector3 velocity, float size, float life, float gravity = 9f)
        {
            if (instance != null) instance.Add(position, color, velocity, size, life, gravity);
        }

        /// <summary>A burst of blocks in random directions (upward-biased), in the given colours.</summary>
        public static void Burst(Vector3 position, Color32[] colors, int count, float speed, float size, float life, float gravity = 9f)
        {
            if (instance == null) return;
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.8f + 0.2f;
                dir.z *= 0.4f; // mostly across the screen, not toward the camera
                instance.Add(position, colors[Random.Range(0, colors.Length)], dir.normalized * speed * Random.Range(0.4f, 1f),
                    size * Random.Range(0.6f, 1.3f), life * Random.Range(0.7f, 1.2f), gravity);
            }
        }

        void Add(Vector3 position, Color32 color, Vector3 velocity, float size, float life, float gravity)
        {
            var go = pool.Count > 0 ? pool.Pop() : NewBlock();
            go.SetActive(true);
            go.GetComponent<MeshFilter>().sharedMesh = MeshFor(color);
            go.transform.position = position;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.one * size;
            blocks.Add(new Block
            {
                transform = go.transform, velocity = velocity, spin = Random.insideUnitSphere * 540f,
                life = life, size = size, gravity = gravity,
            });
        }

        GameObject NewBlock()
        {
            var go = new GameObject("Fx Block", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        Mesh MeshFor(Color32 color)
        {
            if (!meshes.TryGetValue(color, out var mesh))
                meshes[color] = mesh = BlockMesh.Build((x, y, z) => color, Vector3Int.one, 1f, -Vector3.one * 0.5f, "Fx Block");
            return mesh;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = blocks.Count - 1; i >= 0; i--)
            {
                var b = blocks[i];
                b.age += dt;
                if (b.age >= b.life)
                {
                    b.transform.gameObject.SetActive(false);
                    pool.Push(b.transform.gameObject);
                    blocks.RemoveAt(i);
                    continue;
                }
                b.velocity.y -= b.gravity * dt;
                b.transform.position += b.velocity * dt;
                b.transform.Rotate(b.spin * dt);
                b.transform.localScale = Vector3.one * b.size * (1f - b.age / b.life);
            }
        }
    }
}
