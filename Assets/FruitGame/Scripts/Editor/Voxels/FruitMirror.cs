using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Makes a whole fruit from a model that's a half (Docs/DESIGN.md §16): finds the cut, the largest flat face, and
    /// adds a mirror image of the half across it, so the cut faces meet inside and only skin shows. Used for the dragon
    /// fruit, whose only model in the PolyOne pack is cut in half. The result is a prefab next to the source model (kept
    /// out of the public repo with it), used as the fruit type's source model for Bake Fruit.
    /// </summary>
    public static class FruitMirror
    {
        static readonly (string source, string type)[] Halves =
        {
            ("Assets/FruitGame/Art/Models/FruitsPolyOne/SM_Fruits_Dragonfruit_R2.fbx", "Assets/FruitGame/Config/FruitTypes/DragonFruit.asset"),
        };

        [MenuItem("FruitGame/Voxels/Make Whole Fruit From Halves")]
        public static string MakeAll()
        {
            var log = new System.Text.StringBuilder();
            foreach (var (source, typePath) in Halves)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                var type = AssetDatabase.LoadAssetAtPath<FruitType>(typePath);
                if (model == null || type == null) { log.AppendLine($"missing {source} or {typePath}"); continue; }
                var prefab = MakeWhole(model, out string info);
                type.sourceModels = new[] { prefab };
                EditorUtility.SetDirty(type);
                log.AppendLine($"{type.name}: {info}");
            }
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static GameObject MakeWhole(GameObject model, out string info)
        {
            var mf = model.GetComponentInChildren<MeshFilter>();
            var mesh = mf.sharedMesh;
            var (n, d, share) = CutPlane(mesh);
            info = $"cut plane n {n} d {d:F4} ({share:P0} of the surface)";

            // Reflect across the plane and flip the winding so the copy faces outward.
            var v = mesh.vertices;
            var nr = mesh.normals;
            var mv = new Vector3[v.Length];
            var mn = new Vector3[nr.Length];
            for (int i = 0; i < v.Length; i++) mv[i] = v[i] - 2f * (Vector3.Dot(n, v[i]) - d) * n;
            for (int i = 0; i < nr.Length; i++) mn[i] = nr[i] - 2f * Vector3.Dot(n, nr[i]) * n;
            var mirror = new Mesh { name = mesh.name + "_Mirror" };
            mirror.vertices = mv;
            if (mn.Length == mv.Length) mirror.normals = mn;
            mirror.uv = mesh.uv;
            mirror.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                mirror.SetTriangles(t, s);
            }
            mirror.RecalculateBounds();

            string dir = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(model));
            string baseName = model.name.Replace("_R2", "") + "_Whole";
            string meshPath = $"{dir}/{baseName}_Mirror.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mirror, meshPath);

            var root = new GameObject(baseName);
            var half = Object.Instantiate(model, root.transform);
            half.name = "Half";
            var other = new GameObject("Mirror", typeof(MeshFilter), typeof(MeshRenderer));
            other.transform.SetParent(half.transform, false);
            other.transform.localPosition = mf.transform.localPosition;
            other.transform.localRotation = mf.transform.localRotation;
            other.transform.localScale = mf.transform.localScale;
            other.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            other.GetComponent<MeshRenderer>().sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/{baseName}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>The largest group of coplanar triangles: the flat cut of a halved fruit.</summary>
        static (Vector3 n, float d, float share) CutPlane(Mesh mesh)
        {
            var v = mesh.vertices;
            var t = mesh.triangles;
            var groups = new Dictionary<(int, int, int, int), (float area, Vector3 n, float d)>();
            float total = 0f;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;
                if (area < 1e-10f) continue;
                var n = cross.normalized;
                float d = Vector3.Dot(n, (a + b + c) / 3f);
                total += area;
                var key = (Mathf.RoundToInt(n.x * 20f), Mathf.RoundToInt(n.y * 20f), Mathf.RoundToInt(n.z * 20f), Mathf.RoundToInt(d * 200f));
                groups.TryGetValue(key, out var g);
                // Area-weighted average normal and offset within the group.
                float w = g.area + area;
                groups[key] = (w, (g.n * g.area + n * area) / w, (g.d * g.area + d * area) / w);
            }
            var best = groups.Values.OrderByDescending(g => g.area).First();
            return (best.n.normalized, best.d, best.area / total);
        }
    }
}
