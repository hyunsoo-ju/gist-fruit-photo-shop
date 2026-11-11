using UnityEditor;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// FruitGame > Test > Play Without Camera: enters Play with the webcam off (TestMode), for screen recordings in
    /// automated checks. The switch clears itself as soon as Play ends.
    /// </summary>
    [InitializeOnLoad]
    static class TestModeMenu
    {
        static TestModeMenu()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) SessionState.EraseBool(TestMode.CameraOffKey);
            };
            // Never left on by a reload in Edit mode.
            if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.EraseBool(TestMode.CameraOffKey);
        }

        [MenuItem("FruitGame/Test/Play Without Camera")]
        public static void PlayWithoutCamera()
        {
            if (EditorApplication.isPlaying) return;
            SessionState.SetBool(TestMode.CameraOffKey, true);
            EditorApplication.EnterPlaymode();
        }
    }
}
