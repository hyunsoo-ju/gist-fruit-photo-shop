using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// FruitGame > Build > Make App Icon (Docs/DESIGN.md §16): the app's icon, in the photocard's style. The voxel apple as
    /// a sticker (PhotoDecor.StickerArt: the card's own sticker look, cream backing and faint shadow) on a rounded square
    /// of the picnic check with the plates' edge, all on a 16 px art grid, 1024 × 1024 with macOS's margin around the
    /// square. The apple is rendered from the private fruit model, so the PNG is ignored by git (like the models) and
    /// made again whenever it's missing: BuildScript calls Ensure before every build. Set as the default icon.
    /// </summary>
    public static class AppIcon
    {
        public const string PathInProject = "Assets/FruitGame/Art/AppIcon/AppIcon.png";
        const string AppleType = "Assets/FruitGame/Config/FruitTypes/Apple.asset";

        const int Size = 1024, Art = 16, Grid = Size / Art; // 64 × 64 art pixels
        const int Margin = 6;                                // the square spans art pixels 6..57 (832 px, macOS's ~824)
        const int Corner = 11;                               // stepped corner radius, in art pixels
        const int Check = 8;                                 // picnic check squares, in art pixels
        const int AppleHeight = 33;                          // the apple sticker's height, in art pixels
        const float AppleAngle = -8f;
        // The first apple model, stood up (its stem is along -z) and turned a little, so the stem and leaf show on top.
        static readonly Vector3 AppleTurn = new Vector3(-100f, 20f, 0f);

        static readonly Color32 Cream = new Color32(255, 248, 236, 255);
        static readonly Color32 CheckMid = new Color32(253, 232, 218, 255);
        static readonly Color32 CheckDark = new Color32(250, 214, 196, 255);
        static readonly Color32 PlateEdge = new Color32(236, 214, 182, 255);
        static readonly Color32 Shadow = new Color32(120, 80, 40, 60);

        [MenuItem("FruitGame/Build/Make App Icon", priority = 20)]
        static void MakeMenu()
        {
            if (Make()) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(PathInProject));
        }

        /// <summary>Makes the icon if it's missing (before a build). False if it couldn't (no fruit models here).</summary>
        public static bool Ensure() => File.Exists(PathInProject) ? SetAsIcon() : Make();

        /// <summary>Draws the icon, saves it and sets it as the app's icon.</summary>
        public static bool Make()
        {
            var apple = AssetDatabase.LoadAssetAtPath<FruitType>(AppleType);
            if (apple == null || apple.visualPrefabs == null || apple.visualPrefabs.Length == 0 || apple.visualPrefabs[0] == null)
            {
                Debug.LogWarning("[FruitGame] App icon: the apple model isn't here (private assets, see the README), so the icon wasn't made.");
                return false;
            }
            var sticker = PhotoDecor.StickerArt(apple, AppleHeight, AppleAngle, false, null, 0, AppleTurn);
            if (sticker.px == null)
            {
                Debug.LogWarning("[FruitGame] App icon: the apple didn't render.");
                return false;
            }

            // Art pixels, y down.
            var art = new Color32[Grid * Grid];
            int lo = Margin, hi = Grid - 1 - Margin;
            for (int y = 0; y < Grid; y++)
                for (int x = 0; x < Grid; x++)
                {
                    if (!Inside(x, y, lo, hi)) continue;
                    bool edge = !Inside(x - 1, y, lo, hi) || !Inside(x + 1, y, lo, hi) || !Inside(x, y - 1, lo, hi) || !Inside(x, y + 1, lo, hi);
                    if (edge) { art[y * Grid + x] = PlateEdge; continue; }
                    // The check, centred on the square.
                    bool row = Mathf.FloorToInt((y - Grid / 2f) / Check) % 2 == 0;
                    bool col = Mathf.FloorToInt((x - Grid / 2f) / Check) % 2 == 0;
                    art[y * Grid + x] = row && col ? CheckDark : row || col ? CheckMid : Cream;
                }
            // The square's shadow, one art pixel down.
            for (int y = Grid - 1; y > 0; y--)
                for (int x = 0; x < Grid; x++)
                    if (art[y * Grid + x].a == 0 && Inside(x, y - 1, lo, hi)) art[y * Grid + x] = Shadow;

            // The apple, centred (a little high, as stickers sit above their shadow).
            int ox = (Grid - sticker.w) / 2, oy = (Grid - sticker.h) / 2;
            for (int y = 0; y < sticker.h; y++)
                for (int x = 0; x < sticker.w; x++)
                {
                    var c = sticker.px[y * sticker.w + x];
                    if (c.a == 0) continue;
                    int tx = ox + x, ty = oy + y;
                    if (tx < 0 || ty < 0 || tx >= Grid || ty >= Grid) continue;
                    art[ty * Grid + tx] = c.a == 255 ? c : Blend(art[ty * Grid + tx], c);
                }

            // Up to 1024 px (each art pixel 16 × 16), y up for Unity.
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    px[(Size - 1 - y) * Size + x] = art[(y / Art) * Grid + x / Art];
            tex.SetPixels32(px);
            tex.Apply(false);
            Directory.CreateDirectory(Path.GetDirectoryName(PathInProject));
            File.WriteAllBytes(PathInProject, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PathInProject, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(PathInProject) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = Size;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = true;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            Debug.Log($"[FruitGame] App icon: made {PathInProject}");
            return SetAsIcon();
        }

        static bool SetAsIcon()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(PathInProject);
            if (icon == null) return false;
            var current = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (current.Length == 1 && current[0] == icon) return true;
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>Inside the rounded square (corners stepped on the art grid).</summary>
        static bool Inside(int x, int y, int lo, int hi)
        {
            if (x < lo || y < lo || x > hi || y > hi) return false;
            int cx = x < lo + Corner ? lo + Corner : x > hi - Corner ? hi - Corner : x;
            int cy = y < lo + Corner ? lo + Corner : y > hi - Corner ? hi - Corner : y;
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= (Corner + 0.5f) * (Corner + 0.5f);
        }

        static Color32 Blend(Color32 under, Color32 over)
        {
            float a = over.a / 255f;
            if (under.a == 0) return over;
            return new Color32((byte)Mathf.Lerp(under.r, over.r, a), (byte)Mathf.Lerp(under.g, over.g, a), (byte)Mathf.Lerp(under.b, over.b, a), 255);
        }
    }
}
