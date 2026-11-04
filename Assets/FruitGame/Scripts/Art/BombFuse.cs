using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The burning fuse on a falling bomb (Docs/DESIGN.md §4): a flickering yellow-orange flame block at the fuse tip
    /// (the "Fuse Tip" child the prop baker adds) throwing off small sparks.
    /// </summary>
    public class BombFuse : MonoBehaviour
    {
        static readonly Color32[] Sparks =
        {
            new Color32(0xFF, 0xF2, 0x9E, 0xFF), new Color32(0xFF, 0xC4, 0x2E, 0xFF), new Color32(0xFF, 0x7A, 0x1A, 0xFF),
        };

        Transform tip;
        Transform flame;
        Mesh[] flameMeshes;
        float nextSpark;

        void Start()
        {
            tip = FindTip(transform);
            if (tip == null) return;
            flameMeshes = new Mesh[Sparks.Length];
            for (int i = 0; i < Sparks.Length; i++)
            {
                var c = Sparks[i];
                flameMeshes[i] = BlockMesh.Build((x, y, z) => c, Vector3Int.one, 1f, -Vector3.one * 0.5f, "Flame");
            }
            var renderer = GetComponentInChildren<MeshRenderer>();
            flame = new GameObject("Flame", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            flame.SetParent(tip, false);
            var r = flame.GetComponent<MeshRenderer>();
            r.sharedMaterial = FlameMaterial(renderer != null ? renderer.sharedMaterial : null);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static Transform FindTip(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name == "Fuse Tip") return t;
            return null;
        }

        static Material flameMaterial;

        /// <summary>The flame and sparks glow: vertex colours, unlit-bright, shared by every fuse.</summary>
        static Material FlameMaterial(Material fallback)
        {
            if (flameMaterial != null) return flameMaterial;
            var shader = Shader.Find("FruitGame/Voxel Title");
            flameMaterial = shader != null ? new Material(shader) { name = "Fuse Flame" } : fallback;
            if (flameMaterial != null && flameMaterial.HasProperty("_SweepStrength")) flameMaterial.SetFloat("_SweepStrength", 0f);
            return flameMaterial;
        }

        void Update()
        {
            if (tip == null) return;
            // The flame flickers between colours and sizes every frame or two.
            float s = Random.Range(0.11f, 0.17f) / Mathf.Max(0.001f, tip.lossyScale.x);
            flame.localScale = Vector3.one * s;
            flame.localRotation = Random.rotation;
            flame.GetComponent<MeshFilter>().sharedMesh = flameMeshes[Random.Range(0, flameMeshes.Length)];
            if (Time.time >= nextSpark)
            {
                nextSpark = Time.time + 0.025f;
                var dir = (Random.onUnitSphere + Vector3.up * 0.8f).normalized;
                BlockFx.Spark(tip.position, Sparks[Random.Range(0, Sparks.Length)], dir * Random.Range(0.6f, 1.5f),
                    Random.Range(0.04f, 0.07f), Random.Range(0.25f, 0.45f), 3f);
            }
        }
    }
}
