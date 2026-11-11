#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Records the screen for automated checks (Docs/DESIGN.md §13): `TestRecorder.Begin(seconds, fps, folder)`
    /// saves frames as numbered PNGs (half size) plus frames.csv with each frame's time. Editor only, and only while
    /// the camera is off (TestMode), so no one's face is ever recorded.
    /// </summary>
    public class TestRecorder : MonoBehaviour
    {
        public static bool Running { get; private set; }

        public static bool Begin(float seconds, float fps, string folder)
        {
            if (!Application.isPlaying || !TestMode.CameraOff || Running) return false;
            var go = new GameObject("Test Recorder") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<TestRecorder>().StartCoroutine(Record(seconds, fps, folder, go));
            return true;
        }

        static IEnumerator Record(float seconds, float fps, string folder, GameObject owner)
        {
            Running = true;
            Directory.CreateDirectory(folder);
            var csv = new System.Text.StringBuilder("frame,time\n");
            float start = Time.realtimeSinceStartup, next = start;
            int n = 0;
            while (Time.realtimeSinceStartup - start < seconds && TestMode.CameraOff)
            {
                yield return new WaitForEndOfFrame();
                if (Time.realtimeSinceStartup < next) continue;
                next += 1f / fps;
                var full = ScreenCapture.CaptureScreenshotAsTexture();
                var small = new RenderTexture(full.width / 2, full.height / 2, 0);
                // Blit leaves its target active: note what was active before it, and put that back before releasing
                // (releasing the active texture left a dead one active for the next run).
                var prev = RenderTexture.active;
                Graphics.Blit(full, small);
                var tex = new Texture2D(small.width, small.height, TextureFormat.RGB24, false);
                RenderTexture.active = small;
                tex.ReadPixels(new Rect(0, 0, small.width, small.height), 0, 0);
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(folder, $"f{n:000}.png"), tex.EncodeToPNG());
                csv.AppendLine($"{n},{Time.realtimeSinceStartup - start:0.000}");
                Destroy(full); Destroy(tex); small.Release(); Destroy(small);
                n++;
            }
            File.WriteAllText(Path.Combine(folder, "frames.csv"), csv.ToString());
            Running = false;
            Destroy(owner);
        }
    }
}
#endif
