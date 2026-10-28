namespace FruitGame
{
    /// <summary>
    /// Counts a round down. Only advances when ticked, so pausing is simply not ticking.
    /// </summary>
    public class RoundTimer
    {
        public float Duration { get; private set; }
        public float Remaining { get; private set; }
        public float Elapsed => Duration - Remaining;
        public float Progress01 => Duration > 0f ? Elapsed / Duration : 1f;
        public bool Expired => Remaining <= 0f;

        public void Reset(float duration)
        {
            Duration = duration;
            Remaining = duration;
        }

        public void Tick(float dt)
        {
            if (Remaining > 0f)
                Remaining = Remaining - dt > 0f ? Remaining - dt : 0f;
        }
    }
}
