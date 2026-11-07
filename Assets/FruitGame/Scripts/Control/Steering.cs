namespace FruitGame
{
    /// <summary>
    /// How this visitor steers (Docs/DESIGN.md §8): chosen on the setup flow's first step (SetupFlow), and back to the
    /// staff default (GameConfig.steerBy, the staff panel) when the visit ends.
    /// </summary>
    public static class Steering
    {
        public static SteerBy Mode { get; set; } = SteerBy.Slide;

        public static void ResetTo(SteerBy mode) => Mode = mode;
    }
}
