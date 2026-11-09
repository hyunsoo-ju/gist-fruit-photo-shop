using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Bakes the pixel font (Docs/DESIGN.md §15) as a static TMP font asset, so its atlas only changes when the game's
    /// text does. A dynamic asset adds glyphs whenever text is drawn and Unity clears them again, which kept the asset
    /// changing in git. The baked set is PixelFontCharacters.txt (ASCII, symbols, the 2,350 common Hangul syllables)
    /// plus every character in Resources/Strings.txt, the Game scene's texts and the photo caption. Re-bakes on its
    /// own when Strings.txt or the character list changes.
    /// </summary>
    public static class PixelFontBaker
    {
        const string FontPath = "Assets/FruitGame/Art/Fonts/NeoDunggeunmo Pixel.asset";
        const string CharsPath = "Assets/FruitGame/Art/Fonts/PixelFontCharacters.txt";
        const string StringsPath = "Assets/FruitGame/Resources/Strings.txt";
        const string LinesPath = "Assets/FruitGame/Resources/NarrationLines.txt";
        const int AtlasSize = 2048;
        const string ScenePath = "Assets/FruitGame/Scenes/Game.unity";
        const string ConfigPath = "Assets/FruitGame/Config/GameConfig.asset";

        [MenuItem("FruitGame/Fonts/Bake Pixel Font")]
        static void BakeMenu() => Bake();

        /// <param name="onlyIfChanged">Skip when the asset already holds exactly this set (e.g. a fresh clone's first import).</param>
        public static string Bake(bool onlyIfChanged = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) return "no font asset at " + FontPath;

            var chars = WantedCharacters();

            if (onlyIfChanged && font.atlasPopulationMode == AtlasPopulationMode.Static && font.atlasTextures.Length == 1 && IsBaked(font, chars))
                return "already up to date";

            // One atlas only: glyphs on a second atlas are drawn by a separate sub-mesh, which lost the text's own
            // material and showed up as odd single letters (a blurred 주 in a sticker line).
            var so = new SerializedObject(font);
            so.FindProperty("m_AtlasWidth").intValue = AtlasSize;
            so.FindProperty("m_AtlasHeight").intValue = AtlasSize;
            so.ApplyModifiedPropertiesWithoutUndo();
            font.isMultiAtlasTexturesEnabled = false;
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.ClearFontAssetData(true);
            font.TryAddCharacters(chars.ToArray(), out uint[] missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            foreach (var t in font.atlasTextures) if (t != null) { t.filterMode = FilterMode.Point; EditorUtility.SetDirty(t); }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            string result = $"baked {font.characterTable.Count} characters into {font.atlasTextures.Length} atlas texture(s)";
            if (missing != null && missing.Length > 0)
                result += $"; not in the font: {new string(missing.Select(u => (char)u).ToArray())}";
            Debug.Log("Pixel font: " + result);
            return result;
        }

        static SortedSet<uint> WantedCharacters(bool includeCommonList = true)
        {
            var chars = new SortedSet<uint>();
            void Add(string s) { foreach (char c in s) if (c >= 0x20 && !char.IsSurrogate(c)) chars.Add(c); }
            if (includeCommonList)
                foreach (var line in File.ReadAllLines(CharsPath))
                    if (!line.StartsWith("#")) Add(line);
            foreach (var line in File.ReadAllLines(StringsPath))
                if (!line.StartsWith("#")) Add(line.Replace("\\n", ""));
            // The voice's captions are drawn in this font too.
            foreach (var line in File.ReadAllLines(LinesPath))
                if (!line.StartsWith("#")) Add(line.Substring(line.IndexOf('\t') + 1));
            // Scene texts are YAML strings with \uXXXX escapes.
            foreach (Match m in Regex.Matches(File.ReadAllText(ScenePath), "m_text: \"((?:[^\"\\\\]|\\\\.)*)\""))
                Add(Regex.Unescape(m.Groups[1].Value));
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null) Add(config.photoCaption);
            return chars;
        }

        /// <summary>
        /// Checks everything that has broken the text before; run it after UI changes, and in play mode to include the
        /// text built at runtime. Lists problems in the Console and returns them.
        /// </summary>
        [MenuItem("FruitGame/Fonts/Check All Text")]
        public static string CheckAllText()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            var problems = new List<string>();
            if (font.atlasTextures.Length != 1) problems.Add($"font has {font.atlasTextures.Length} atlases (want 1)");
            foreach (var t in font.atlasTextures)
                if (t != null && t.filterMode != FilterMode.Point) problems.Add($"atlas '{t.name}' is not point-filtered");
            var have = new HashSet<uint>(font.characterTable.Select(c => c.unicode));
            var lacking = WantedCharacters(false).Where(u => !have.Contains(u)).Select(u => (char)u).ToArray();
            if (lacking.Length > 0) problems.Add("characters not in the atlas: " + new string(lacking));
            foreach (Match m in Regex.Matches(File.ReadAllText(StringsPath), "<size=(\\d+)>"))
                if (int.Parse(m.Groups[1].Value) % 16 != 0) problems.Add("Strings.txt has an inline size that isn't a multiple of 16: " + m.Value);

            int checkedTexts = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (!t.gameObject.scene.IsValid()) continue;
                checkedTexts++;
                string name = t.transform.parent != null ? t.transform.parent.name + "/" + t.name : t.name;
                if (t.font != font) problems.Add($"{name}: uses font '{(t.font != null ? t.font.name : "none")}'");
                if (Mathf.Abs(t.fontSize / 16f - Mathf.Round(t.fontSize / 16f)) > 0.001f) problems.Add($"{name}: size {t.fontSize} isn't a multiple of 16");
                if (t.enableAutoSizing) problems.Add($"{name}: auto-sizing is on");
                var m = t.fontSharedMaterial;
                if (m == null || m.shader == null || m.shader.name != "FruitGame/Pixel Text") problems.Add($"{name}: material '{(m != null ? m.name : "none")}' doesn't use the pixel text shader");
            }
            string report = $"Text check: {checkedTexts} texts, {have.Count} glyphs, {problems.Count} problem(s)";
            foreach (var p in problems) report += "\n  " + p;
            Debug.Log(report);
            return report;
        }

        // The asset holds exactly the wanted characters, apart from ones the source font doesn't have.
        static bool IsBaked(TMP_FontAsset font, SortedSet<uint> chars)
        {
            var have = new HashSet<uint>(font.characterTable.Select(c => c.unicode));
            if (!have.IsSubsetOf(chars)) return false;
            var lacking = chars.Where(u => !have.Contains(u)).ToList();
            if (lacking.Count == 0) return true;
            // The asset only keeps its source font as a GUID in the Editor.
            string guid = new SerializedObject(font).FindProperty("m_SourceFontFileGUID").stringValue;
            var source = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
            if (source == null || FontEngine.LoadFontFace(source) != FontEngineError.Success) return false;
            return lacking.All(u => !FontEngine.TryGetGlyphIndex(u, out _));
        }

        class Watcher : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (imported.Contains(StringsPath) || imported.Contains(CharsPath) || imported.Contains(LinesPath))
                    EditorApplication.delayCall += () => Bake(onlyIfChanged: true);
            }
        }
    }
}
