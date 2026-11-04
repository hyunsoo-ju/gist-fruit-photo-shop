using UnityEngine;

namespace FruitGame
{
    /// <summary>The golden fruit's trail of gold and white sparkles drifting up behind it (Docs/DESIGN.md §5).</summary>
    public class GoldenSparkle : MonoBehaviour
    {
        static readonly Color32[] Colors =
        {
            new Color32(0xFF, 0xE0, 0x6A, 0xFF), new Color32(0xFF, 0xFF, 0xF0, 0xFF), new Color32(0xF5, 0xC0, 0x2E, 0xFF),
        };

        public float radius = 0.4f;
        float next;

        void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.06f;
            var offset = Random.insideUnitSphere * radius;
            offset.z = -Mathf.Abs(offset.z) - 0.1f; // in front of the fruit, so the sparkles show
            BlockFx.Spark(transform.position + offset, Colors[Random.Range(0, Colors.Length)],
                new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0.2f, 0.6f), 0f), Random.Range(0.03f, 0.06f), 0.5f, -0.3f);
        }
    }
}
