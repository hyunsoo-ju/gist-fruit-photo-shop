using UnityEngine;

namespace FruitGame
{
    public enum EndCondition
    {
        Timer,
        Lives,
        TimerOrLives
    }

    /// <summary>The mode each visit starts in (staff Settings, Docs/DESIGN.md §6).</summary>
    public enum DefaultMode { Normal, Easy, Sound, Hard }

    /// <summary>What moves the basket (M6 2.3): sliding the head sideways, or turning it.</summary>
    public enum SteerBy { Slide, Turn }

    /// <summary>One screen (guest view, staff views on keys) or two (guest display + staff display).</summary>
    public enum ScreenMode
    {
        /// <summary>Two when a second display is connected (builds), or a Game view is set to Display 2 (Editor).</summary>
        Auto,
        One,
        /// <summary>Two even in the Editor, where the staff screen shows in a Game view set to Display 2.</summary>
        Two
    }

    /// <summary>
    /// Every gameplay tunable in one asset. See Docs/DESIGN.md §17 for what each value means.
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
        public float fallSpeedMax = 4.05f;
        public float driftCapStart = 0.7f;
        public float driftCapMax = 1.8f;
        [Tooltip("Exponent of the ease-in curve that ramps difficulty over the round (1 = linear).")]
        public float difficultyCurve = 1f;
        public Vector2 spinDegPerSec = new Vector2(120f, 240f);
        public float spawnXRange = 2.1f;
        public float spawnY = 3.6f;
        [Tooltip("Gap before the next fruit at the start of the round; it shrinks to nextFruitDelayEnd by the end.")]
        public float nextFruitDelay = 0.6f;
        public float nextFruitDelayEnd = 0.3f;
        [Tooltip("Visual diameter every fruit is normalised to.")]
        public float fruitSize = 0.7f;

        [Header("Play area")]
        public float wallX = 2.25f;
        public float rimY = -2.0f;
        public float missY = -2.6f;
        [Tooltip("Fruit below this y are removed.")]
        public float despawnY = -4.2f;

        [Header("Basket")]
        public float basketMouthWidth = 1.3f;
        public float basketXLimit = 1.55f;
        public float basketMaxSpeed = 10f;
        [Tooltip("The basket eases toward its target over about this long (SmoothDamp), so sudden head moves don't slam it into fruit.")]
        public float basketSmoothTime = 0.08f;
        public float basketHeight = 0.65f;
        [Tooltip("Bottom radius as a fraction of the mouth radius (the tub tapers).")]
        public float basketTaper = 0.75f;
        public float basketWallThickness = 0.08f;

        [Header("Fruit physics (after hitting the basket)")]
        [Tooltip("Downward acceleration once a fruit has bounced off the basket and is no longer on its scripted fall.")]
        public float knockedGravity = 9f;
        [Range(0f, 1f)] public float fruitBounciness = 0.45f;
        [Range(0f, 1f)] public float fruitFriction = 0.3f;
        [Tooltip("A knocked fruit's sideways speed is capped at this (m/s).")]
        public float knockedMaxSideSpeed = 2.2f;
        [Tooltip("A knocked fruit's upward speed is capped at this (m/s).")]
        public float knockedMaxUpSpeed = 2.5f;
        [Tooltip("Basket hits harder than the first value (m/s) bounce less, down to the bounce scale at the second.")]
        public Vector2 hardHitRange = new Vector2(2f, 7f);
        [Range(0.1f, 1f)] public float hardHitBounceScale = 0.35f;
        [Tooltip("A knocked fruit resting on or in the basket's mouth this long counts as caught (no fruit gets stuck there).")]
        public float restCatchTime = 0.35f;
        [Tooltip("A knocked fruit that hasn't landed in the basket or dropped past the miss line after this long counts as missed.")]
        public float knockedTimeout = 4f;

        [Header("Background fruit pile (Docs/DESIGN.md §12)")]
        [Tooltip("Centre depth of the pile container, between the person (z 2) and the bokeh (z 6).")]
        public float pileZ = 3.6f;
        [Tooltip("Container depth as a multiple of the largest fruit's diameter (one fruit deep, so it stacks like 2D).")]
        public float pileDepthFactor = 1.15f;
        public float pileFloorY = -3.6f;
        public float pileHalfWidth = 3.0f;
        public float pileSpawnY = 3.6f;
        [Tooltip("Safety cap on the background pile; it normally stops growing when it reaches the drop height. Nothing is removed.")]
        [Min(1)] public int pileMax = 400;
        public float pileFreezeSpeed = 0.05f;
        public float pileFreezeTime = 1.5f;

