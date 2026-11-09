using UnityEngine;

namespace FruitGame
{
    /// <summary>
    /// Steers the basket for the attract screen's sample round (Docs/DESIGN.md §15, M6 3.3) a little like a person would:
    /// at a limited speed, slightly off-centre, avoiding bombs, and now and then not quite making it.
    /// </summary>
    public class DemoPilot : BasketInputSource
    {
        const float Speed = 3.6f, BombClearance = 1.05f, MissChance = 0.12f;

        FruitSpawner spawner;
        GameConfig config;
        FallingFruit aim;
        float x, offset;

        public void Init(FruitSpawner s, GameConfig c) { spawner = s; config = c; }
        public void Restart() { x = 0f; aim = null; }

        public override bool HasTarget => true;
        public override float TargetX => x;

        void Update()
        {
            if (spawner == null) return;
            FallingFruit lowest = null, bomb = null;
            foreach (var f in spawner.Active)
            {
                if (f == null || f.Resolved) continue;
                bool isBomb = f.FruitKind == FallingFruit.Kind.Bomb;
                float y = f.transform.position.y;
                if (isBomb) { if (bomb == null || y < bomb.transform.position.y) bomb = f; }
                else if (lowest == null || y < lowest.transform.position.y) lowest = f;
            }
            if (lowest != aim)
            {
                aim = lowest;
                offset = Random.value < MissChance ? (Random.value < 0.5f ? -1.2f : 1.2f) : Random.Range(-0.22f, 0.22f);
            }
            float target = aim != null ? aim.transform.position.x + offset : x;
            // A bomb coming down near the basket's path pushes it out of the way.
            if (bomb != null && bomb.transform.position.y < config.spawnY * 0.5f)
            {
                float bx = bomb.transform.position.x;
                if (Mathf.Abs(bx - target) < BombClearance) target = bx + (target >= bx ? BombClearance : -BombClearance);
            }
            target = Mathf.Clamp(target, -config.basketXLimit, config.basketXLimit);
            x = Mathf.MoveTowards(x, target, Speed * Time.deltaTime);
        }
    }
}
