using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Standby (Docs/DESIGN.md §15): the booth is closed. The guest screen shows a calm card ("잠시 쉬고 있어요" / "We're
    /// taking a short break", in the game's language) over everything, blocks touches and keys, and the game is silent;
    /// the staff panel stays usable. Every session starts in standby; staff open the booth from the button beside "Reset
    /// settings". Going in or out passes through a short loading screen that fades in and out, so the game can settle
    /// behind it (the camera's first frames, the models, the first shaders) and the visit is reset to the title.
    /// The card sizes itself to the screen, so it reads the same in landscape and portrait.
    /// While the card is up (Idle) the heavy work stops: the camera is closed (so background removal, face finding and
    /// the face mesh rest), the cameras that draw the guest screen are switched off (a blank stand-in keeps Camera.main
    /// for the scripts that ask for it), the lighting pauses, and the frame rate is held to 20; the staff screen goes on.
    /// It all comes back behind the loading screen on the way out.
    /// </summary>
    public class Standby : MonoBehaviour
    {
        const float Fade = 0.45f, MinLoading = 1.2f, MaxLoading = 10f;
        static readonly Color Back = new Color32(0x1E, 0x22, 0x33, 0xFF);
        static readonly Color Cream = new Color32(0xFF, 0xF6, 0xE6, 0xFF);
        static readonly Color Soft = new Color32(0xC9, 0xCF, 0xE0, 0xFF);
        static readonly Color Accent = new Color32(0xFF, 0xC8, 0x57, 0xFF);

        static Standby instance;

        /// <summary>True while the guest screen is covered (standby, or loading in or out of it).</summary>
        public static bool Active => instance != null && instance.covered;
        /// <summary>True while staff have the booth closed (also through the loading on the way in).</summary>
        public static bool On => instance != null && instance.wantOn;
        /// <summary>True while a loading screen is showing.</summary>
        public static bool Busy => instance != null && instance.routine != null;
        /// <summary>True while the standby card is fully up and the heavy work is switched off.</summary>
        public static bool Idle => instance != null && instance.idle;

        const int IdleFrameRate = 20;
        bool idle;
        readonly System.Collections.Generic.List<Camera> stopped = new System.Collections.Generic.List<Camera>();
        Camera standIn;
        int savedFrameRate, savedVSync;

        bool covered, wantOn = true;
        Coroutine routine;
        CanvasGroup group, card, loading;
        TMP_Text title, message, loadingText;
        RectTransform cardRect;
        GameStateMachine game;
        float savedVolume = 1f;

        /// <summary>
        /// Every Game session starts in standby: when Play starts in the Game scene, and in builds when the Game scene
        /// loads after Boot.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
            Create();
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode) => Create();

        static void Create()
        {
            if (instance != null || FindAnyObjectByType<GameStateMachine>() == null) return;
            new GameObject("Standby").AddComponent<Standby>();
        }

        /// <summary>Opens or closes the booth (the staff panel's button).</summary>
        public static void Set(bool on)
        {
            if (instance == null || on == instance.wantOn) return;
            instance.wantOn = on;
            if (instance.routine != null) instance.StopCoroutine(instance.routine);
            instance.routine = instance.StartCoroutine(on ? instance.Enter() : instance.Leave());
        }

        void Awake()
        {
            instance = this;
            game = FindAnyObjectByType<GameStateMachine>();
            Build();
            covered = true;
            savedVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            group.alpha = 1f;
            ShowLoading(1f);
            // The camera stays closed until the booth opens: opening it only to close it again for the standby card
            // flashed the camera's prompts (an iPhone's Continuity Camera asked, then cancelled).
            var source = FindAnyObjectByType<WebcamSource>();
            if (source != null) source.Suspend(true);
            routine = StartCoroutine(Startup());
        }

        void OnDestroy()
        {
            if (instance != this) return;
            GoIdle(false);
            instance = null;
            if (covered) AudioListener.volume = savedVolume;
        }

        void Build()
        {
            var font = FindGameFont();
            var canvasGo = new GameObject("Standby Screen", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = GuestDisplay.Index;
            canvas.sortingOrder = 29500; // over the game and its fades, under the one-screen staff panel (30000)
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1080f); // sized by the short side: the same in either orientation
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            group = canvasGo.GetComponent<CanvasGroup>();

            var back = Box("Back", canvasGo.transform, Back);
            back.GetComponent<Image>().raycastTarget = true; // touches stop here
            Stretch(back, 0f);

            // The standby card: a title and a message, centred, wrapping within the screen's width.
            card = Layer("Card", back);
            cardRect = (RectTransform)card.transform;
            var stripe = Box("Stripe", cardRect, Accent);
            stripe.anchorMin = stripe.anchorMax = new Vector2(0.5f, 0.5f);
            stripe.sizeDelta = new Vector2(120f, 10f);
            stripe.anchoredPosition = new Vector2(0f, 150f);
            title = Text("Title", cardRect, font, 84f, Cream);
            Area(title.rectTransform, 40f, 140f);
            message = Text("Message", cardRect, font, 40f, Soft);
            Area(message.rectTransform, -90f, 180f);

            // The loading screen: one line and three dots that come and go.
            loading = Layer("Loading", back);
            loadingText = Text("Loading Text", loading.transform, font, 48f, Soft);
            Area(loadingText.rectTransform, 0f, 80f);
        }

        void Update()
        {
            if (!covered) return;
            title.text = Strings.Get("standby.title");
            message.text = Strings.Get("standby.message");
            int dots = Mathf.FloorToInt(Time.unscaledTime * 2.5f) % 4;
            loadingText.text = Strings.Get("standby.loading") + "<alpha=#FF>" + new string('.', dots) + "<alpha=#00>" + new string('.', 3 - dots);
            // The game behind is silent while covered, and comes back up as the cover fades.
            AudioListener.volume = savedVolume * (1f - group.alpha);
        }

        /// <summary>First start: the loading screen until the game has settled, then the standby card.</summary>
        IEnumerator Startup()
        {
            yield return Settle();
            yield return Crossfade(toLoading: false);
            GoIdle(true);
            routine = null;
            // `-openbooth` on the command line: open the booth by itself once loaded (for trying a build).
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-openbooth") >= 0) Set(false);
        }

        IEnumerator Enter()
        {
            Cover(true);
            ShowLoading(1f);
            yield return FadeGroup(1f);
            if (game != null) { game.EndDemo(); game.AbandonVisit(); }
            yield return Settle();
            yield return Crossfade(toLoading: false);
            GoIdle(true);
            routine = null;
        }

        IEnumerator Leave()
        {
            yield return Crossfade(toLoading: true);
            GoIdle(false);
            // Behind the loading screen: back to a fresh title, as if the booth had just opened.
            if (game != null) { game.EndDemo(); game.AbandonVisit(); }
            yield return Settle();
            yield return FadeGroup(0f);
            Cover(false);
            routine = null;
        }

        /// <summary>
        /// Waits until the game has settled: at least a moment, the camera delivering (or off, or failed), and ten
        /// smooth frames in a row (the first frames compile shaders and load models, and hitch). At most 10 s.
        /// </summary>
        IEnumerator Settle()
        {
            float start = Time.unscaledTime;
            var source = FindAnyObjectByType<WebcamSource>();
            int smooth = 0;
            while (Time.unscaledTime - start < MaxLoading)
            {
                smooth = Time.unscaledDeltaTime < 0.05f ? smooth + 1 : 0;
                bool camera = source == null || source.HasFrames || source.Failed || source.Idle;
                if (Time.unscaledTime - start >= MinLoading && camera && smooth >= 10) break;
                yield return null;
            }
        }

        IEnumerator FadeGroup(float to)
        {
            float from = group.alpha;
            for (float t = 0f; t < Fade; t += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / Fade));
                yield return null;
            }
            group.alpha = to;
        }

        /// <summary>The card fades out, then the loading line in (or the other way), so the two never overlap.</summary>
        IEnumerator Crossfade(bool toLoading)
        {
            CanvasGroup from = toLoading ? card : loading, to = toLoading ? loading : card;
            float start = from.alpha, half = Fade * 0.5f;
            for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
            {
                from.alpha = Mathf.Lerp(start, 0f, t / half);
                yield return null;
            }
            from.alpha = 0f;
            for (float t = 0f; t < half; t += Time.unscaledDeltaTime)
            {
                to.alpha = t / half;
                yield return null;
            }
            to.alpha = 1f;
        }

        void ShowLoading(float amount)
        {
            loading.alpha = amount;
            card.alpha = 1f - amount;
        }

        void Cover(bool on)
        {
            if (on && !covered) { savedVolume = AudioListener.volume; group.alpha = 0f; }
            covered = on;
            group.gameObject.SetActive(on);
            group.blocksRaycasts = on;
            if (!on) AudioListener.volume = savedVolume;
        }

        /// <summary>Switches the heavy work off while the standby card is up, or back on.</summary>
        void GoIdle(bool on)
        {
            if (on == idle) return;
            idle = on;
            var source = FindAnyObjectByType<WebcamSource>();
            if (source != null) source.Suspend(on);
            if (on)
            {
                // A blank stand-in where the main camera is, so Camera.main still answers (the physical UI, clouds).
                var main = Camera.main;
                standIn = new GameObject("Standby Camera").AddComponent<Camera>();
                if (main != null)
                {
                    standIn.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
                    standIn.fieldOfView = main.fieldOfView;
                    standIn.nearClipPlane = main.nearClipPlane;
                    standIn.farClipPlane = main.farClipPlane;
                    standIn.orthographic = main.orthographic;
                    standIn.orthographicSize = main.orthographicSize;
                }
                standIn.targetDisplay = GuestDisplay.Index;
                standIn.cullingMask = 0;
                standIn.clearFlags = CameraClearFlags.SolidColor;
                standIn.backgroundColor = Back;
                standIn.depth = -100f;
                // Everything else that draws the guest screen or into the game's own textures (the pixel layers).
                stopped.Clear();
                foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                {
                    if (c == standIn || !c.enabled || (c.targetDisplay != GuestDisplay.Index && c.targetTexture == null)) continue;
                    c.enabled = false;
                    stopped.Add(c);
                }
                standIn.tag = "MainCamera";
                // Drawn at about 20 fps. Builds skip drawing on the screen refreshes in between (OnDemandRendering) rather
                // than capping the frame rate: with Metal display link, a capped frame rate sleeps inside the display's
                // refresh callback and macOS can't hand the app any clicks meanwhile, so the staff panel stopped
                // answering. The Editor isn't paced by the display, so there it's still a frame-rate cap.
                if (Application.isEditor)
                {
                    savedFrameRate = Application.targetFrameRate;
                    savedVSync = QualitySettings.vSyncCount;
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = IdleFrameRate;
                }
                else
                {
                    double hz = Screen.currentResolution.refreshRateRatio.value;
                    UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = Mathf.Max(1, Mathf.RoundToInt((float)(hz > 1.0 ? hz : 60.0) / IdleFrameRate));
                }
            }
            else
            {
                if (standIn != null) { standIn.enabled = false; Destroy(standIn.gameObject); standIn = null; }
                foreach (var c in stopped) if (c != null) c.enabled = true;
                stopped.Clear();
                if (Application.isEditor)
                {
                    QualitySettings.vSyncCount = savedVSync;
                    Application.targetFrameRate = savedFrameRate;
                }
                else UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
            }
        }

        /// <summary>The game's pixel font, from a text already on the guest screen.</summary>
        static TMP_FontAsset FindGameFont()
        {
            foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.font != null && t.font.name.Contains("NeoDunggeunmo")) return t.font;
            return TMP_Settings.defaultFontAsset;
        }

        static CanvasGroup Layer(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            var g = go.GetComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            Stretch((RectTransform)go.transform, 0f);
            return g;
        }

        static RectTransform Box(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = colour;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, float size, Color colour)
        {
            var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            if (font != null) t.font = font;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMin = size * 0.5f;
            t.fontSizeMax = size;
            t.color = colour;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.lineSpacing = 20f;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A band across the middle of the screen (8 % in from each side), centred `y` above the middle.</summary>
        static void Area(RectTransform r, float y, float height)
        {
            r.anchorMin = new Vector2(0.08f, 0.5f); r.anchorMax = new Vector2(0.92f, 0.5f); r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(0f, height);
        }

        static void Stretch(RectTransform r, float inset)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset); r.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