        [Header("Assist")]
        public float assistRadius = 2.6f;
        public float assistStrength = 7f;
        [Tooltip("Staff assist level: 0 off, 0.5 low, 1 normal. Scales the assist in every mode.")]
        [Range(0f, 1f)] public float assistScale = 1f;
        [Tooltip("The mode each visit starts in; Easy and Sound go back to it after a round / a visit.")]
        public DefaultMode defaultMode = DefaultMode.Normal;
        public float assistExtraSpeedCap = 1.6f;
        [Tooltip("Assist strength at the end of the round, as a fraction of its start (it fades over the round).")]
        [Range(0f, 1f)] public float assistEndLevel = 1f;
        [Tooltip("The assist follows a moving basket: at this sideways speed (m/s), or just after, it pulls at full strength.")]
        public float assistFullAtSpeed = 1.2f;
        [Tooltip("How long (s) the pull lingers after the basket stops.")]
        public float assistLinger = 0.6f;
        [Tooltip("Pull left for a basket that sits still (fraction of full).")]
        [Range(0f, 1f)] public float assistIdleLevel = 0.15f;

        [Header("Harder round (Docs/DESIGN.md §5, M16.1)")]
        [Tooltip("Fruit dropped per 10 s at the end of the round. The rate climbs in a straight line from the opening pace " +
                 "(one fall plus the gap, about 3.7 per 10 s) to this, so the round speeds up evenly.")]
        public float endDropsPer10s = 9f;
        [Tooltip("Most fruit in the air at once.")]
        [Min(1)] public int maxFruitInAir = 3;
        [Tooltip("Fruit drop at least this far (m) from the basket: the first value at the start, the second by the end " +
                 "(not in Easy), so a basket left in one place catches little.")]
        public Vector2 spawnAwayFromBasket = new Vector2(0.5f, 1.3f);
        [Tooltip("Golden fruit per round (faster, worth goldenPoints), at random times spread over the round.")]
        [Min(0)] public int goldenPerRound = 3;
        public float goldenSpeedFactor = 1.25f;
        public int goldenPoints = 3;
        [Tooltip("Bombs per round, at random times spread between bombAfterSeconds and the end (none in Easy or Sound mode).")]
        [Min(0)] public int bombsPerRound = 4;
        public float bombAfterSeconds = 15f;
        [Tooltip("No golden fruit or bomb is scheduled in the round's last this many seconds.")]
        public float specialsEndBefore = 5f;
        [Tooltip("Points lost for catching a bomb (the score never drops below 0).")]
        public int bombPenalty = 5;
        [Tooltip("From this many catches in a row, every catch scores comboMultiplier times.")]
        public int comboFrom = 5;
        public int comboMultiplier = 2;

        [Header("Hard (Docs/DESIGN.md §6, M6 2.6: faster and busier, never a smaller basket or a wider reach)")]
        public float hardFallSpeedMax = 5.2f;
        public float hardDriftCapMax = 2.1f;
        public float hardNextFruitDelayEnd = 0.2f;
        public float hardEndDropsPer10s = 13f;
        [Min(1)] public int hardMaxFruitInAir = 4;
        [Min(0)] public int hardBombsPerRound = 7;

        [Header("Auditory assist mode (Docs/DESIGN.md §11, M18)")]
        [Tooltip("Steady fall speed (m/s), no ramp.")]
        public float auditoryFallSpeed = 2.3f;
        public float auditoryDriftCap = 0.4f;
        [Tooltip("Pause after each fruit before the next (s).")]
        public float auditoryFruitDelay = 1f;
        [Tooltip("Assist in the auditory mode: always on (it doesn't need the basket moving), at this radius (m), strength (m/s²) and extra sideways speed cap (m/s).")]
        public float auditoryAssistRadius = 2.2f;
        public float auditoryAssistStrength = 5.5f;
        public float auditoryAssistExtraSpeedCap = 1.3f;

        [Header("Easy (the gentle round)")]
        public float easyFallSpeedMax = 3.8f;
        [Tooltip("Assist in Easy: the original gentle assist, always on, at this radius (m), strength (m/s²) and extra sideways speed cap (m/s).")]
        public float easyAssistRadius = 1.8f;
        public float easyAssistStrength = 4f;
        public float easyAssistExtraSpeedCap = 1f;
        public float easyDriftCapMax = 1.2f;

