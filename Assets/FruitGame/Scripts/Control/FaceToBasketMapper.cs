using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Turns the tracked face position into a basket x (Docs/DESIGN.md §8, "Mapping, per frame"):
    /// calibrated range (one smooth curve through the centre, no kink) × sensitivity → dead zone (0 by default) →
    /// soft clamp → One-Euro filter.
    /// The basket's own max speed (BasketController) is the final step.
    /// </summary>
    public class FaceToBasketMapper : BasketInputSource
    {
        [SerializeField] GameConfig config;
        [SerializeField] FaceTracker tracker;
        [Tooltip("For steering by head turn (GameConfig.steerBy).")]
        [SerializeField] FaceMesh mesh;

        public CalibrationData Calibration { get; set; }
        /// <summary>The normalised position before clamping (−1 … +1 inside the calibrated range); for debugging.</summary>
        public float T { get; private set; }

        public override bool HasTarget => HeadSignal.Has(config, tracker, mesh);
        public override float TargetX => targetX;

        OneEuroFilter filter;
        SteerBy loadedFor;
        float targetX;

        void Awake()
        {
            Steering.ResetTo(config.steerBy);
            Calibration = CalibrationData.Load(Steering.Mode);
            loadedFor = Steering.Mode;
            filter = new OneEuroFilter(config.oneEuroMinCutoff, config.oneEuroBeta);
        }

        public void ResetFilter() => filter.Reset();

        void Update()
        {
            FaceMesh.NeededForSteering = Steering.Mode == SteerBy.Turn;
            // Staff switched how to steer: use that way's own saved range.
            if (Steering.Mode != loadedFor)
            {
                loadedFor = Steering.Mode;
                Calibration = CalibrationData.Load(loadedFor);
                filter.Reset();
            }
            if (!HasTarget) return;

            float x = config.basketXLimit * SoftClamp(Normalise(HeadSignal.Value(config, tracker, mesh)));
            targetX = filter.Filter(x, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Face u → t, where ±1 is the edge of the player's comfortable range on that side. One curve
        /// t = lin·d + quad·d² (d = u − centre) through both calibrated edges, so the speed changes smoothly across the
        /// centre instead of jumping between the left and right scales; it's a straight line when the two sides match.
        /// Past an edge it carries on in a straight line at that edge's slope.
        /// </summary>
        float Normalise(float u)
        {
            var c = Calibration;
            float minRange = config.calibMinRangePerSide;
            float l = Mathf.Max(c.centre - c.left, minRange), r = Mathf.Max(c.right - c.centre, minRange);
            float d = u - c.centre;
            float t;
            // The curve only stays one-way while neither side is more than about 2.4× the other; past that, two lines.
            if (Mathf.Max(l, r) > 2.2f * Mathf.Min(l, r)) t = d / (d < 0f ? l : r);
            else
            {
                float k = l * r * (l + r);
                float lin = (l * l + r * r) / k, quad = (l - r) / k;
                if (d > r) t = 1f + (lin + 2f * quad * r) * (d - r);
                else if (d < -l) t = -1f + (lin - 2f * quad * l) * (d + l);
                else t = lin * d + quad * d * d;
            }
            t *= config.sensitivity;

            // Dead zone, rescaled so movement starts smoothly from 0 at its edge instead of jumping.
            float a = Mathf.Abs(t);
            float dz = config.deadZone;
            t = a <= dz ? 0f : Mathf.Sign(t) * (a - dz) / (1f - dz);
            T = t;
            return t;
        }

        /// <summary>Linear up to the knee, then eases toward ±1 so overshooting the range doesn't feel like a wall.</summary>
        float SoftClamp(float t)
        {
            float k = config.softClampKnee;
            float a = Mathf.Abs(t);
            if (a <= k) return t;
            float over = (a - k) / (1f - k);
            return Mathf.Sign(t) * (k + (1f - k) * (float)System.Math.Tanh(over));
        }
    }
}
