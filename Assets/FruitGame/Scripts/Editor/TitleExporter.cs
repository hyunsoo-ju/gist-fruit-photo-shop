using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Exports the voxel title and icon as flat pixel-art PNGs (Docs/DESIGN.md §11) for the README, the photo frame,
    /// signage and the app icon. Each is rendered with an orthographic camera at 4 px per block (lit like the scene,
    /// transparent background), then scaled up with hard pixels. The app icon is 1024 × 1024 on the sky colour.
    /// </summary>
    public static class TitleExporter
    {
        const string OutDir = "Assets/FruitGame/Art/Title/Export";
        const string DocsImages = "Docs/images";
        const int PxPerBlock = 4;
        static readonly Color Sky = new Color(0.663f, 0.776f, 0.867f);

        [MenuItem("FruitGame/Title/Export PNGs")]
        public static string Export()
        {
            var sceneTitle = Object.FindAnyObjectByType<VoxelTitle>(FindObjectsInactive.Include);
            if (sceneTitle == null) return "no VoxelTitle in the scene";
            if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder(Path.GetDirectoryName(OutDir), "Export");
            var stage = new Vector3(0f, -2000f, 0f);

            // Title: a copy of the scene's title, built in place.
            var title = Object.Instantiate(sceneTitle.gameObject);
            title.transform.position = stage;
            var vt = title.GetComponent<VoxelTitle>();
            vt.Build();
            float block = new SerializedObject(sceneTitle).FindProperty("block").floatValue;
            var titlePng = Render(title, block, null, 3);
            Object.DestroyImmediate(title);

            // Icon on its own.
            var cells = VoxelTitle.CameraIconCells(out var size);
            var iconMat = new SerializedObject(sceneTitle).FindProperty("material").objectReferenceValue as Material;
            var mesh = BlockMesh.Build((x, y, z) => cells[x, y, z], size, 0.05f, -(Vector3)size * 0.025f, "Icon");
            var icon = new GameObject("IconExport", typeof(MeshFilter), typeof(MeshRenderer));
            icon.transform.position = stage + new Vector3(100f, 0f, 0f);
            icon.GetComponent<MeshFilter>().sharedMesh = mesh;
            icon.GetComponent<MeshRenderer>().sharedMaterial = iconMat;
            var iconPng = Render(icon, 0.05f, null, 5);
            var appIcon = Render(icon, 0.05f, Sky, 8, square: true); // 128 px × 8 = 1024 × 1024
            Object.DestroyImmediate(icon);
            Object.DestroyImmediate(mesh);

            var summary = $"Title {titlePng.width}x{titlePng.height}, Icon {iconPng.width}x{iconPng.height}, AppIcon {appIcon.width}x{appIcon.height}";
            Save(titlePng, OutDir + "/Title.png");
            Save(iconPng, OutDir + "/Icon.png");
            Save(appIcon, OutDir + "/AppIcon.png");
            Directory.CreateDirectory(DocsImages);
            File.Copy(OutDir + "/Title.png", DocsImages + "/title.png", true);
            File.Copy(OutDir + "/Icon.png", DocsImages + "/icon.png", true);
            AssetDatabase.Refresh();
            return summary;
        }

        /// <summary>Renders an object's bounds orthographically at PxPerBlock pixels per block, then upscales.</summary>
        static Texture2D Render(GameObject go, float block, Color? background, int upscale, bool square = false)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float pad = block * 2f;
            float w = b.size.x + pad * 2f, h = b.size.y + pad * 2f;
            if (square) w = h = 128f / PxPerBlock * block; // exactly 128 px
            int pw = Mathf.CeilToInt(w / block * PxPerBlock - 0.01f), ph = Mathf.CeilToInt(h / block * PxPerBlock - 0.01f);

            var camGo = new GameObject("ExportCamera", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = h * 0.5f;
            cam.aspect = w / h;
            cam.transform.position = new Vector3(b.center.x, b.center.y, b.min.z - 5f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = b.size.z + 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background ?? Color.clear;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var data = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;

            var rt = RenderTexture.GetTemporary(pw, ph, 24, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Point;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var small = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, pw, ph), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(camGo);

            // Hard-pixel upscale.
            var big = new Texture2D(pw * upscale, ph * upscale, TextureFormat.RGBA32, false);
            var src = small.GetPixels32();
            var dst = new Color32[big.width * big.height];
            for (int y = 0; y < big.height; y++)
            for (int x = 0; x < big.width; x++)
                dst[y * big.width + x] = src[(y / upscale) * pw + x / upscale];
            big.SetPixels32(dst);
            big.Apply();
            Object.DestroyImmediate(small);
            return big;
        }

        static void Save(Texture2D tex, string path)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
