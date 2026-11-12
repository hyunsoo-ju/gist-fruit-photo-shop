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

        object[] args;

        /// <summary>Values for the entry's {0}, {1}… (for labels built at runtime).</summary>
        public object[] Args
        {
            get => args;
            set { args = value; if (isActiveAndEnabled) Refresh(); }
        }

        void OnEnable()
        {
            Strings.Changed += Refresh;
            Refresh();
        }

        void OnDisable() => Strings.Changed -= Refresh;

        void Refresh()
        {
            // A label built at runtime is enabled the moment it's added, before it's been given its key.
            if (!string.IsNullOrEmpty(key)) GetComponent<TMP_Text>().text = args != null ? Strings.Get(key, args) : Strings.Get(key);
        }
    }
}
