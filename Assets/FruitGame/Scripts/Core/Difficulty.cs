using System;

namespace FruitGame
{
    /// <summary>
    /// The round's difficulty setting (Docs/DESIGN.md §6). Normal is the harder round from M16.1; Easy keeps the
    /// earlier gentle round (slower ramp, steady gaps, full assist, one fruit at a time, no bombs); Auditory is the
    /// auditory assist mode for blind and low-vision players (M18): gentler still, played by ear with a narrator.
    /// Staff toggle Easy with E and Auditory with A (or its keycap) on the title screen. Easy goes back to Normal when a
    /// round ends; Auditory when the visit ends (back on the title screen), so the narrator can guide the photo too.
    /// </summary>
    public static class Difficulty
    {
        static bool easy, auditory;

        public static bool Easy
        {
            get => easy;
            set
            {
                if (easy == value) return;
                easy = value;
                if (value) auditory = false;
                Changed?.Invoke();
            }
        }

        public static bool Auditory
        {
            get => auditory;
            set
            {
                if (auditory == value) return;
                auditory = value;
                if (value) easy = false;
                Changed?.Invoke();
            }
        }

        /// <summary>Easy or Auditory: one fruit at a time, steady gaps, full assist, no bombs, drops anywhere.</summary>
        public static bool Gentle => easy || auditory;

        public static event Action Changed;
    }
}
