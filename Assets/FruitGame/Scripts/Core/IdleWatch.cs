using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitGame
{
    /// <summary>
    /// What the booth does when nobody's there (Docs/DESIGN.md §3, M6 3.3). On the title, the buttons give way to a sample
    /// round playing itself, with a slow loop of invitations, after a while with nobody in front of the camera. On any screen of a visit, a visitor who has
    /// walked away gets a warning first, then the booth goes back to the title. A pause that runs too long ends the round.
    /// Every timeout warns timeoutWarning seconds ahead; any touch or key (or the face coming back) extends it.
    /// </summary>
    public class IdleWatch : MonoBehaviour
    {
        const float AttractLineTime = 4f;
        const int AttractLines = 3;

        GameStateMachine game;
        BasketInputRouter router;
        GameHUD hud;
        Narrator narrator;
        GameConfig config;

        GameState seen;
        float stateSince, lastInput, nextLine;
        bool warning, attract;
        int shownLeft = -1, line;

        public void Init(GameStateMachine g, BasketInputRouter r, GameHUD h, Narrator n, GameConfig c)
        {
            game = g;
            router = r;
            hud = h;
            narrator = n;
            config = c;
            seen = g.State;
            stateSince = lastInput = Time.unscaledTime;
        }

        static bool AnyInput()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            var pointer = Pointer.current;
            return pointer != null && pointer.press.wasPressedThisFrame;
        }

        void Update()
        {
            if (game == null) return;
            float now = Time.unscaledTime;
            if (game.State != seen)
            {
                seen = game.State;
                stateSince = now;
                ClearWarning();
                if (attract) EndAttract();
            }
            if (AnyInput())
            {
                lastInput = now;
                ClearWarning();
                if (attract) EndAttract();
            }
            float still = now - Mathf.Max(lastInput, stateSince);
            bool usingFace = router != null && router.UsingFace;
            float away = usingFace ? Mathf.Min(still, router.FaceMissingFor) : still;

            switch (game.State)
            {
                case GameState.Boot:
                    if (attract && usingFace && router.FaceMissingFor <= 0f) EndAttract();
                    else if (!attract && away >= config.attractAfter) BeginAttract();
                    if (attract && now >= nextLine) NextLine();
                    break;

                case GameState.Setup:
                case GameState.Calibrate:
                case GameState.Gate:
                case GameState.GameOver:
                case GameState.Photo:
                    // Played on keys (no camera or the numpad's steering): staff are at hand, and there's no way to tell.
                    if (!usingFace || away <= 0f) { ClearWarning(); break; }
                    Countdown(away - config.walkAwayAfter, "idle.warn", "idle_warn", game.AbandonVisit);
                    break;

                case GameState.Paused:
                    Countdown(still - (config.pauseLimit - config.timeoutWarning), "idle.pauseWarn", "pause_warn", game.EndRoundFromPause);
                    break;

                default:
                    ClearWarning();
                    break;
            }
        }

        /// <summary>`into` is how many seconds into the warning period we are; negative before it starts.</summary>
        void Countdown(float into, string textKey, string voiceKey, System.Action expire)
        {
            if (into < 0f) { ClearWarning(); return; }
            float left = config.timeoutWarning - into;
            if (left <= 0f)
            {
                ClearWarning();
                expire();
                return;
            }
            int whole = Mathf.CeilToInt(left);
            if (!warning)
            {
                warning = true;
                if (Difficulty.Auditory && narrator != null) narrator.SayNow(voiceKey);
            }
            if (whole != shownLeft)
            {
                shownLeft = whole;
                hud.ShowNotice(Strings.Get(textKey, whole));
            }
        }

        void ClearWarning()
        {
            if (!warning) return;
            warning = false;
            shownLeft = -1;
            hud.HideNotice();
        }

        void BeginAttract()
        {
            attract = true;
            line = -1;
            game.BeginDemo();
            hud.ShowStartMenu(false);
            NextLine();
        }

        void NextLine()
        {
            line = (line + 1) % AttractLines;
            nextLine = Time.unscaledTime + AttractLineTime;
            hud.ShowBanner(Strings.Get("attract." + (line + 1)));
        }

        void EndAttract()
        {
            attract = false;
            hud.HideBanner();
            game.EndDemo();
            if (game.State == GameState.Boot) hud.ShowStartMenu(true);
            lastInput = Time.unscaledTime;
        }
    }
}
