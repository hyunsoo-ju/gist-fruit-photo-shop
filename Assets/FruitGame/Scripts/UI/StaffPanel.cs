using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The staff panel (Docs/DESIGN.md §15, M6 3.2): one place for every staff action and setting, so staff only
    /// remember **F1**. A Status card (camera, face, matte, mask, printer) over four pages: Gameplay, Camera, Display and
    /// Printing. Every row is clickable; while the panel's keys are on, the letter printed beside an action does it,
    /// ↑ ↓ pick a setting, ◀ ▶ change it, Enter runs an action, 1–4 or Tab switch pages and Esc turns the keys off.
    /// With one screen, F1 (or holding the top-left corner for 3 s) opens the panel over the right of the guest screen
    /// and turns its keys on; with two it's always on the staff screen and F1 only turns its keys on and off.
    /// Settings are saved on this computer (PlayerPrefs) and restored at the next launch; Reset goes back to GameConfig.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class StaffPanel : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] GameStateMachine game;
        [SerializeField] WebcamSource source;
        [SerializeField] PersonSegmenter segmenter;
        [SerializeField] BasketInputRouter router;
        [SerializeField] FaceDebugOverlay debug;
        [SerializeField] BlindTest blindTest;
        [SerializeField] Printer printer;
        [Tooltip("A text to copy the font from.")]
        [SerializeField] TMP_Text style;
        [SerializeField] float cornerHoldSeconds = 3f;

        /// <summary>Shown (always, with two screens).</summary>
        public static bool Open { get; private set; }
        /// <summary>The panel's letter keys are on: the game ignores its own letter keys meanwhile.</summary>
        public static bool KeysActive { get; private set; }
        /// <summary>The frame the panel last used a key in (so the game doesn't also act on it, e.g. Esc).</summary>
        public static int HandledFrame { get; private set; } = -1;

        /// <summary>A staff window used this frame's key (so the game doesn't act on it too).</summary>
        public static void Consume() => HandledFrame = Time.frameCount;

        const string PrefPrefix = "FruitGame.set.";
        static readonly Color CardColor = new Color(0.12f, 0.13f, 0.18f, 1f);
        // Every colour fully opaque: the game view composites alpha, so any transparency lets the game show through.
        static readonly Color PanelBack = new Color(0.06f, 0.07f, 0.1f, 1f);
        static readonly Color RowColor = new Color(0.1f, 0.11f, 0.14f, 1f);
        static readonly Color RowSelected = new Color(0.27f, 0.23f, 0.13f, 1f);
        static readonly Color HeadingColor = new Color(1f, 0.79f, 0.24f, 1f);
        static readonly Color TabColor = new Color(0.2f, 0.22f, 0.29f, 1f);
        static readonly Color TabActive = new Color(0.35f, 0.42f, 0.49f, 1f);

        // ---- Settings and actions -------------------------------------------------------------------------------------

        /// <summary>A setting (◀ ▶ change it) or an action (a letter, Enter or a click runs it).</summary>
        class Row
        {
            public string label;
            public string letter;           // action shortcut, or null
            public Func<string> value;      // what it's set to now
            public Action<int> change;      // settings: −1 / +1
            public Action run;              // actions
            public Func<bool> enabled = () => true;
            public Image back;
            public TMP_Text text;
        }

        /// <summary>A saved value: written as text to PlayerPrefs, with the GameConfig value as its default.</summary>
        class Pref
        {
            public string key;
            public Func<string> save;
            public Action<string> load;
            public string defaults;
        }

        readonly List<Pref> prefs = new List<Pref>();
        readonly List<(string name, List<Row> rows, RectTransform page)> pages = new List<(string, List<Row>, RectTransform)>();
        readonly List<Image> tabs = new List<Image>();
        int page;
        int selected;
        bool twoScreens;
        bool resetArmed;
        float resetArmedUntil;
        float cornerHeld;
        bool cornerFired;
        string[] printerNames = new string[0];

        RectTransform root;
        TMP_Text header;
        TMP_Text printerLine;
        RectTransform statusCard;
        RectTransform printingBoxArea;

        public RectTransform StatusCard => statusCard;
        public RectTransform PrintingBoxArea => printingBoxArea;

        void Awake()
        {
            DefinePrefs();
            LoadPrefs();
        }

        void OnDestroy()
        {
#if UNITY_EDITOR
            // GameConfig is an asset: put the Editor's copy back as it was, so play sessions don't change it.
            foreach (var p in prefs) p.load(p.defaults);
#endif
        }

        void DefinePrefs()
        {
            var inv = CultureInfo.InvariantCulture;
            void F(string key, Func<float> get, Action<float> set) => prefs.Add(new Pref
            {
                key = key, save = () => get().ToString(inv), load = s => { if (float.TryParse(s, NumberStyles.Float, inv, out var v)) set(v); }
            });
            void S(string key, Func<string> get, Action<string> set) => prefs.Add(new Pref { key = key, save = get, load = set });

            F("defaultMode", () => (int)config.defaultMode, v => config.defaultMode = (DefaultMode)(int)v);
            F("assistScale", () => config.assistScale, v => config.assistScale = v);
            F("sensitivity", () => config.sensitivity, v => config.sensitivity = v);
            F("steadyTolerance", () => config.steadyTolerance, v => config.steadyTolerance = v);
            F("steadyHoldTime", () => config.steadyHoldTime, v => config.steadyHoldTime = v);
            F("roundDuration", () => config.roundDuration, v => config.roundDuration = v);
            F("mirrorCamera", () => config.mirrorCamera ? 1 : 0, v => config.mirrorCamera = v > 0.5f);
            F("matteModel", () => (int)PersonSegmenter.Preferred, v => PersonSegmenter.Preferred = (PersonSegmenter.Option)(int)v);
            F("orientation", () => (int)config.orientation, v => config.orientation = (OrientationSetting)(int)v);
            F("language", () => (int)config.language, v => config.language = (Language)(int)v);
            F("screens", () => (int)config.screens, v => config.screens = (ScreenMode)(int)v);
            F("printCopies", () => config.printCopies, v => config.printCopies = Mathf.RoundToInt(v));
            S("printerName", () => config.printerName ?? "", v => config.printerName = v);
        }

        void LoadPrefs()
        {
            foreach (var p in prefs)
            {
                p.defaults = p.save();
                if (PlayerPrefs.HasKey(PrefPrefix + p.key)) p.load(PlayerPrefs.GetString(PrefPrefix + p.key));
            }
        }

        void Save()
        {
            foreach (var p in prefs) PlayerPrefs.SetString(PrefPrefix + p.key, p.save());
            PlayerPrefs.Save();
        }

        void ResetToDefaults()
        {
            foreach (var p in prefs)
            {
                p.load(p.defaults);
                PlayerPrefs.DeleteKey(PrefPrefix + p.key);
            }
            PlayerPrefs.Save();
            ApplyLive();
        }

        /// <summary>Pushes settings that need more than a config value into the running game.</summary>
        void ApplyLive()
        {
            Strings.SetLanguage(config.language);
            if (segmenter != null && segmenter.Current != PersonSegmenter.Preferred) segmenter.Use(PersonSegmenter.Preferred);
            if (game != null && game.State == GameState.Boot) Difficulty.ResetTo(config.defaultMode);
        }

        static int Step<T>(T[] options, T current, int dir)
        {
            int i = Array.IndexOf(options, current);
            if (i < 0) i = 0;
            return Mathf.Clamp(i + dir, 0, options.Length - 1);
        }

        static string Pick(float[] options, string[] labels, float v)
        {
            int best = 0;
            for (int i = 1; i < options.Length; i++) if (Mathf.Abs(options[i] - v) < Mathf.Abs(options[best] - v)) best = i;
            return labels[best];
        }

        static float Nearest(float[] options, float v, int dir)
        {
            int best = 0;
            for (int i = 1; i < options.Length; i++) if (Mathf.Abs(options[i] - v) < Mathf.Abs(options[best] - v)) best = i;
            return options[Mathf.Clamp(best + dir, 0, options.Length - 1)];
        }

        Row Setting(string label, Func<string> value, Action<int> change) =>
            new Row { label = label, value = value, change = d => { change(d); Save(); ApplyLive(); } };

        Row Action(string label, string letter, Func<string> value, Action run, Func<bool> enabled = null) =>
            new Row { label = label, letter = letter, value = value, run = run, enabled = enabled ?? (() => true) };

        void DefineRows()
        {
            bool OnTitle() => game != null && game.State == GameState.Boot;
            string OnOff(bool b) => b ? "On" : "Off";
            var assist = new[] { 0f, 0.5f, 1f };
            var assistNames = new[] { "Off", "Low", "Normal" };
            var tolerances = new[] { 0.01f, 0.015f, 0.02f, 0.03f, 0.04f };
            var holds = new[] { 0.5f, 0.75f, 1f, 1.5f, 2f };
            var rounds = new[] { 30f, 45f, 60f, 90f };

            pages.Clear();
            pages.Add(("Gameplay", new List<Row>
            {
                Setting("Mode each visit starts in", () => config.defaultMode.ToString(),
                    d => config.defaultMode = (DefaultMode)Mathf.Clamp((int)config.defaultMode + d, 0, 2)),
                Setting("Assist", () => Pick(assist, assistNames, config.assistScale), d => config.assistScale = Nearest(assist, config.assistScale, d)),
                Setting("Head sensitivity", () => config.sensitivity.ToString("0.0"),
                    d => config.sensitivity = Mathf.Clamp(Mathf.Round((config.sensitivity + d * 0.1f) * 10f) / 10f, 0.6f, 1.6f)),
                Setting("Calibration: how still", () => $"± {config.steadyTolerance:0.000}", d => config.steadyTolerance = Nearest(tolerances, config.steadyTolerance, d)),
                Setting("Calibration: hold time", () => $"{config.steadyHoldTime:0.##} s", d => config.steadyHoldTime = Nearest(holds, config.steadyHoldTime, d)),
                Setting("Round length (next round)", () => $"{config.roundDuration:0} s", d => config.roundDuration = Nearest(rounds, config.roundDuration, d)),
                Action("Easy for the next round", "E", () => OnOff(Difficulty.Easy), () => Difficulty.Easy = !Difficulty.Easy, OnTitle),
                Action("Recalibrate", null, () => OnTitle() ? "" : "on the title screen", () => game.StartCalibration(), OnTitle),
                Action("Basket follows the mouse", "M", () => OnOff(router != null && router.MouseControl), () => router?.ToggleMouseControl()),
            }, null));
            pages.Add(("Camera", new List<Row>
            {
                Action("Change camera", "C", () => source != null ? source.DeviceName ?? "none" : "none", () => CameraMenu.Toggle()),
                Setting("Mirror the camera", () => OnOff(config.mirrorCamera), d => config.mirrorCamera = d > 0),
                Setting("Background removal", () => ModelName(PersonSegmenter.Preferred),
                    d => PersonSegmenter.Preferred = (PersonSegmenter.Option)Mathf.Clamp((int)PersonSegmenter.Preferred + d, 0, 2)),
                Action("Rotation", null, () => "with the DSLR (M17)", () => { }, () => false),
                Action("Face box on the guest screen", "D", () => OnOff(debug != null && debug.Visible),
                    () => { if (debug != null) debug.Visible = !debug.Visible; }, () => !twoScreens),
            }, null));
            pages.Add(("Display", new List<Row>
            {
                Setting("Orientation", () => config.orientation.ToString(),
                    d => config.orientation = (OrientationSetting)Mathf.Clamp((int)config.orientation + d, 0, 2)),
                Setting("Language", () => config.language == Language.Korean ? "한국어" : "English",
                    d => config.language = d < 0 ? Language.Korean : Language.English),
                Setting("Screens (after restart)", () => config.screens.ToString(),
                    d => config.screens = (ScreenMode)Mathf.Clamp((int)config.screens + d, 0, 2)),
                Action("Blank the guest screen", "B", () => OnOff(blindTest != null && blindTest.On), () => blindTest?.Toggle()),
            }, null));
            pages.Add(("Printing", new List<Row>
            {
                Setting("Copies per print", () => config.printCopies.ToString(), d => config.printCopies = Mathf.Clamp(config.printCopies + d, 1, 2)),
                Setting("Printer", () => string.IsNullOrEmpty(config.printerName) ? "Default" : config.printerName, d =>
                {
                    var names = new List<string> { "" };
                    names.AddRange(printerNames);
                    config.printerName = names[Step(names.ToArray(), config.printerName ?? "", d)];
                }),
                Action("Paper tray refilled", "R", () => printer != null ? $"{printer.PaperLeft}/{config.trayCapacity}" : "", () => printer?.PaperRefilled()),
                Action("New ink cassette", "I", () => printer != null ? $"{printer.InkLeft}/{config.inkCapacity}" : "", () => printer?.InkReplaced()),
            }, null));
        }

        static string ModelName(PersonSegmenter.Option o) =>
            o == PersonSegmenter.Option.RvmMobileNet ? "MobileNetV3" : o == PersonSegmenter.Option.RvmResNet50 ? "ResNet-50" : "MediaPipe";

        // ---- Building -------------------------------------------------------------------------------------------------

        /// <summary>Builds the panel into `column` (StaffScreen gives it the right-hand column of its canvas).</summary>
        public void Build(RectTransform column, bool twoScreens)
        {
            this.twoScreens = twoScreens;
            root = column;
            DefineRows();
            printerNames = Printer.ListPrinters();

            // (Not `??`: a missing component is a Unity "fake null" that `??` doesn't see.)
            if (!column.TryGetComponent<Image>(out var bg)) bg = column.gameObject.AddComponent<Image>();
            bg.color = PanelBack;
            bg.raycastTarget = true; // clicks on the panel never reach the game behind it

            header = Text("Header", column, 22f);
            Place(header.rectTransform, 0f, 40f);
            header.color = HeadingColor;

            statusCard = Card("Status", column, 52f, 330f);
            printerLine = Text("Printer Line", statusCard, 20f);
            printerLine.textWrappingMode = TextWrappingModes.Normal;
            printerLine.overflowMode = TextOverflowModes.Overflow;
            var pl = printerLine.rectTransform;
            pl.anchorMin = new Vector2(0f, 0f); pl.anchorMax = new Vector2(1f, 0f); pl.pivot = new Vector2(0.5f, 0f);
            pl.anchoredPosition = new Vector2(0f, 10f); pl.sizeDelta = new Vector2(-30f, 50f);

            // Tabs.
            float tabTop = 52f + 330f + 12f;
            // The tab bar lines up with the cards and rows (12 in from each side).
            var tabBar = new GameObject("Tabs", typeof(RectTransform)).GetComponent<RectTransform>();
            tabBar.SetParent(column, false);
            tabBar.anchorMin = new Vector2(0f, 1f); tabBar.anchorMax = Vector2.one; tabBar.pivot = new Vector2(0.5f, 1f);
            tabBar.anchoredPosition = new Vector2(0f, -tabTop); tabBar.sizeDelta = new Vector2(-18f, 44f);
            for (int i = 0; i < pages.Count; i++)
            {
                int index = i;
                var t = Panel($"Tab {pages[i].name}", tabBar, TabColor, true);
                float w = (column.rect.width > 0 ? column.rect.width : 560f) / pages.Count;
                t.anchorMin = new Vector2((float)i / pages.Count, 1f);
                t.anchorMax = new Vector2((float)(i + 1) / pages.Count, 1f);
                t.pivot = new Vector2(0.5f, 1f);
                t.anchoredPosition = Vector2.zero;
                t.sizeDelta = new Vector2(-6f, 44f);
                t.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowPage(index));
                var label = Text("Label", t, 20f);
                label.text = $"{i + 1} {pages[i].name}";
                label.alignment = TextAlignmentOptions.Center;
                Fill(label.rectTransform);
                tabs.Add(t.GetComponent<Image>());
            }

            // Pages.
            float pageTop = tabTop + 44f + 8f;
            for (int p = 0; p < pages.Count; p++)
            {
                var (name, rows, _) = pages[p];
                var pageRect = new GameObject($"Page {name}", typeof(RectTransform)).GetComponent<RectTransform>();
                pageRect.SetParent(column, false);
                pageRect.anchorMin = Vector2.zero; pageRect.anchorMax = Vector2.one;
                pageRect.offsetMin = new Vector2(0f, 70f); pageRect.offsetMax = new Vector2(0f, -pageTop);
                for (int r = 0; r < rows.Count; r++) BuildRow(rows[r], pageRect, r, p);
                if (name == "Printing")
                {
                    printingBoxArea = new GameObject("Printer Box Area", typeof(RectTransform)).GetComponent<RectTransform>();
                    printingBoxArea.SetParent(pageRect, false);
                    printingBoxArea.anchorMin = Vector2.zero; printingBoxArea.anchorMax = new Vector2(1f, 1f);
                    printingBoxArea.offsetMin = new Vector2(16f, 0f); printingBoxArea.offsetMax = new Vector2(-16f, -rows.Count * 50f - 10f);
                }
                pages[p] = (name, rows, pageRect);
            }

            // Footer: reset (two clicks).
            var reset = Panel("Reset", column, new Color(0.45f, 0.2f, 0.22f, 1f), true);
            reset.anchorMin = new Vector2(0f, 0f); reset.anchorMax = new Vector2(1f, 0f); reset.pivot = new Vector2(0.5f, 0f);
            reset.anchoredPosition = new Vector2(0f, 14f); reset.sizeDelta = new Vector2(-32f, 44f);
            var resetLabel = Text("Label", reset, 20f);
            resetLabel.alignment = TextAlignmentOptions.Center;
            Fill(resetLabel.rectTransform);
            reset.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (resetArmed) { ResetToDefaults(); resetArmed = false; }
                else { resetArmed = true; resetArmedUntil = Time.unscaledTime + 4f; }
            });
            resetRow = resetLabel;

            ShowPage(0);
            Open = twoScreens;
            KeysActive = false;
            SetShown(Open);
        }

        TMP_Text resetRow;

        void BuildRow(Row row, RectTransform pageRect, int index, int pageIndex)
        {
            var r = Panel(row.label, pageRect, RowColor, true);
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -index * 50f); r.sizeDelta = new Vector2(-24f, 44f);
            row.back = r.GetComponent<Image>();
            var button = r.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() =>
            {
                selected = index;
                if (row.run != null) { if (row.enabled()) row.run(); }
                else row.change?.Invoke(+1);
            });
            row.text = Text("Text", r, 20f);
            row.text.richText = true;
            Fill(row.text.rectTransform, 14f);

            if (row.change != null)
            {
                // ◀ ▶ on the right for settings.
                AddArrow(r, "◀", -96f, () => { selected = index; row.change(-1); });
                AddArrow(r, "▶", -40f, () => { selected = index; row.change(+1); });
            }
        }

        void AddArrow(RectTransform rowRect, string glyph, float x, Action onClick)
        {
            var a = Panel("Arrow", rowRect, TabActive, true);
            a.anchorMin = a.anchorMax = new Vector2(1f, 0.5f); a.pivot = new Vector2(0.5f, 0.5f);
            a.anchoredPosition = new Vector2(x, 0f); a.sizeDelta = new Vector2(48f, 36f);
            a.gameObject.AddComponent<Button>().onClick.AddListener(() => onClick());
            var t = Text("Glyph", a, 20f);
            t.text = glyph;
            t.alignment = TextAlignmentOptions.Center;
            Fill(t.rectTransform);
        }

        void ShowPage(int index)
        {
            page = Mathf.Clamp(index, 0, pages.Count - 1);
            selected = 0;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].page.gameObject.SetActive(i == page);
                tabs[i].color = i == page ? TabActive : TabColor;
            }
            if (pages[page].name == "Printing") printerNames = Printer.ListPrinters();
        }

        void SetShown(bool shown)
        {
            if (root != null) root.gameObject.SetActive(shown);
        }

        // ---- Running --------------------------------------------------------------------------------------------------

        void Start() => ApplyLive();

        void Update()
        {
            if (root == null) return;
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) ToggleKeys();
            CornerHold();

            if (KeysActive && kb != null && !CameraMenu.IsOpen) HandleKeys(kb);
            if (resetArmed && Time.unscaledTime > resetArmedUntil) resetArmed = false;
            if (Open) Refresh();
        }

        /// <summary>F1: one screen opens/closes the panel (keys on while open); two screens turn its keys on/off.</summary>
        void ToggleKeys()
        {
            HandledFrame = Time.frameCount;
            if (twoScreens) KeysActive = !KeysActive;
            else
            {
                Open = !Open;
                KeysActive = Open;
                SetShown(Open);
            }
        }

        /// <summary>Touch-only booths: holding the top-left corner of the guest screen opens or closes the panel.</summary>
        void CornerHold()
        {
            if (twoScreens) return;
            var pointer = Pointer.current;
            bool held = false;
            if (pointer != null && pointer.press.isPressed)
            {
                var p = pointer.position.ReadValue();
                var size = GuestDisplay.Size;
                held = p.x < size.x * 0.08f && p.y > size.y * 0.88f;
            }
            if (!held) { cornerHeld = 0f; cornerFired = false; return; }
            cornerHeld += Time.unscaledDeltaTime;
            if (!cornerFired && cornerHeld >= cornerHoldSeconds)
            {
                cornerFired = true;
                ToggleKeys();
            }
        }

        void HandleKeys(Keyboard kb)
        {
            var rows = pages[page].rows;
            bool used = true;
            if (kb.escapeKey.wasPressedThisFrame) ToggleKeys();
            else if (kb.tabKey.wasPressedThisFrame) ShowPage((page + 1) % pages.Count);
            else if (kb.digit1Key.wasPressedThisFrame) ShowPage(0);
            else if (kb.digit2Key.wasPressedThisFrame) ShowPage(1);
            else if (kb.digit3Key.wasPressedThisFrame) ShowPage(2);
            else if (kb.digit4Key.wasPressedThisFrame) ShowPage(3);
            else if (kb.upArrowKey.wasPressedThisFrame) selected = Mathf.Max(0, selected - 1);
            else if (kb.downArrowKey.wasPressedThisFrame) selected = Mathf.Min(rows.Count - 1, selected + 1);
            else if (kb.leftArrowKey.wasPressedThisFrame) rows[selected].change?.Invoke(-1);
            else if (kb.rightArrowKey.wasPressedThisFrame) rows[selected].change?.Invoke(+1);
            else if (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            {
                var row = rows[selected];
                if (row.run != null && row.enabled()) row.run();
            }
            else used = Letter(kb);
            if (used) HandledFrame = Time.frameCount;
        }

        /// <summary>An action's letter works from any page.</summary>
        bool Letter(Keyboard kb)
        {
            foreach (var (_, rows, _) in pages)
                foreach (var row in rows)
                {
                    if (row.letter == null || row.run == null) continue;
                    var key = kb[(Key)Enum.Parse(typeof(Key), row.letter)];
                    if (key.wasPressedThisFrame)
                    {
                        if (row.enabled()) row.run();
                        return true;
                    }
                }
            return false;
        }

        void Refresh()
        {
            header.text = twoScreens
                ? (KeysActive ? "Staff panel · <color=#7CFC9A>keys on</color>  (F1 or Esc: off)" : "Staff panel · keys off  (F1: on)")
                : "Staff panel  (F1 or Esc: close)";
            if (printerLine != null && printer != null)
                printerLine.text = (printer.HasProblem ? "<color=#FF7A6E>" : "") + "Printer: " + printer.Summary;
            if (resetRow != null) resetRow.text = resetArmed ? "Click again to reset every setting" : "Reset settings to defaults";

            var rows = pages[page].rows;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool on = row.enabled();
                string letter = row.letter != null ? $"<color=#FFC93C>[{row.letter}]</color> " : "";
                string value = row.value != null ? row.value() : "";
                string body = $"{letter}{row.label}";
                // Settings leave room on the right for ◀ ▶.
                string right = row.change != null ? $"<pos=55%>{value}" : $"<pos=62%>{value}";
                row.text.text = on ? body + right : $"<color=#777777>{body}{right}</color>";
                row.back.color = i == selected && KeysActive ? RowSelected : RowColor;
            }
        }

        // ---- UI helpers -----------------------------------------------------------------------------------------------

        RectTransform Card(string title, RectTransform parent, float top, float height)
        {
            var card = Panel(title, parent, CardColor, false);
            card.anchorMin = new Vector2(0f, 1f); card.anchorMax = Vector2.one; card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -top); card.sizeDelta = new Vector2(-24f, height);
            var head = Text("Heading", card, 24f);
            head.text = title;
            head.color = HeadingColor;
            Place(head.rectTransform, 10f, 34f, 16f);
            return card;
        }

        static RectTransform Panel(string name, Transform parent, Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return (RectTransform)go.transform;
        }

        TMP_Text Text(string name, Transform parent, float size)
        {
            var t = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            if (style != null) { t.font = style.font; t.fontSharedMaterial = style.fontSharedMaterial; }
            t.fontSize = size;
            t.color = Color.white;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.alignment = TextAlignmentOptions.Left;
            t.raycastTarget = false;
            return t;
        }

        static void Place(RectTransform r, float top, float height, float inset = 16f)
        {
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, -top); r.sizeDelta = new Vector2(-inset * 2f, height);
        }

        static void Fill(RectTransform r, float inset = 0f)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, 0f); r.offsetMax = new Vector2(-inset, 0f);
        }
    }
}
