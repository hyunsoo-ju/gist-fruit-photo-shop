using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Builds the scenery variety from the M8 style test into Game.unity (Docs/DESIGN.md §3, L1 and L4b):
    /// - <b>Far orchard</b>: six extra trees at the sides (smaller, at varied depths), a leaf tint per tree, fruit blocks on
    ///   the camera-facing side of every canopy, and three block clouds near the top. The centre stays open.
    /// - <b>Foreground plants</b>: a leaf tint per plant, a petal colour per flower, and twelve extra small flowers and
    ///   grass tufts along the front edge (never big bushes, which hid the flowers and covered the basket).
    /// Generated objects live in "Generated" containers and are replaced on every run; tints are palette materials.
    /// Run before "Voxels/Mark Scene Meshes" and "Voxels/Bake Scene", which turn the new copies into blocks too.
    /// </summary>
    public static class OrchardBuilder
    {
        const string ConfigPath = "Assets/FruitGame/Config/GameConfig.asset";
        static readonly Color Haze = new Color(0.663f, 0.776f, 0.867f); // #A9C6DD
        const float FruitDotFade = 0.8f;  // × farDither: fruit blocks stay a little more visible than leaves
        const float CloudFade = 0.35f;    // × farDither: clouds are already sky-coloured
        const string DitherDir = "Assets/FruitGame/Art/Materials/Palette/Dither";
        const string Sep = "__";          // material variant names: <base>__<variant>

        static readonly (string name, Color tint)[] Greens =
        {
            ("base", new Color(1f, 1f, 1f)), ("deep", new Color(0.78f, 0.9f, 0.72f)), ("lime", new Color(1.15f, 1.12f, 0.72f)),
            ("teal", new Color(0.8f, 1.0f, 1.05f)), ("olive", new Color(0.95f, 0.88f, 0.62f)),
        };
        static readonly (string name, Color color)[] Petals =
        {
            ("pink", new Color(0.95f, 0.55f, 0.7f)), ("white", new Color(0.97f, 0.95f, 0.9f)), ("orange", new Color(0.95f, 0.6f, 0.24f)),
            ("red", new Color(0.88f, 0.29f, 0.31f)), ("lilac", new Color(0.62f, 0.54f, 1f)), ("yellow", new Color(1f, 0.69f, 0.28f)),
        };
        static readonly (string name, Color color)[] FruitColors =
        {
            ("red", new Color(0.86f, 0.2f, 0.18f)), ("orange", new Color(0.98f, 0.55f, 0.12f)), ("yellow", new Color(0.98f, 0.82f, 0.2f)),
        };

        [MenuItem("FruitGame/Scene/Build Orchard And Plant Variety")]
        public static string Build()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            string far = BuildFarOrchard(config.farBlockSize, config.farDither);
            string plants = BuildPlants();
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return far + "\n" + plants;
        }

        static string BuildFarOrchard(float block, float fade)
        {
            Random.InitState(11);
            var farRoot = GameObject.Find("L4b_FarTrees").transform;
            var generated = Container(farRoot);
            var originals = farRoot.Cast<Transform>().Where(t => t != generated).ToList();
            var fullLeaves = ForegroundMaterials();

            var spots = new[] { (-3.1f, -0.9f, 5.6f, 0.42f), (3.15f, -0.7f, 5.5f, 0.45f), (-2.6f, -1.9f, 5.3f, 0.3f), (2.7f, -2.0f, 5.25f, 0.32f), (-3.6f, 0.6f, 5.8f, 0.5f), (3.7f, 0.9f, 5.8f, 0.48f) };
            var trees = new List<(Transform tree, int tint)>();
            for (int i = 0; i < originals.Count; i++) trees.Add((originals[i], (i * 2) % Greens.Length));
            for (int i = 0; i < spots.Length; i++)
            {
                var (x, y, z, scale) = spots[i];
                var src = originals[i % originals.Count];
                var copy = Object.Instantiate(src.gameObject, generated);
                copy.name = src.name + "_Extra" + (i + 1);
                copy.transform.position = new Vector3(x, y, z);
                copy.transform.localScale = src.localScale * scale;
                copy.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                trees.Add((copy.transform, (i + 1) % Greens.Length));
            }

            // Every far material dithers toward the sky from its full (foreground) colour; leaves also get a tint per tree.
            foreach (var (tree, tint) in trees)
                foreach (var r in tree.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterials = r.sharedMaterials.Select(m =>
                    {
                        if (m == null) return m;
                        var baseName = BaseName(m.name);
                        var full = fullLeaves.TryGetValue(baseName.Replace("Far_", ""), out var fm) ? fm.GetColor("_BaseColor") : m.GetColor("_BaseColor");
                        if (!baseName.Contains("Leaves")) return Dither(baseName, full, fade);
                        var (tintName, tintColor) = Greens[tint];
                        return tintName == "base" ? Dither(baseName, full, fade) : Dither(baseName + Sep + tintName, full * tintColor, fade);
                    }).ToArray();

            // Fruit blocks on the camera-facing side of each canopy (picked from the original smooth leaf vertices).
            var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            int dots = 0;
            foreach (var (tree, _) in trees)
            {
                var surface = new List<Vector3>();
                var bounds = new Bounds();
                bool any = false;
                foreach (var r in tree.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var vs = r.GetComponent<VoxelSource>();
                    var mesh = vs != null && vs.original != null ? vs.original : mf != null ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    var mats = r.sharedMaterials;
                    var verts = mesh.vertices;
                    for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                    {
                        if (mats[s] == null || !mats[s].name.Contains("Leaves")) continue;
                        foreach (int vi in mesh.GetTriangles(s).Distinct())
                        {
                            var w = r.transform.TransformPoint(verts[vi]);
                            surface.Add(w);
                            if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; } else bounds.Encapsulate(w);
                        }
                    }
                }
                if (!any) continue;
                var front = surface.Where(p => p.z < bounds.center.z - bounds.extents.z * 0.2f && p.y > bounds.min.y + bounds.size.y * 0.15f).ToList();
                if (front.Count == 0) continue;
                var (colorName, color) = FruitColors[Random.Range(0, FruitColors.Length)];
                var mat = Dither("Far_FruitDot" + Sep + colorName, color, fade * FruitDotFade);
                int count = Mathf.RoundToInt(Mathf.Clamp(bounds.size.x * bounds.size.y * 0.5f, 4f, 14f));
                for (int k = 0; k < count; k++)
                {
                    var p = front[Random.Range(0, front.Count)] + new Vector3(0f, 0f, -block * 0.5f);
                    p = new Vector3(Mathf.Round(p.x / block) * block, Mathf.Round(p.y / block) * block, p.z);
                    Block("FruitDot", generated, p, block, cubeMesh, mat);
                    dots++;
                }
            }

            // Block clouds: three rows of cubes, the bottom row slightly shaded.
            var cloudTop = Dither("Far_Cloud", Color.white, fade * CloudFade);
            var cloudBase = Dither("Far_Cloud" + Sep + "shade", new Color(0.9f, 0.94f, 0.98f), fade * CloudFade);
            var clouds = new[] { (-1.7f, 3.2f, 5.9f, 9), (1.5f, 3.6f, 5.9f, 7), (0.1f, 2.75f, 5.95f, 5) };
            foreach (var (cx, cy, cz, width) in clouds)
            {
                var cloud = new GameObject("Cloud").transform;
                cloud.SetParent(generated, false);
                cloud.position = new Vector3(cx, cy, cz);
                float s = block * 2f;
                for (int row = 0; row < 3; row++)
                {
                    int w = width - row * 3 + (row == 0 ? 0 : Random.Range(-1, 2));
                    float shift = row == 0 ? 0f : Random.Range(-1, 2) * s;
                    for (int c = 0; c < w; c++)
                        Block("Puff", cloud, cloud.position + new Vector3((c - (w - 1) * 0.5f) * s + shift, row * s, 0f), s, cubeMesh, row == 0 ? cloudBase : cloudTop);
                }
            }
            return $"far orchard: {spots.Length} extra trees, {trees.Count} tinted, {dots} fruit blocks, {clouds.Length} clouds";
        }

        static string BuildPlants()
        {
            Random.InitState(5);
            var fg = GameObject.Find("L1_Foreground").transform;
            var shrubs = fg.Find("Shrubs");
            var generated = Container(shrubs);
            var flowers = shrubs.Cast<Transform>().Where(t => t.name.StartsWith("flower")).ToList();
            var grass = shrubs.Cast<Transform>().Where(t => t.name.StartsWith("grass") && !t.name.Contains("leafsLarge")).ToList();
            var gaps = new[] { (-2.85f, -0.5f, true), (-1.25f, -0.52f, true), (-0.4f, -0.5f, false), (0.2f, -0.5f, true), (0.65f, -0.48f, false), (1.15f, -0.5f, true), (2.05f, -0.5f, false), (2.9f, -0.5f, true), (-2.3f, -0.48f, false), (-1.6f, -0.5f, true), (1.6f, -0.45f, true), (-0.05f, -0.46f, true) };
            foreach (var (x, z, flower) in gaps)
            {
                var pool = flower ? flowers : grass;
                var src = pool[Random.Range(0, pool.Count)];
                var copy = Object.Instantiate(src.gameObject, generated);
                copy.name = src.name + "_Extra";
                copy.transform.position = new Vector3(x, src.position.y, z);
                copy.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                copy.transform.localScale = src.localScale * Random.Range(0.7f, 0.95f);
            }

            int tinted = 0;
            var plants = shrubs.Cast<Transform>().Where(t => t != generated).Concat(generated.Cast<Transform>()).Concat(fg.Find("Small Trees").Cast<Transform>()).ToList();
            foreach (var plant in plants)
            {
                var (greenName, green) = Greens[Random.Range(0, Greens.Length)];
                var (petalName, petal) = Petals[Random.Range(0, Petals.Length)];
                foreach (var r in plant.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterials = r.sharedMaterials.Select(m =>
                    {
                        if (m == null) return m;
                        var baseName = BaseName(m.name);
                        var baseMat = BaseMaterial(m);
                        if (baseName.Contains("grass") || baseName.Contains("Leaves"))
                            return greenName == "base" ? baseMat : ArtLibrary.PaletteMaterial(baseName + Sep + greenName, baseMat.GetColor("_BaseColor") * green);
                        if (plant.name.StartsWith("flower") && baseName.StartsWith("Obj_color"))
                            return ArtLibrary.PaletteMaterial("Petal" + Sep + petalName, petal);
                        return baseMat;
                    }).ToArray();
                    tinted++;
                }
            }
            return $"plants: {gaps.Length} extra flowers and grass tufts, {tinted} renderers tinted";
        }

        /// <summary>A far-layer material: full colour, dithered toward the sky by fade (FruitGame/Dither Fade).</summary>
        static Material Dither(string name, Color full, float fade)
        {
            if (!AssetDatabase.IsValidFolder(DitherDir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(DitherDir), "Dither");
            var path = $"{DitherDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("FruitGame/Dither Fade")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", full);
            m.SetColor("_FadeColor", Haze);
            m.SetFloat("_Fade", fade);
            m.SetFloat("_Levels", 4f);
            m.SetFloat("_DitherPixel", 1f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Transform Container(Transform parent)
        {
            var existing = parent.Find("Generated");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            var go = new GameObject("Generated");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void Block(string name, Transform parent, Vector3 position, float size, Mesh mesh, Material mat)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var ls = go.transform.lossyScale;
            go.transform.localScale = new Vector3(size / Mathf.Abs(ls.x), size / Mathf.Abs(ls.y), size / Mathf.Abs(ls.z));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static string BaseName(string materialName)
        {
            int i = materialName.IndexOf(Sep, System.StringComparison.Ordinal);
            return i < 0 ? materialName : materialName.Substring(0, i);
        }

        /// <summary>The untinted material a variant was made from (same palette folder, name without the variant).</summary>
        static Material BaseMaterial(Material m)
        {
            var baseName = BaseName(m.name);
            if (baseName == m.name) return m;
            var path = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(m)) + "/" + baseName + ".mat";
            return AssetDatabase.LoadAssetAtPath<Material>(path) ?? m;
        }

        static Dictionary<string, Material> ForegroundMaterials() =>
            GameObject.Find("L1_Foreground").GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials).Where(m => m != null)
                .GroupBy(m => BaseName(m.name)).ToDictionary(g => g.Key, g => BaseMaterial(g.First()));
    }
}
