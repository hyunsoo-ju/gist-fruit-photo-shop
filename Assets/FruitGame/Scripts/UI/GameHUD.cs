using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Score (top-left), timer (top-right), lives, centre messages, score popups (combo, golden fruit, bomb penalty), and
    /// the mouse-driven panels: start menu (with the Easy tag), pause button + pause menu, and the game over screen. Buttons are wired in the scene
    /// straight to GameStateMachine methods.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [SerializeField] TMP_Text scoreText;
        [SerializeField] TMP_Text timerText;
        [SerializeField] TMP_Text livesText;
        [SerializeField] TMP_Text messageTitle;
        [SerializeField] TMP_Text messageSub;

        [Header("Panels")]
        [SerializeField] GameObject startMenu;
        [SerializeField] GameObject pauseButton;
        [SerializeField] GameObject pauseMenu;
        [SerializeField] TMP_Text pauseTitle;

        [Header("Game over")]
        [SerializeField] GameObject gameOverPanel;
        [SerializeField] TMP_Text gameOverScore;
        [SerializeField] TMP_Text gameOverBest;
        [SerializeField] TMP_Text gameOverTallyLeft;
        [SerializeField] TMP_Text gameOverMissed;

        [Header("Animation")]
        [SerializeField] float popScale = 1.25f;
        [SerializeField] float popTime = 0.2f;
        [SerializeField] float pulseFromSeconds = 10f;

        public enum PopupKind { Combo, Golden, Penalty }

        static readonly Color ComboColor = new Color32(0xFF, 0x5C, 0x9E, 0xFF);
        static readonly Color GoldenColor = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        static readonly Color PenaltyColor = new Color32(0xE8, 0x3A, 0x2A, 0xFF);
        const float PopupTime = 0.9f;

        readonly System.Collections.Generic.List<(TMP_Text text, float age)> popups = new System.Collections.Generic.List<(TMP_Text, float)>();
        TMP_Text easyTag;

        float popT = -1f;
        float messageHideAt = -1f;
        float timeLeft;
        bool timerVisible;
        bool livesVisible;
        bool roundHud = true;

        public void SetScore(int score, int delta) => scoreText.text = score.ToString();

        public void PopScore(int points) => popT = 0f;

        public void SetTime(float secondsLeft, bool visible)
        {
            timeLeft = secondsLeft;
            timerVisible = visible;
            timerText.gameObject.SetActive(visible && roundHud);
            int s = Mathf.CeilToInt(secondsLeft);
            timerText.text = $"{s / 60}:{s % 60:00}";
        }

        public void ShowLives(bool visible)
        {
            livesVisible = visible;
            livesText.gameObject.SetActive(visible && roundHud);
        }

        /// <summary>The score, timer and lives show only while a round is on (countdown to game over), not on the
        /// title, calibration, game-over or photo booth screens.</summary>
        public void ShowRoundHud(bool visible)
        {
            roundHud = visible;
            scoreText.gameObject.SetActive(visible);
            timerText.gameObject.SetActive(visible && timerVisible);
            livesText.gameObject.SetActive(visible && livesVisible);
        }

        public void SetLives(int remaining) => livesText.text = Strings.Get("hud.lives", remaining);

        public void ShowStartMenu(bool visible) => SetActive(startMenu, visible);
        public void ShowPauseButton(bool visible) => SetActive(pauseButton, visible);
        public void ShowPauseMenu(bool visible) => SetActive(pauseMenu, visible);
        public void SetPauseTitle(string title)
        {
            if (pauseTitle != null) pauseTitle.text = title;
        }

        public void ShowMessage(string title, string sub, float duration = -1f)
        {
            messageTitle.gameObject.SetActive(true);
            messageTitle.text = title;
            messageSub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            messageSub.text = sub ?? "";
            messageHideAt = duration > 0f ? Time.unscaledTime + duration : -1f;
        }

        public void HideMessage()
        {
            messageTitle.gameObject.SetActive(false);
            messageSub.gameObject.SetActive(false);
            messageHideAt = -1f;
        }

        public void ShowGameOver(RoundStats stats, int best, bool newBest, float roundDuration)
        {
            lastGameOver = (stats, best, newBest, roundDuration);
            gameOverScore.text = stats.Score.ToString();
            gameOverBest.text = newBest ? Strings.Get("over.newBest") : Strings.Get("over.best", best);

            // The fruit in centred rows, each name staying with its count however the rows wrap.
            var tally = new StringBuilder();
            foreach (var kv in stats.CaughtByType)
                tally.Append("<nobr>").Append(Strings.Fruit(kv.Key)).Append(" ×").Append(kv.Value).Append("</nobr>    ");
            gameOverTallyLeft.text = tally.Length == 0 ? Strings.Get("over.none") : tally.ToString().TrimEnd();

            var extras = new List<string>();
            if (stats.GoldenCaught > 0) extras.Add(Strings.Get("over.golden", stats.GoldenCaught));
            extras.Add(Strings.Get("over.missed", stats.TotalMissed));
            if (stats.BombsCaught > 0) extras.Add(Strings.Get("over.bombs", stats.BombsCaught));
            gameOverMissed.text = string.Join("  ·  ", extras);

            UiTransition.Set(gameOverPanel, true);
        }

        public void HideGameOver() => SetActive(gameOverPanel, false);

        (RoundStats stats, int best, bool newBest, float duration)? lastGameOver;

        /// <summary>Language flipped (staff panel L): re-fill the game-over lines if they're on screen.</summary>
        void RefreshLanguage()
        {
            RefreshEasyTag();
            if (!gameOverPanel.activeSelf || !lastGameOver.HasValue) return;
            var g = lastGameOver.Value;
            ShowGameOver(g.stats, g.best, g.newBest, g.duration);
        }

        void OnEnable() => Strings.Changed += RefreshLanguage;
        void OnDisable() => Strings.Changed -= RefreshLanguage;
        void OnDestroy() => Difficulty.Changed -= RefreshEasyTag;

        static void SetActive(GameObject go, bool active) => UiTransition.Set(go, active);

        GameObject notice, banner;
        TMP_Text noticeText, bannerText;

        /// <summary>A timeout's warning (IdleWatch): an opaque card in the middle of the screen, over everything.</summary>
        public void ShowNotice(string text)
        {
            if (notice == null) notice = BuildCard("Timeout Notice", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 320f), out noticeText);
            noticeText.text = text;
            notice.SetActive(true);
            notice.transform.SetAsLastSibling();
        }

        public void HideNotice()
        {
            if (notice != null) notice.SetActive(false);
        }

        /// <summary>The attract screen's invitation: an opaque strip along the top of the screen.</summary>
        public void ShowBanner(string text)
        {
            if (banner == null) banner = BuildCard("Attract Banner", new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1200f, 150f), out bannerText);
            bannerText.text = text;
            banner.SetActive(true);
            banner.transform.SetAsLastSibling();
        }

        public void HideBanner()
        {
            if (banner != null) banner.SetActive(false);
        }

        GameObject BuildCard(string cardName, Vector2 anchor, Vector2 position, Vector2 size, out TMP_Text label)
        {
            var style = gameOverPanel.GetComponent<UnityEngine.UI.Image>();
            var card = new GameObject(cardName, typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
            card.SetParent(messageSub.rectTransform.parent, false);
            card.anchorMin = card.anchorMax = card.pivot = anchor;
            card.anchoredPosition = position;
            card.sizeDelta = size;
            var image = card.GetComponent<UnityEngine.UI.Image>();
            image.sprite = style.sprite;
            image.type = style.type;
            image.pixelsPerUnitMultiplier = style.pixelsPerUnitMultiplier;
            var c = style.color;
            image.color = new Color(c.r, c.g, c.b, 1f);
            image.raycastTarget = false;
            label = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TMP_Text>();
            var r = label.rectTransform;
            r.SetParent(card, false);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(48f, 16f);
            r.offsetMax = new Vector2(-48f, -16f);
            label.font = messageSub.font;
            label.fontSharedMaterial = messageSub.font.material;
            label.fontSize = 64f;
            label.color = new Color32(0x1E, 0x22, 0x33, 0xFF);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return card.gameObject;
        }

        /// <summary>A short line under the score that rises and fades: "+3" for a golden fruit, the combo, or "−5".</summary>
        public void ShowPopup(string text, PopupKind kind)
        {
            var t = Instantiate(messageSub, scoreText.rectTransform.parent);
            t.gameObject.SetActive(true);
            t.name = "Popup";
            foreach (var lt in t.GetComponents<LocalizedText>()) Destroy(lt);
            var rt = t.rectTransform;
            var score = scoreText.rectTransform;
            rt.anchorMin = rt.anchorMax = score.anchorMin;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(640f, 96f);
            rt.anchoredPosition = score.anchoredPosition + new Vector2((0.5f - score.pivot.x) * score.rect.width, -150f);
            t.text = text;
            t.fontSize = 64;
            t.alignment = TextAlignmentOptions.Center;
            t.color = kind == PopupKind.Combo ? ComboColor : kind == PopupKind.Golden ? GoldenColor : PenaltyColor;
            t.raycastTarget = false;
            popups.Add((t, 0f));
        }

        void UpdatePopups()
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var (t, age) = popups[i];
                age += Time.unscaledDeltaTime;
                if (age >= PopupTime || t == null)
                {
                    if (t != null) Destroy(t.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                popups[i] = (t, age);
                float u = age / PopupTime;
                t.rectTransform.anchoredPosition += new Vector2(0f, 90f * Time.unscaledDeltaTime / PopupTime);
                var c = t.color;
                c.a = u < 0.6f ? 1f : 1f - (u - 0.6f) / 0.4f;
                t.color = c;
            }
        }

        /// <summary>"쉬움 모드" or "소리 모드" under the lowest title button while Easy or the auditory mode is on.</summary>
        void BuildEasyTag()
        {
            var calibrate = startMenu != null ? startMenu.transform.Find("Auditory Button") : null;
            if (calibrate == null && startMenu != null) calibrate = startMenu.transform.Find("Calibrate Button");
            if (calibrate == null) return;
            easyTag = Instantiate(messageSub, calibrate);
            easyTag.name = "Easy Tag";
            foreach (var lt in easyTag.GetComponents<LocalizedText>()) Destroy(lt);
            var rt = easyTag.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(640f, 80f);
            rt.anchoredPosition = new Vector2(0f, -24f);
            rt.localScale = Vector3.one;
            easyTag.fontSize = 48;
            easyTag.alignment = TextAlignmentOptions.Center;
            easyTag.color = ComboColor;
            easyTag.raycastTarget = false;
            RefreshEasyTag();
        }

        void RefreshEasyTag()
        {
            if (easyTag == null) return;
            easyTag.gameObject.SetActive(Difficulty.Easy || Difficulty.Auditory);
            easyTag.text = Strings.Get(Difficulty.Auditory ? "start.auditoryTag" : "start.easy");
        }

        void Awake()
        {
            BuildEasyTag();
            Difficulty.Changed += RefreshEasyTag;
            HideMessage();
            HideGameOver();
            ShowPauseMenu(false);
            ShowPauseButton(false);
        }

        void Update()
        {
            if (messageHideAt > 0f && Time.unscaledTime >= messageHideAt) HideMessage();
            UpdatePopups();

            float s = 1f;
            if (popT >= 0f)
            {
                popT += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(popT / popTime);
                s = 1f + (popScale - 1f) * Mathf.Sin(u * Mathf.PI);
                if (u >= 1f) popT = -1f;
            }
            scoreText.rectTransform.localScale = Vector3.one * s;

            float pulse = 1f;
            if (timerVisible && timeLeft > 0f && timeLeft <= pulseFromSeconds)
                pulse = 1f + 0.12f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * Mathf.PI));
            timerText.rectTransform.localScale = Vector3.one * pulse;
        }
    }
}
