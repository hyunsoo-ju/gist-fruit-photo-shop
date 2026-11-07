using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Staff camera menu (Docs/DESIGN.md §13; the staff panel's Change camera, C): lists every camera the computer sees, plus "Auto" (the preferred name
    /// in GameConfig, else the first external camera), with a live preview of the camera in use. Click a row or press its
    /// number to switch; the choice is remembered across restarts. English, like the other debug views. Built at
    /// runtime on the UI canvas, in the debug text's font.
    /// </summary>
    public class CameraMenu : MonoBehaviour
    {
        [SerializeField] WebcamSource source;
        [Tooltip("A debug text to copy the font and colour from (the F1 status line).")]
        [SerializeField] TMP_Text style;
        [SerializeField] float fontSize = 36f;

        /// <summary>True while the menu is open (other staff boxes leave the number keys to it).</summary>
        public static bool IsOpen { get; private set; }

        static CameraMenu instance;
        bool toggleRequested;

        /// <summary>Opens or closes the menu (the staff panel's "Change camera", C).</summary>
        public static void Toggle()
        {
            if (instance != null) instance.toggleRequested = true;
        }

        static readonly Color Panel = new Color(0.08f, 0.09f, 0.14f, 1f); // fully opaque: the game view composites alpha
        static readonly Color Row = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color RowActive = new Color(0.2f, 1f, 0.45f, 0.22f);

        GameObject root;
        TMP_Text header;
        RawImage preview;
        RectTransform list;
        readonly List<(string device, Image back, TMP_Text label)> rows = new List<(string, Image, TMP_Text)>();
        int deviceCount = -1;

        void Awake()
        {
            instance = this;
            Build();
        }

        GameObject menuCanvas;

        void OnDisable() => IsOpen = false;

        void OnDestroy()
        {
            if (menuCanvas != null) Destroy(menuCanvas);
        }

        void Build()
        {
            // Its own overlay canvas above everything (like the F5 cover), scaled like the game's UI.
            // A root canvas: nested under the game's UI it would inherit that UI's fade and draw order.
            var canvasGo = new GameObject("Camera Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            menuCanvas = canvasGo;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 31000;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            var uiScaler = GetComponentInParent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = uiScaler != null ? uiScaler.referenceResolution : new Vector2(2560f, 1440f);
            scaler.matchWidthOrHeight = uiScaler != null ? uiScaler.matchWidthOrHeight : 0.5f;

            root = new GameObject("Camera Menu", typeof(RectTransform), typeof(Image));
            root.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1500f, 760f);
            root.GetComponent<Image>().color = Panel;

            header = Text("Header", root.transform, TextAlignmentOptions.TopLeft);
            Place(header.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -32f), new Vector2(1420f, 110f));

            list = new GameObject("List", typeof(RectTransform)).GetComponent<RectTransform>();
            list.SetParent(root.transform, false);
            Place(list, new Vector2(0f, 1f), new Vector2(40f, -160f), new Vector2(820f, 560f));

            preview = new GameObject("Preview", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            preview.transform.SetParent(root.transform, false);
            preview.raycastTarget = false;
            Place(preview.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -160f), new Vector2(560f, 560f), new Vector2(1f, 1f));

            root.SetActive(false);
        }

        static void Place(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot ?? new Vector2(0f, 1f);
            r.anchoredPosition = pos;
            r.sizeDelta = size;
        }

        TMP_Text Text(string name, Transform parent, TextAlignmentOptions align)
        {
            var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            if (style != null)
            {
                t.font = style.font;
                t.fontSharedMaterial = style.fontSharedMaterial;
                t.color = style.color;
            }
            t.fontSize = fontSize;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Rebuilds the rows: Auto, then each camera (cameras can be plugged in while the game runs).</summary>
        void BuildRows()
        {
            foreach (Transform c in list) Destroy(c.gameObject);
            rows.Clear();
            var names = new List<string> { null };
            foreach (var d in WebCamTexture.devices) names.Add(d.name);
            deviceCount = names.Count - 1;
            for (int i = 0; i < names.Count && i < 10; i++)
            {
                string device = names[i];
                var go = new GameObject(device ?? "Auto", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(list, false);
                Place((RectTransform)go.transform, new Vector2(0f, 1f), new Vector2(0f, -i * 84f), new Vector2(820f, 72f));
                var back = go.GetComponent<Image>();
                go.GetComponent<Button>().onClick.AddListener(() => Pick(device));
                var label = Text("Label", go.transform, TextAlignmentOptions.Left);
                var lr = label.rectTransform;
                lr.anchorMin = Vector2.zero;
                lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(20f, 0f);
                lr.offsetMax = new Vector2(-20f, 0f);
                label.text = $"[{i}] {device ?? "Auto"}";
                rows.Add((device, back, label));
            }
        }

        void Pick(string device)
        {
            if (source == null) return;
            source.Use(device);
            Refresh();
        }

        void Refresh()
        {
            string choice = WebcamSource.Choice;
            for (int i = 0; i < rows.Count; i++)
            {
                var (device, back, label) = rows[i];
                bool selected = string.IsNullOrEmpty(choice) ? device == null : device == choice;
                bool live = device != null && source != null && device == source.DeviceName;
                back.color = selected ? RowActive : Row;
                label.text = $"[{i}] {device ?? "Auto"}" + (selected ? "  (selected)" : "") + (live ? "  * in use" : "");
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (toggleRequested || (IsOpen && kb != null && kb.escapeKey.wasPressedThisFrame))
            {
                if (!toggleRequested) StaffPanel.Consume(); // Esc closed the menu: the game shouldn't also pause
                toggleRequested = false;
                IsOpen = !IsOpen;
                // On the staff screen when there is one (StaffScreen), else over the game.
                menuCanvas.GetComponent<Canvas>().targetDisplay = StaffScreen.StaffDisplay;
                root.SetActive(IsOpen);
                if (IsOpen) BuildRows();
            }
            if (!IsOpen) return;

            if (WebCamTexture.devices.Length != deviceCount) BuildRows();
            if (kb != null)
                for (int i = 0; i < rows.Count; i++)
                {
                    var key = i == 0 ? kb.digit0Key : kb[(Key)((int)Key.Digit1 + i - 1)];
                    if (key.wasPressedThisFrame) Pick(rows[i].device);
                }

            Refresh();
            string auto = "Auto = GameConfig's preferred camera, else the first external camera.";
            header.text = $"Camera  (Esc or C to close)\n{(source != null ? source.Status : "no camera source")}\n" +
                          "Click a camera or press its number. " + auto;

            // Live preview of the camera in use, at its own shape inside the square.
            if (source != null && source.Output != null)
            {
                preview.texture = source.Output;
                float aspect = (float)source.Output.width / source.Output.height;
                preview.rectTransform.sizeDelta = aspect >= 1f ? new Vector2(560f, 560f / aspect) : new Vector2(560f * aspect, 560f);
            }
        }
    }
}