        [Header("Face control (Docs/DESIGN.md §8)")]
        [Range(0.6f, 1.6f)] public float sensitivity = 1f;
        [Tooltip("Slide: the face's sideways position. Turn: the head's yaw from the face mesh (staff setting while comparing).")]
        public SteerBy steerBy = SteerBy.Slide;
        [Tooltip("Turn: degrees of head turn that count like the whole width of the camera (signal = 0.5 + yaw / this).")]
        public float turnDegreesPerWidth = 100f;
        [Tooltip("Dead zone in normalised t (fraction of the calibrated range), so it scales with each player's range. 0 = none: the basket follows the head linearly through the centre.")]
        public float deadZone = 0f;
        [Tooltip("Where the face → basket mapping stops being linear and eases into the travel limit.")]
        [Range(0.5f, 0.99f)] public float softClampKnee = 0.8f;
        public float calibMinRangePerSide = 0.03f;
        [Tooltip("One-Euro filter on basket x (metres), Hz: lower = smoother at rest but slow, small moves lag. 3 suits a clean camera (was 1 for a noisy webcam).")]
        public float oneEuroMinCutoff = 3f;
        [Tooltip("One-Euro filter: higher = less lag when moving fast.")]
        public float oneEuroBeta = 1f;
        [Tooltip("Calibration: how still the face must be (± in webcam u) to count as holding a pose. Raise for tremor.")]
        public float steadyTolerance = 0.02f;
        [Tooltip("Calibration: how long a pose must be held.")]
        public float steadyHoldTime = 1f;
        [Tooltip("Calibration: seconds to read the prompt before the centre pose starts counting (after the narration, by ear).")]
        public float calibLeadInCentre = 5f;
        [Tooltip("Calibration: the same for the left and right poses.")]
        public float calibLeadIn = 3f;
        public float faceLostPauseDelay = 1f;
        public float faceReturnGrace = 1f;

        [Header("Nobody there (Docs/DESIGN.md §3, M6 3.3)")]
        [Tooltip("Seconds with nobody in front of the camera before the title shows its invitations.")]
        public float attractAfter = 30f;
        [Tooltip("The attract screen's sample round plays until this score, then fades out and starts again from 0.")]
        [Min(1)] public int demoScoreTarget = 200;
        [Tooltip("Seconds for each half of the sample round's fade (out, then back in).")]
        [Min(0f)] public float demoFade = 0.6f;
        [Tooltip("Seconds with no face and no touch on a visit's screens before the warning starts.")]
        public float walkAwayAfter = 20f;
        [Tooltip("Every timeout warns this many seconds ahead; any touch or key extends it.")]
        public float timeoutWarning = 20f;
        [Tooltip("A pause this long (seconds) ends the round.")]
        public float pauseLimit = 60f;

        [Header("Photo booth (Docs/DESIGN.md §14)")]
        [Tooltip("Saved cards older than this many days are deleted when the game starts (0 = keep them).")]
        [Min(0)] public int photoAutoDeleteDays = 14;
        [Tooltip("Caption along the top of the photo frame. {date} becomes today's date. Placeholder until the event name is known.")]
        public string photoCaption = "EVENT NAME · {date}";
        [Tooltip("Korean-style beauty filter on the player in the photo booth (0 = off, 1 = full).")]
        [Range(0f, 1f)] public float beautyStrength = 1f;
        [Tooltip("Photo booth face reshape: eye enlargement (0 = off). Needs the face mesh.")]
        [Range(0f, 1f)] public float bigEye = 0.4f;
        [Tooltip("Photo booth face reshape: V-line jaw slimming (0 = off). Needs the face mesh.")]
        [Range(0f, 1f)] public float faceSlim = 0.0f;
        [Tooltip("Skin smoothing strength (0 = none; over 1 = stronger still). Staff panel: Beauty.")]
        [Range(0f, 4f)] public float skinSmoothing = 1.4f;
        [Tooltip("Skin smoothing width: spacing in texels of the blur that finds the local skin tone (GPUPixel's 2; wider = softer).")]
        [Range(1f, 4f)] public float skinSmoothWidth = 3f;
        [Tooltip("Cheek blush strength (0 = off).")]
        [Range(0f, 0.3f)] public float blushStrength = 0.04f;
        [Tooltip("Blush tone: 0.5 = the base cool pink; lower = warmer (peach), higher = cooler (lilac).")]
        [Range(0f, 1f)] public float blushCoolness = 0.5f;
        [Tooltip("Lip tint strength (0 = off).")]
        [Range(0f, 1f)] public float lipStrength = 0.22f;
        [Tooltip("Lip tone: 0.5 = the base cool pink; lower = warmer (rose), higher = cooler (berry).")]
        [Range(0f, 1f)] public float lipCoolness = 0.5f;

