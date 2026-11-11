using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FruitGame.EditorTools
{
    /// <summary>
    /// Shows the title screen in the Editor before Play as the game draws it (Docs/DESIGN.md §16): the voxel buttons,
    /// the 3D title, the crate walls, hills, scenery bands, the crate platform, the living scenery's colours, shrubs,
    /// butterflies and clouds, and the pixel render. Each of those is built at runtime; here every IEditPreview in the
    /// open scene builds its own preview. Everything the preview creates is hidden and never saved, and it's all taken
    /// down (with what it changed put back) before every save, before Play, before scripts reload and before a player
    /// build, then built again afterwards. (A build left up wiped this class's record of the preview without undoing
    /// it: the main camera kept the pixel layers' mask and renderers kept materials the build destroyed, so after a few
    /// builds Play's realistic look drew nothing and the leaves drew magenta.) Nothing moves until Play: the preview is a still of the title screen.
    /// FruitGame > Title Preview turns it on or off, and Refresh rebuilds it after an edit.
    /// </summary>
    [InitializeOnLoad]
    static class TitlePreview
    {
        const string Pref = "FruitGame.TitlePreview";
        const string ToggleMenu = "FruitGame/Title Preview/Show Before Play";

        static readonly List<IEditPreview> built = new List<IEditPreview>();
        static readonly List<GameObject> made = new List<GameObject>();
        static bool active;

        static bool Enabled => EditorPrefs.GetBool(Pref, true);

        static TitlePreview()
        {
            EditorSceneManager.sceneSaving += (scene, path) => End();
            EditorSceneManager.sceneSaved += scene => Schedule();
            EditorSceneManager.sceneOpened += (scene, mode) => { End(); Schedule(); };
            EditorSceneManager.sceneClosing += (scene, removing) => End();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode) End();
                else if (state == PlayModeStateChange.EnteredEditMode) Schedule();
            };
            AssemblyReloadEvents.beforeAssemblyReload += End;
            // Portrait / landscape changes the buttons' sizes and the pixel targets: rebuild for the new layout.
            ScreenLayout.Changed += () => { if (active) { End(); Schedule(); } };
            Schedule();
        }

        [MenuItem(ToggleMenu)]
        static void Toggle()
        {
            EditorPrefs.SetBool(Pref, !Enabled);
            End();
            Schedule();
        }

        [MenuItem(ToggleMenu, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenu, Enabled);
            return !EditorApplication.isPlaying;
        }

        [MenuItem("FruitGame/Title Preview/Refresh")]
        static void Refresh()
        {
            End();
            Schedule();
        }

        static bool pending;
        /// <summary>A player build took the preview down: keep trying to build it until the scene is back.</summary>
        static bool afterBuild;

        /// <summary>Builds at the next Editor update when Unity is idle (it may be compiling, importing or changing mode).</summary>
        static void Schedule()
        {
            pending = true;
            EditorApplication.update -= TryBuild;
            EditorApplication.update += TryBuild;
        }

        static void TryBuild()
        {
            if (!pending) { EditorApplication.update -= TryBuild; return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
                || BuildPipeline.isBuildingPlayer) return;
            pending = false;
            EditorApplication.update -= TryBuild;
            Build();
        }

        static void Build()
        {
            if (active || !Enabled || EditorApplication.isPlayingOrWillChangePlaymode) return;

            var targets = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(m => m is IEditPreview && m.isActiveAndEnabled)
                .Cast<IEditPreview>()
                .OrderBy(p => p.PreviewOrder)
                .ToList();
            if (targets.Count == 0)
            {
                // Mid-build the scene is swapped out: try again until it's back.
                if (afterBuild) Schedule();
                return;
            }
            afterBuild = false;

            var before = new HashSet<GameObject>(Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            active = true;
            foreach (var t in targets)
            {
                try
                {
                    t.BuildPreview();
                    built.Add(t);
                }
                catch (Exception e)
                {
                    built.Add(t); // still ended, to undo whatever it got through
                    Debug.LogWarning($"[FruitGame] Title preview: {t.GetType().Name} couldn't build: {e.Message}");
                }
            }
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (before.Contains(go)) continue;
                go.hideFlags = HideFlags.HideAndDontSave;
                made.Add(go);
            }
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        static void End()
        {
            if (!afterBuild) pending = false; // a build closes and reopens the scene: the rebuild after it stays due
            if (!active) return;
            active = false;
            for (int i = built.Count - 1; i >= 0; i--)
            {
                try { built[i]?.EndPreview(); }
                catch (Exception e) { Debug.LogWarning($"[FruitGame] Title preview: couldn't end {built[i]?.GetType().Name}: {e.Message}"); }
            }
            built.Clear();

            // What the made objects use that isn't an asset on disk: built for the preview, so freed with it.
            var trash = new HashSet<Object>();
            var madeSet = new HashSet<GameObject>(made.Where(g => g != null));
            foreach (var go in madeSet)
            {
                foreach (var f in go.GetComponents<MeshFilter>()) trash.Add(f.sharedMesh);
                foreach (var r in go.GetComponents<Renderer>()) foreach (var m in r.sharedMaterials) trash.Add(m);
                foreach (var ri in go.GetComponents<RawImage>()) trash.Add(ri.texture);
                foreach (var c in go.GetComponents<Camera>()) trash.Add(c.targetTexture);
            }
            foreach (var go in madeSet)
            {
                if (go == null) continue;
                var parent = go.transform.parent;
                if (parent == null || !madeSet.Contains(parent.gameObject)) Object.DestroyImmediate(go);
            }
            foreach (var o in trash)
            {
                if (o == null || EditorUtility.IsPersistent(o)) continue;
                if (o is RenderTexture rt) rt.Release();
                Object.DestroyImmediate(o);
            }
            made.Clear();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>Takes the preview down before a player build; it's built again once the build is over (TryBuild waits).</summary>
        sealed class BeforeBuild : UnityEditor.Build.IPreprocessBuildWithReport
        {
            public int callbackOrder => -1000;
            public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
            {
                End();
                afterBuild = true;
                Schedule();
            }
        }
    }
}
