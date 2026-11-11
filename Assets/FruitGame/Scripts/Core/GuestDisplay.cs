using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The guest screen's size and picture (Docs/DESIGN.md §15). In a build that's the main display: `Screen` always
    /// means it, even with a staff display connected. In the Editor, `Screen` means whichever Game view is being
    /// drawn or has focus, so with a second Game view open for the staff screen the guest layout would follow the
    /// wrong window; here the Game view set to Display 1 is found and used instead.
    /// </summary>
    public static class GuestDisplay
    {
        public static int Width => Size.x;
        public static int Height => Size.y;

        public static Vector2Int Size
        {
            get
            {
#if UNITY_EDITOR
                var rt = EditorTexture;
                if (rt != null) lastEditorSize = new Vector2Int(rt.width, rt.height);
                // No Game view on Display 1 right now (e.g. the only one is switched to Display 2): keep the last
                // guest size rather than taking the staff view's shape.
                if (lastEditorSize.x > 0) return lastEditorSize;
#endif
                return new Vector2Int(Screen.width, Screen.height);
            }
        }

#if UNITY_EDITOR
        static Vector2Int lastEditorSize;
        static UnityEditor.EditorWindow view;
        static float nextLookup;
        static readonly System.Type GameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        static readonly System.Reflection.PropertyInfo TargetDisplay = GameViewType?.GetProperty("targetDisplay", Any);
        static readonly System.Reflection.FieldInfo TargetTexture = FindField("m_TargetTexture");

        static System.Reflection.FieldInfo FindField(string name)
        {
            for (var t = GameViewType; t != null; t = t.BaseType)
            {
                var f = t.GetField(name, Any);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>True when a Game view is set to Display 2, so the staff screen has somewhere to show.</summary>
        public static bool EditorHasStaffView
        {
            get
            {
                if (GameViewType == null || TargetDisplay == null) return false;
                foreach (var w in Resources.FindObjectsOfTypeAll(GameViewType))
                    if (w is UnityEditor.EditorWindow && (int)TargetDisplay.GetValue(w) == 1) return true;
                return false;
            }
        }

        /// <summary>The Display 1 Game view's picture (with its UI), or null if there isn't one.</summary>
        public static RenderTexture EditorTexture
        {
            get
            {
                if (GameViewType == null || TargetDisplay == null || TargetTexture == null) return null;
                // Look again every second, and at once if the cached view was switched to another display.
                if (view != null && (int)TargetDisplay.GetValue(view) != 0) { view = null; nextLookup = 0f; }
                if (Time.realtimeSinceStartup >= nextLookup)
                {
                    nextLookup = Time.realtimeSinceStartup + 1f;
                    view = null;
                    foreach (var w in Resources.FindObjectsOfTypeAll(GameViewType))
                        if (w is UnityEditor.EditorWindow win && (int)TargetDisplay.GetValue(win) == 0) { view = win; break; }
                }
                var rt = view != null ? TargetTexture.GetValue(view) as RenderTexture : null;
                return rt != null && rt.width > 0 && rt.height > 0 ? rt : null;
            }
        }
#endif
    }
}
