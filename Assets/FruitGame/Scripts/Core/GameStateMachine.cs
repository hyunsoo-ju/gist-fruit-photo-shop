using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitGame
{
    public enum GameState
    {
        Boot,
        /// <summary>The walkthrough after Start: how to move, then difficulty (SetupFlow).</summary>
        Setup,
        Calibrate,
        Countdown,
        Playing,
        RoundEnding,
        Paused,
        GameOver,
        Photo,
        /// <summary>Voice assist: instructions read out, waiting for the visitor to press 5 (or 확인).</summary>
        Gate
    }

    /// <summary>
    /// Runs the round: start menu → setup (how to move, difficulty) → calibration → countdown → playing →
    /// (last fruit lands) → game over.
    /// Docs/DESIGN.md §3. Every transition is a public method so on-screen buttons (and later face gestures)
    /// can drive it; the keyboard shortcuts call the same methods. Calibrate is reserved for M3.
    /// </summary>
    public class GameStateMachine : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] FruitSpawner spawner;
        [SerializeField] BasketController basket;
        [SerializeField] FallbackInput fallbackInput;
        [SerializeField] GameHUD hud;
        [SerializeField] AudioFeedback audioFeedback;
        [SerializeField] FruitPile pile;
        [SerializeField] BasketInputRouter router;
        [SerializeField] CalibrationController calibration;
        [SerializeField] PhotoBooth photoBooth;
        [Tooltip("The walkthrough after Start: how to move and difficulty, before calibration.")]
        [SerializeField] SetupFlow setup;
        [Tooltip("The auditory assist mode's narrator (Docs/DESIGN.md §11, M18).")]
        [SerializeField] Narrator narrator;
        [Tooltip("Minimum time between basket-bump sounds, so a rattling fruit doesn't machine-gun them.")]
        [SerializeField] float bumpSoundCooldown = 0.12f;

        public GameState State { get; private set; } = GameState.Boot;
        public RoundStats Stats { get; } = new RoundStats();
        public RoundTimer Timer { get; } = new RoundTimer();
        public LivesCounter Lives { get; } = new LivesCounter();

        enum PauseReason { User, FaceLost }

        GameState stateBeforePause;
        PauseReason pauseReason;
        float faceBackSince = -1f;
        float countdownLeft;
        int lastCountdownShown;
        float lastBumpSound = -1f;
        int nextAnnouncement;
        static readonly int[] Announcements = { 45, 30, 15 };

        /// <summary>By ear: the time left and the score at 45, 30 and 15 seconds left.</summary>
        void AnnounceTime()
        {
            if (!Narrating || nextAnnouncement >= Announcements.Length) return;
            int at = Announcements[nextAnnouncement];
            if (Timer.Remaining > at) return;
            nextAnnouncement++;
            narrator.Say("time_" + at, Number("score_now_", Stats.Score));
        }

        static readonly Color32[] Explosion =
        {
            new Color32(0xFF, 0xE0, 0x6A, 0xFF), new Color32(0xFF, 0x9A, 0x2E, 0xFF), new Color32(0xE8, 0x4A, 0x2A, 0xFF),
            new Color32(0xFF, 0xF6, 0xD8, 0xFF),
        };
        static readonly Color32[] Smoke = { new Color32(0x5A, 0x5A, 0x62, 0xFF), new Color32(0x86, 0x86, 0x8C, 0xFF) };

        void OnEnable()
        {
            spawner.FruitResolved += HandleFruitResolved;
            spawner.FruitSpawned += HandleFruitSpawned;
            spawner.FruitHitBasket += HandleFruitHitBasket;
            spawner.Progress = () => Timer.Progress01;
            spawner.Elapsed = () => Timer.Elapsed;
            Stats.Config = config;
            if (calibration != null) calibration.Finished += HandleCalibrationFinished;
            if (setup != null) setup.Finished += HandleSetupFinished;
            if (photoBooth != null) photoBooth.Closed += ReturnToMenu;
            Stats.ScoreChanged += hud.SetScore;
            Lives.Changed += hud.SetLives;
        }

        void OnDisable()
        {
            spawner.FruitResolved -= HandleFruitResolved;
            spawner.FruitSpawned -= HandleFruitSpawned;
            spawner.FruitHitBasket -= HandleFruitHitBasket;
            if (calibration != null) calibration.Finished -= HandleCalibrationFinished;
            if (setup != null) setup.Finished -= HandleSetupFinished;
            if (photoBooth != null) photoBooth.Closed -= ReturnToMenu;
            Stats.ScoreChanged -= hud.SetScore;
            Lives.Changed -= hud.SetLives;
            Time.timeScale = 1f;
        }

        void Start()
        {
            hud.ShowLives(config.UsesLives);
            gameObject.AddComponent<IdleWatch>().Init(this, router, hud, narrator, config);
            ResetRound();
            EnterBoot();
        }

        /// <summary>Space/Enter: start from the menu, resume from pause, or play again.</summary>
        public void Confirm()
        {
            switch (State)
            {
                case GameState.Boot:
                case GameState.GameOver:
                    StartRound();
                    break;
                case GameState.Paused:
                    Resume();
                    break;
                case GameState.Setup:
                    setup.ChooseHighlighted();
                    break;
            }
        }

        /// <summary>
        /// Start on the title: the walkthrough (how to move, difficulty), then calibration, then the round; every
        /// visitor plays once, so everyone goes through it. Play again on the game-over screen goes straight to the
        /// countdown with the same choices (calibrating first if this way of steering has never been calibrated).
        /// </summary>
        public void StartRound()
        {
            if (State == GameState.Paused) Time.timeScale = 1f;
            if (State == GameState.Boot && setup != null)
            {
                ResetRound();
                SetState(GameState.Setup);
                hud.HideMessage();
                setup.Begin();
                return;
            }
            if (calibration != null && router != null && router.UsingFace && !CalibrationData.HasSaved(Steering.Mode))
            {
                BeginCalibration(true);
                return;
            }
            EnterCountdown();
        }

        /// <summary>"Take a photo" button on the game over screen: opens the photo booth with this round's pile.</summary>
        public void StartPhoto()
        {
            if (State != GameState.GameOver || photoBooth == null) return;
            SetState(GameState.Photo);
            photoBooth.Open(Stats.Score);
        }

        /// <summary>Calibrate button / C key: re-run calibration from the menu or game over screen.</summary>
        public void StartCalibration()
        {
            if (State != GameState.Boot && State != GameState.GameOver) return;
            BeginCalibration(false);
        }

        void BeginCalibration(bool thenPlay)
        {
            ResetRound();
            RunCalibration(thenPlay);
        }

        void RunCalibration(bool thenPlay)
        {
            SetState(GameState.Calibrate);
            hud.HideMessage();
            calibration.Begin(thenPlay);
        }

        System.Action gateGo;

        /// <summary>Voice assist: says the instructions, and nothing happens until the visitor presses 5.</summary>
        void Gate(System.Action go, params string[] keys)
        {
            SetState(GameState.Gate);
            gateGo = go;
            hud.ShowMessage(Strings.Get("voice.pressFive"), null);
            narrator.Quiet = false;
            narrator.SayNow(keys);
        }

        /// <summary>The 5 key: answers whichever "press 5" is waiting. False if none is.</summary>
        public bool AffirmIfWaiting()
        {
            if (State == GameState.Gate)
            {
                hud.HideMessage();
                gateGo();
                return true;
            }
            return State == GameState.Calibrate && calibration != null && calibration.Affirm();
        }

        /// <summary>The walkthrough's choices are made (calibrate, then play) or it was backed out of (title).</summary>
        void HandleSetupFinished(bool go)
        {
            if (!go) { EnterBoot(); return; }
            if (calibration != null && router != null && router.UsingFace) BeginCalibration(true);
            else EnterCountdown();
        }

        void HandleCalibrationFinished(bool accepted, bool thenPlay)
        {
            // Back out of calibration during the walkthrough: back to the difficulty step.
            if (!accepted && thenPlay && setup != null)
            {
                SetState(GameState.Setup);
                setup.ReturnTo(1);
                return;
            }
            if (accepted && thenPlay) EnterCountdown();
            else EnterBoot();
        }

        public void Pause()
        {
            if (State == GameState.Playing || State == GameState.Countdown || State == GameState.RoundEnding)
                EnterPause(PauseReason.User);
        }

        public void Resume()
        {
            if (State == GameState.Paused) ExitPause();
        }

        public void TogglePause()
        {
            if (State == GameState.Paused) ExitPause();
            else Pause();
        }

        /// <summary>The "소리로 하기" keycap / A on the title screen: the auditory assist mode, confirmed out loud.</summary>
        public void ToggleAuditory()
        {
            if (State != GameState.Boot) return;
            Difficulty.Auditory = !Difficulty.Auditory;
            if (narrator == null) return;
            narrator.Quiet = false;
            narrator.Click();
            if (Difficulty.Auditory) narrator.SayNow("mode_on", Narrator.Buttons);
            else narrator.SayNow("mode_off");
        }

        bool Narrating => Difficulty.Auditory && narrator != null;

        void Say(params string[] keys)
        {
            if (Narrating) narrator.Say(keys);
        }

        /// <summary>The numpad's 7 in the round: the time left and the score.</summary>
        public void SayRoundStatus()
        {
            if (narrator == null) return;
            narrator.SayNow(Number("time_left_", Mathf.CeilToInt(Timer.Remaining)), Number("score_now_", Stats.Score));
        }

        static string Number(string prefix, int n) => prefix + Mathf.Clamp(n, 0, 99);

        /// <summary>Main menu button: abandons the current round and goes back to the start screen.</summary>
        public void ReturnToMenu()
        {
            Time.timeScale = 1f;
            EndVisitSettings();
            ResetRound();
            EnterBoot();
        }

        /// <summary>The visit is over: the next player starts in the staff's default mode.</summary>
        void EndVisitSettings()
        {
            if (narrator != null) narrator.ResetVisit();
            if (router != null) router.EndVisit();
            HighContrast.On = false;
            Difficulty.ResetTo(config.defaultMode);
            Steering.ResetTo(config.steerBy);
        }

        /// <summary>The attract screen's sample round is playing (still on the title: any input or face ends it).</summary>
        public bool DemoRunning { get; private set; }
        DemoPilot pilot;
        BasketInputSource playerInput;

        /// <summary>Starts the silent self-playing round. False if the booth's default is voice assist, which it can't show.</summary>
        public bool BeginDemo()
        {
            if (State != GameState.Boot || DemoRunning) return false;
            EndVisitSettings();
            if (Difficulty.Auditory) return false;
            if (pilot == null)
            {
                pilot = gameObject.AddComponent<DemoPilot>();
                pilot.Init(spawner, config);
            }
            playerInput = basket.Input;
            basket.SetInput(pilot);
            audioFeedback.Muted = true;
            if (pile != null) pile.SetSeeThrough(false); // the sample round shows the pile solid
            DemoRunning = true;
            hud.ShowRoundHud(true);
            StartDemoRound();
            hud.SetTime(Timer.Remaining, false);
            return true;
        }

        void StartDemoRound()
        {
            ResetRound();
            pilot.Restart();
            spawner.Begin();
        }

        public void EndDemo()
        {
            if (!DemoRunning) return;
            DemoRunning = false;
            if (pile != null) pile.SetSeeThrough(true);
            demoFadeTime = -1f;
            if (fade != null) fade.Alpha = 0f;
            audioFeedback.Muted = false;
            basket.SetInput(playerInput);
            ResetRound();
            hud.ShowRoundHud(false);
        }

        ScreenFade fade;
        float demoFadeTime = -1f; // seconds into the fade between sample rounds; negative while playing

        void TickDemo()
        {
            // No clock in the sample round (it plays to a score, not a time). It still runs, held short of time up, to ramp the fruit.
            if (Timer.Remaining - Time.deltaTime > 1f) Timer.Tick(Time.deltaTime);
            hud.SetTime(Timer.Remaining, false);

            if (demoFadeTime < 0f)
            {
                // Plays on until the target score, then fades out and starts again from 0.
                if (Stats.Score >= config.demoScoreTarget) demoFadeTime = 0f;
                return;
            }

            if (fade == null) fade = gameObject.AddComponent<ScreenFade>();
            float half = Mathf.Max(0.01f, config.demoFade);
            float before = demoFadeTime;
            demoFadeTime += Time.deltaTime;
            if (before < half && demoFadeTime >= half) StartDemoRound(); // fully covered: reset behind it
            fade.Alpha = demoFadeTime < half ? demoFadeTime / half : 1f - (demoFadeTime - half) / half;
            if (demoFadeTime >= 2f * half)
            {
                fade.Alpha = 0f;
                demoFadeTime = -1f;
            }
        }

        /// <summary>Nobody's there any more: closes whatever screen the visit was on and goes back to the title.</summary>
        public void AbandonVisit()
        {
            if (State == GameState.Photo && photoBooth != null) { photoBooth.Done(); return; }
            if (State == GameState.Calibrate && calibration != null) calibration.Abort();
            if (State == GameState.Setup && setup != null) setup.Hide();
            ReturnToMenu();
        }

        /// <summary>A pause ran past its limit: the round ends with the score so far.</summary>
        public void EndRoundFromPause()
        {
            if (State != GameState.Paused) return;
            Time.timeScale = 1f;
            spawner.Clear();
            EnterGameOver();
        }

        void Update()
        {
            var kb = Keyboard.current;
            // In standby (or loading in or out of it) the guest screen is covered: guests' keys do nothing.
            if (kb != null && !Standby.Active)
            {
                // While the staff panel's keys are on (F1), keys are the panel's, except P to pause.
                bool staff = StaffPanel.KeysActive || StaffPanel.HandledFrame == Time.frameCount;
                if (staff) { if (kb.pKey.wasPressedThisFrame) TogglePause(); }
                else if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Confirm();
                else if (kb.escapeKey.wasPressedThisFrame && State == GameState.Calibrate) calibration.Cancel();
                else if (kb.pKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) TogglePause();
                else if (kb.cKey.wasPressedThisFrame) StartCalibration();
                else if (kb.aKey.wasPressedThisFrame && State == GameState.Boot) ToggleAuditory();
            }

            UpdateFaceLost();
            if (DemoRunning) TickDemo();

            switch (State)
            {
                case GameState.Countdown:
                    TickCountdown();
                    break;

                case GameState.Playing:
                    Timer.Tick(Time.deltaTime);
                    hud.SetTime(Timer.Remaining, config.UsesTimer);
                    if (config.UsesTimer && Timer.Expired) BeginRoundEnd();
                    AnnounceTime();
                    break;

                case GameState.RoundEnding:
                    if (!spawner.HasActiveFruit) EnterGameOver();
                    break;
            }
        }

        void ResetRound()
        {
            spawner.Clear();
            if (pile != null) pile.Clear();
            Stats.Reset();
            Timer.Reset(config.roundDuration);
            Lives.Reset(config.lives);
            basket.ResetPosition();
            if (fallbackInput != null) fallbackInput.ResetTarget();
            hud.SetTime(Timer.Remaining, config.UsesTimer);
        }

        void SetState(GameState next)
        {
            EndDemo();
            // A new screen cuts the voice; the round's own lines (countdown into play) run on.
            if (narrator != null && next != GameState.Playing && next != GameState.RoundEnding) narrator.Stop();
            State = next;
            hud.ShowStartMenu(next == GameState.Boot);
            hud.ShowPauseMenu(next == GameState.Paused);
            hud.ShowPauseButton(next == GameState.Playing || next == GameState.Countdown || next == GameState.RoundEnding);
            hud.ShowRoundHud(next == GameState.Countdown || next == GameState.Playing || next == GameState.RoundEnding || next == GameState.Paused);
            if (next != GameState.GameOver) hud.HideGameOver();
        }

        void EnterBoot()
        {
            SetState(GameState.Boot);
            hud.HideMessage();
        }

        void EnterCountdown()
        {
            ResetRound();
            if (Narrating)
            {
                // The Sound round has no bombs, so only golden fruit need explaining (the line says 3 points: goldenPoints).
                if (config.goldenPerRound > 0) Gate(RunCountdown, "intro", "rules_golden", "game_start_ask");
                else Gate(RunCountdown, "intro", "game_start_ask");
                return;
            }
            RunCountdown();
        }

        void RunCountdown()
        {
            SetState(GameState.Countdown);
            countdownLeft = config.countdownSeconds;
            lastCountdownShown = -1;
            nextAnnouncement = 0;
            if (config.countdownSeconds <= 0) EnterPlaying();
        }

        void TickCountdown()
        {
            countdownLeft -= Time.deltaTime;
            int shown = Mathf.CeilToInt(countdownLeft);
            if (shown != lastCountdownShown && shown > 0)
            {
                lastCountdownShown = shown;
                hud.ShowMessage(shown.ToString(), ScoreRules.Explain(config));
                audioFeedback.PlayCountdownTick();
                if (shown <= 3) Say("count_" + shown);
            }
            if (countdownLeft <= 0f) EnterPlaying();
        }

        void EnterPlaying()
        {
            SetState(GameState.Playing);
            hud.ShowMessage(Strings.Get("hud.go"), null, 0.6f);
            Say("go");
            spawner.Begin();
        }

        void BeginRoundEnd()
        {
            SetState(GameState.RoundEnding);
            spawner.StopSpawning();
        }

        void EnterGameOver()
        {
            SetState(GameState.GameOver);
            bool newBest = SaveData.SubmitScore(Stats.Score);
            hud.HideMessage();
            hud.ShowGameOver(Stats, SaveData.BestScore, newBest, config.roundDuration);
            audioFeedback.PlayRoundOver();
            // Easy is for one player: the next round goes back to the staff default unless staff switch it on again.
            if (Difficulty.Easy && config.defaultMode != DefaultMode.Easy) Difficulty.Easy = false;
            // By ear: the final score (not the tally), then the photo.
            int caught = 0;
            foreach (var kv in Stats.CaughtByType) caught += kv.Value;
            Say("time_up", Number("score_final_", Stats.Score), Number("caught_", caught), Number("missed_", Stats.TotalMissed));
            if (Stats.BombsCaught > 0) Say(Number("bombs_", Stats.BombsCaught));
            if (newBest) Say("best_new");
            Say("photo_ask", Narrator.Buttons);
        }

        void EnterPause(PauseReason reason)
        {
            pauseReason = reason;
            stateBeforePause = State;
            SetState(GameState.Paused);
            Time.timeScale = 0f;
            hud.HideMessage();
            if (reason == PauseReason.FaceLost) Say("paused");
            else Say("pause_user", Narrator.Buttons);
            hud.SetPauseTitle(reason == PauseReason.FaceLost
                ? "<size=112>" + Strings.Get("facelost.title") + "</size>\n<size=64>" + Strings.Get("facelost.sub") + "</size>"
                : Strings.Get("pause.title"));
        }

        /// <summary>
        /// Pauses when the face has been gone for faceLostPauseDelay during a round, and resumes once it has been
        /// back for faceReturnGrace (only for face-lost pauses; a manual pause waits for the player).
        /// </summary>
        void UpdateFaceLost()
        {
            if (router == null || !router.UsingFace) return;

            bool active = State == GameState.Playing || State == GameState.Countdown || State == GameState.RoundEnding;
            if (active && router.FaceMissingFor > config.faceLostPauseDelay)
            {
                EnterPause(PauseReason.FaceLost);
                faceBackSince = -1f;
                return;
            }

            if (State == GameState.Paused && pauseReason == PauseReason.FaceLost)
            {
                if (router.FaceMissingFor > 0f) faceBackSince = -1f;
                else if (faceBackSince < 0f) faceBackSince = Time.unscaledTime;
                else if (Time.unscaledTime - faceBackSince >= config.faceReturnGrace) ExitPause();
            }
        }

        void ExitPause()
        {
            Time.timeScale = 1f;
            SetState(stateBeforePause);
            Say("resumed");
            // The countdown re-shows its number on the next tick.
            if (State == GameState.Countdown) lastCountdownShown = -1;
        }

        void HandleFruitSpawned(FallingFruit fruit) => audioFeedback.PlaySpawn();

        void HandleFruitHitBasket(FallingFruit fruit)
        {
            basket.PlayBump();
            if (Time.time - lastBumpSound < bumpSoundCooldown) return;
            lastBumpSound = Time.time;
            audioFeedback.PlayRimGraze();
        }

        void HandleFruitResolved(FallingFruit fruit, FallingFruit.Outcome outcome)
        {
            bool bomb = fruit.FruitKind == FallingFruit.Kind.Bomb;
            if (outcome == FallingFruit.Outcome.Caught && bomb)
            {
                int change = Stats.RecordBomb(Timer.Elapsed);
                BlockFx.Burst(fruit.transform.position, Explosion, 46, 5f, 0.1f, 0.7f, 6f);
                BlockFx.Burst(fruit.transform.position, Smoke, 14, 1.4f, 0.16f, 1.1f, -0.6f);
                basket.PlayBump();
                audioFeedback.PlayBomb();
                hud.ShowPopup(change.ToString(), GameHUD.PopupKind.Penalty);
                return;
            }
            if (outcome == FallingFruit.Outcome.Caught)
            {
                bool golden = fruit.FruitKind == FallingFruit.Kind.Golden;
                int points = Stats.RecordCatch(fruit.Type, fruit.FruitKind, Timer.Elapsed);
                if (pile != null) pile.Add(fruit.Type, golden);
                basket.PlayCatch();
                audioFeedback.PlayCatch(golden);
                hud.PopScore(points);
                if (ScoreRules.IsCombo(Stats.Streak, config)) hud.ShowPopup(Strings.Get("hud.combo", Stats.Streak, config.comboMultiplier), GameHUD.PopupKind.Combo);
                else if (golden) hud.ShowPopup("+" + points, GameHUD.PopupKind.Golden);
                if (golden) Say("golden");
                return;
            }
            // A bomb that falls past is simply gone: dodging it is the point.
            if (bomb) return;

            Stats.RecordMiss(fruit.Type);
            Lives.LoseOne();
            audioFeedback.PlayMiss();

            if (config.UsesLives && Lives.Depleted && State == GameState.Playing) BeginRoundEnd();
        }
    }
}
