using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The "slight attractor with capped radius" from the plan: a gentle horizontal pull
    /// toward the basket mouth, only above the rim and only within assistRadius.
    /// </summary>
    public class BasketAssist : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] BasketController basket;
        [Tooltip("Multiplier for the Off / Low / Normal setting.")]
        [Range(0f, 1f)] public float level = 1f;

        /// <summary>Horizontal acceleration (m/s²) to apply to a fruit at this position.</summary>
        public float AccelerationFor(Vector2 fruitPos)
        {
            if (level <= 0f || fruitPos.y <= config.rimY) return 0f;

            var mouth = new Vector2(basket.X, config.rimY);
            float d = Vector2.Distance(fruitPos, mouth);
            if (d >= config.assistRadius) return 0f;

            float dx = mouth.x - fruitPos.x;
            if (Mathf.Abs(dx) < 0.01f) return 0f;
            return Mathf.Sign(dx) * config.assistStrength * level * (1f - d / config.assistRadius);
        }
    }
}
