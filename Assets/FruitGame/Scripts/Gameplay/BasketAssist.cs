using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The assist (Docs/DESIGN.md §7): a horizontal pull toward the basket mouth, only above the rim and only within
    /// assistRadius. It follows a moving basket: full strength while the basket moves (or has just moved), fading to
    /// assistIdleLevel when it sits still, so reaching for fruit is helped and standing still doesn't pay off.
    /// </summary>
    public class BasketAssist : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BasketController basket;
        [Tooltip("Multiplier for the Off / Low / Normal setting.")]
        [Range(0f, 1f)] public float level = 1f;
        /// <summary>Fades over the round (set by the spawner from GameConfig.AssistLevelAt).</summary>
        [System.NonSerialized] public float roundLevel = 1f;

        float recentSpeed;

        /// <summary>0…1: how much the basket has been moving lately (speed, lingering assistLinger after it stops). In Easy
        /// and the auditory mode the assist is always on (1), like the original assist.</summary>
        public float Activity => Difficulty.Gentle ? 1f : Mathf.Lerp(config.assistIdleLevel, 1f, Mathf.Clamp01(recentSpeed / Mathf.Max(0.01f, config.assistFullAtSpeed)));

        void Update()
        {
            float speed = basket != null ? Mathf.Abs(basket.VelocityX) : 0f;
            recentSpeed = Mathf.Max(speed, recentSpeed * Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, config.assistLinger)));
        }

        /// <summary>Horizontal acceleration (m/s²) to apply to a fruit at this position.</summary>
        public float AccelerationFor(Vector2 fruitPos)
        {
            if (level <= 0f || fruitPos.y <= config.rimY) return 0f;
            float radius = config.AssistRadius;
            float strength = config.AssistStrength;

            var mouth = new Vector2(basket.X, config.rimY);
            float d = Vector2.Distance(fruitPos, mouth);
            if (d >= radius) return 0f;

            float dx = mouth.x - fruitPos.x;
            if (Mathf.Abs(dx) < 0.01f) return 0f;
            return Mathf.Sign(dx) * strength * level * roundLevel * Activity * (1f - d / radius);
        }
    }
}
