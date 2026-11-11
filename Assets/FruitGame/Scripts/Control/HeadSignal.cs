namespace FruitGame
{
    /// <summary>
    /// The steering signal (Docs/DESIGN.md §8, M6 2.3), in the same 0..1 units for both ways of steering, so calibration
    /// and the basket mapping don't care which one is used: **Slide** is the face's sideways position in the 3:4 play
    /// area, from the face mesh's outline (the BlazeFace box only until the mesh has the face); **Turn** is the head's yaw from the face mesh, 0.5 + yaw / turnDegreesPerWidth (yaw + = turned toward the
    /// screen's right, like sliding right).
    /// </summary>
    public static class HeadSignal
    {
        public static bool Has(GameConfig config, FaceTracker tracker, FaceMesh mesh)
        {
            if (tracker == null || !tracker.HasFace) return false;
            return Steering.Mode == SteerBy.Slide || (mesh != null && mesh.HasMesh);
        }

        public static float Value(GameConfig config, FaceTracker tracker, FaceMesh mesh)
        {
            if (Steering.Mode == SteerBy.Turn && mesh != null)
                return 0.5f + mesh.Yaw / UnityEngine.Mathf.Max(1f, config.turnDegreesPerWidth);
            if (mesh != null && mesh.HasMesh) return mesh.PlayCentre.x;
            return tracker.PlayFaceCenter.x;
        }
    }
}
