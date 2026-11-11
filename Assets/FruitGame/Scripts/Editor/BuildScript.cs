using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// FruitGame > Build > macOS (Docs/DESIGN.md §16): builds the booth app the same way every time, Boot then Game, into
    /// `Builds/macOS/` at the project root (ignored by git), replacing the last build there, and shows it in Finder.
    /// From the command line (Unity closed):
    /// `Unity -batchmode -quit -projectPath . -executeMethod FruitGame.EditorTools.BuildScript.BuildMac -logFile -`
    /// exits non-zero if the build fails.
    /// </summary>
    public static class BuildScript
    {
        static readonly string[] Scenes = { "Assets/FruitGame/Scenes/Boot.unity", "Assets/FruitGame/Scenes/Game.unity" };

        /// <summary>The project folder's `Builds/macOS`.</summary>
        public static string MacFolder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Builds", "macOS");

        [MenuItem("FruitGame/Build/macOS", priority = 0)]
        static void BuildMacMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Build", "Stop Play first.", "OK");
                return;
            }
            var report = Build();
            if (report.summary.result == BuildResult.Succeeded)
                EditorUtility.RevealInFinder(report.summary.outputPath);
            else
                EditorUtility.DisplayDialog("Build failed", $"{report.summary.totalErrors} error(s); see the Console.", "OK");
        }

        [MenuItem("FruitGame/Build/Open Builds Folder", priority = 1)]
        static void OpenFolder()
        {
            Directory.CreateDirectory(MacFolder);
            EditorUtility.RevealInFinder(MacFolder);
        }

        /// <summary>For `-executeMethod`: builds, and quits with code 1 if it failed.</summary>
        public static void BuildMac()
        {
            var report = Build();
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Builds the macOS app into Builds/macOS and returns Unity's report.</summary>
        public static BuildReport Build()
        {
            Directory.CreateDirectory(MacFolder);
            AppIcon.Ensure(); // the icon is made from the private apple model, so it isn't in git
            string app = Path.Combine(MacFolder, PlayerSettings.productName + ".app");
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = app,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            };
            var started = DateTime.Now;
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[FruitGame] Build {s.result}: {PlayerSettings.productName} {PlayerSettings.bundleVersion} → {app} " +
                      $"({s.totalSize / (1024f * 1024f):0} MB, {(DateTime.Now - started).TotalSeconds:0} s, {s.totalErrors} errors)");
            return report;
        }
    }
}
