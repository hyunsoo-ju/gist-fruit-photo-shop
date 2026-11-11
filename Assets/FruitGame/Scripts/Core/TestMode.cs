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
    }
}
