using UnityEngine;
using UnityEngine.InputSystem;

namespace FruitGame
{
    /// <summary>
    /// Loads the string table and sets the starting language from GameConfig (Korean by default). The staff panel's L flips between
    /// Korean and English (Docs/DESIGN.md §15).
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


    }
}
