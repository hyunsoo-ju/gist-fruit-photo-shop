using TMPro;
using UnityEngine;

namespace FruitGame
{
    /// <summary>A static label that shows its string-table entry in the current language (Docs/DESIGN.md §11).</summary>
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] string key;

        void OnEnable()
        {
            Strings.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => Strings.Changed -= Refresh;

        void Refresh() => GetComponent<TMP_Text>().text = Strings.Get(key);
    }
}
