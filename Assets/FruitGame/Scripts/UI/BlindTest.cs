using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// Staff test for the auditory assist mode (Docs/DESIGN.md §11, M18): F5 covers the whole screen in black, HUD and
    /// all, while the game keeps running, so a sighted tester can check a round can be played by ear alone.
    /// </summary>
    public class BlindTest : MonoBehaviour
    {
        GameObject cover;

        void Awake()
        {
            var canvasGo = new GameObject("Blind Test Cover", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var image = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(canvasGo.transform, false);
            image.color = Color.black;
            image.raycastTarget = false;
            var rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            cover = canvasGo;
            cover.SetActive(false);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f5Key.wasPressedThisFrame) Toggle();
        }

        /// <summary>Blanks or un-blanks the guest screen (F5, or B in the staff panel).</summary>
        public void Toggle() => cover.SetActive(!cover.activeSelf);
        public bool On => cover != null && cover.activeSelf;
    }
}