        [Tooltip("Get it on your phone: share the photo link through a Cloudflare quick tunnel, an https link phones open on mobile data without joining any Wi-Fi (needs cloudflared and internet on this computer; works on Wi-Fi that keeps devices apart). Off = the local-network link, for phones on the same Wi-Fi. While the tunnel isn't up the local link is used. Staff panel: Printing.")]
        public bool shareOverInternet = true;
        [Tooltip("Path to the cloudflared program. Empty = look in the usual places (Homebrew, /usr/local/bin, Program Files), then PATH.")]
        public string cloudflaredPath = "";

        [Header("Voxel look (Docs/DESIGN.md §4, §16)")]
        [Tooltip("Classic: the pixel-art look. Realistic: full-resolution, physically based glossy blocks, a low sun from behind, sky lighting and reflections, volumetric beams, fog and cinematic post-processing (SceneLighting). Staff panel: Display.")]
        public LightingStyle lightingStyle = LightingStyle.Classic;
        [Tooltip("Block size in metres for fruit (falling and pile). Re-bake fruit after changing (FruitGame > Voxels).")]
        public float fruitBlockSize = 0.044f;
        [Tooltip("Block size in metres for the play layers: basket, foreground trees and plants.")]
        public float worldBlockSize = 0.063f;
        [Tooltip("Block size in metres for the far orchard.")]
        public float farBlockSize = 0.09f;
        [Tooltip("Colours per fruit after clean-up (the voxeliser snaps every block to one of these).")]
        [Range(2, 8)] public int fruitColors = 5;

        [Header("Screen layout (Docs/DESIGN.md §4)")]
        [Tooltip("Auto picks portrait 9:16 or landscape 16:9 from the window's shape.")]
        public OrientationSetting orientation = OrientationSetting.Auto;
        [Tooltip("Width of the play column at z = 0, crate walls included: the view's width in portrait.")]
        public float playColumnWidth = 5.4f;
        [Tooltip("Height of the play area at z = 0: the view's height in landscape.")]
        public float playHeight = 6.43f;
        [Tooltip("Screen pixels per UI art pixel on the 1080-pixel short side: 4 was the 3:4 build's size, 3 suits a full-size monitor.")]
        [Range(2, 4)] public int uiPixelScale = 3;

        [Header("Pixel render (Docs/DESIGN.md §4)")]
        [Tooltip("Game layers render at this fraction of the layout's reference size (1080 × 1920 or 1920 × 1080) and are scaled up with hard pixels.")]
        [Range(0.25f, 1f)] public float renderScale = 0.5f;
        [Tooltip("Dither toward the sky on the background pile (0 = full colour).")]
        [Range(0f, 1f)] public float pileDither = 0.5f;
        [Tooltip("Dither toward the sky on the far orchard. Re-run FruitGame > Scene > Build Orchard after changing.")]
        [Range(0f, 1f)] public float farDither = 0.55f;
        [Tooltip("Dark outline around the player, in pixels at 1080 wide (0 = off).")]
        public float playerOutline = 3f;
        public Color playerOutlineColor = new Color32(0x1E, 0x22, 0x33, 0xFF);

        [Header("Language (Docs/DESIGN.md §15)")]
        [Tooltip("Starting language; the staff panel's L flips between Korean and English at runtime.")]
        public Language language = Language.Korean;
        [Tooltip("The staff panel's language, separate from the game's (staff panel ▸ Display).")]
        public Language staffLanguage = Language.Korean;

        [Header("Screens (Docs/DESIGN.md §15)")]
        [Tooltip("Auto: the guest screen on the main display and the staff screen on a second one when it's connected. " +
                 "In the Editor, Auto uses two when a Game view is set to Display 2.")]
        public ScreenMode screens = ScreenMode.Auto;
        [Tooltip("Staff screen mirror refreshes per second (a copy of the guest screen).")]
        public float staffMirrorRate = 30f;

        [Header("Webcam")]
        [Tooltip("Part of the camera name to prefer (e.g. \"iPhone\"). Empty = first non-built-in camera, else any.")]
        public string preferredCameraName = "Symplectomorphism";
        [Tooltip("Show the camera mirrored, like a mirror (the usual for a selfie booth).")]
        public bool mirrorCamera = true;

