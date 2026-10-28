using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    public enum FruitColliderShape
    {
        Sphere,
        Capsule
    }

    /// <summary>
    /// One asset per fruit. Every fruit is tracked as its own type so scoring can vary per fruit later.
    /// </summary>
    [CreateAssetMenu(menuName = "FruitGame/Fruit Type", fileName = "FruitType")]
    public class FruitType : ScriptableObject
    {
        public string id = "apple";
        public string displayName = "Apple";
        [Tooltip("Visual model. If empty, a coloured sphere is used (greybox).")]
        public GameObject[] visualPrefabs;
        public int points = 1;
        [Min(0f)] public float spawnWeight = 1f;
        [Tooltip("Size relative to GameConfig.fruitSize.")]
        public float sizeScale = 1f;
        public FruitColliderShape colliderShape = FruitColliderShape.Sphere;
        public Color greyboxColor = Color.red;
        public Sprite icon;

        [Tooltip("The original (smooth) models the voxel visuals are baked from. Filled in by the voxel baker.")]
        public GameObject[] sourceModels;
        [Tooltip("The source models' colour atlas when it isn't their own material's texture (models from another pack).")]
        public Texture2D sourceAtlas;

        [Header("Atlas override (models from another pack)")]
        [Tooltip("Colour atlas of these models when it isn't the shared fruit atlas; swapped into the live and photo materials. Empty = the materials' own.")]
        public Texture2D atlas;
        [Tooltip("Hazed copy of atlas for the background pile. Empty = the haze material's own.")]
        public Texture2D atlasFar;

        static readonly Dictionary<(Material, Texture2D), Material> retextured = new Dictionary<(Material, Texture2D), Material>();

        /// <summary>mat with its base map swapped for tex (shared, cached per pair); mat itself when tex is empty.</summary>
        public static Material WithAtlas(Material mat, Texture2D tex)
        {
            if (mat == null || tex == null) return mat;
            if (!retextured.TryGetValue((mat, tex), out var m) || m == null)
            {
                m = new Material(mat) { name = $"{mat.name}_{tex.name}" };
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                retextured[(mat, tex)] = m;
            }
            return m;
        }
    }
}
