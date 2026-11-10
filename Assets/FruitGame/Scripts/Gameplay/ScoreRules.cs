namespace FruitGame
{
    /// <summary>
    /// The single place points are decided (Docs/DESIGN.md §5): a fruit scores its type's points (a golden one
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

        /// <summary>
        /// The rules line shown before the round (under the countdown and on the try-it screen): what golden fruit are
        /// worth and, when the current mode has bombs, to dodge them. Empty when a round has neither.
        /// </summary>
        public static string Explain(GameConfig config)
        {
            bool golden = config.goldenPerRound > 0, bombs = config.BombsPerRound > 0;
            if (golden && bombs) return Strings.Get("hud.rules", config.goldenPoints, config.bombPenalty);
            if (golden) return Strings.Get("hud.rulesGolden", config.goldenPoints);
            if (bombs) return Strings.Get("hud.rulesBombs", config.bombPenalty);
            return "";
        }
    }
}
