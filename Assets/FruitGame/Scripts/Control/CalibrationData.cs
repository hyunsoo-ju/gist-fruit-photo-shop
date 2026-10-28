using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// A player's comfortable head range, in mirrored webcam u (0 = left edge, 1 = right edge). Left and right are
    /// kept separately so an asymmetric range still reaches both edges (Docs/DESIGN.md §6). Saved in PlayerPrefs.
    /// </summary>
    [System.Serializable]
    public struct CalibrationData
    {
        public float centre;
        public float left;
        public float right;

        const string Key = "FruitGame.Calibration";

        public static CalibrationData Default => new CalibrationData { centre = 0.5f, left = 0.35f, right = 0.65f };

        public static bool HasSaved => PlayerPrefs.HasKey(Key);

        public static CalibrationData Load()
        {
            if (!HasSaved) return Default;
            var data = JsonUtility.FromJson<CalibrationData>(PlayerPrefs.GetString(Key));
            return data.IsValid ? data : Default;
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public static void Clear() => PlayerPrefs.DeleteKey(Key);

        public bool IsValid => left < centre && centre < right && left >= 0f && right <= 1f;
    }
}
