using System;
using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Score plus caught/missed counts per fruit type for one round.
    /// </summary>
    public class RoundStats
    {
        public int Score { get; private set; }
        public int TotalCaught { get; private set; }
        public int TotalMissed { get; private set; }
        public int Streak { get; private set; }

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
            caught.Clear();
            missed.Clear();
            history.Clear();
            history.Add(Vector2.zero);
            ScoreChanged?.Invoke(0, 0);
        }

        public int RecordCatch(FruitType type, float elapsed)
        {
            int points = ScoreRules.PointsFor(type, this);
            Score += points;
            TotalCaught++;
            Streak++;
            caught[type] = caught.TryGetValue(type, out var n) ? n + 1 : 1;
            history.Add(new Vector2(elapsed, Score));
            ScoreChanged?.Invoke(Score, points);
            return points;
        }

        public void RecordMiss(FruitType type)
        {
            TotalMissed++;
            Streak = 0;
            missed[type] = missed.TryGetValue(type, out var n) ? n + 1 : 1;
        }
    }
}
