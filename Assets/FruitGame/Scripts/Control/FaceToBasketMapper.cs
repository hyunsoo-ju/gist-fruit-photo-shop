using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Turns the tracked face position into a basket x (Docs/DESIGN.md §8, "Mapping, per frame"):
    /// per-side calibrated range × sensitivity → dead zone → soft clamp → One-Euro filter.
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

        /// <summary>Face u → t, where ±1 is the edge of the player's comfortable range on that side.</summary>
        float Normalise(float u)
        {
            var c = Calibration;
            float minRange = config.calibMinRangePerSide;
            float range = u < c.centre ? Mathf.Max(c.centre - c.left, minRange) : Mathf.Max(c.right - c.centre, minRange);
            float t = (u - c.centre) / range * config.sensitivity;

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
