using System.Collections.Generic;
using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// The change between UI screens (Docs/DESIGN.md §15, M6 1.6): the old panel drops away in 0.1 s and the new one rises
    /// with a small overshoot in 0.2 s, over the live game. It never flashes. With voice assist on, panels leave at once,
    /// so the spoken button list only ever names what's really on screen.
    /// </summary>
    public class UiTransition : MonoBehaviour
    {
        const float InTime = 0.2f, OutTime = 0.1f, Distance = 96f;

        class Run
        {
            public GameObject go;
            public RectTransform rect;
            public CanvasGroup group;
            public Vector2 home;
            public float t;
            public bool showing;
        }

        static UiTransition host;
        static readonly Dictionary<GameObject, Run> runs = new Dictionary<GameObject, Run>();
        static readonly List<GameObject> finished = new List<GameObject>();

        /// <summary>Shows or hides a panel, animated; does nothing if it's already that way.</summary>
        public static void Set(GameObject go, bool show)
        {
            if (go == null) return;
            runs.TryGetValue(go, out var run);
            if (run != null && run.showing == show) return;
            if (run == null && go.activeSelf == show) return;
            if (!Application.isPlaying || (!show && Difficulty.Auditory) || !go.activeInHierarchy && !show)
            {
                if (run != null) Finish(run);
                go.SetActive(show);
                return;
            }
            if (show) go.SetActive(true);
            if (!go.activeInHierarchy) return; // A parent is off: nothing to animate, it's simply on when the parent is.
            Begin(go, run, show);
        }

        /// <summary>Plays the rise on a panel that's already showing (a new step inside one screen).</summary>
        public static void Pop(GameObject go)
        {
            if (go == null || !Application.isPlaying || !go.activeInHierarchy) return;
            runs.TryGetValue(go, out var run);
            Begin(go, run, true);
            runs[go].t = 0f;
        }

        static void Begin(GameObject go, Run run, bool show)
        {
            if (host == null) host = new GameObject("UI Transitions").AddComponent<UiTransition>();
            if (run == null)
            {
                run = new Run { go = go, rect = go.transform as RectTransform };
                if (!go.TryGetComponent(out run.group)) run.group = go.AddComponent<CanvasGroup>();
                run.home = run.rect != null ? run.rect.anchoredPosition : Vector2.zero;
                runs[go] = run;
            }
            run.showing = show;
            run.t = 0f;
            run.group.blocksRaycasts = show;
            Apply(run);
        }

        void OnDestroy()
        {
            if (host == this) host = null;
            runs.Clear();
        }

        void Update()
        {
            finished.Clear();
            foreach (var kv in runs)
            {
                var run = kv.Value;
                if (run.go == null) { finished.Add(kv.Key); continue; }
                run.t += Time.unscaledDeltaTime;
                Apply(run);
                if (run.t >= (run.showing ? InTime : OutTime)) finished.Add(kv.Key);
            }
            foreach (var go in finished)
            {
                var run = runs[go];
                bool hide = !run.showing;
                Finish(run);
                if (hide && go != null) go.SetActive(false);
            }
        }

        static void Apply(Run run)
        {
            float offset, alpha;
            if (run.showing)
            {
                float u = Mathf.Clamp01(run.t / InTime);
                // Ease out with a small overshoot past the resting place.
                const float back = 1.70158f;
                float v = u - 1f;
                float eased = 1f + (back + 1f) * v * v * v + back * v * v;
                offset = -Distance * (1f - eased);
                alpha = Mathf.Clamp01(u * 2.5f);
            }
            else
            {
                float u = Mathf.Clamp01(run.t / OutTime);
                offset = -Distance * u * u;
                alpha = 1f - u;
            }
            if (run.rect != null) run.rect.anchoredPosition = run.home + new Vector2(0f, offset);
            if (run.group != null) run.group.alpha = alpha;
        }

        static void Finish(Run run)
        {
            if (run.go != null)
            {
                if (run.rect != null) run.rect.anchoredPosition = run.home;
                if (run.group != null) { run.group.alpha = 1f; run.group.blocksRaycasts = true; }
            }
            runs.Remove(run.go);
        }
    }
}
