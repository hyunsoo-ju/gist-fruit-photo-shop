using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FruitGame
{
    /// <summary>
    /// The first scene of a build (Docs/DESIGN.md §3, M6 3.4): shows "getting ready" and then loads the game, whose models
    /// take several seconds to load and would otherwise leave the display blank.
    /// </summary>
    public class BootLoader : MonoBehaviour
    {
        [SerializeField] string gameScene = "Game";

        IEnumerator Start()
        {
            // Let the screen draw before the heavy load begins.
            yield return null;
            yield return null;
            var load = SceneManager.LoadSceneAsync(gameScene);
            while (load != null && !load.isDone) yield return null;
        }
    }
}
