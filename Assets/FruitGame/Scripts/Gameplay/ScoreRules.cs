namespace FruitGame
{
    /// <summary>
    /// The single place points are decided. Today every fruit is worth its type's points;
    /// combos, streaks or rarity bonuses go here later.
    /// </summary>
    public static class ScoreRules
    {
        public static int PointsFor(FruitType type, RoundStats stats)
        {
            return type != null ? type.points : 1;
        }
    }
}
