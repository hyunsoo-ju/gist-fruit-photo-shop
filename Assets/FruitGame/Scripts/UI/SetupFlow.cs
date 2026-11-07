using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The walkthrough between Start and the round (Docs/DESIGN.md §3, §15): each visitor plays once, so every visit
    /// goes **1 · how to move** (sliding, recommended, or turning, for players who find moving their body hard; each
    /// card shows a looping pixel figure, MoveDiagram) → **2 · difficulty** (Easy, Normal, Sound) → **3 · calibration**
    /// (CalibrationController), then the countdown. One step per screen: a big question, large cards to tap, step dots
    /// and Back. Keys: ◀ ▶ pick, Space / Enter choose, Esc back; A picks Sound mode. The narrator reads each step
    /// aloud for everyone (quieter outside Sound mode). Built at runtime in the HUD frame, in the game's own keycap and
    /// panel art.
    /// </summary>
    public class SetupFlow : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] Narrator narrator;
        [Tooltip("The HUD frame to build in.")]
        [SerializeField] RectTransform frame;
        [Tooltip("The cream panel (its sprite and look are copied for the cards).")]
        [SerializeField] Image panelStyle;
        [Tooltip("A big title text to copy the style from (sticker).")]
        [SerializeField] TMP_Text titleStyle;
        [Tooltip("A dark text on the cream panel to copy the style from.")]
        [SerializeField] TMP_Text bodyStyle;
        [Tooltip("A slate keycap to clone for Back.")]
        [SerializeField] Button backStyle;

        public bool Active => root != null && root.gameObject.activeSelf;

        /// <summary>Choices made; the game starts calibration (true) or goes back to the title (false).</summary>
        public event Action<bool> Finished;

        class Card
        {
            public RectTransform rect;
            public Image highlight;
            public Action choose;
        }

        RectTransform root, cardRow;
        TMP_Text title, dots;
        readonly List<Card> cards = new List<Card>();
        int step, highlighted;

        static readonly Color Highlight = new Color32(0xC2, 0x18, 0x5B, 0xFF);

        void Awake() => Build();

        void Build()
        {
            root = new GameObject("Setup Flow", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(frame, false);
            Stretch(root, Vector2.zero, Vector2.one);

            title = Instantiate(titleStyle, root);
            title.name = "Question";
            Strip(title.gameObject);
            var tr = title.rectTransform;
            tr.anchorMin = new Vector2(0.05f, 0.80f); tr.anchorMax = new Vector2(0.95f, 0.94f);
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            title.alignment = TextAlignmentOptions.Center;

            dots = Instantiate(titleStyle, root);
            dots.name = "Step Dots";
            Strip(dots.gameObject);
            var dr = dots.rectTransform;
            dr.anchorMin = new Vector2(0.3f, 0.94f); dr.anchorMax = new Vector2(0.7f, 0.99f);
            dr.offsetMin = dr.offsetMax = Vector2.zero;
            dots.fontSize = 48;
            dots.alignment = TextAlignmentOptions.Center;

            cardRow = new GameObject("Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            cardRow.SetParent(root, false);
            cardRow.anchorMin = new Vector2(0.04f, 0.17f); cardRow.anchorMax = new Vector2(0.96f, 0.78f);
            cardRow.offsetMin = cardRow.offsetMax = Vector2.zero;
            var row = cardRow.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 48f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            var back = Instantiate(backStyle, root);
            back.name = "Back Button";
            back.onClick = new Button.ButtonClickedEvent();
            back.onClick.AddListener(Back);
            var label = back.GetComponentInChildren<LocalizedText>(true);
            if (label != null) label.Key = "walk.back";
            var br = (RectTransform)back.transform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.07f);
            br.pivot = new Vector2(0.5f, 0.5f);
            br.anchoredPosition = Vector2.zero;

            root.gameObject.SetActive(false);
            Strings.Changed += Refresh;
        }

        void OnDestroy() => Strings.Changed -= Refresh;

        /// <summary>Removes components a cloned label shouldn't keep (its own string key, AspectLayout poses).</summary>
        static void Strip(GameObject go)
        {
            foreach (var c in go.GetComponents<MonoBehaviour>())
                if (!(c is TMP_Text)) Destroy(c);
            foreach (Transform child in go.transform) Destroy(child.gameObject);
        }

        public void Begin()
        {
            root.gameObject.SetActive(true);
            ShowStep(0);
        }

        /// <summary>Back into the flow at a step (calibration's Back returns to the difficulty step).</summary>
        public void ReturnTo(int stepIndex)
        {
            root.gameObject.SetActive(true);
            ShowStep(stepIndex);
        }

        public void Hide() => root.gameObject.SetActive(false);

        void ShowStep(int index)
        {
            step = index;
            foreach (Transform c in cardRow) { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            cards.Clear();
            if (step == 0)
            {
                AddCard("walk.slide", "walk.slide.desc", true, SteerBy.Slide, () => ChooseSteer(SteerBy.Slide));
                AddCard("walk.turn", "walk.turn.desc", false, SteerBy.Turn, () => ChooseSteer(SteerBy.Turn));
                highlighted = Steering.Mode == SteerBy.Turn ? 1 : 0;
                Say("walk_steer");
            }
            else
            {
                AddCard("walk.easy", "walk.easy.desc", false, null, () => ChooseMode(DefaultMode.Easy));
                AddCard("walk.normal", "walk.normal.desc", false, null, () => ChooseMode(DefaultMode.Normal));
                AddCard("walk.sound", "walk.sound.desc", false, null, () => ChooseMode(DefaultMode.Sound));
                highlighted = Difficulty.Auditory ? 2 : Difficulty.Easy ? 0 : 1;
                Say("walk_difficulty");
            }
            Refresh();
        }

        void AddCard(string titleKey, string descKey, bool recommended, SteerBy? diagram, Action choose)
        {
            // Holder (laid out in the row) → a pink frame behind (shown for the keyboard's pick) → the cream card.
            var holder = new GameObject(titleKey, typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            holder.SetParent(cardRow, false);
            var le = holder.GetComponent<LayoutElement>();
            le.preferredWidth = 640f;
            le.flexibleWidth = 1f;
            // Cards with a picture are tall; the difficulty cards (words only) are shorter, their text centred.
            le.preferredHeight = diagram.HasValue ? 760f : 440f;

            var hl = new GameObject("Highlight", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            hl.transform.SetParent(holder, false);
            Stretch(hl.rectTransform, Vector2.zero, Vector2.one);
            hl.rectTransform.offsetMin = new Vector2(-12f, -12f);
            hl.rectTransform.offsetMax = new Vector2(12f, 12f);
            hl.color = Highlight;
            hl.raycastTarget = false;

            var card = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button), typeof(VerticalLayoutGroup))
                .GetComponent<RectTransform>();
            card.SetParent(holder, false);
            Stretch(card, Vector2.zero, Vector2.one);
            var img = card.GetComponent<Image>();
            img.sprite = panelStyle.sprite;
            img.type = panelStyle.type;
            img.pixelsPerUnitMultiplier = panelStyle.pixelsPerUnitMultiplier;
            var pc = panelStyle.color;
            img.color = new Color(pc.r, pc.g, pc.b, 1f); // fully opaque: the game view composites alpha
            var v = card.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(40, 40, 36, 40);
            v.spacing = 20f;
            v.childAlignment = diagram.HasValue ? TextAnchor.UpperCenter : TextAnchor.MiddleCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // The 추천해요 badge (an empty line on the other picture card keeps the two lined up).
            if (diagram.HasValue)
            {
                var badge = Text(card, recommended ? "walk.recommended" : null, 32f, 48f);
                badge.color = Highlight;
            }

            if (diagram.HasValue)
            {
                var pic = new GameObject("Diagram", typeof(RectTransform), typeof(RawImage), typeof(LayoutElement), typeof(MoveDiagram));
                pic.transform.SetParent(card, false);
                pic.GetComponent<MoveDiagram>().mode = diagram.Value;
                var pl = pic.GetComponent<LayoutElement>();
                pl.preferredWidth = 48 * 8f;
                pl.preferredHeight = 40 * 8f;
                pl.flexibleWidth = 0f;
                pic.GetComponent<RawImage>().raycastTarget = false;
                var fit = pic.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                fit.aspectRatio = 48f / 40f;
            }

            Text(card, titleKey, 64f, 90f).fontSize = 64f;
            Text(card, descKey, 32f, 140f);

            int index = cards.Count;
            card.GetComponent<Button>().onClick.AddListener(() => { highlighted = index; choose(); });
            cards.Add(new Card { rect = holder, highlight = hl, choose = choose });
        }

        TMP_Text Text(RectTransform parent, string key, float size, float height)
        {
            var t = Instantiate(bodyStyle, parent);
            Strip(t.gameObject);
            t.gameObject.SetActive(true);
            t.name = key ?? "Badge";
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var lt = t.gameObject.AddComponent<LocalizedText>();
            lt.Key = key ?? "walk.none";
            if (key == null) t.text = "";
            return t;
        }

        void Refresh()
        {
            if (title == null) return;
            title.text = Strings.Get(step == 0 ? "walk.steer.title" : "walk.diff.title");
            dots.text = Strings.Get(step == 0 ? "walk.dots1" : "walk.dots2");
            for (int i = 0; i < cards.Count; i++)
                if (cards[i].highlight != null) cards[i].highlight.enabled = i == highlighted;
        }

        void ChooseSteer(SteerBy mode)
        {
            Steering.Mode = mode;
            Say(mode == SteerBy.Turn ? "walk_turn_chosen" : "walk_slide_chosen");
            ShowStep(1);
        }

        void ChooseMode(DefaultMode mode)
        {
            bool wasAuditory = Difficulty.Auditory;
            Difficulty.ResetTo(mode);
            if (mode == DefaultMode.Sound) Say("mode_on");
            else if (wasAuditory) Say("mode_off");
            else Say(mode == DefaultMode.Easy ? "walk_easy_chosen" : "walk_normal_chosen");
            Hide();
            Finished?.Invoke(true);
        }

        void Back()
        {
            if (step > 0) { ShowStep(step - 1); return; }
            Hide();
            if (narrator != null) narrator.Stop();
            Finished?.Invoke(false);
        }

        /// <summary>Space / Enter (from the game): choose the highlighted card.</summary>
        public void ChooseHighlighted()
        {
            if (highlighted >= 0 && highlighted < cards.Count) cards[highlighted].choose();
        }

        void Update()
        {
            if (!Active) return;
            var kb = Keyboard.current;
            if (kb == null || StaffPanel.KeysActive) return;
            if (kb.leftArrowKey.wasPressedThisFrame) { highlighted = Mathf.Max(0, highlighted - 1); Refresh(); }
            if (kb.rightArrowKey.wasPressedThisFrame) { highlighted = Mathf.Min(cards.Count - 1, highlighted + 1); Refresh(); }
            if (kb.escapeKey.wasPressedThisFrame) { StaffPanel.Consume(); Back(); }
            if (kb.aKey.wasPressedThisFrame && step == 1) { highlighted = 2; ChooseMode(DefaultMode.Sound); }
        }

        /// <summary>Reads a step aloud for everyone during setup; quieter unless in Sound mode.</summary>
        void Say(string key)
        {
            if (narrator == null || !config.narrateSetup) return;
            narrator.Quiet = !Difficulty.Auditory;
            narrator.SayNow(key);
        }

        static void Stretch(RectTransform r, Vector2 min, Vector2 max)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }
    }
}
