using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    public enum Language
    {
        Korean,
        English
    }

    /// <summary>
    /// Every player-facing string, in Korean (in-game tone: 해요체) and English (Docs/DESIGN.md §11). The table is
    /// Resources/Strings.txt: one line per key, "key, Korean, English" separated by tabs, "\n" for a new line and
    /// {0}, {1} for values. Load() runs on the main thread at startup (LanguageSwitch); after that, reads are safe from
    /// any thread (the phone page is built on the photo server's thread).
    /// </summary>
    public static class Strings
    {
        static Dictionary<string, string[]> table;

        public static Language Current { get; private set; } = Language.Korean;

        /// <summary>Raised when the language changes, so on-screen labels can refresh.</summary>
        public static event Action Changed;

        public static void Load()
        {
            if (table != null) return;
            table = new Dictionary<string, string[]>();
            var asset = Resources.Load<TextAsset>("Strings");
            if (asset == null) { Debug.LogWarning("[FruitGame] Resources/Strings.txt is missing"); return; }
            foreach (var raw in asset.text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var cols = line.Split('\t');
                if (cols.Length < 3) continue;
                table[cols[0]] = new[] { cols[1].Replace("\\n", "\n"), cols[2].Replace("\\n", "\n") };
            }
        }

        public static void SetLanguage(Language language)
        {
            Load();
            if (Current == language) return;
            Current = language;
            Changed?.Invoke();
        }

        /// <summary>The string for key in the current language (the key itself if it's missing), with values filled in.</summary>
        public static string Get(string key, params object[] args)
        {
            Load();
            if (!table.TryGetValue(key, out var pair)) return key;
            string s = pair[(int)Current];
            return args != null && args.Length > 0 ? string.Format(s, args) : s;
        }

        /// <summary>A fruit's name in the current language (falls back to its English display name).</summary>
        public static string Fruit(FruitType type)
        {
            if (type == null) return "";
            Load();
            return table.TryGetValue("fruit." + type.id, out var pair) ? pair[(int)Current] : type.displayName;
        }
    }
}
