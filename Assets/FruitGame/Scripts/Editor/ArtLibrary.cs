using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Turns imported models into game-ready prefabs: shared palette materials, no colliders,
    /// saved under Assets/FruitGame/Prefabs/Art. Used by the scene builder (Docs/DESIGN.md §12).
    /// </summary>
    public static class ArtLibrary
    {
        const string ModelDir = "Assets/FruitGame/Art/Models";
        const string PaletteDir = "Assets/FruitGame/Art/Materials/Palette";
        const string PrefabDir = "Assets/FruitGame/Prefabs/Art";
        const string FruitAtlasTexture = ModelDir + "/Fruits/FruitAtlas.png";

        static readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();

        public static Material FruitAtlas()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(FruitAtlasTexture);
            var mat = PaletteMaterial("Fruit_Atlas", Color.white);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// The fruit atlas pushed into the distance like the far trees, for the background pile so it can't be
        /// mistaken for a falling fruit. Uses FruitAtlas_Far.png, a copy of the atlas with the haze baked in
        /// (made with: magick FruitAtlas.png -fill "#A9C6DD" -colorize 65% FruitAtlas_Far.png).
        /// </summary>
        public static Material FruitAtlasFar()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelDir + "/Fruits/FruitAtlas_Far.png");
            var mat = PaletteMaterial("Fruit_Atlas_Far", Color.white);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Smoothness", 0f);
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The two model variants (_01, _02) of a fruit from the Low Poly Food pack.</summary>
        public static GameObject[] FruitPrefabs(string fruitName)
        {
            var atlas = FruitAtlas();
            var list = new List<GameObject>();
            foreach (var suffix in new[] { "_01", "_02" })
            {
                var p = Prefab($"{ModelDir}/Fruits/{fruitName}{suffix}.fbx", $"Fruits/{fruitName}{suffix}", _ => atlas);
                if (p != null) list.Add(p);
            }
            return list.ToArray();
        }

        /// <summary>Tree from the Low Poly Trees pack; its embedded colours import correctly.</summary>
        public static GameObject Tree(string file) => Prefab($"{ModelDir}/Trees/{file}.fbx", $"Trees/{file}", null);

        /// <summary>
        /// A distant version of a tree: every colour pulled toward a light-blue haze (aerial perspective),
        /// so it reads as far away even in front of webcam footage.
        /// </summary>
        public static GameObject FarTree(string file, Color haze, float amount) =>
            Prefab($"{ModelDir}/Trees/{file}.fbx", $"Trees/{file}_Far", m =>
            {
                if (m == null) return null;
                var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
                return PaletteMaterial("Far_" + m.name, Color.Lerp(c, haze, amount));
            });

        /// <summary>Kenney Nature Kit (recoloured) OBJ, with colours corrected (see ObjColour).</summary>
        public static GameObject Nature(string name) => Prefab($"{ModelDir}/Nature/{name}.obj", $"Nature/{name}", ObjColour);

        public static GameObject Basket() => Prefab($"{ModelDir}/Basket/Basket.obj", "Basket/Basket", ObjColour);

        /// <summary>
        /// Unity reads OBJ "Kd" values as linear and converts them to sRGB, which washes the colours out.
        /// The .mtl files hold the intended sRGB colours, so undoing that conversion gives them back.
        /// Materials are shared by name across models.
        /// </summary>
        static Material ObjColour(Material imported)
        {
            if (imported == null) return null;
            var c = imported.HasProperty("_BaseColor") ? imported.GetColor("_BaseColor") : imported.color;
            var lin = c.linear;
            return PaletteMaterial("Obj_" + imported.name, new Color(lin.r, lin.g, lin.b, 1f));
        }

        public static Material PaletteMaterial(string name, Color color)
        {
            if (palette.TryGetValue(name, out var cached) && cached != null) return cached;
            EnsureFolder(PaletteDir);
            string path = $"{PaletteDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            palette[name] = mat;
            return mat;
        }

        /// <summary>
        /// Instantiates a model, swaps its materials through remap (null keeps the imported ones),
        /// strips colliders, and saves it as a plain prefab. Rebuilt on every call so it tracks the model.
        /// </summary>
        static GameObject Prefab(string modelPath, string prefabName, System.Func<Material, Material> remap)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogWarning($"[FruitGame] Model not found: {modelPath}");
                return null;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = System.IO.Path.GetFileName(prefabName);

            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (remap == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = remap(mats[i]);
                r.sharedMaterials = mats;
            }
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);

            string path = $"{PrefabDir}/{prefabName}.prefab";
            EnsureFolder(path.Substring(0, path.LastIndexOf('/')));
            var prefab = PrefabUtility.SaveAsPrefabAsset(inst, path);
            Object.DestroyImmediate(inst);
            return prefab;
        }

        /// <summary>World-space bounds of every renderer under a transform.</summary>
        public static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>
        /// Places a prefab instance so its bounds' bottom-centre sits at basePos and its height is targetHeight.
        /// </summary>
        public static Transform Place(GameObject prefab, Transform parent, Vector3 basePos, float targetHeight,
            float yaw = 0f, bool mirror = false)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var t = go.transform;
            t.rotation = Quaternion.Euler(0f, yaw, 0f) * t.rotation;
            var b = RendererBounds(go);
            if (b.size.y > 1e-4f) t.localScale *= targetHeight / b.size.y;
            if (mirror) t.localScale = Vector3.Scale(t.localScale, new Vector3(-1f, 1f, 1f));
            b = RendererBounds(go);
            t.position += basePos - new Vector3(b.center.x, b.min.y, b.center.z);
            return t;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
