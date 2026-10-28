using UnityEngine;

namespace FruitGame
{
    public static class SaveData
    {
        const string BestScoreKey = "FruitGame.BestScore";

        public static int BestScore
        {
            get => PlayerPrefs.GetInt(BestScoreKey, 0);
            set
            {
                PlayerPrefs.SetInt(BestScoreKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Stores the score if it beats the best. Returns true on a new best.</summary>
        public static bool SubmitScore(int score)
        {
            if (score <= BestScore) return false;
            BestScore = score;
            return true;
        }
    }
}
