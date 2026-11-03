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
    /// Bakes the pixel font (Docs/DESIGN.md §11) as a static TMP font asset, so its atlas only changes when the game's
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
        const string ScenePath = "Assets/FruitGame/Scenes/Game.unity";
        const string ConfigPath = "Assets/FruitGame/Config/GameConfig.asset";

        [MenuItem("FruitGame/Fonts/Bake Pixel Font")]
        static void BakeMenu() => Bake();

        /// <param name="onlyIfChanged">Skip when the asset already holds exactly this set (e.g. a fresh clone's first import).</param>
        public static string Bake(bool onlyIfChanged = false)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null) return "no font asset at " + FontPath;

            var chars = new SortedSet<uint>();
            void Add(string s) { foreach (char c in s) if (c >= 0x20 && !char.IsSurrogate(c)) chars.Add(c); }
            foreach (var line in File.ReadAllLines(CharsPath))
                if (!line.StartsWith("#")) Add(line);
            foreach (var line in File.ReadAllLines(StringsPath))
                if (!line.StartsWith("#")) Add(line.Replace("\\n", ""));
            // Scene texts are YAML strings with \uXXXX escapes.
            foreach (Match m in Regex.Matches(File.ReadAllText(ScenePath), "m_text: \"((?:[^\"\\\\]|\\\\.)*)\""))
                Add(Regex.Unescape(m.Groups[1].Value));
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (config != null) Add(config.photoCaption);

            if (onlyIfChanged && font.atlasPopulationMode == AtlasPopulationMode.Static && IsBaked(font, chars))
                return "already up to date";

            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.ClearFontAssetData(true);
            font.TryAddCharacters(chars.ToArray(), out uint[] missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();

            string result = $"baked {font.characterTable.Count} characters into {font.atlasTextures.Length} atlas texture(s)";
            if (missing != null && missing.Length > 0)
                result += $"; not in the font: {new string(missing.Select(u => (char)u).ToArray())}";
            Debug.Log("Pixel font: " + result);
            return result;
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
                if (imported.Contains(StringsPath) || imported.Contains(CharsPath))
                    EditorApplication.delayCall += () => Bake(onlyIfChanged: true);
            }
        }
    }
}
