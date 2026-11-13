using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Keeps the booth running while staff work elsewhere (Docs/DESIGN.md §16): the guest screen must carry on when the
    /// staff screen's desktop is switched away or another app is in front. Three parts, in the macOS build:
    ///  - frames are paced by a fixed frame rate (BoothFrameRate), not by the display: Unity's Metal display link (off in
    ///    Player Settings) follows the main window, and macOS stops it while that window can't be seen, which froze the
    ///    whole player, the guest window too; vsync is off for the same reason;
    ///  - an activity assertion (NSProcessInfo beginActivity, user-initiated and latency-critical) so macOS doesn't put
    ///    the app to sleep (App Nap) or let the Mac idle to sleep while it isn't the frontmost app;
    ///  - Run In Background (Player Settings) and the Info.plist App Nap keys (MacBuildPlist) back that up.
    /// Standby lowers the frame rate while the booth is closed and puts this one back.
    /// </summary>
    public static class KeepRunning
    {
        /// <summary>The guest touch display's refresh rate.</summary>
        public const int BoothFrameRate = 60;

        static IntPtr activity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = BoothFrameRate;
            Application.runInBackground = true;
            BeginActivity();
#endif
        }

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendActivity(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

        // NSActivityOptions: NSActivityUserInitiated (0x00FFFFFF | NSActivityIdleSystemSleepDisabled) | NSActivityLatencyCritical.
        const ulong UserInitiated = 0x00FFFFFFUL | (1UL << 20);
        const ulong LatencyCritical = 0xFF00000000UL;

        /// <summary>[[NSProcessInfo processInfo] beginActivityWithOptions:reason:], kept (retained) for the app's life.</summary>
        static void BeginActivity()
        {
            if (activity != IntPtr.Zero) return;
            try
            {
                var info = Send(objc_getClass("NSProcessInfo"), sel_registerName("processInfo"));
                var reason = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "Booth guest screen keeps running");
                var token = SendActivity(info, sel_registerName("beginActivityWithOptions:reason:"), UserInitiated | LatencyCritical, reason);
                if (token != IntPtr.Zero) activity = Send(token, sel_registerName("retain"));
                Debug.Log("[FruitGame] KeepRunning: App Nap and idle sleep held off" + (activity == IntPtr.Zero ? " (no activity token!)" : ""));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[FruitGame] KeepRunning: couldn't hold off App Nap (" + e.Message + ")");
            }
        }
#endif
    }
}
