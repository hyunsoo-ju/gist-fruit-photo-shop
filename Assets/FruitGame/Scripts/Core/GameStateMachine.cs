using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitGame
{
    public enum GameState
    {
        Boot,
        Calibrate,
        Countdown,
        Playing,
        RoundEnding,
        Paused,
        GameOver,
        Photo
    }

    /// <summary>
    /// Runs the round: start menu → countdown → playing → (last fruit lands) → game over.
    /// Docs/DESIGN.md §2. Every transition is a public method so on-screen buttons (and later face gestures)
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

        void OnEnable()
        {
            spawner.FruitResolved += HandleFruitResolved;
            spawner.FruitSpawned += HandleFruitSpawned;
            spawner.FruitHitBasket += HandleFruitHitBasket;
            spawner.Progress = () => Timer.Progress01;
            if (calibration != null) calibration.Finished += HandleCalibrationFinished;
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
            if (photoBooth != null) photoBooth.Closed -= ReturnToMenu;
            Stats.ScoreChanged -= hud.SetScore;
            Lives.Changed -= hud.SetLives;
            Time.timeScale = 1f;
        }

        void Start()
        {
            hud.ShowLives(config.UsesLives);
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
            }
        }

        /// <summary>Play button / Play again button. The first round with a live camera calibrates first.</summary>
        public void StartRound()
        {
            if (State == GameState.Paused) Time.timeScale = 1f;
            if (calibration != null && router != null && router.UsingFace && !CalibrationData.HasSaved)
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
            photoBooth.Open(Stats.Score, SaveData.BestScore);
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
            SetState(GameState.Calibrate);
            hud.HideMessage();
            calibration.Begin(thenPlay);
        }

        void HandleCalibrationFinished(bool accepted, bool thenPlay)
        {
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

        /// <summary>Main menu button: abandons the current round and goes back to the start screen.</summary>
        public void ReturnToMenu()
        {
            Time.timeScale = 1f;
            ResetRound();
            EnterBoot();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Confirm();
                else if (kb.escapeKey.wasPressedThisFrame && State == GameState.Calibrate) calibration.Cancel();
                else if (kb.pKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) TogglePause();
                else if (kb.cKey.wasPressedThisFrame) StartCalibration();
            }

            UpdateFaceLost();

            switch (State)
            {
                case GameState.Countdown:
                    TickCountdown();
                    break;

                case GameState.Playing:
                    Timer.Tick(Time.deltaTime);
                    hud.SetTime(Timer.Remaining, config.UsesTimer);
                    if (config.UsesTimer && Timer.Expired) BeginRoundEnd();
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
            State = next;
            hud.ShowStartMenu(next == GameState.Boot);
            hud.ShowPauseMenu(next == GameState.Paused);
            hud.ShowPauseButton(next == GameState.Playing || next == GameState.Countdown || next == GameState.RoundEnding);
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
            SetState(GameState.Countdown);
            countdownLeft = config.countdownSeconds;
            lastCountdownShown = -1;
            if (config.countdownSeconds <= 0) EnterPlaying();
        }

        void TickCountdown()
        {
            countdownLeft -= Time.deltaTime;
            int shown = Mathf.CeilToInt(countdownLeft);
            if (shown != lastCountdownShown && shown > 0)
            {
                lastCountdownShown = shown;
                hud.ShowMessage(shown.ToString(), null);
                audioFeedback.PlayCountdownTick();
            }
            if (countdownLeft <= 0f) EnterPlaying();
        }

        void EnterPlaying()
        {
            SetState(GameState.Playing);
            hud.ShowMessage(Strings.Get("hud.go"), null, 0.6f);
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
        }

        void EnterPause(PauseReason reason)
        {
            pauseReason = reason;
            stateBeforePause = State;
            SetState(GameState.Paused);
            Time.timeScale = 0f;
            hud.HideMessage();
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
            if (outcome == FallingFruit.Outcome.Caught)
            {
                int points = Stats.RecordCatch(fruit.Type, Timer.Elapsed);
                if (pile != null) pile.Add(fruit.Type);
                basket.PlayCatch();
                audioFeedback.PlayCatch();
                hud.PopScore(points);
                return;
            }

            Stats.RecordMiss(fruit.Type);
            Lives.LoseOne();
            audioFeedback.PlayMiss();

            if (config.UsesLives && Lives.Depleted && State == GameState.Playing) BeginRoundEnd();
        }
    }
}
