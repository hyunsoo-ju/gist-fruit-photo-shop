using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Bakes the voxel look (Docs/DESIGN.md §16) at the block sizes in GameConfig:
    /// - <b>Fruit</b>: every FruitType's source models become voxel prefabs coloured from one shared palette texture
    ///   (a few clean colours per fruit), set as the type's visual and atlas. The pile dithers the same palette.
    /// - <b>Scene</b>: every <see cref="VoxelSource"/> in the open scene gets a block mesh built from its original
    ///   mesh at its own scale, keeping its materials.
    /// - <b>Props</b>: the crate pack (crate and barrel for the walls, and the basin used as the basket) and the bomb become voxel prefabs at
    ///   set sizes, coloured from their textures through a shared prop palette, for the crate walls and the basket.
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
            foreach (var vs in Object.FindObjectsByType<VoxelSource>(FindObjectsInactive.Include))
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

        const string PropPalettePath = OutDir + "/PropPalette.png";
        const string PropModels = "Assets/FruitGame/Art/Models/Props";

        /// <summary>Prop model, its largest size in metres once baked, block size and colour count.</summary>
        static readonly (string model, float size, float block, int colors)[] Props =
        {
            ("Box", 0.7f, 0.044f, 5), ("barrel", 0.6f, 0.044f, 5), ("basin", 1.6f, 0.063f, 5), ("low_poly_bomb", 0.88f, 0.044f, 5),
        };

        [MenuItem("FruitGame/Voxels/Bake Props")]
        public static string BakeProps()
        {
            EnsureFolder(OutDir + "/Props");
            var palette = new List<Color>();
            var meshes = new List<(string name, Mesh mesh)>();
            var readable = new Dictionary<Texture, Texture2D>();
            var log = new System.Text.StringBuilder();
            foreach (var (modelName, size, block, colors) in Props)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{PropModels}/{modelName}.fbx");
                if (model == null) { log.AppendLine($"{modelName}: missing"); continue; }
                var src = new VoxelMesher.Source();
                var slots = new List<(Texture2D tex, Color tint)>();
                // Keep the import's root rotation (these FBX files are Z-up; the importer stands them up).
                var rootInv = Matrix4x4.Translate(-model.transform.position);
                foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var mats = mf.GetComponent<Renderer>().sharedMaterials;
                    int offset = src.positions.Count == 0 ? 0 : src.slotCount;
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                    {
                        var m = s < mats.Length ? mats[s] : null;
                        var tex = m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") as Texture2D : null;
                        slots.Add((Readable(tex, readable), m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white));
                    }
                    VoxelMesher.Add(src, mf.sharedMesh, rootInv * mf.transform.localToWorldMatrix, offset);
                }
                src.sampleColor = (uv, slot) =>
                {
                    var (tex, tint) = slots[Mathf.Clamp(slot, 0, slots.Count - 1)];
                    var c = (tex != null ? tex.GetPixelBilinear(uv.x, uv.y) : Color.white) * tint;
                    // The basin's rope handles are near-black burlap, which reads as holes in blocks: make them rope.
                    if (modelName == "basin" && c.grayscale < 0.25f) c = Rope;
                    // The bomb's grey body reads as stone; darken its unsaturated colours toward black (the fuse stays).
                    if (modelName == "low_poly_bomb")
                    {
                        Color.RGBToHSV(c, out _, out float sat, out _);
                        if (sat < 0.25f) c = new Color(c.r * 0.38f, c.g * 0.4f, c.b * 0.46f, c.a);
                    }
                    return c;
                };
                src.edit = PropEdit(modelName);
                var min = src.positions[0];
                var max = src.positions[0];
                foreach (var p in src.positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                float extent = Mathf.Max(max.x - min.x, Mathf.Max(max.y - min.y, max.z - min.z));
                float c0 = extent / (size / block);
                int baseSlot = palette.Count;
                var result = VoxelMesher.Build(src, new Vector3(c0, c0, c0), modelName + "_Vox", colors, i => PaletteUv(baseSlot + i));
                palette.AddRange(result.colors);
                // The mesh is in model units; scale it to metres so the prefab needs no scale.
                var mesh = result.mesh;
                var verts = mesh.vertices;
                float k = size / extent;
                for (int i = 0; i < verts.Length; i++) verts[i] *= k;
                mesh.vertices = verts;
                mesh.RecalculateBounds();
                meshes.Add((modelName, SaveMesh(mesh, $"{OutDir}/Props/{modelName}_Vox.asset")));
                log.AppendLine($"{modelName}: {size / block:F0} blocks across, {result.colors.Length} colours, {result.faces} faces");
            }
            if (palette.Count > PaletteSide * PaletteSide) return $"palette overflow: {palette.Count} colours";
            WritePalette(PropPalettePath, palette);
            var mat = PropVoxelMaterial();
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(PropPalettePath));
            foreach (var (name, mesh) in meshes)
            {
                var go = new GameObject(name + "_Vox", typeof(MeshFilter), typeof(MeshRenderer));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                if (name == "low_poly_bomb") AddFuseTip(go.transform, mesh);
                PrefabUtility.SaveAsPrefabAsset(go, $"{OutDir}/Props/{name}_Vox.prefab");
                Object.DestroyImmediate(go);
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static readonly Color Rope = new Color(0.72f, 0.56f, 0.36f);
        static readonly Color GrooveWood = new Color(0.42f, 0.26f, 0.12f);
        static readonly Color LidWood = new Color(0.66f, 0.42f, 0.22f);

        /// <summary>Per-prop block edits: plank grooves on the crate, a closed lid on the barrel.</summary>
        static System.Func<Vector3Int, Vector3Int, Vector3Int, bool, bool, int, (VoxelMesher.CellEdit, Color)> PropEdit(string modelName)
        {
            const int Frame = 2, Plank = 4;
            const int PosX = 1, NegX = 2, PosY = 4, PosZ = 16, NegZ = 32;
            var keep = (VoxelMesher.CellEdit.Keep, default(Color));
            switch (modelName)
            {
                case "Box":
                    // Hidden inside: solid dark wood, so carved grooves show depth. On each side and the top, a block-deep
                    // groove every few blocks between the frame borders, so the planks read.
                    return (c, min, max, solid, sampled, exposed) =>
                    {
                        if (!solid) return keep;
                        if (exposed == 0) return sampled ? keep : (VoxelMesher.CellEdit.Fill, GrooveWood);
                        bool Inner(int v, int lo, int hi) => v - lo >= Frame && hi - v >= Frame;
                        bool Groove(int v, int lo) => (v - lo - Frame) % Plank == Plank - 1;
                        var remove = (VoxelMesher.CellEdit.Remove, default(Color));
                        if (exposed == PosY)
                            return Inner(c.x, min.x, max.x) && Inner(c.z, min.z, max.z) && Groove(c.x, min.x) ? remove : keep;
                        bool sideX = exposed == PosX || exposed == NegX, sideZ = exposed == PosZ || exposed == NegZ;
                        if (!sideX && !sideZ) return keep; // edges and corners: the frame
                        int u = sideX ? c.z : c.x, ulo = sideX ? min.z : min.x, uhi = sideX ? max.z : max.x;
                        return Inner(u, ulo, uhi) && Inner(c.y, min.y, max.y) && Groove(u, ulo) ? remove : keep;
                    };
                case "barrel":
                    // The lid sits below the rim; fill up to the rim so it doesn't read as a hole.
                    return (c, min, max, solid, sampled, exposed) =>
                    {
                        if (solid || c.y < max.y - 2 || c.y > max.y) return keep;
                        float cx = (min.x + max.x) * 0.5f, cz = (min.z + max.z) * 0.5f;
                        float r = Mathf.Min(max.x - min.x, max.z - min.z) * 0.5f;
                        float dx = c.x - cx, dz = c.z - cz;
                        return dx * dx + dz * dz < r * r ? (VoxelMesher.CellEdit.Fill, LidWood) : keep;
                    };
                default:
                    return null;
            }
        }

        /// <summary>Marks the top of the bomb's fuse (its highest blocks) for the burning-fuse effect (BombFuse).</summary>
        static void AddFuseTip(Transform root, Mesh mesh)
        {
            var verts = mesh.vertices;
            float top = verts.Max(v => v.y);
            var tip = verts.Where(v => v.y >= top - 0.01f).Aggregate(Vector3.zero, (a, v) => a + v) / verts.Count(v => v.y >= top - 0.01f);
            var marker = new GameObject("Fuse Tip").transform;
            marker.SetParent(root, false);
            marker.localPosition = tip + Vector3.up * 0.02f;
        }

        static Material PropVoxelMaterial()
        {
            var path = OutDir + "/Props/Prop_Voxel.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Prop_Voxel" };
            m.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(m, path);
            return m;
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
