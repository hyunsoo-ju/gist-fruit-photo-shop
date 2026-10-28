using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Takes the three action shots of the four-cut photo during the round (Docs/DESIGN.md §10). They're spaced evenly
    /// (a 60 s round: at 15, 30 and 45 s). Before each one a small 3·2·1 counter appears out of the way and the screen
    /// flashes lightly on every number; then the shutter sounds and the scene is captured as it is, mid-play. Everything
    /// follows the round timer, so it pauses with the game. The final cut is the photo booth's own shot after the round.
    /// </summary>
    public class ActionShots : MonoBehaviour
    {
        [SerializeField] GameStateMachine game;
        [SerializeField] PhotoBooth booth;
        [SerializeField] AudioFeedback audioFeedback;
        [Tooltip("Small counter text, somewhere unobtrusive.")]
        [SerializeField] TMP_Text counter;
        [Tooltip("Full-screen white image for the light flashes (not a raycast target).")]
        [SerializeField] Image flash;
        [SerializeField] int shots = 3;
        [SerializeField] int countdownSeconds = 3;
        [SerializeField, Range(0f, 1f)] float flashStrength = 0.3f;
        [SerializeField] float flashTime = 0.25f;

        int taken;
        int lastShown = -1;
        float lastElapsed;
        float flashT = -1f;

        void Start()
        {
            counter.gameObject.SetActive(false);
            flash.gameObject.SetActive(false);
        }

        void Update()
        {
            UpdateFlash();
            bool playing = game.State == GameState.Playing;
            float elapsed = game.Timer.Elapsed;

            // A new round (the timer went back to the start): forget the last round's shots.
            if (elapsed < lastElapsed - 0.5f || (game.State == GameState.Countdown && taken > 0))
            {
                taken = 0;
                booth.ClearActionShots();
            }
            lastElapsed = elapsed;

            if (!playing || taken >= shots)
            {
                if (counter.gameObject.activeSelf) counter.gameObject.SetActive(false);
                lastShown = -1;
                return;
            }

            float at = game.Timer.Duration * (taken + 1) / (shots + 1);
            float left = at - elapsed;
            if (left > countdownSeconds)
            {
                if (counter.gameObject.activeSelf) counter.gameObject.SetActive(false);
                return;
            }

            if (left > 0f)
            {
                int shown = Mathf.CeilToInt(left);
                if (shown != lastShown)
                {
                    lastShown = shown;
                    counter.gameObject.SetActive(true);
                    counter.text = shown.ToString();
                    Flash();
                }
                return;
            }

            // Shutter.
            counter.gameObject.SetActive(false);
            lastShown = -1;
            if (audioFeedback != null) audioFeedback.PlayShutter();
            booth.TakeActionShot();
            taken++;
        }

        void Flash()
        {
            flashT = 0f;
            flash.gameObject.SetActive(true);
        }

        void UpdateFlash()
        {
            if (flashT < 0f) return;
            flashT += Time.unscaledDeltaTime;
            float a = flashStrength * (1f - Mathf.Clamp01(flashT / flashTime));
            flash.color = new Color(1f, 1f, 1f, a);
            if (a <= 0f) { flashT = -1f; flash.gameObject.SetActive(false); }
        }
    }
}
