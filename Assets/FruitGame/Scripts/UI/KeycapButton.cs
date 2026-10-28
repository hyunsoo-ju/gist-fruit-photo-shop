using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FruitGame
{
    /// <summary>
    /// A pixel-art "keycap" button (Docs/DESIGN.md §11): the button's own image is the dark lip, and the face (holding the
    /// label) sits on top of it, leaving lipPixels of lip showing below. Hover lifts the face one pixel; pressing sinks
    /// it all the way down so the lip disappears, like a key going down. Positions snap in whole UI pixels.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class KeycapButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] RectTransform face;
        [Tooltip("Canvas units per UI pixel (the sprites are drawn at 1 pixel = this many units).")]
        [SerializeField] float pixel = 4f;
        [Tooltip("Lip height in UI pixels.")]
        [SerializeField] int lipPixels = 3;

        Button button;
        bool hover, pressed;

        void Awake()
        {
            button = GetComponent<Button>();
            Apply();
        }

        void OnDisable()
        {
            hover = pressed = false;
            Apply();
        }

        public void OnPointerEnter(PointerEventData e) { hover = true; Apply(); }
        public void OnPointerExit(PointerEventData e) { hover = false; pressed = false; Apply(); }
        public void OnPointerDown(PointerEventData e) { pressed = true; Apply(); }
        public void OnPointerUp(PointerEventData e) { pressed = false; Apply(); }

        void Apply()
        {
            if (face == null) return;
            bool live = button == null || button.interactable;
            float lip = lipPixels * pixel;
            float shift = !live ? 0f : pressed ? -lip : hover ? pixel : 0f;
            face.anchorMin = Vector2.zero;
            face.anchorMax = Vector2.one;
            face.offsetMin = new Vector2(0f, lip + shift);
            face.offsetMax = new Vector2(0f, shift);
        }
    }
}
