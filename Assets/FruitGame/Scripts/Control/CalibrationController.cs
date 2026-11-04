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

        public void Begin(bool startRoundAfter)
        {
            thenPlay = startRoundAfter;
            previous = mapper.Calibration;
            working = CalibrationData.Default;
            panel.SetActive(true);
            if (ByEar) narrator.SayNow("calib_intro");
            Enter(Step.Centre);
        }

        bool ByEar => Difficulty.Auditory && narrator != null;

        /// <summary>"Looks good": save and finish.</summary>
        public void Accept()
        {
            if (step != Step.Try) return;
            working.Save();
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

        void Finish(bool accepted)
        {
            step = Step.Idle;
            panel.SetActive(false);
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
            // By ear: "좋아요!" for the pose just held, then the next prompt (queued after anything already speaking).
            if (ByEar)
            {
                if (next != Step.Centre) narrator.Say("calib_ok");
                narrator.Say(next == Step.Centre ? "calib_centre" : next == Step.Left ? "calib_left" : next == Step.Right ? "calib_right" : "calib_try");
            }
            switch (next)
            {
                case Step.Centre: title.text = Strings.Get("calib.centre"); break;
                case Step.Left: title.text = Strings.Get("calib.left"); break;
                case Step.Right: title.text = Strings.Get("calib.right"); break;
                case Step.Try:
                    title.text = Strings.Get("calib.try");
                    hint.text = "";
                    mapper.Calibration = working;
                    mapper.ResetFilter();
                    break;
            }
        }

        void Update()
        {
            if (step == Step.Idle || step == Step.Try) return;

            if (tracker == null || !tracker.HasFace)
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

            float u = face.center.x;
            float now = Time.unscaledTime;
            history.Add((now, u));
            history.RemoveAll(h => now - h.time > config.steadyHoldTime);

            float min = float.MaxValue, max = float.MinValue;
            foreach (var h in history) { min = Mathf.Min(min, h.u); max = Mathf.Max(max, h.u); }
            bool steady = max - min <= config.steadyTolerance * 2f;

            float minRange = config.calibMinRangePerSide;
            switch (step)
            {
                case Step.Centre:
                    if (Hold(steady, Strings.Get("calib.holdStill"))) { working.centre = u; Enter(Step.Left); }
                    break;
                case Step.Left:
                    if (u > working.centre - minRange) { Prompt("calib_more_left"); Hold(false, "A little further left"); break; }
                    if (Hold(steady, Strings.Get("calib.holdThere"))) { working.left = u; Enter(Step.Right); }
                    break;
                case Step.Right:
                    if (u < working.centre + minRange) { Prompt("calib_more_right"); Hold(false, "A little further right"); break; }
                    if (Hold(steady, Strings.Get("calib.holdThere"))) { working.right = u; Enter(Step.Try); }
                    break;
            }
        }

        float lastPrompt = -10f;
        bool holdSpoken;

        /// <summary>By ear: a distance prompt, at most every few seconds.</summary>
        void Prompt(string key)
        {
            if (!Difficulty.Auditory || narrator == null || narrator.IsSpeaking || Time.unscaledTime - lastPrompt < 4f) return;
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
                ringFill.fillAmount = 0f;
                return false;
            }
            if (steadySince < 0f)
            {
                steadySince = Time.unscaledTime;
                // By ear: once per pose, tell the player to hold it there (the ring that shows progress can't be seen).
                if (ByEar && !holdSpoken && !narrator.IsSpeaking) { holdSpoken = true; narrator.Say("calib_hold"); }
            }
            float progress = Mathf.Clamp01((Time.unscaledTime - steadySince) / config.steadyHoldTime);
            ringFill.fillAmount = progress;
            return progress >= 1f;
        }

        /// <summary>Centres the progress ring on the face (a rect in HUD frame UV).</summary>
        void PlaceRing(Rect face)
        {
            ring.gameObject.SetActive(true);
            var c = face.center;
            // A circle spanning `size` in u spans size × aspect in v (the frame is 9:16 or 16:9).
            float aspect = ScreenLayout.Aspect;
            float size = Mathf.Max(face.width, face.height / aspect) * 1.6f;
            ring.anchorMin = new Vector2(c.x - size * 0.5f, c.y - size * aspect * 0.5f);
            ring.anchorMax = new Vector2(c.x + size * 0.5f, c.y + size * aspect * 0.5f);
            ring.offsetMin = ring.offsetMax = Vector2.zero;
        }
    }
}
