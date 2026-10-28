using UnityEngine;

namespace FruitGame
{
    public enum EndCondition
    {
        Timer,
        Lives,
        TimerOrLives
    }

    /// <summary>
    /// Every gameplay tunable in one asset. See Docs/DESIGN.md §13 for what each value means.
    /// </summary>
    [CreateAssetMenu(menuName = "FruitGame/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        [Header("Round")]
        public EndCondition endCondition = EndCondition.Timer;
        [Min(1f)] public float roundDuration = 60f;
        [Min(1)] public int lives = 3;
        [Min(0)] public int countdownSeconds = 3;

        [Header("Falling fruit")]
        public float fallSpeedStart = 2.7f;
        public float fallSpeedMax = 3.8f;
        public float driftCapStart = 0.7f;
        public float driftCapMax = 1.2f;
        [Tooltip("Exponent of the ease-in curve that ramps difficulty over the round (1 = linear).")]
        public float difficultyCurve = 2f;
        public Vector2 spinDegPerSec = new Vector2(120f, 240f);
        public float spawnXRange = 2.1f;
        public float spawnY = 3.6f;
        public float nextFruitDelay = 0.6f;
        [Tooltip("Visual diameter every fruit is normalised to.")]
        public float fruitSize = 0.88f;

        [Header("Play area")]
        public float wallX = 2.25f;
        public float rimY = -2.0f;
        public float missY = -2.6f;
        [Tooltip("Fruit below this y are removed.")]
        public float despawnY = -4.2f;

        [Header("Basket")]
        public float basketMouthWidth = 1.6f;
        public float basketXLimit = 1.45f;
        public float basketMaxSpeed = 18f;
        public float basketHeight = 0.8f;
        [Tooltip("Bottom radius as a fraction of the mouth radius (the tub tapers).")]
        public float basketTaper = 0.75f;
        public float basketWallThickness = 0.08f;

        [Header("Fruit physics (after hitting the basket)")]
        [Tooltip("Downward acceleration once a fruit has bounced off the basket and is no longer on its scripted fall.")]
        public float knockedGravity = 9f;
        [Range(0f, 1f)] public float fruitBounciness = 0.45f;
        [Range(0f, 1f)] public float fruitFriction = 0.3f;
        [Tooltip("A knocked fruit that hasn't landed in the basket or dropped past the miss line after this long counts as missed.")]
        public float knockedTimeout = 4f;

        [Header("Background fruit pile (Docs/DESIGN.md §8)")]
        [Tooltip("Centre depth of the pile container, between the person (z 2) and the bokeh (z 6).")]
        public float pileZ = 3.6f;
        [Tooltip("Container depth as a multiple of the largest fruit's diameter (one fruit deep, so it stacks like 2D).")]
        public float pileDepthFactor = 1.15f;
        public float pileFloorY = -3.6f;
        public float pileHalfWidth = 3.0f;
        public float pileSpawnY = 3.6f;
        [Min(1)] public int pileMax = 40;
        public float pileFreezeSpeed = 0.05f;
        public float pileFreezeTime = 1.5f;

        [Header("Assist")]
        public float assistRadius = 1.8f;
        public float assistStrength = 4f;
        public float assistExtraSpeedCap = 1.0f;

        [Header("Face control (Docs/DESIGN.md §6)")]
        [Range(0.6f, 1.6f)] public float sensitivity = 1f;
        [Tooltip("Dead zone in normalised t (fraction of the calibrated range), so it scales with each player's range.")]
        public float deadZone = 0.04f;
        [Tooltip("Where the face → basket mapping stops being linear and eases into the travel limit.")]
        [Range(0.5f, 0.99f)] public float softClampKnee = 0.8f;
        public float calibMinRangePerSide = 0.03f;
        [Tooltip("One-Euro filter on basket x (metres): lower = smoother at rest.")]
        public float oneEuroMinCutoff = 1.2f;
        [Tooltip("One-Euro filter: higher = less lag when moving fast.")]
        public float oneEuroBeta = 0.4f;
        [Tooltip("Calibration: how still the face must be (± in webcam u) to count as holding a pose. Raise for tremor.")]
        public float steadyTolerance = 0.015f;
        [Tooltip("Calibration: how long a pose must be held.")]
        public float steadyHoldTime = 1f;
        public float faceLostPauseDelay = 1f;
        public float faceReturnGrace = 1f;

        [Header("Photo booth (Docs/DESIGN.md §10)")]
        [Tooltip("Caption along the top of the photo frame. {date} becomes today's date. Placeholder until the event name is known.")]
        public string photoCaption = "EVENT NAME · {date}";
        [Tooltip("Korean-style beauty filter on the player in the photo booth (0 = off, 1 = full).")]
        [Range(0f, 1f)] public float beautyStrength = 1f;
        [Tooltip("Photo booth face reshape: eye enlargement (0 = off). Needs the face mesh.")]
        [Range(0f, 1f)] public float bigEye = 0.4f;
        [Tooltip("Photo booth face reshape: V-line jaw slimming (0 = off). Needs the face mesh.")]
        [Range(0f, 1f)] public float faceSlim = 0.0f;

        [Header("Voxel look (Docs/DESIGN.md §3, §12)")]
        [Tooltip("Block size in metres for fruit (falling and pile). Re-bake fruit after changing (FruitGame > Voxels).")]
        public float fruitBlockSize = 0.044f;
        [Tooltip("Block size in metres for the play layers: basket, foreground trees and plants.")]
        public float worldBlockSize = 0.063f;
        [Tooltip("Block size in metres for the far orchard.")]
        public float farBlockSize = 0.09f;
        [Tooltip("Colours per fruit after clean-up (the voxeliser snaps every block to one of these).")]
        [Range(2, 8)] public int fruitColors = 5;

        [Header("Pixel render (Docs/DESIGN.md §3)")]
        [Tooltip("Game layers render at this fraction of 1080 x 1440 and are scaled up with hard pixels.")]
        [Range(0.25f, 1f)] public float renderScale = 0.5f;
        [Tooltip("Dither toward the sky on the background pile (0 = full colour).")]
        [Range(0f, 1f)] public float pileDither = 0.5f;
        [Tooltip("Dither toward the sky on the far orchard. Re-run FruitGame > Scene > Build Orchard after changing.")]
        [Range(0f, 1f)] public float farDither = 0.55f;
        [Tooltip("Dark outline around the player, in pixels at 1080 wide (0 = off).")]
        public float playerOutline = 3f;
        public Color playerOutlineColor = new Color32(0x1E, 0x22, 0x33, 0xFF);

        [Header("Language (Docs/DESIGN.md §11)")]
        [Tooltip("Starting language; F3 flips between Korean and English at runtime.")]
        public Language language = Language.Korean;

        [Header("Webcam")]
        [Tooltip("Part of the camera name to prefer (e.g. \"iPhone\"). Empty = first non-built-in camera, else any.")]
        public string preferredCameraName = "Symplectomorphism";

        [Header("Fallback input")]
        public float keyboardBasketSpeed = 7f;

        public float FallSpeedAt(float progress01) =>
            Mathf.Lerp(fallSpeedStart, fallSpeedMax, Ease(progress01));

        public float DriftCapAt(float progress01) =>
            Mathf.Lerp(driftCapStart, driftCapMax, Ease(progress01));

        public bool UsesTimer => endCondition != EndCondition.Lives;
        public bool UsesLives => endCondition != EndCondition.Timer;

        float Ease(float t) => Mathf.Pow(Mathf.Clamp01(t), Mathf.Max(0.01f, difficultyCurve));
    }
}
