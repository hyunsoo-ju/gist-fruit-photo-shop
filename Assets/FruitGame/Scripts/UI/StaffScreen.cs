using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The staff screen (Docs/DESIGN.md §15, M6 3.1). With two screens, the guest plays on the main display (the touch
    /// display; make it the main display in macOS) and the laptop shows the staff screen: a live mirror of the guest
    /// screen on the left with the face box drawn over it, the performance meter (PerfPanel) under it, and the staff
    /// panel (StaffPanel) on the right. Debug views
    /// never show on the guest screen then. With one screen, the guest view fills it and the staff panel opens over its
    /// right side on F1 (or a 3 s hold on the top-left corner).
    /// Built at runtime: the status line, mask view and printer box under the debug overlay move into the panel.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class StaffScreen : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [Tooltip("The guest UI's game-view frame (the HUD Frame), to place the face box over the mirror.")]
        [SerializeField] RectTransform guestFrame;
        [SerializeField] FaceDebugOverlay debug;
        [SerializeField] RectTransform faceBox;
        [SerializeField] TMP_Text status;
        [SerializeField] RawImage maskPreview;
        [SerializeField] Printer printer;
        [SerializeField] RectTransform printerPanel;
        [SerializeField] TMP_Text printerBox;
        [SerializeField] RectTransform printerWarning;
        [SerializeField] WebcamSource source;
        [SerializeField] StaffPanel panel;
        [Tooltip("A text to copy the font from.")]
        [SerializeField] TMP_Text style;

        /// <summary>True when the staff screen is on its own display.</summary>
        public static bool TwoScreens { get; private set; }
        /// <summary>The display staff windows (camera menu, settings) should open on.</summary>
        public static int StaffDisplay => TwoScreens ? GuestDisplay.StaffIndex : 0;

        const float RefW = 1920f, RefH = 1080f, Column = 560f, Gap = 24f;
        /// <summary>The performance meter's height, under the mirror (a 16:9 guest mirror is 724 tall, leaving this).</summary>
        const float PerfH = 276f;
        static readonly Color Back = new Color(0.06f, 0.07f, 0.1f, 1f);
        static readonly Color Card = new Color(0.12f, 0.13f, 0.18f, 1f);

        GameObject alert;
        TMP_Text alertText;
        RenderTexture mirror;
        RawImage mirrorImage;
        RectTransform frameMirror;
        TMP_Text noGuestHint;
        float nextCapture;

        void Awake()
        {
            bool two = config.screens == ScreenMode.Two
                       || (config.screens == ScreenMode.Auto && (Application.isEditor ? EditorHasStaffView : Display.displays.Length > 1));
            if (two && !Application.isEditor)
            {
                // Unity's main window sits on the computer's main display and its second window (Display 2) on the other.
                if (Display.displays.Length > 1) Display.displays[1].Activate();
                else two = false; // asked for two, but only one is connected: fall back to one screen
            }
            TwoScreens = two;
            // Builds: by default the staff screen in the main window and the guests in the second, on the extended
            // display, so the touch display needn't be made the main display (staff panel: Display). The Editor keeps
            // Display 1 for the guests: its Game views can sit on either monitor anyway. (This swap once flickered, the
            // main window's picture leaking into the guest screen every other frame: Unity bug UUM-103506, which the
            // unswapped arrangement hit too, fixed by Metal display link.)
            GuestDisplay.Index = two && !Application.isEditor && config.staffOnMainDisplay ? 1 : 0;
            if (GuestDisplay.Index != 0) MoveGuestScreen(GuestDisplay.Index);
            if (!Application.isEditor) StartCoroutine(KeepGameWindowHome());

            Build(two);
            BuildAlert(two);
            if (!two) return;
            if (printer != null) printer.AlwaysShowBox = true;
            if (debug != null) debug.StaffScreen = true;
            StartCoroutine(Mirror());
            if (!Application.isEditor && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nomirror") < 0)
                UnityEngine.Rendering.RenderPipelineManager.endContextRendering += CopyGuestPicture;
        }

        void OnDestroy()
        {
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering -= CopyGuestPicture;
            copyGuest?.Release();
            if (mirror != null) { mirror.Release(); Destroy(mirror); }
        }

        void Build(bool two)
        {
            var canvasGo = new GameObject("Staff Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = two ? GuestDisplay.StaffIndex : 0;
            canvas.sortingOrder = two ? 0 : 30000; // one screen: over the game, under the camera menu and blind cover
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)canvasGo.transform;
            staffRoot = root;
            if (!two)
            {
                BuildColumn(root, false);
                return;
            }

            // In the Editor, a plain camera for the staff display, so its Game view doesn't say "no cameras". Not in
            // builds: a camera drawing the second window while the game draws the main one failed every so often on
            // this Mac (its buffers sized by the main window), and the staff screen blinked black; the full-screen
            // background panel covers the window anyway.
            if (Application.isEditor)
            {
                var bgCam = new GameObject("Staff Background Camera", typeof(Camera)).GetComponent<Camera>();
                bgCam.transform.SetParent(transform, false);
                bgCam.targetDisplay = 1;
                bgCam.clearFlags = CameraClearFlags.SolidColor;
                bgCam.backgroundColor = Back;
                bgCam.cullingMask = 0;
                bgCam.depth = -100;
            }

            var back = Panel("Background", root, Back);
            Stretch(back, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            // The mirror: the whole guest screen, fitted into the area left of the column, above the performance meter.
            var area = new GameObject("Mirror Area", typeof(RectTransform), typeof(AspectRatioFitter)).GetComponent<RectTransform>();
            area.SetParent(root, false);
            Stretch(area, Vector2.zero, new Vector2(1f, 1f), new Vector2(Gap, Gap * 2f + PerfH), new Vector2(-Column - Gap * 2f, -Gap));
            var perfArea = new GameObject("Performance Area", typeof(RectTransform)).GetComponent<RectTransform>();
            perfArea.SetParent(root, false);
            Stretch(perfArea, Vector2.zero, new Vector2(1f, 0f), new Vector2(Gap, Gap), new Vector2(-Column - Gap * 2f, Gap + PerfH));
            PerfPanel.Build(perfArea, style);
            mirrorImage = new GameObject("Guest Mirror", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter)).GetComponent<RawImage>();
            mirrorImage.transform.SetParent(area, false);
            mirrorImage.raycastTarget = false;
            var fit = mirrorImage.GetComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = GuestAspect;
            Destroy(area.GetComponent<AspectRatioFitter>());

            // The guest game view inside the mirror, for the face box (placed each frame from the guest Frame).
            frameMirror = new GameObject("Guest Frame", typeof(RectTransform)).GetComponent<RectTransform>();
            frameMirror.SetParent(mirrorImage.transform, false);
            if (faceBox != null) faceBox.SetParent(frameMirror, false);
#if UNITY_EDITOR
            noGuestHint = Text("No Guest View", mirrorImage.transform, 32f);
            noGuestHint.text = "No Game view is showing Display 1, so there's no guest screen to mirror.\n" +
                               "Open another Game view (Window > General > Game) and leave it on Display 1.";
            noGuestHint.alignment = TextAlignmentOptions.Center;
            Stretch(noGuestHint.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 0f), new Vector2(-40f, 0f));
            noGuestHint.gameObject.SetActive(false);
#endif

            BuildColumn(root, true);
        }

        /// <summary>The staff panel column on the right, with the debug views and the printer box moved into it.</summary>
        void BuildColumn(RectTransform root, bool two)
        {
            var column = new GameObject("Staff Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            column.SetParent(root, false);
            Stretch(column, new Vector2(1f, 0f), Vector2.one, new Vector2(-Column - Gap, Gap), new Vector2(-Gap, -Gap));
            if (!two) Stretch(column, new Vector2(1f, 0f), Vector2.one, new Vector2(-Column, 0f), Vector2.zero);
            panel.Build(column, two);

            var statusCard = panel.StatusCard;
            if (status != null) Fill(status.rectTransform, statusCard, new Vector2(0f, 160f), 16f, 52f);
            if (maskPreview != null)
            {
                maskPreview.transform.SetParent(statusCard, false);
                var r = maskPreview.rectTransform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 0f);
                r.anchoredPosition = new Vector2(16f, 66f);
                r.sizeDelta = new Vector2(160f, 90f);
            }
            if (printerPanel != null && panel.PrintingBoxArea != null)
            {
                printerPanel.SetParent(panel.PrintingBoxArea, false);
                Stretch(printerPanel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var img = printerPanel.GetComponent<Image>();
                if (img != null) img.enabled = false;
                if (printerBox != null) Fill(printerBox.rectTransform, printerPanel, Vector2.zero, 16f, 0f);
            }
            if (two && printerWarning != null) printerWarning.gameObject.SetActive(false);
        }

        /// <summary>
        /// Puts everything the scene draws on the main window (the game's cameras and screen-space canvases) on `display`
        /// instead. Runs first (StaffScreen is early), so whatever is made later copies or asks GuestDisplay.Index.
        /// </summary>
        static void MoveGuestScreen(int display)
        {
            foreach (var c in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.targetTexture == null && c.targetDisplay == 0) c.targetDisplay = display;
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.targetDisplay == 0) c.targetDisplay = display;
            Debug.Log($"[FruitGame] Screens: staff screen on the main display, guest screen on display {display + 1}");
        }

        /// <summary>
        /// Builds: the main window full screen on the main display, whatever Unity remembered from the last run (it
        /// stores the window's monitor and mode, and a window left on the other monitor sat under the staff window).
        /// </summary>
        IEnumerator KeepGameWindowHome()
        {
            if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow) Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            var layout = new System.Collections.Generic.List<DisplayInfo>();
            Screen.GetDisplayLayout(layout);
            // Unity's display 1 is the main display; find it in the monitor list by its width.
            int home = layout.FindIndex(d => d.width == Display.displays[0].systemWidth);
            if (home >= 0 && !layout[home].Equals(Screen.mainWindowDisplayInfo))
            {
                Debug.Log($"[FruitGame] Screens: moving the game's window from '{Screen.mainWindowDisplayInfo.name}' to the main display '{layout[home].name}'");
                yield return Screen.MoveMainWindowTo(layout[home], Vector2Int.zero);
            }
            yield return null;
            CapGameResolution();
        }

        /// <summary>The most pixels the game's 3D view draws on its long and short side (it's scaled up to fill bigger screens).</summary>
        const int MaxLongSide = 1920, MaxShortSide = 1080;

        /// <summary>
        /// Full screen at the monitor's own size, with the 3D view drawn at no more than 1920 × 1080 (or 1080 × 1920
        /// upright) through URP's render scale: on a big or Retina monitor the window is drawn at the panel's full backing
        /// size (6016 × 3384 on a 4K monitor), and the game ran at 20 fps, the GPU drawing ten times the pixels of the
        /// booth's 1080p display. (Shrinking the window's resolution instead also worked for speed, but broke the staff
        /// screen's clicks: uGUI then rescales mouse positions off the main display by the two sizes' ratio and loses
        /// which display a click is on.)
        /// </summary>
        static void CapGameResolution()
        {
            var d = Screen.mainWindowDisplayInfo;
            int w = d.width > 0 ? d.width : Screen.width, h = d.height > 0 ? d.height : Screen.height;
            // Unity remembers the window's last resolution: undo a smaller one (an earlier build shrank it).
            if (Screen.width != w || Screen.height != h) Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
            // The guest screen in the second window: its display decides (the staff screen draws no 3D).
            string name = d.name;
            if (GuestDisplay.Index > 0 && GuestDisplay.Index < Display.displays.Length)
            {
                var g = Display.displays[GuestDisplay.Index];
                w = g.renderingWidth; h = g.renderingHeight; name = $"display {GuestDisplay.Index + 1}";
            }
            float k = Mathf.Min(1f, Mathf.Min((float)MaxLongSide / Mathf.Max(w, h), (float)MaxShortSide / Mathf.Min(w, h)));
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.renderScale = Mathf.Max(0.1f, k);
            Debug.Log($"[FruitGame] Screens: guest screen '{name}' is {w}x{h}; the 3D view draws at {Mathf.RoundToInt(w * k)}x{Mathf.RoundToInt(h * k)} (render scale {k:0.###}), scaled up to fill it");
        }

        /// <summary>In the Editor, Auto uses two screens when a Game view is set to Display 2.</summary>
        static bool EditorHasStaffView
        {
            get
            {
#if UNITY_EDITOR
                return GuestDisplay.EditorHasStaffView;
#else
                return false;
#endif
            }
        }

        /// <summary>Guest display's shape (the main display; in the Editor, the Game view's).</summary>
        static float GuestAspect
        {
            get
            {
                var size = GuestDisplay.Size;
                return size.y > 0 ? (float)size.x / size.y : 16f / 9f;
            }
        }

        Transform staffRoot;

        /// <summary>The visitor's call for help: a red bar across the top of the staff screen (bottom right with one
        /// screen, clear of the score and timer), until it's clicked or cleared from the panel. No sound.</summary>
        void BuildAlert(bool two)
        {
            var bar = Panel("Help Call", staffRoot, new Color32(0xC6, 0x28, 0x28, 0xFF));
            bar.GetComponent<Image>().raycastTarget = true;
            bar.gameObject.AddComponent<Button>().onClick.AddListener(StaffAlert.Clear);
            if (two) Stretch(bar, new Vector2(0f, 1f), Vector2.one, new Vector2(Gap, -Gap - 112f), new Vector2(-Column - Gap * 2f, -Gap));
            else Stretch(bar, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Column, 96f), new Vector2(0f, 208f));
            alertText = Text("Text", bar, 32f);
            alertText.alignment = TextAlignmentOptions.Left;
            Stretch(alertText.rectTransform, Vector2.zero, Vector2.one, new Vector2(24f, 8f), new Vector2(-24f, -8f));
            alert = bar.gameObject;
            alert.SetActive(false);
        }

        void UpdateAlert()
        {
            if (alert == null) return;
            if (alert.activeSelf != StaffAlert.Active) alert.SetActive(StaffAlert.Active);
            if (!StaffAlert.Active) return;
            alert.transform.SetAsLastSibling();
            alertText.text = $"Visitor pressed 0: they need help ({StaffAlert.Waiting})\n<size=16>Click here, or H with the panel keys on (F1), to clear</size>";
        }

        void Update()
        {
            UpdateAlert();
            LayoutStatusCard();
            if (!TwoScreens) return;
            mirrorImage.GetComponent<AspectRatioFitter>().aspectRatio = GuestAspect;

            // Keep the face-box frame on the guest's game view inside the mirror.
            if (guestFrame != null)
            {
                var corners = new Vector3[4];
                guestFrame.GetWorldCorners(corners); // an overlay canvas: world = pixels of the guest display
                var canvasRect = ((RectTransform)guestFrame.GetComponentInParent<Canvas>().rootCanvas.transform).rect;
                var scale = guestFrame.GetComponentInParent<Canvas>().rootCanvas.transform.lossyScale;
                float w = Mathf.Max(1f, canvasRect.width * scale.x), h = Mathf.Max(1f, canvasRect.height * scale.y);
                frameMirror.anchorMin = new Vector2(corners[0].x / w, corners[0].y / h);
                frameMirror.anchorMax = new Vector2(corners[2].x / w, corners[2].y / h);
                frameMirror.offsetMin = frameMirror.offsetMax = Vector2.zero;
            }
        }

        /// <summary>
        /// The Status card: the status lines at the top, as tall as their text, and the person mask as large as the rest
        /// of the card allows above the printer line (at the camera frame's shape), so neither runs into the other.
        /// </summary>
        void LayoutStatusCard()
        {
            if (status == null || maskPreview == null || panel == null || panel.StatusCard == null) return;
            var card = panel.StatusCard;
            const float Top = 52f, Bottom = 66f, Side = 20f, Between = 10f;
            float innerW = card.rect.width - Side * 2f;
            if (innerW <= 0f) return;
            float textH = Mathf.Ceil(status.GetPreferredValues(status.text, innerW, 0f).y);
            var t = status.rectTransform;
            t.anchorMin = new Vector2(0f, 1f); t.anchorMax = Vector2.one; t.pivot = new Vector2(0.5f, 1f);
            t.offsetMin = new Vector2(Side, -Top - textH); t.offsetMax = new Vector2(-Side, -Top);
            float aspect = Mathf.Max(0.1f, CameraFrame.Aspect);
            float h = Mathf.Max(0f, Mathf.Min(card.rect.height - Top - textH - Between - Bottom, innerW / aspect));
            var m = maskPreview.rectTransform;
            m.anchoredPosition = new Vector2(Side, Bottom);
            m.sizeDelta = new Vector2(h * aspect, h);
        }

        /// <summary>Copies the guest screen (with its UI) into the mirror a set number of times a second.</summary>
        IEnumerator Mirror()
        {
            var end = new WaitForEndOfFrame();
            while (true)
            {
                yield return end;
                if (Time.unscaledTime < nextCapture) continue;
                nextCapture = Time.unscaledTime + 1f / Mathf.Max(1f, config.staffMirrorRate);
#if UNITY_EDITOR
                // In the Editor the capture copies whichever Game view has focus; show the Display 1 view's own
                // picture instead (stored top row first, so flipped).
                // Never fall back to the capture here: it would copy the staff view itself.
                var view = GuestDisplay.EditorTexture;
                mirrorImage.texture = view;
                mirrorImage.uvRect = new Rect(0f, 1f, 1f, -1f);
                mirrorImage.color = view != null ? Color.white : Card;
                if (noGuestHint != null) noGuestHint.gameObject.SetActive(view == null);
#else
                var size = GuestDisplay.Size;
                int w = size.x, h = size.y;
                if (w <= 0 || h <= 0) continue;
                if (mirror == null || mirror.width != w || mirror.height != h)
                {
                    if (mirror != null) { mirror.Release(); Destroy(mirror); }
                    mirror = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "Guest Mirror" };
                    mirror.Create();
                    mirrorImage.texture = mirror;
                }
                // Copied when the game's last camera has drawn the guest screen (CopyGuestPicture): a screenshot here, at
                // the end of the frame, copied the staff window, which is drawn last.
                captureDue = true;
                mirrorImage.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
#endif
            }
        }

        bool captureDue;

        /// <summary>
        /// Builds: right after the last camera that draws the guest screen (the main window) has finished, its picture
        /// is copied into the mirror, before the staff window is drawn.
        /// </summary>
        void CopyGuestPicture(UnityEngine.Rendering.ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            if (!captureDue || mirror == null) return;
            bool guest = false;
            foreach (var c in cameras) if (c != null && c.targetTexture == null && c.targetDisplay == GuestDisplay.Index) guest = true;
            if (!guest) return;
            captureDue = false;
            // The guest display's picture once this frame's cameras (and the overlay UI) have drawn it.
            copyGuest ??= new UnityEngine.Rendering.CommandBuffer { name = "Guest Mirror" };
            copyGuest.Clear();
            copyGuest.Blit(Display.displays[GuestDisplay.Index].colorBuffer, mirror);
            Graphics.ExecuteCommandBuffer(copyGuest);
            if (!mirrorLogged) { mirrorLogged = true; Debug.Log($"[FruitGame] Staff mirror: copying the guest display's picture after {cameras.Count} cameras ({mirror.width}x{mirror.height})"); }
        }

        UnityEngine.Rendering.CommandBuffer copyGuest;
        bool mirrorLogged;

        static RectTransform Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        TMP_Text Text(string name, Transform parent, float size)
        {
            var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            if (style != null) { t.font = style.font; t.fontSharedMaterial = style.fontSharedMaterial; }
            t.fontSize = size;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Moves a debug text into a card below its heading, at a staff-screen size.</summary>
        static void Fill(RectTransform r, RectTransform card, Vector2 bottomInset, float size, float top = 56f)
        {
            r.SetParent(card, false);
            Stretch(r, Vector2.zero, Vector2.one, new Vector2(20f, 12f + bottomInset.y), new Vector2(-20f, -top));
            var t = r.GetComponent<TMP_Text>();
            if (t == null) return;
            t.fontSize = size;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.alignment = TextAlignmentOptions.TopLeft;
        }

        static void Stretch(RectTransform r, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.offsetMin = offsetMin;
            r.offsetMax = offsetMax;
        }
    }
}
