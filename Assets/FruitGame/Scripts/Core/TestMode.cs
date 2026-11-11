namespace FruitGame
{
    /// <summary>
    /// Test play without the webcam (Docs/DESIGN.md §13), for automated checks that record the Game view: no camera
    /// image, so no one's face is ever captured. Editor only and one-shot: FruitGame > Test > Play Without Camera sets
    /// it for the next Play session, and it clears itself when that session ends, so ordinary Play always has the camera.
    /// </summary>
    public static class TestMode
    {
        public const string CameraOffKey = "FruitGame.TestMode.CameraOff";
        public const string TestFramesKey = "FruitGame.TestMode.TestFrames";

        /// <summary>True while a camera-off test session is running (always false in builds).</summary>
        public static bool CameraOff
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetBool(CameraOffKey, false);
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// With the camera off, feed WebcamSource generated frames (a moving pattern, 60 a second) so tracking and
        /// background removal run for real and their cost can be measured, still without any camera image.
        /// FruitGame > Test > Play With Test Frames.
        /// </summary>
        public static bool TestFrames
        {
            get
            {
#if UNITY_EDITOR
                return CameraOff && UnityEditor.SessionState.GetBool(TestFramesKey, false);
#else
                return false;
#endif
            }
        }
    }
}
