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
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    SessionState.EraseBool(TestMode.CameraOffKey);
                    SessionState.EraseBool(TestMode.TestFramesKey);
                }
            };
            // Never left on by a reload in Edit mode.
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseBool(TestMode.CameraOffKey);
                SessionState.EraseBool(TestMode.TestFramesKey);
            }
        }

        [MenuItem("FruitGame/Test/Play Without Camera")]
        public static void PlayWithoutCamera()
        {
            if (EditorApplication.isPlaying) return;
            SessionState.SetBool(TestMode.CameraOffKey, true);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Camera off, but generated frames go through tracking and background removal (to measure their cost).</summary>
        [MenuItem("FruitGame/Test/Play With Test Frames")]
        public static void PlayWithTestFrames()
        {
            if (EditorApplication.isPlaying) return;
            SessionState.SetBool(TestMode.CameraOffKey, true);
            SessionState.SetBool(TestMode.TestFramesKey, true);
            EditorApplication.EnterPlaymode();
        }
    }
}
