using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The calibration stage (Docs/DESIGN.md §9): look straight → head left → head right, each captured by holding still,
    /// then a "try it" step where the basket follows the new range, with Looks good / Redo.
    /// Left and right are stored separately, so a player who can turn further one way still reaches both edges.
    /// Also drives its own UI: instruction text, a hint line, a ring that fills around the face while holding,
    /// and the try-it buttons.
    /// </summary>
    public class CalibrationController : MonoBehaviour
    {
        enum Step { Idle, Centre, Left, Right, Try }

        [SerializeField] GameConfig config;
        [SerializeField] FaceTracker tracker;
        [SerializeField] FaceToBasketMapper mapper;
        [Tooltip("For calibrating head turn (GameConfig.steerBy).")]
        [SerializeField] FaceMesh mesh;

        [Header("UI")]
        [SerializeField] GameObject panel;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text hint;
        [SerializeField] RectTransform ring;
        [SerializeField] Image ringFill;
        [SerializeField] GameObject tryButtons;
        [Tooltip("Speaks the prompts in the auditory assist mode (M18).")]
        [SerializeField] Narrator narrator;

        /// <summary>Raised when calibration ends: (accepted, thenPlay).</summary>
        public event Action<bool, bool> Finished;
        public bool Running => step != Step.Idle;

        Step step = Step.Idle;
        bool thenPlay;
        CalibrationData working;
        CalibrationData previous;
        readonly List<(float time, float u)> history = new List<(float, float)>();
        float steadySince = -1f;
        float smoothedU;
        /// <summary>Time constant (s) of the smoothing the stillness check looks at.</summary>
        const float SteadySmoothing = 0.12f;

        public void Begin(bool startRoundAfter)
        {
            thenPlay = startRoundAfter;
            previous = mapper.Calibration;
            working = CalibrationData.Default;
            UiTransition.Set(panel, true);
            if (ByEar) { narrator.Quiet = false; narrator.SayNow("calib_intro"); }
            Enter(Step.Centre);
        }

        /// <summary>Spoken prompts: only with voice assist on.</summary>
        bool ByEar => narrator != null && Difficulty.Auditory;

        /// <summary>"Looks good": save and finish.</summary>
        public void Accept()
        {
            if (step != Step.Try) return;
            working.Save(Steering.Mode);
            mapper.Calibration = working;
            if (ByEar) narrator.SayNow("calib_done");
            Finish(true);
        }

        /// <summary>"Redo": start over from the centre pose.</summary>
        public void Redo()
        {
            if (step == Step.Idle) return;
            working = CalibrationData.Default;
            if (ByEar) narrator.SayNow("calib_redo");
            Enter(Step.Centre);
        }

        /// <summary>Back without saving; the previous calibration stays.</summary>
        public void Cancel()
        {
            if (step == Step.Idle) return;
            mapper.Calibration = previous;
            Finish(false);
        }

        /// <summary>The visitor has walked away: closes without saving and without going back to the walkthrough.</summary>
        public void Abort()
        {
            if (step == Step.Idle) return;
            mapper.Calibration = previous;
            step = Step.Idle;
            awaitingFive = false;
            UiTransition.Set(panel, false);
            mapper.ResetFilter();
        }

        /// <summary>The 5 key at a pose: the visitor knows the direction, so the lead-in can start. False if not waiting.</summary>
        public bool Affirm()
        {
            if (!awaitingFive || step == Step.Idle || step == Step.Try) return false;
            awaitingFive = false;
            narrator.Stop();
            return true;
        }

        void Finish(bool accepted)
        {
            step = Step.Idle;
            awaitingFive = false;
            UiTransition.Set(panel, false);
            mapper.ResetFilter();
            Finished?.Invoke(accepted, thenPlay);
        }

        void Enter(Step next)
        {
            step = next;
            history.Clear();
            steadySince = -1f;
            tryButtons.SetActive(next == Step.Try);
            ring.gameObject.SetActive(false);
            holdSpoken = false;
            leadInStart = -1f;
            // By ear: "좋아요!" for the pose just held, then the next prompt (queued after anything already speaking).
            if (ByEar)
            {
                if (next != Step.Centre) narrator.Say("calib_ok");
                string side = Turning ? "_turn" : "";
                narrator.Say(next == Step.Centre ? "calib_centre" : next == Step.Left ? "calib_left" + side : next == Step.Right ? "calib_right" + side : "calib_try");
                narrator.Say(next == Step.Try ? Narrator.Buttons : "calib_affirm_ask");
                awaitingFive = next != Step.Try;
            }
            switch (next)
            {
                case Step.Centre: title.text = Strings.Get("calib.centre"); break;
                case Step.Left: title.text = Strings.Get(Turning ? "calib.leftTurn" : "calib.left"); break;
                case Step.Right: title.text = Strings.Get(Turning ? "calib.rightTurn" : "calib.right"); break;
                case Step.Try:
                    title.text = Strings.Get("calib.try");
                    hint.text = ScoreRules.Explain(config);
                    mapper.Calibration = working;
                    mapper.ResetFilter();
                    break;
            }
        }

        void Update()
        {
            if (step == Step.Idle || step == Step.Try) return;

            // Voice assist: nothing counts until the visitor has heard the direction and pressed 5.
            if (awaitingFive)
            {
                hint.text = Strings.Get("voice.pressFive");
                ring.gameObject.SetActive(false);
                history.Clear();
                steadySince = -1f;
                return;
            }

            // A lead-in to read the prompt before the pose starts counting: 5 s for the centre, 3 s after that.
            // By ear it starts once the narrator has finished the prompt.
            // It starts once per pose, after the step's prompt, and never restarts (later lines, such as "hold it
            // there", must not set it back).
            if (leadInStart < 0f && !(ByEar && narrator.IsSpeaking)) leadInStart = Time.unscaledTime;
            float leadIn = step == Step.Centre ? config.calibLeadInCentre : config.calibLeadIn;
            float left = leadInStart < 0f ? leadIn : leadIn - (Time.unscaledTime - leadInStart);
            if (left > 0f)
            {
                hint.text = Strings.Get("calib.ready", Mathf.CeilToInt(left));
                ring.gameObject.SetActive(false);
                history.Clear();
                steadySince = -1f;
                return;
            }

            if (!HeadSignal.Has(config, tracker, mesh))
            {
                hint.text = Strings.Get("calib.looking");
                Prompt("calib_looking");
                ring.gameObject.SetActive(false);
                history.Clear();
                steadySince = -1f;
                return;
            }

            // Measured in the 3:4 play area (the same in either screen layout); the ring is drawn where the face is on screen.
            var face = tracker.PlayFaceRect;
            PlaceRing(CameraFrame.ToView(tracker.FaceRect));

            // Too far or too close makes tracking unreliable; don't count the hold until it's fixed.
            if (face.width < 0.12f) { Prompt("calib_closer"); Hold(false, Strings.Get("calib.closer")); return; }
            if (face.width > 0.45f) { Prompt("calib_farther"); Hold(false, Strings.Get("calib.farther")); return; }

            float raw = HeadSignal.Value(config, tracker, mesh);
            float now = Time.unscaledTime;
            // Stillness is judged on a lightly smoothed signal: the face detector works on a 128 px copy of the frame,
            // so its box wobbles by a pixel or two (about 0.02 of the play area) even when the head is still. The
            // smoothing takes that out and still shows any real movement.
            bool fresh = history.Count == 0;
            float u = fresh ? raw : Mathf.Lerp(smoothedU, raw, 1f - Mathf.Exp(-Time.unscaledDeltaTime / SteadySmoothing));
            smoothedU = u;
            history.Add((now, u));
            history.RemoveAll(h => now - h.time > config.steadyHoldTime);

            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            foreach (var h in history) { min = Mathf.Min(min, h.u); max = Mathf.Max(max, h.u); sum += h.u; }
            bool steady = max - min <= config.steadyTolerance * 2f;
            // The pose is recorded as the average over the hold, not the last sample.
            float held = sum / history.Count;

            float minRange = config.calibMinRangePerSide;
            switch (step)
            {
                case Step.Centre:
                    if (Hold(steady, Strings.Get("calib.holdStill"))) { working.centre = held; Enter(Step.Left); }
                    break;
                case Step.Left:
                    if (u > working.centre - minRange) { Prompt(Turning ? "calib_more_left_turn" : "calib_more_left"); Hold(false, Strings.Get(Turning ? "calib.moreLeftTurn" : "calib.moreLeft")); break; }
                    if (Hold(steady, Strings.Get("calib.holdThere"))) { working.left = held; Enter(Step.Right); }
                    break;
                case Step.Right:
                    if (u < working.centre + minRange) { Prompt(Turning ? "calib_more_right_turn" : "calib_more_right"); Hold(false, Strings.Get(Turning ? "calib.moreRightTurn" : "calib.moreRight")); break; }
                    if (Hold(steady, Strings.Get("calib.holdThere"))) { working.right = held; Enter(Step.Try); }
                    break;
            }
        }

        float leadInStart = -1f;
        bool awaitingFive;
        bool Turning => Steering.Mode == SteerBy.Turn;

        float lastPrompt = -10f;
        bool holdSpoken;

        /// <summary>By ear: a distance prompt, at most every few seconds.</summary>
        void Prompt(string key)
        {
            if (!ByEar || narrator.IsSpeaking || Time.unscaledTime - lastPrompt < 4f) return;
            lastPrompt = Time.unscaledTime;
            narrator.Say(key);
        }

        /// <summary>Advances the hold timer while steady; returns true once held long enough.</summary>
        bool Hold(bool steady, string message)
        {
            hint.text = message;
            if (!steady)
            {
                steadySince = -1f;
                SetRingProgress(0f);
                return false;
            }
            if (steadySince < 0f)
            {
                steadySince = Time.unscaledTime;
                // By ear: once per pose, tell the player to hold it there (the ring that shows progress can't be seen).
                if (ByEar && !holdSpoken && !narrator.IsSpeaking) { holdSpoken = true; narrator.Say("calib_hold"); }
            }
            float progress = Mathf.Clamp01((Time.unscaledTime - steadySince) / config.steadyHoldTime);
            SetRingProgress(progress);
            return progress >= 1f;
        }

        // ---- The hold ring: pixel art around the face --------------------------------------------------------------

        /// <summary>Canvas units per UI art pixel (the same as the buttons and text; ScreenLayout).</summary>
        const float ArtPixel = 4f;
        const int RingBand = 8;   // art pixels of fill between the outlines
        static readonly Color32 RingOutline = new Color32(0x1E, 0x22, 0x33, 0xE6);
        static readonly Color32 RingTrack = new Color32(0xFF, 0xF8, 0xEC, 0x8C);
        static readonly Color32 RingFill = new Color32(0xC2, 0x18, 0x5B, 0xFF);
        static readonly Color32 RingFillLight = new Color32(0xE8, 0x5A, 0x8E, 0xFF);

        RawImage ringImage;
        Texture2D ringTex;
        Color32[] ringPixels;
        int ringSize;
        float ringShown = -1f;
        // Per ring pixel: its index, how far round it is (0 at the top, clockwise), and whether it's outline, lit edge or band.
        int[] ringIndex;
        float[] ringAngle;
        byte[] ringKind;

        void Awake()
        {
            // The old disk images stay in the scene but no longer draw; the ring is a generated, point-filtered texture.
            foreach (var g in ring.GetComponentsInChildren<Graphic>(true)) g.enabled = false;
            ringImage = new GameObject("Pixel Ring", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            ringImage.transform.SetParent(ring, false);
            var rt = ringImage.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            ringImage.raycastTarget = false;
        }

        void OnDestroy()
        {
            if (ringTex != null) Destroy(ringTex);
        }

        /// <summary>
        /// Centres the progress ring on the face (a rect in HUD frame UV), about 1.45 face widths across. Its size is a
        /// whole number of art pixels and its centre sits on the art-pixel grid, so every pixel stays square.
        /// </summary>
        void PlaceRing(Rect face)
        {
            ring.gameObject.SetActive(true);
            var parent = (RectTransform)ring.parent;
            Vector2 area = parent.rect.size;
            var c = face.center;
            // A circle spanning `size` in u spans size × aspect in v (the frame is 9:16 or 16:9): the same canvas units.
            float size = Mathf.Max(face.width, face.height / ScreenLayout.Aspect) * 1.45f;
            int n = Mathf.Max(24, Mathf.RoundToInt(size * area.x / ArtPixel / 2f) * 2);
            ring.anchorMin = ring.anchorMax = Vector2.zero;
            ring.pivot = new Vector2(0.5f, 0.5f);
            ring.sizeDelta = new Vector2(n * ArtPixel, n * ArtPixel);
            ring.anchoredPosition = new Vector2(Mathf.Round(c.x * area.x / ArtPixel) * ArtPixel, Mathf.Round(c.y * area.y / ArtPixel) * ArtPixel);
            if (n != ringSize) BuildRing(n);
        }

        /// <summary>Works out which pixels of an n × n ring are outline, band and lit edge, and their angle round it.</summary>
        void BuildRing(int n)
        {
            ringSize = n;
            if (ringTex == null) ringTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Calibration Ring" };
            else ringTex.Reinitialize(n, n);
            ringImage.texture = ringTex;
            ringPixels = new Color32[n * n];
            var index = new List<int>();
            var angle = new List<float>();
            var kind = new List<byte>();
            float outer = n * 0.5f, inner = outer - RingBand - 2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - outer, dy = y + 0.5f - outer;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > outer || d <= inner) continue;
                    byte k = d > outer - 1f || d <= inner + 1f ? (byte)0 : d > outer - 2f ? (byte)1 : (byte)2;
                    float a = Mathf.Atan2(dx, dy) / (2f * Mathf.PI);
                    index.Add(y * n + x);
                    angle.Add(a < 0f ? a + 1f : a);
                    kind.Add(k);
                }
            ringIndex = index.ToArray();
            ringAngle = angle.ToArray();
            ringKind = kind.ToArray();
            ringShown = -1f;
            SetRingProgress(Mathf.Max(0f, ringFill != null ? ringFill.fillAmount : 0f), true);
        }

        /// <summary>Lights the band pixel by pixel, clockwise from the top, as the hold progresses (0…1).</summary>
        void SetRingProgress(float progress, bool force = false)
        {
            if (ringFill != null) ringFill.fillAmount = progress;
            if (ringTex == null || (!force && Mathf.Abs(progress - ringShown) < 0.001f)) return;
            ringShown = progress;
            System.Array.Clear(ringPixels, 0, ringPixels.Length);
            for (int i = 0; i < ringIndex.Length; i++)
            {
                byte k = ringKind[i];
                ringPixels[ringIndex[i]] = k == 0 ? RingOutline
                    : ringAngle[i] < progress ? (k == 1 ? RingFillLight : RingFill)
                    : RingTrack;
            }
            ringTex.SetPixels32(ringPixels);
            ringTex.Apply(false);
        }
    }
}
