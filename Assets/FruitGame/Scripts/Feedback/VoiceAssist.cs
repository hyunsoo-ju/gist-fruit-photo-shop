using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Everything around the voice that isn't the voice (Docs/DESIGN.md M6 item 6): the title's voice assist and high
    /// contrast buttons, a touch bar of speech controls and the caption strip while voice assist is on, the short
    /// spoken invitation when someone comes up to the title, and optional **numpad** control. Nothing needs the numpad;
    /// its keys (or the number row) mirror the touch controls: 7 repeat, 8/2 volume, 9 speed, 3 stop, 4/6 pick a button
    /// (the steer in the round), 5 press, 1 back, 0 call staff. Added by Narrator at startup.
    /// </summary>
    public class VoiceAssist : MonoBehaviour
    {
        const float InviteCooldown = 30f;
        const float FaceAwayBeforeInvite = 3f;
        static readonly HashSet<string> BackKeys = new HashSet<string> { "walk.back", "common.back", "photo.close", "common.mainMenu" };
        static readonly Color FocusColour = new Color32(0xFF, 0xD4, 0x00, 0xFF);

        public static VoiceAssist Instance { get; private set; }

        Narrator narrator;
        GameStateMachine game;
        FaceTracker tracker;
        FallbackInput fallback;
        BasketInputRouter router;
        RectTransform frame;
        Canvas guestCanvas;
        GameObject bar, captionStrip;
        readonly Dictionary<TMP_Text, RectTransform> plates = new Dictionary<TMP_Text, RectTransform>();
        readonly List<TMP_Text> plateKeys = new List<TMP_Text>();
        float nextPlateScan;
        TMP_Text captionText;
        RectTransform focusRing;
        Button focused;
        float lastInvite = -999f, lastFaceSeen = -999f;
        bool faceWasThere, built;

        void Awake() => Instance = this;

        void Start()
        {
            narrator = GetComponent<Narrator>();
            game = FindFirstObjectByType<GameStateMachine>();
            tracker = FindFirstObjectByType<FaceTracker>();
            fallback = FindFirstObjectByType<FallbackInput>();
            router = FindFirstObjectByType<BasketInputRouter>();
            if (VoxelButtons.Instance == null || VoxelButtons.Instance.Frame == null) return;
            frame = VoxelButtons.Instance.Frame;
            guestCanvas = frame.GetComponentInParent<Canvas>().rootCanvas;
            Build();
            built = true;
        }

        // ---- Building ------------------------------------------------------------------------------------------------

        void Build()
        {
            var menu = frame.Find("Start Menu");
            var auditory = menu != null ? menu.Find("Auditory Button") : null;
            var calibrate = menu != null ? menu.Find("Calibrate Button") : null;
            var pause = frame.Find("Pause Menu/Menu Button");
            if (auditory != null)
            {
                RemoveLayout(auditory.gameObject);
                StackUnderPlay((RectTransform)auditory, 0);
                auditory.gameObject.SetActive(true);
            }
            if (calibrate != null)
            {
                var contrast = Instantiate(calibrate.gameObject, menu).GetComponent<Button>();
                contrast.name = "Contrast Button";
                contrast.onClick = new Button.ButtonClickedEvent();
                contrast.onClick.AddListener(ToggleContrast);
                var label = contrast.GetComponentInChildren<LocalizedText>(true);
                if (label != null) label.Key = "start.contrast";
                RemoveLayout(contrast.gameObject);
                StackUnderPlay((RectTransform)contrast.transform, 1);
                contrast.gameObject.SetActive(true);
                // The "voice assist on" tag sits under the lowest button.
                var tag = auditory != null ? auditory.Find("Easy Tag") : null;
                if (tag != null) tag.SetParent(contrast.transform, false);
            }

            // Focus ring: four bars, moved onto the focused button.
            focusRing = new GameObject("Focus Ring", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            focusRing.GetComponent<LayoutElement>().ignoreLayout = true;
            focusRing.SetParent(frame, false);
            Edge(new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -12f), Vector2.zero);
            Edge(Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 12f));
            Edge(Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(12f, 0f));
            Edge(new Vector2(1f, 0f), Vector2.one, new Vector2(-12f, 0f), Vector2.zero);
            focusRing.gameObject.SetActive(false);

            // Caption strip along the bottom (opaque: the game view composites alpha).
            captionStrip = new GameObject("Caption Strip", typeof(RectTransform), typeof(Image));
            captionStrip.transform.SetParent(frame, false);
            var sr = (RectTransform)captionStrip.transform;
            sr.anchorMin = Vector2.zero; sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(0.5f, 0f);
            sr.offsetMin = Vector2.zero; sr.offsetMax = new Vector2(0f, 112f);
            var strip = captionStrip.GetComponent<Image>();
            strip.color = Color.black;
            strip.raycastTarget = false;
            var source = frame.GetComponentInChildren<TMP_Text>(true);
            captionText = new GameObject("Caption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            captionText.transform.SetParent(sr, false);
            var tr = captionText.rectTransform;
            Stretch(tr);
            tr.offsetMin = new Vector2(24f, 4f); tr.offsetMax = new Vector2(-24f, -4f);
            captionText.font = source.font;
            captionText.fontSharedMaterial = source.font.material;
            captionText.fontSize = 32f;
            captionText.color = Color.white;
            captionText.alignment = TextAlignmentOptions.Center;
            captionText.textWrappingMode = TextWrappingModes.Normal;
            captionText.raycastTarget = false;
            captionStrip.SetActive(false);

            // Touch speech controls: a column on the left edge.
            bar = new GameObject("Voice Bar", typeof(RectTransform), typeof(VerticalLayoutGroup));
            bar.transform.SetParent(frame, false);
            var br = (RectTransform)bar.transform;
            br.anchorMin = br.anchorMax = new Vector2(0f, 0.55f);
            br.pivot = new Vector2(0f, 0.5f);
            br.anchoredPosition = new Vector2(12f, 0f);
            br.sizeDelta = new Vector2(148f, 560f);
            var layout = bar.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            if (pause != null)
            {
                BarButton(pause.gameObject, "voice.repeat", () => narrator.Repeat());
                BarButton(pause.gameObject, "voice.stop", () => narrator.StopOrMute());
                BarButton(pause.gameObject, "voice.louder", () => narrator.ChangeVolume(1));
                BarButton(pause.gameObject, "voice.quieter", () => narrator.ChangeVolume(-1));
                BarButton(pause.gameObject, "voice.speed", () => narrator.NextSpeed());
            }
            bar.SetActive(false);
        }

        void BarButton(GameObject style, string key, System.Action action)
        {
            var b = Instantiate(style, bar.transform).GetComponent<Button>();
            b.name = key;
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(() => action());
            RemoveLayout(b.gameObject);
            var label = b.GetComponentInChildren<LocalizedText>(true);
            if (label != null) label.Key = key;
            var text = b.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.fontSize = 32f;
            b.gameObject.SetActive(true);
        }

        void Edge(Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            var img = new GameObject("Edge", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(focusRing, false);
            img.color = FocusColour;
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = min; r.anchorMax = max; r.offsetMin = offMin; r.offsetMax = offMax;
        }

        static void RemoveLayout(GameObject go)
        {
            foreach (var a in go.GetComponents<AspectLayout>()) Destroy(a);
        }

        /// <summary>The title's buttons in one column, as wide as Play and centred on it, in either layout.</summary>
        void StackUnderPlay(RectTransform r, int row)
        {
            const float RowGap = 150f, Width = 560f, Height = 130f;
            var play = frame.Find("Start Menu/Play Button").GetComponent<AspectLayout>();
            AspectLayout.Pose Below(AspectLayout.Pose p) => new AspectLayout.Pose
            {
                set = true,
                anchorMin = p.anchorMin,
                anchorMax = p.anchorMax,
                pivot = p.pivot,
                anchoredPosition = p.anchoredPosition - new Vector2(0f, p.sizeDelta.y * 0.5f + 40f + Height * 0.5f + row * RowGap),
                sizeDelta = new Vector2(Width, Height),
                localScale = Vector3.one,
            };
            var layout = r.gameObject.AddComponent<AspectLayout>();
            layout.portrait = Below(play.portrait);
            layout.landscape = Below(play.landscape);
            layout.Apply();
        }

        static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        // ---- Each frame ----------------------------------------------------------------------------------------------

        bool InRound => game.State == GameState.Playing || game.State == GameState.RoundEnding;

        void Update()
        {
            if (!built || game == null) return;
            bool voice = Difficulty.Auditory;
            Toggle(bar, voice && !InRound);
            bool showCaption = voice && narrator.Caption.Length > 0;
            Toggle(captionStrip, showCaption);
            if (showCaption)
            {
                captionText.text = narrator.Caption;
                captionStrip.transform.SetAsLastSibling();
                // At the top on the menus (their Back buttons sit at the bottom), at the bottom in the round (score and timer).
                bool bottom = InRound || game.State == GameState.Countdown;
                var sr = (RectTransform)captionStrip.transform;
                float y = bottom ? 0f : 1f;
                sr.anchorMin = new Vector2(0f, y);
                sr.anchorMax = new Vector2(1f, y);
                sr.pivot = new Vector2(0.5f, y);
                float height = captionText.GetPreferredValues(captionText.text, frame.rect.width - 48f, 0f).y + 16f;
                sr.offsetMin = Vector2.zero;
                sr.offsetMax = Vector2.zero;
                sr.sizeDelta = new Vector2(0f, height);
            }
            RefreshContrast();
            UpdateInvite();
            if (focused != null && !focused.isActiveAndEnabled) SetFocus(null);
            if (fallback != null) FallbackInput.KeyAxis = 0f;
            UpdateKeys();
        }

        static void Toggle(GameObject go, bool on) { if (go.activeSelf != on) go.SetActive(on); }

        /// <summary>High contrast: a black plate behind every text that sits straight on the scenery.</summary>
        void RefreshContrast()
        {
            if (!HighContrast.On)
            {
                foreach (var p in plates.Values) if (p != null) Destroy(p.gameObject);
                plates.Clear();
                return;
            }
            if (Time.unscaledTime >= nextPlateScan)
            {
                nextPlateScan = Time.unscaledTime + 0.25f;
                foreach (var t in frame.GetComponentsInChildren<TMP_Text>(false))
                    if (!plates.ContainsKey(t) && OnScenery(t)) plates[t] = MakePlate(t);
            }
            plateKeys.Clear();
            plateKeys.AddRange(plates.Keys);
            foreach (var t in plateKeys)
            {
                var plate = plates[t];
                if (t == null || plate == null)
                {
                    if (plate != null) Destroy(plate.gameObject);
                    plates.Remove(t);
                    continue;
                }
                var bounds = t.textBounds;
                bool show = t.isActiveAndEnabled && t.text.Length > 0 && bounds.size.x > 0f && bounds.size.y > 0f;
                Toggle(plate.gameObject, show);
                if (!show) continue;
                plate.position = t.transform.TransformPoint(bounds.center);
                plate.sizeDelta = Vector2.Scale(bounds.size, t.transform.localScale) + new Vector2(32f, 20f);
            }
        }

        bool OnScenery(TMP_Text t)
        {
            if (!t.enabled || t.color.a < 0.5f || t.name == "Popup" || t.transform.IsChildOf(captionStrip.transform)) return false;
            for (var p = t.transform.parent; p != null && p != frame; p = p.parent)
                if (p.TryGetComponent<Graphic>(out var g) && g.enabled && g.color.a > 0.5f) return false;
            return true;
        }

        static RectTransform MakePlate(TMP_Text t)
        {
            var plate = new GameObject("Contrast Plate", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<RectTransform>();
            plate.GetComponent<LayoutElement>().ignoreLayout = true;
            plate.SetParent(t.transform.parent, false);
            plate.SetSiblingIndex(t.transform.GetSiblingIndex());
            plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(0.5f, 0.5f);
            var image = plate.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return plate;
        }

        public void ToggleContrast()
        {
            HighContrast.On = !HighContrast.On;
            narrator.Click();
            if (Difficulty.Auditory) narrator.SayFocus(HighContrast.On ? "contrast_on" : "contrast_off");
        }

        /// <summary>One short quiet line when someone comes up to the title, for visitors who can't see the screen.</summary>
        void UpdateInvite()
        {
            bool face = tracker != null && tracker.HasFace;
            if (game.State == GameState.Boot && !Difficulty.Auditory && face && !faceWasThere
                && Time.unscaledTime - lastFaceSeen >= FaceAwayBeforeInvite && Time.unscaledTime - lastInvite >= InviteCooldown)
            {
                lastInvite = Time.unscaledTime;
                narrator.Quiet = true;
                narrator.SayNow("title_invite");
            }
            if (face) lastFaceSeen = Time.unscaledTime;
            faceWasThere = face;
        }

        // ---- Numpad (optional) ---------------------------------------------------------------------------------------

        static bool Pressed(Keyboard kb, Key numpad, Key digit) => kb[numpad].wasPressedThisFrame || kb[digit].wasPressedThisFrame;
        static bool Held(Keyboard kb, Key numpad, Key digit) => kb[numpad].isPressed || kb[digit].isPressed;

        void UpdateKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            // Voice assist's "press 5 when you've understood": the only way on, and it works with the staff panel's keys on.
            if (Pressed(kb, Key.Numpad5, Key.Digit5) && game.AffirmIfWaiting()) { narrator.Click(); return; }
            if (StaffPanel.KeysActive || StaffPanel.HandledFrame == Time.frameCount) return;

            if (Pressed(kb, Key.Numpad8, Key.Digit8)) narrator.ChangeVolume(1);
            if (Pressed(kb, Key.Numpad2, Key.Digit2)) narrator.ChangeVolume(-1);
            if (Pressed(kb, Key.Numpad9, Key.Digit9)) narrator.NextSpeed();
            if (Pressed(kb, Key.Numpad3, Key.Digit3)) narrator.StopOrMute();
            if (Pressed(kb, Key.Numpad0, Key.Digit0)) narrator.SayNow("staff_called");

            if (InRound)
            {
                float axis = (Held(kb, Key.Numpad6, Key.Digit6) ? 1f : 0f) - (Held(kb, Key.Numpad4, Key.Digit4) ? 1f : 0f);
                if (axis != 0f && router != null) router.UseKeysForThisVisit();
                FallbackInput.KeyAxis = axis;
                if (Pressed(kb, Key.Numpad7, Key.Digit7)) game.SayRoundStatus();
                if (Pressed(kb, Key.Numpad5, Key.Digit5)) game.Pause();
                return;
            }

            if (Pressed(kb, Key.Numpad7, Key.Digit7)) narrator.Repeat();
            if (Pressed(kb, Key.Numpad4, Key.Digit4)) Move(-1);
            if (Pressed(kb, Key.Numpad6, Key.Digit6)) Move(1);
            if (Pressed(kb, Key.Numpad1, Key.Digit1)) PressBack();
            if (Pressed(kb, Key.Numpad5, Key.Digit5)) PressFocused();
        }

        void PressFocused()
        {
            // On the title, the first 5 is the one the invitation asks for.
            if (game.State == GameState.Boot && !Difficulty.Auditory) { game.ToggleAuditory(); return; }
            if (focused == null) { Move(1); return; }
            var target = focused;
            narrator.Click();
            narrator.Stop();
            target.onClick.Invoke();
        }

        void PressBack()
        {
            foreach (var b in GuestButtons())
                if (BackKeys.Contains(KeyOf(b)))
                {
                    narrator.Click();
                    narrator.Stop();
                    b.onClick.Invoke();
                    return;
                }
            narrator.SayFocus("no_back");
        }

        void Move(int by)
        {
            var list = GuestButtons();
            if (list.Count == 0) return;
            int i = list.IndexOf(focused);
            i = i < 0 ? (by > 0 ? 0 : list.Count - 1) : (i + by + list.Count) % list.Count;
            SetFocus(list[i]);
            string key = KeyOf(list[i]);
            if (key != null) narrator.SayFocus(LineOf(key));
        }

        void SetFocus(Button b)
        {
            focused = b;
            focusRing.SetParent(b != null ? b.transform : frame, false);
            focusRing.gameObject.SetActive(b != null);
            if (b == null) return;
            focusRing.anchorMin = Vector2.zero;
            focusRing.anchorMax = Vector2.one;
            focusRing.offsetMin = new Vector2(-14f, -14f);
            focusRing.offsetMax = new Vector2(14f, 14f);
            focusRing.SetAsLastSibling();
        }

        static string LineOf(string key) => "btn_" + key.Replace('.', '_');

        /// <summary>The clips naming this screen's buttons, in the order 4 and 6 move through them.</summary>
        public List<string> ButtonLines()
        {
            var lines = new List<string>();
            foreach (var b in GuestButtons())
            {
                string key = KeyOf(b);
                if (key != null) lines.Add(LineOf(key));
            }
            return lines;
        }

        /// <summary>The string key that names a button; a setup card is named by its holder.</summary>
        static string KeyOf(Button b)
        {
            if (b.transform.parent != null && b.name == "Card") return b.transform.parent.name;
            var label = b.GetComponentInChildren<LocalizedText>(true);
            return label != null ? label.Key : null;
        }

        /// <summary>The guest screen's usable buttons, top to bottom then left to right; an open phone panel keeps focus inside it.</summary>
        List<Button> GuestButtons()
        {
            var all = new List<Button>();
            bool phone = false;
            foreach (var b in FindObjectsByType<Button>(FindObjectsInactive.Exclude))
            {
                if (!b.interactable || b.GetComponentInParent<Canvas>().rootCanvas != guestCanvas) continue;
                if (b.transform.IsChildOf(bar.transform) || b.name == "Pause Button") continue;
                all.Add(b);
                phone |= b.transform.parent != null && b.GetComponentInParent<RectTransform>().root != null && IsInPhonePanel(b);
            }
            if (phone) all.RemoveAll(b => !IsInPhonePanel(b));
            all.Sort((a, c) =>
            {
                var pa = VoxelButtons.Instance.FromMiddle((RectTransform)a.transform);
                var pc = VoxelButtons.Instance.FromMiddle((RectTransform)c.transform);
                int row = Mathf.RoundToInt(pc.y / 80f).CompareTo(Mathf.RoundToInt(pa.y / 80f));
                return row != 0 ? row : pa.x.CompareTo(pc.x);
            });
            return all;
        }

        static bool IsInPhonePanel(Button b)
        {
            for (var t = b.transform; t != null; t = t.parent)
                if (t.name == "Phone Panel") return true;
            return false;
        }
    }
}
