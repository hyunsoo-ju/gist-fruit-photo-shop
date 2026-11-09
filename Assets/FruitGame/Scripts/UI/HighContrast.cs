using System;

namespace FruitGame
{
    /// <summary>The visitor's high-contrast toggle (Docs/DESIGN.md M6 6.9): white-on-black buttons and black plates behind text that sits on the scenery; the game stays visible.</summary>
    public static class HighContrast
    {
        static bool on;
        public static bool On
        {
            get => on;
            set { if (on == value) return; on = value; Changed?.Invoke(); }
        }
        public static event Action Changed;
    }
}
