using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitGame
{
    /// <summary>
    /// Loads the string table and sets the starting language from GameConfig (Korean by default). F3 flips between
    /// Korean and English, for staff (Docs/DESIGN.md §11).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class LanguageSwitch : MonoBehaviour
    {
        [SerializeField] GameConfig config;

        void Awake()
        {
            Strings.Load();
            Strings.SetLanguage(config != null ? config.language : Language.Korean);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame)
                Strings.SetLanguage(Strings.Current == Language.Korean ? Language.English : Language.Korean);
        }
    }
}
