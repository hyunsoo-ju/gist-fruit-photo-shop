namespace FruitGame
{
    /// <summary>
    /// The single place points are decided (Docs/DESIGN.md §4): a fruit scores its type's points (a golden one
    /// goldenPoints), doubled (comboMultiplier) from the comboFrom-th catch in a row; a caught bomb costs bombPenalty.
    /// </summary>
    public static class ScoreRules
    {
        /// <param name="streak">Catches in a row including this one.</param>
        public static int PointsFor(FruitType type, FallingFruit.Kind kind, int streak, GameConfig config)
        {
            int points = kind == FallingFruit.Kind.Golden ? config.goldenPoints : type != null ? type.points : 1;
            return IsCombo(streak, config) ? points * config.comboMultiplier : points;
        }

        public static bool IsCombo(int streak, GameConfig config) => config.comboFrom > 0 && streak >= config.comboFrom;
    }
}
