using System;

namespace FruitGame
{
    /// <summary>
    /// Fallback end condition. Always counts, but only ends the round when
    /// GameConfig.endCondition includes lives.
    /// </summary>
    public class LivesCounter
    {
        public int Max { get; private set; }
        public int Remaining { get; private set; }
        public bool Depleted => Remaining <= 0;

        public event Action<int> Changed;

        public void Reset(int max)
        {
            Max = max;
            Remaining = max;
            Changed?.Invoke(Remaining);
        }

        public void LoseOne()
        {
            if (Remaining <= 0) return;
            Remaining--;
            Changed?.Invoke(Remaining);
        }
    }
}
