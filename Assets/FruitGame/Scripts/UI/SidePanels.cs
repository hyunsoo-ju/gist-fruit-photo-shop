using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// The landscape HUD in the side areas beside the play column (Docs/DESIGN.md §3, §11), shown during a round. Left,
    /// under the score: a cream "점수표" (scoreboard) panel with the live score graph and the catch tally. Right, under
    /// the timer: a film strip of the round's three action shots, 3:4 portrait like the photos that get printed, each
    /// slot filling in as its shot is taken.
    /// Built at start; hidden in portrait, where the HUD stays over the play area.
    /// </summary>
    public class SidePanels : MonoBehaviour
    {
        [SerializeField] GameStateMachine game;
        [SerializeField] PhotoBooth booth;
        [SerializeField] Sprite panelSprite;
        [SerializeField] Sprite stripSprite;
        [SerializeField] TMP_FontAsset font;
        [Tooltip("Empty = the font's plain Pixel material.")]
        [SerializeField] Material textMaterial;
        [Tooltip("Width of each side panel in canvas units (the side areas are about 675 wide at the booth UI scale, and the crates sit at their inner edge).")]
        [SerializeField] float panelWidth = 520f;
        [Tooltip("Gap between the screen edge and each panel, in canvas units.")]
        [SerializeField] float margin = 48f;
        [SerializeField] float panelTop = 300f;
        [Tooltip("Top of the film strip, so it sits midway between the timer and the frame's bottom edge.")]
        [SerializeField] float stripTop = 224f;

        static readonly Color Ink = new Color32(0x1E, 0x22, 0x33, 0xFF);
        static readonly Color Cream = new Color32(0xFF, 0xF8, 0xEC, 0xFF);
        const int Cuts = 3;
        const float StripHeight = 1100f;

        GameObject left, right;
        ScoreGraph graph;
        TMP_Text heading, tally;
        readonly RawImage[] slots = new RawImage[Cuts];
        readonly TMP_Text[] slotLabels = new TMP_Text[Cuts];
        int shownHistory = -1, shownShots = -1;

        void Awake()
        {
            left = Panel("Left Panel", new Vector2(0f, 1f), new Vector2(panelWidth * 0.5f + margin, -panelTop), panelWidth, 840f, panelSprite);
            heading = Text(left.transform, "Heading", new Vector2(0f, -28f), new Vector2(panelWidth - 64f, 64f), 48, TextAlignmentOptions.Top);
            graph = Child<ScoreGraph>(left.transform, "Live Graph", new Vector2(0f, -120f), new Vector2(panelWidth - 64f, 300f));
            tally = Text(left.transform, "Tally", new Vector2(0f, -460f), new Vector2(panelWidth - 64f, 340f), 32, TextAlignmentOptions.TopLeft);

            // Three 3:4 slots stacked, the strip just wide enough for them.
            float gap = 24f, pad = 32f;
            float slotH = (StripHeight - 2f * pad - (Cuts - 1) * gap) / Cuts, slotW = slotH * 3f / 4f;
            float stripW = slotW + 2f * pad;
            right = Panel("Film Strip", new Vector2(1f, 1f), new Vector2(-(stripW * 0.5f + margin), -stripTop), stripW, StripHeight, stripSprite);
            for (int i = 0; i < Cuts; i++)
            {
                var pos = new Vector2(0f, -pad - i * (slotH + gap));
                var back = Child<Image>(right.transform, $"Slot {i + 1}", pos, new Vector2(slotW, slotH));
                back.color = new Color(1f, 1f, 1f, 0.12f);
                slotLabels[i] = Text(back.transform, "Label", Vector2.zero, new Vector2(slotW, slotH), 64, TextAlignmentOptions.Center);
                slotLabels[i].color = Cream;
                slotLabels[i].rectTransform.anchorMin = slotLabels[i].rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                slotLabels[i].rectTransform.pivot = new Vector2(0.5f, 0.5f);
                slots[i] = Child<RawImage>(back.transform, "Shot", Vector2.zero, new Vector2(slotW, slotH));
            }
            Strings.Changed += Refresh;
        }

        void OnDestroy() => Strings.Changed -= Refresh;

        void Refresh()
        {
            shownHistory = -1;
            shownShots = -1;
        }

        void Update()
        {
            var s = game.State;
            bool inRound = s == GameState.Countdown || s == GameState.Playing || s == GameState.RoundEnding || s == GameState.Paused;
            bool show = ScreenLayout.IsLandscape && inRound;
            left.SetActive(show);
            right.SetActive(show);
            if (!show) return;

            var stats = game.Stats;
            if (stats.History.Count != shownHistory)
            {
                shownHistory = stats.History.Count;
                graph.SetData(stats.History, game.Timer.Duration);
                var sb = new StringBuilder();
                foreach (var kv in stats.CaughtByType) sb.AppendLine($"{Strings.Fruit(kv.Key)} × {kv.Value}");
                tally.text = sb.Length == 0 ? Strings.Get("hud.noCatch") : sb.ToString();
                // 48 px while the list fits the panel (6 lines), 32 px for a long one.
                tally.fontSize = stats.CaughtByType.Count <= 6 ? 48 : 32;
            }
            heading.text = Strings.Get("hud.scoreboard");
            int shots = booth != null ? booth.ActionShotCount : 0;
            if (shots != shownShots)
            {
                shownShots = shots;
                for (int i = 0; i < Cuts; i++)
                {
                    var shot = booth != null ? booth.ActionShot(i) : null;
                    slots[i].texture = shot;
                    slots[i].enabled = shot != null;
                    slotLabels[i].text = (i + 1).ToString();
                }
            }
        }

        GameObject Panel(string name, Vector2 anchor, Vector2 position, float width, float height, Sprite sprite)
        {
            var image = Child<Image>(transform, name, position, new Vector2(width, height));
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = position;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.25f;
            return image.gameObject;
        }

        /// <summary>A child at the top centre of its parent, `position` below its top edge.</summary>
        static T Child<T>(Transform parent, string name, Vector2 position, Vector2 size) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            var g = go.GetComponent<T>();
            g.raycastTarget = false;
            return g;
        }

        TMP_Text Text(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, TextAlignmentOptions align)
        {
            var t = Child<TextMeshProUGUI>(parent, name, position, size);
            t.font = font;
            t.fontSharedMaterial = textMaterial != null ? textMaterial : font.material;
            t.fontSize = fontSize;
            t.color = Ink;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }
    }
}
