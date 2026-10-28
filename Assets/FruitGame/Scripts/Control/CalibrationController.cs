using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The calibration stage (Docs/DESIGN.md §6): look straight → head left → head right, each captured by holding still,
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
            Enter(Step.Centre);
        }

        /// <summary>"Looks good": save and finish.</summary>
        public void Accept()
        {
            if (step != Step.Try) return;
            working.Save();
            mapper.Calibration = working;
            Finish(true);
        }

        /// <summary>"Redo": start over from the centre pose.</summary>
        public void Redo()
        {
            if (step == Step.Idle) return;
            working = CalibrationData.Default;
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
                ring.gameObject.SetActive(false);
                history.Clear();
                steadySince = -1f;
                return;
            }

            var face = tracker.FaceRect;
            PlaceRing(face);

            // Too far or too close makes tracking unreliable; don't count the hold until it's fixed.
            if (face.width < 0.12f) { Hold(false, Strings.Get("calib.closer")); return; }
            if (face.width > 0.45f) { Hold(false, Strings.Get("calib.farther")); return; }

            float u = tracker.FaceCenter.x;
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
                    if (u > working.centre - minRange) { Hold(false, "A little further left"); break; }
                    if (Hold(steady, Strings.Get("calib.holdThere"))) { working.left = u; Enter(Step.Right); }
                    break;
                case Step.Right:
                    if (u < working.centre + minRange) { Hold(false, "A little further right"); break; }
                    if (Hold(steady, Strings.Get("calib.holdThere"))) { working.right = u; Enter(Step.Try); }
                    break;
            }
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
            if (steadySince < 0f) steadySince = Time.unscaledTime;
            float progress = Mathf.Clamp01((Time.unscaledTime - steadySince) / config.steadyHoldTime);
            ringFill.fillAmount = progress;
            return progress >= 1f;
        }

        /// <summary>Centres the progress ring on the face (webcam UV == HUD frame UV).</summary>
        void PlaceRing(Rect face)
        {
            ring.gameObject.SetActive(true);
            var c = face.center;
            // The frame is 3:4, so a circle spanning `size` in u spans size × 0.75 in v.
            float size = Mathf.Max(face.width, face.height / 0.75f) * 1.6f;
            ring.anchorMin = new Vector2(c.x - size * 0.5f, c.y - size * 0.375f);
            ring.anchorMax = new Vector2(c.x + size * 0.5f, c.y + size * 0.375f);
            ring.offsetMin = ring.offsetMax = Vector2.zero;
        }
    }
}
