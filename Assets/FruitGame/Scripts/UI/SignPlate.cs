using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// A raised block on a signboard (SignBoard), under some of the board's text: the 랭킹's rows (Docs/DESIGN.md §15).
    /// The element's Image is the flat look; on the board it is a block of the plate's colour standing proud of the
    /// planks, and the texts inside it sit on the plate. High contrast: a white plate with black letters.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SignPlate : MonoBehaviour
    {
        public Color32 colour = new Color32(0xD9, 0xBE, 0x8C, 0xFF);
    }
}
