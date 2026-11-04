using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Score plus caught/missed counts per fruit type for one round, the streak for the combo, and golden fruit and
    /// bombs caught.
    /// </summary>
    public class RoundStats
    {
        public int Score { get; private set; }
        public int TotalCaught { get; private set; }
        public int TotalMissed { get; private set; }
        public int Streak { get; private set; }
        public int GoldenCaught { get; private set; }
        public int BombsCaught { get; private set; }
        /// <summary>Points rules (golden, combo, bomb); set once by the game.</summary>
        public GameConfig Config { get; set; }

        readonly Dictionary<FruitType, int> caught = new Dictionary<FruitType, int>();
        readonly Dictionary<FruitType, int> missed = new Dictionary<FruitType, int>();

        public event Action<int, int> ScoreChanged; // (newScore, delta)

        public IReadOnlyDictionary<FruitType, int> CaughtByType => caught;
        public IReadOnlyDictionary<FruitType, int> MissedByType => missed;
        /// <summary>(seconds into the round, score) after every catch, starting at (0, 0). Drives the score graph.</summary>
        public IReadOnlyList<Vector2> History => history;

        readonly List<Vector2> history = new List<Vector2>();

        public void Reset()
        {
            Score = 0;
            TotalCaught = 0;
            TotalMissed = 0;
            Streak = 0;
            GoldenCaught = 0;
            BombsCaught = 0;
            caught.Clear();
            missed.Clear();
            history.Clear();
            history.Add(Vector2.zero);
            ScoreChanged?.Invoke(0, 0);
        }

        public int RecordCatch(FruitType type, FallingFruit.Kind kind, float elapsed)
        {
            Streak++;
            int points = ScoreRules.PointsFor(type, kind, Streak, Config);
            Score += points;
            TotalCaught++;
            if (kind == FallingFruit.Kind.Golden) GoldenCaught++;
            caught[type] = caught.TryGetValue(type, out var n) ? n + 1 : 1;
            history.Add(new Vector2(elapsed, Score));
            ScoreChanged?.Invoke(Score, points);
            return points;
        }

        /// <summary>A caught bomb: loses bombPenalty points (never below 0) and breaks the streak. Returns the change.</summary>
        public int RecordBomb(float elapsed)
        {
            int loss = Mathf.Min(Score, Config.bombPenalty);
            Score -= loss;
            Streak = 0;
            BombsCaught++;
            history.Add(new Vector2(elapsed, Score));
            ScoreChanged?.Invoke(Score, -loss);
            return -loss;
        }

        public void RecordMiss(FruitType type)
        {
            TotalMissed++;
            Streak = 0;
            missed[type] = missed.TryGetValue(type, out var n) ? n + 1 : 1;
        }
    }
}
