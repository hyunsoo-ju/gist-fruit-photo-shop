using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Bakes the voxel look (Docs/DESIGN.md §12) at the block sizes in GameConfig:
    /// - <b>Fruit</b>: every FruitType's source models become voxel prefabs coloured from one shared palette texture
    ///   (a few clean colours per fruit), set as the type's visual and atlas. The pile dithers the same palette.
    /// - <b>Scene</b>: every <see cref="VoxelSource"/> in the open scene gets a block mesh built from its original
    ///   mesh at its own scale, keeping its materials.
    /// Re-run either after changing block sizes or models; assets are overwritten in place, so references survive.
    /// </summary>
    public static class VoxelBaker
    {
        const string ConfigPath = "Assets/FruitGame/Config/GameConfig.asset";
        const string OutDir = "Assets/FruitGame/Art/Voxels";
        const string PalettePath = OutDir + "/FruitPalette.png";
        const int PaletteSide = 16;

        static readonly string[] SceneRoots = { "L1_Foreground", "L2_Play", "L4b_FarTrees" };

        [MenuItem("FruitGame/Voxels/Mark Scene Meshes")]
        public static string MarkScene()
        {
            int n = 0;
            foreach (var rootName in SceneRoots)
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.GetComponent<VoxelSource>() != null || mf.sharedMesh == null) continue;
                    // Built-in primitives (fruit blocks, cloud puffs) are already blocks.
                    if (AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith("Library/")) continue;
                    var vs = Undo.AddComponent<VoxelSource>(mf.gameObject);
                    vs.original = mf.sharedMesh;
                    vs.layer = rootName == "L4b_FarTrees" ? VoxelLayer.Far : VoxelLayer.World;
                    n++;
                }
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return $"{n} meshes marked";
        }

        [MenuItem("FruitGame/Voxels/Bake Scene")]
        public static string BakeScene()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            EnsureFolder(OutDir + "/World");
            var made = new Dictionary<(Mesh, Vector3Int), Mesh>();
            int objects = 0, faces = 0;
            foreach (var vs in Object.FindObjectsByType<VoxelSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (vs.original == null) continue;
                float block = vs.blockSizeOverride > 0f ? vs.blockSizeOverride
                    : vs.layer == VoxelLayer.Far ? config.farBlockSize : config.worldBlockSize;
                var s = vs.transform.lossyScale;
                var cell = new Vector3(block / Mathf.Abs(s.x), block / Mathf.Abs(s.y), block / Mathf.Abs(s.z));
                var key = (vs.original, Vector3Int.RoundToInt(cell * 100000f));
                if (!made.TryGetValue(key, out var mesh))
                {
                    var src = new VoxelMesher.Source();
                    VoxelMesher.Add(src, vs.original, Matrix4x4.identity);
                    var name = $"{vs.original.name}_{Mathf.RoundToInt(block * 1000)}mm_{Mathf.RoundToInt(Mathf.Abs(s.x) * 100)}";
                    var result = VoxelMesher.Build(src, cell, name);
                    mesh = SaveMesh(result.mesh, $"{OutDir}/World/{Sanitize(name)}.asset");
                    made[key] = mesh;
                    faces += result.faces;
                }
                var mf = vs.GetComponent<MeshFilter>();
                Undo.RecordObject(mf, "Bake voxels");
                mf.sharedMesh = mesh;
                objects++;
            }
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"{objects} scene meshes → {made.Count} voxel meshes ({faces} faces)";
        }

        [MenuItem("FruitGame/Voxels/Bake Fruit")]
        public static string BakeFruit()
        {
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            EnsureFolder(OutDir + "/Fruit");
            var types = AssetDatabase.FindAssets("t:FruitGame.FruitType")
                .Select(g => AssetDatabase.LoadAssetAtPath<FruitType>(AssetDatabase.GUIDToAssetPath(g)))
                .OrderBy(t => t.name).ToList();

            var palette = new List<Color>();
            var bakes = new List<(FruitType type, List<GameObject> prefabs)>();
            var log = new System.Text.StringBuilder();
            var readable = new Dictionary<Texture, Texture2D>();

            foreach (var type in types)
            {
                if (type.sourceModels == null || type.sourceModels.Length == 0)
                    type.sourceModels = type.visualPrefabs.Where(p => p != null && !AssetDatabase.GetAssetPath(p).StartsWith(OutDir)).ToArray();
                // The first bake replaces atlas with the shared palette, so remember the models' real atlas.
                if (type.sourceAtlas == null && type.atlas != null && AssetDatabase.GetAssetPath(type.atlas) != PalettePath)
                    type.sourceAtlas = type.atlas;
                var prefabs = new List<GameObject>();
                foreach (var model in type.sourceModels)
                {
                    if (model == null) continue;
                    var src = new VoxelMesher.Source();
                    var slotTextures = new List<(Texture2D tex, Color tint)>();
                    var rootInv = model.transform.worldToLocalMatrix;
                    foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (mf.sharedMesh == null) continue;
                        var mats = mf.GetComponent<Renderer>().sharedMaterials;
                        for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                        {
                            var m = s < mats.Length ? mats[s] : null;
                            var tex = type.sourceAtlas != null ? type.sourceAtlas : m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : null;
                            var tint = type.sourceAtlas != null || m == null ? Color.white : m.GetColor("_BaseColor");
                            slotTextures.Add((Readable(tex, readable), tint));
                        }
                        VoxelMesher.Add(src, mf.sharedMesh, rootInv * mf.transform.localToWorldMatrix, src.triangles.Count == 0 && src.positions.Count == 0 ? 0 : src.slotCount);
                    }
                    src.sampleColor = (uv, slot) =>
                    {
                        var (tex, tint) = slotTextures[Mathf.Clamp(slot, 0, slotTextures.Count - 1)];
                        var c = tex != null ? tex.GetPixelBilinear(uv.x, uv.y) : Color.white;
                        return c * tint;
                    };

                    var min = src.positions[0];
                    var max = src.positions[0];
                    foreach (var p in src.positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                    float extent = Mathf.Max(max.x - min.x, Mathf.Max(max.y - min.y, max.z - min.z));
                    float blocksAcross = config.fruitSize * type.sizeScale / config.fruitBlockSize;
                    float c0 = extent / blocksAcross;
                    int baseSlot = palette.Count;
                    var result = VoxelMesher.Build(src, new Vector3(c0, c0, c0), model.name + "_Vox", config.fruitColors, i => PaletteUv(baseSlot + i));
                    palette.AddRange(result.colors);

                    var mesh = SaveMesh(result.mesh, $"{OutDir}/Fruit/{model.name}_Vox.asset");
                    prefabs.Add(SaveFruitPrefab(model.name + "_Vox", mesh));
                    log.AppendLine($"{type.name}/{model.name}: {blocksAcross:F0} blocks across, {result.colors.Length} colours, {result.faces} faces");
                }
                bakes.Add((type, prefabs));
            }

            if (palette.Count > PaletteSide * PaletteSide) return $"palette overflow: {palette.Count} colours";
            WritePalette(PalettePath, palette);
            var paletteTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
            FruitVoxelMaterial().SetTexture("_BaseMap", paletteTex);

            foreach (var (type, prefabs) in bakes)
            {
                type.visualPrefabs = prefabs.ToArray();
                type.atlas = paletteTex;
                type.atlasFar = null; // the pile's dither material fades the same palette
                EditorUtility.SetDirty(type);
            }
            AssetDatabase.SaveAssets();
            log.Insert(0, $"{bakes.Count} fruit types, {palette.Count} palette colours\n");
            return log.ToString();
        }

        static Vector2 PaletteUv(int i) => new Vector2((i % PaletteSide + 0.5f) / PaletteSide, (i / PaletteSide + 0.5f) / PaletteSide);

        static void WritePalette(string path, List<Color> colors)
        {
            var tex = new Texture2D(PaletteSide, PaletteSide, TextureFormat.RGBA32, false);
            var pixels = Enumerable.Repeat(Color.magenta, PaletteSide * PaletteSide).ToArray();
            for (int i = 0; i < colors.Count; i++)
            {
                var c = colors[i];
                c.a = 1f;
                pixels[i] = c; // row (i / side) from the bottom, matching PaletteUv
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        /// <summary>A CPU-readable copy of a texture asset, read straight from its file (colour values as stored).</summary>
        static Texture2D Readable(Texture2D tex, Dictionary<Texture, Texture2D> cache)
        {
            if (tex == null) return null;
            if (cache.TryGetValue(tex, out var r)) return r;
            r = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            r.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(tex)));
            cache[tex] = r;
            return r;
        }

        static Material FruitVoxelMaterial()
        {
            var path = OutDir + "/Fruit_Voxel.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fruit_Voxel" };
            m.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject SaveFruitPrefab(string name, Mesh mesh)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = FruitVoxelMaterial();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{OutDir}/Fruit/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Saves a mesh asset, overwriting an existing one in place so references to it survive.</summary>
        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string Sanitize(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_'));
    }
}
