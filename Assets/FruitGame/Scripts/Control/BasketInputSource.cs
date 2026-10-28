using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Anything that can steer the basket: keyboard/mouse now, face tracking from M3.
    /// </summary>
    public abstract class BasketInputSource : MonoBehaviour
    {
        /// <summary>False when the source has nothing to say (e.g. face lost).</summary>
        public abstract bool HasTarget { get; }

        /// <summary>Desired basket x in world metres, already clamped to the basket's travel.</summary>
        public abstract float TargetX { get; }
    }
}
