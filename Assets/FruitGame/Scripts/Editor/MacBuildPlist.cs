using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// After a macOS build (Docs/DESIGN.md §16): opts the app out of App Nap in its Info.plist (NSAppSleepDisabled, and
    /// LSAppNapIsDisabled, which some macOS versions read instead), so the guest screen keeps its frame rate while the
    /// staff screen's desktop is switched away. Runtime KeepRunning's activity assertion is the dependable part; these
    /// keys back it up. Unity has already signed the app (ad hoc) by now, and an edited Info.plist breaks that signature,
    /// which made macOS deny the camera even with the permission granted; so the app is signed again (ad hoc, as Unity
    /// does: no entitlements, no hardened runtime) and the signature checked.
    /// </summary>
    sealed class MacBuildPlist : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX) return;
            string plist = Path.Combine(report.summary.outputPath, "Contents", "Info.plist");
            if (!File.Exists(plist)) return;
            string text = File.ReadAllText(plist);
            text = AddTrue(text, "NSAppSleepDisabled");
            text = AddTrue(text, "LSAppNapIsDisabled");
            if (text == File.ReadAllText(plist)) return;
            File.WriteAllText(plist, text);
            Resign(report.summary.outputPath);
        }

        static void Resign(string app)
        {
            var (code, output) = Run("/usr/bin/codesign", $"--force --deep --sign - \"{app}\"");
            if (code != 0) { UnityEngine.Debug.LogError($"[FruitGame] Re-signing the app failed ({code}): {output}"); return; }
            (code, output) = Run("/usr/bin/codesign", $"--verify --deep --strict \"{app}\"");
            if (code != 0) UnityEngine.Debug.LogError($"[FruitGame] The app's signature doesn't verify: {output}");
            else UnityEngine.Debug.Log("[FruitGame] Info.plist: App Nap keys added, app re-signed (ad hoc)");
        }

        static (int code, string output) Run(string file, string args)
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file, args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            });
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, output.Trim());
        }

        static string AddTrue(string text, string key)
        {
            if (text.Contains("<key>" + key + "</key>")) return text;
            int end = text.LastIndexOf("</dict>", System.StringComparison.Ordinal);
            if (end < 0) return text;
            return text.Insert(end, "\t<key>" + key + "</key>\n\t<true/>\n");
        }
    }
}
