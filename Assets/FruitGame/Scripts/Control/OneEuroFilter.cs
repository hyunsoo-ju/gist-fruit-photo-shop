using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// One-Euro filter (Casiez et al. 2012): heavy smoothing when the signal is still, little lag when it moves fast.
    /// </summary>
    public class OneEuroFilter
    {
        public float MinCutoff;
        public float Beta;
        public float DerivativeCutoff;

        bool initialised;
        float x;
        float dx;

        public OneEuroFilter(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
        }

        public void Reset() => initialised = false;

        public float Filter(float value, float dt)
        {
            if (!initialised || dt <= 0f)
            {
                initialised = true;
                x = value;
                dx = 0f;
                return x;
            }

            float rawDx = (value - x) / dt;
            dx = Mathf.Lerp(dx, rawDx, Alpha(DerivativeCutoff, dt));
            float cutoff = MinCutoff + Beta * Mathf.Abs(dx);
            x = Mathf.Lerp(x, value, Alpha(cutoff, dt));
            return x;
        }

        static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-4f));
            return 1f / (1f + tau / dt);
        }
    }
}