        [Header("Printing (Docs/DESIGN.md §14, M17)")]
        [Tooltip("Printer name as the OS knows it (macOS: lpstat -p). Empty = the system default printer.")]
        public string printerName = "";
        [Tooltip("Paper size option for the print (the SELPHY's borderless postcard: Postcard.Fullbleed).")]
        public string printMedia = "Postcard.Fullbleed";
        [Tooltip("Copies per Print press (2 for pairs).")]
        [Range(1, 4)] public int printCopies = 1;
        [Tooltip("About how long one print takes, for the waiting message (the SELPHY postcard: about 40 s).")]
        public float printSeconds = 40f;

        [Header("Fallback input")]
        public float keyboardBasketSpeed = 7f;

        public float FallSpeedAt(float progress01) =>
            Difficulty.Auditory ? auditoryFallSpeed
                : Mathf.Lerp(fallSpeedStart, Difficulty.Easy ? easyFallSpeedMax : Difficulty.Hard ? hardFallSpeedMax : fallSpeedMax, Ease(progress01));

        public float DriftCapAt(float progress01) =>
            Difficulty.Auditory ? auditoryDriftCap
                : Mathf.Lerp(driftCapStart, Difficulty.Easy ? easyDriftCapMax : Difficulty.Hard ? hardDriftCapMax : driftCapMax, Ease(progress01));

        /// <summary>Gap before the next fruit: shrinks over the round (steady in Easy).</summary>
        public float NextFruitDelayAt(float progress01) =>
            Difficulty.Auditory ? auditoryFruitDelay
                : Difficulty.Easy ? nextFruitDelay : Mathf.Lerp(nextFruitDelay, Difficulty.Hard ? hardNextFruitDelayEnd : nextFruitDelayEnd, Ease(progress01));

        /// <summary>
        /// Time between drops (−1 in Easy, which waits for each fruit to land). The drop rate climbs in a straight line
        /// from the opening pace (one fruit's fall plus the gap, the same as one at a time) to endDropsPer10s, so the
        /// round speeds up by the same amount every few seconds; late on, the next fruit drops before the last lands.
        /// </summary>
        public float DropIntervalAt(float progress01)
        {
            if (Difficulty.Gentle) return -1f;
            float startRate = 1f / ((spawnY - rimY) / fallSpeedStart + nextFruitDelay);
            float endRate = Mathf.Max(startRate, (Difficulty.Hard ? hardEndDropsPer10s : endDropsPer10s) / 10f);
            return 1f / Mathf.Lerp(startRate, endRate, Mathf.Clamp01(progress01));
        }

        /// <summary>The assist's reach (m) for the current mode.</summary>
        public float AssistRadius => Difficulty.Auditory ? auditoryAssistRadius : Difficulty.Easy ? easyAssistRadius : assistRadius;
        /// <summary>The assist's strength (m/s²) for the current mode.</summary>
        public float AssistStrength => Difficulty.Auditory ? auditoryAssistStrength : Difficulty.Easy ? easyAssistStrength : assistStrength;
        /// <summary>How much faster than the drift cap the assist may push a fruit sideways (m/s), for the current mode.</summary>
        public float AssistExtraSpeedCap => Difficulty.Auditory ? auditoryAssistExtraSpeedCap : Difficulty.Easy ? easyAssistExtraSpeedCap : assistExtraSpeedCap;

        /// <summary>Assist strength factor: fades over the round (full in Easy).</summary>
        public float AssistLevelAt(float progress01) =>
            Difficulty.Gentle ? 1f : Mathf.Lerp(1f, assistEndLevel, Mathf.Clamp01(progress01));

        /// <summary>Most fruit in the air at once, for the current mode.</summary>
        public int MaxFruitInAir => Difficulty.Hard ? hardMaxFruitInAir : maxFruitInAir;
        /// <summary>Bombs per round, for the current mode (the gentle modes have none).</summary>
        public int BombsPerRound => Difficulty.Gentle ? 0 : Difficulty.Hard ? hardBombsPerRound : bombsPerRound;

        public bool UsesTimer => endCondition != EndCondition.Lives;
        public bool UsesLives => endCondition != EndCondition.Timer;

        float Ease(float t) => Mathf.Pow(Mathf.Clamp01(t), Mathf.Max(0.01f, difficultyCurve));
    }
}
