using UnityEngine;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// A full-screen dark cover over the guest screen (HUD and all) whose opacity can be faded, used between the attract
    /// screen's sample rounds (Docs/DESIGN.md §3). Under the staff screen, camera menu and blind-test cover.
    /// </summary>
    public class ScreenFade : MonoBehaviour
    {
        CanvasGroup group;

        void Awake()
        {
            var canvasGo = new GameObject("Screen Fade", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29000;
            canvas.targetDisplay = GuestDisplay.Index;
            var image = new GameObject("Cover", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(canvasGo.transform, false);
            image.color = new Color32(0x1E, 0x22, 0x33, 0xFF);
            image.raycastTarget = false;
            var rt = image.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            group = canvasGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            Alpha = 0f;
        }

        /// <summary>0 = clear, 1 = fully covered.</summary>
        public float Alpha
        {
            get => group != null ? group.alpha : 0f;
            set
            {
                if (group == null) return;
                group.alpha = Mathf.Clamp01(value);
                group.gameObject.SetActive(group.alpha > 0f);
            }
        }
    }
}
