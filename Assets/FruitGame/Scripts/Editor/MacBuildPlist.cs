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
    /// keys back it up.
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
            File.WriteAllText(plist, text);
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
