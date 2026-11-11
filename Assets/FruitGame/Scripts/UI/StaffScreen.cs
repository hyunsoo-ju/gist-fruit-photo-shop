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
        public static int StaffDisplay => TwoScreens ? 1 : 0;

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
                if (Display.displays.Length > 1) Display.displays[1].Activate();
                else two = false; // asked for two, but only one is connected: fall back to one screen
            }
            TwoScreens = two;

            Build(two);
            BuildAlert(two);
            if (!two) return;
            if (printer != null) printer.AlwaysShowBox = true;
            if (debug != null) debug.StaffScreen = true;
            StartCoroutine(Mirror());
        }

        void OnDestroy()
        {
            if (mirror != null) { mirror.Release(); Destroy(mirror); }
        }

        void Build(bool two)
        {
            var canvasGo = new GameObject("Staff Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = two ? 1 : 0;
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

            // A plain camera for the staff display, so it has a background (and the Editor doesn't say "no cameras").
            var bgCam = new GameObject("Staff Background Camera", typeof(Camera)).GetComponent<Camera>();
            bgCam.transform.SetParent(transform, false);
            bgCam.targetDisplay = 1;
            bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.backgroundColor = Back;
            bgCam.cullingMask = 0;
            bgCam.depth = -100;

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
                int w = Screen.width, h = Screen.height;
                if (w <= 0 || h <= 0) continue;
                if (mirror == null || mirror.width != w || mirror.height != h)
                {
                    if (mirror != null) { mirror.Release(); Destroy(mirror); }
                    mirror = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "Guest Mirror" };
                    mirror.Create();
                    mirrorImage.texture = mirror;
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(mirror);
                // The capture comes out upside down where the GPU's texture origin is the top.
                mirrorImage.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
#endif
            }
        }

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
