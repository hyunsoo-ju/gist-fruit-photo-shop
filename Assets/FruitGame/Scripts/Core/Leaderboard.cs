using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The 랭킹 on the game over screen (Docs/DESIGN.md §15): the five best rounds, kept until staff reset it (staff
    /// panel ▸ Session), across days and restarts. Visitors don't type a name: each round gets a random one, an
    /// adjective and one of the game's fruit (용감한 수박 / Brave Watermelon), stored as the two words' numbers so it
    /// shows in whichever language the game is in. No two names on the board are the same.
    /// </summary>
    public static class Leaderboard
    {
        public const int Size = 5;
        const string Key = "FruitGame.Leaderboard";
        const int Adjectives = 20; // rank.adj.0 … rank.adj.19 in Strings.txt
        static readonly string[] Fruit =
        {
            "apple", "avocado", "banana", "cherries", "coconut", "dragonfruit", "grapefruit", "grapes",
            "lemon", "mango", "mangosteen", "melon", "orange", "pear", "pineapple", "watermelon",
        };

        /// <summary>The round's setting, as the 랭킹 shows it.</summary>
        public enum Mode { Normal, Easy, Hard, Auditory }

        [Serializable]
        public struct Entry
        {
            public int adjective, fruit, mode, score;
            public long id;

            public string Name => Strings.Get("rank.name", Strings.Get("rank.adj." + adjective),
                Strings.Get("fruit." + Fruit[Mathf.Clamp(fruit, 0, Fruit.Length - 1)]));

            public string ModeName => (Mode)mode switch
            {
                Mode.Easy => Strings.Get("walk.easy"),
                Mode.Hard => Strings.Get("walk.hard"),
                Mode.Auditory => Strings.Get("start.auditory"),
                _ => Strings.Get("walk.normal"),
            };
        }

        [Serializable]
        class Saved { public List<Entry> entries = new List<Entry>(); }

        static List<Entry> entries;

        public static event Action Changed;

        /// <summary>The board, best first (at most Size).</summary>
        public static IReadOnlyList<Entry> Entries
        {
            get { Load(); return entries; }
        }

        public static Mode CurrentMode =>
            Difficulty.Auditory ? Mode.Auditory : Difficulty.Easy ? Mode.Easy : Difficulty.Hard ? Mode.Hard : Mode.Normal;

        /// <summary>
        /// A new round: a random name not already on the board. Returns the visitor's entry and its place on the board
        /// (0 = first), or −1 if it didn't make the top five. A tie goes below the earlier score.
        /// </summary>
        public static (Entry entry, int rank) Submit(int score, Mode mode)
        {
            Load();
            var e = new Entry { mode = (int)mode, score = score, id = DateTime.Now.Ticks };
            for (int tries = 0; tries < 50; tries++)
            {
                e.adjective = UnityEngine.Random.Range(0, Adjectives);
                e.fruit = UnityEngine.Random.Range(0, Fruit.Length);
                if (!entries.Exists(o => o.adjective == e.adjective && o.fruit == e.fruit)) break;
            }
            if (score <= 0) return (e, -1);
            int rank = entries.FindIndex(o => score > o.score);
            if (rank < 0) rank = entries.Count;
            if (rank >= Size) return (e, -1);
            entries.Insert(rank, e);
            if (entries.Count > Size) entries.RemoveRange(Size, entries.Count - Size);
            Save();
            return (e, rank);
        }

        /// <summary>Staff panel: an empty board.</summary>
        public static void Clear()
        {
            Load();
            entries.Clear();
            Save();
        }

        static void Load()
        {
            if (entries != null) return;
            entries = new List<Entry>();
            var json = PlayerPrefs.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var saved = JsonUtility.FromJson<Saved>(json);
                if (saved?.entries != null) entries = saved.entries;
            }
            catch (Exception e) { Debug.LogWarning("Leaderboard: couldn't read the saved board (" + e.Message + ")"); }
        }

        static void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Saved { entries = entries }));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
