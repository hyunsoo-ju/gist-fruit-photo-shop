using UnityEngine;

namespace FruitGame
{
    public static class SaveData
    {
        /// <summary>The best is per day (M6 1.8), so a morning's visitors aren't measured against yesterday's.</summary>
        static string BestScoreKey => "FruitGame.BestScore." + System.DateTime.Now.ToString("yyyyMMdd");

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
