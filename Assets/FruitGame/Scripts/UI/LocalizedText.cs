using TMPro;
using UnityEngine;

namespace FruitGame
{
    /// <summary>A static label that shows its string-table entry in the current language (Docs/DESIGN.md §15).</summary>
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] string key;

        /// <summary>The string-table key; setting it shows the new text at once (for labels built at runtime).</summary>
        public string Key
        {
            get => key;
            set { key = value; if (isActiveAndEnabled) Refresh(); }
        }

        void OnEnable()
        {
            Strings.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => Strings.Changed -= Refresh;

        void Refresh() => GetComponent<TMP_Text>().text = Strings.Get(key);
    }
}
