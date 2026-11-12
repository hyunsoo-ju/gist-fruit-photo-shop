#!/usr/bin/env python3
"""Simulates rounds of the fruit game for each difficulty, without playing (Docs/DESIGN.md §5–§6).

Replays FruitSpawner's timeline with GameConfig's rules: the drop beat that quickens over the round, the gap after
the last fruit lands, the most fruit in the air, fall speeds (golden fruit faster), golden fruit and bombs scheduled
one per even slot, bombs only after bombAfterSeconds and none in the last specialsEndBefore seconds. A caught fruit
resolves at the basket's rim, a missed one at the miss line. A player is a catch rate (each fruit, golden ones too,
caught with that chance) and a bomb-catch rate. Scores follow ScoreRules: golden worth goldenPoints, combos from
comboFrom in a row, a caught bomb costs bombPenalty (never below 0) and breaks the streak; a dodged bomb doesn't.

    python3 Tools/simulate_difficulty.py [--runs 4000] [--json out.json]
"""
import argparse, json, random, statistics

FALL_TO_RIM = 3.6 - (-2.0)   # spawnY - rimY
FALL_TO_MISS = 3.6 - (-2.6)  # spawnY - missY
ROUND = 60.0

BASE = dict(
    fallSpeedStart=2.7, nextFruitDelay=0.6, goldenSpeedFactor=1.25, goldenPoints=3, bombPenalty=5,
    bombAfterSeconds=15.0, specialsEndBefore=5.0, comboFrom=5,
)

# Each mode's rules, current (as in GameConfig.asset) and proposed.
CURRENT = {
    "Easy":   dict(BASE, fallMax=3.8, gentle=True, gap=0.6, gapEnd=0.6, endPer10=None, maxAir=1, golden=3, bombs=0,
                   combo="flat", comboMult=2, fallCap=None),
    "Sound":  dict(BASE, fallMax=2.3, fallStart=2.3, gentle=True, gap=1.0, gapEnd=1.0, endPer10=None, maxAir=1, golden=3,
                   bombs=0, combo="flat", comboMult=2, fallCap=None),
    "Normal": dict(BASE, fallMax=4.05, gentle=False, gap=0.6, gapEnd=0.3, endPer10=9, maxAir=3, golden=3, bombs=4,
                   combo="flat", comboMult=2, fallCap=None),
    "Hard":   dict(BASE, fallMax=5.2, gentle=False, gap=0.6, gapEnd=0.2, endPer10=13, maxAir=4, golden=3, bombs=7,
                   combo="flat", comboMult=2, fallCap=None),
}

def proposed():
    p = {k: dict(v) for k, v in CURRENT.items()}
    for k in p:
        p[k]["combo"] = "growing"   # x2 from comboFrom in a row, +1 every comboStep more, up to the mode's cap
        p[k]["comboStep"] = 5
        p[k]["fallCap"] = 4.8       # no fruit (golden included) falls faster than this
    p["Easy"]["comboCap"] = 3
    p["Sound"]["comboCap"] = 3
    p["Normal"]["comboCap"] = 4
    p["Hard"]["comboCap"] = 5
    p["Normal"]["bombs"] = 5
    p["Normal"]["golden"] = 4
    p["Hard"]["bombs"] = 9
    p["Hard"]["golden"] = 6
    p["Hard"]["fallMax"] = 4.8
    p["Hard"]["startPer10"] = 6   # opens faster than Normal (about 3.7 per 10 s), same end rate
    return p

PLAYERS = [  # name, catch rate, bomb-catch rate
    ("Low", 0.35, 0.30),
    ("Average", 0.65, 0.15),
    ("Good", 0.85, 0.05),
    ("Perfect", 1.00, 0.00),
]


def multiplier(streak, m):
    if streak < m["comboFrom"]:
        return 1
    if m["combo"] == "flat":
        return m["comboMult"]
    return min(m["comboCap"], 2 + (streak - m["comboFrom"]) // m["comboStep"])


def schedule(rng, count, start, end):
    if count <= 0 or end <= start:
        return []
    slot = (end - start) / count
    return [start + slot * (i + rng.uniform(0.2, 0.8)) for i in range(count)]


def fall_speed(m, p, golden):
    v = m["fallMax"] if m.get("fallStart") else m["fallSpeedStart"] + (m["fallMax"] - m["fallSpeedStart"]) * p
    if m.get("fallStart"):
        v = m["fallStart"]
    if golden:
        v *= m["goldenSpeedFactor"]
    if m["fallCap"]:
        v = min(v, m["fallCap"])
    return v


def beat(m, p):
    if m["gentle"]:
        return -1
    start = 1 / (FALL_TO_RIM / m["fallSpeedStart"] + m["nextFruitDelay"])
    if m.get("startPer10"):
        start = max(start, m["startPer10"] / 10)
    end = max(start, m["endPer10"] / 10)
    return 1 / (start + (end - start) * p)


def play(m, catch, bomb_catch, rng, dt=0.01):
    goldens = schedule(rng, m["golden"], 0, ROUND - m["specialsEndBefore"])
    bombs = schedule(rng, m["bombs"], m["bombAfterSeconds"], ROUND - m["specialsEndBefore"])
    air = []  # (resolve time, kind, caught)
    score = streak = caught = dropped = golden_seen = bombs_seen = bombs_caught = best = 0
    next_spawn, last_spawn, fastest = 0.0, -1.0, 0.0
    t = 0.0
    while t < ROUND or air:
        # Resolve what reaches the rim or the miss line.
        still = []
        for when, kind, got in air:
            if when > t:
                still.append((when, kind, got)); continue
            if kind == "bomb":
                if got:
                    score = max(0, score - m["bombPenalty"]); streak = 0; bombs_caught += 1
            elif got:
                streak += 1; caught += 1
                pts = (m["goldenPoints"] if kind == "golden" else 1) * multiplier(streak, m)
                score += pts; best = max(best, streak)
            else:
                streak = 0
        landed = len(still) < len(air)
        air = still
        if landed and not air and t < ROUND:
            next_spawn = t + (m["gap"] + (m["gapEnd"] - m["gap"]) * min(1, t / ROUND))
        if t < ROUND:
            p = t / ROUND
            b = beat(m, p)
            after = not air and next_spawn >= 0 and t >= next_spawn
            on_beat = b > 0 and len(air) < m["maxAir"] and last_spawn >= 0 and t >= last_spawn + b
            if after or on_beat:
                next_spawn = -1
                kind = "fruit"
                if bombs and t >= bombs[0]:
                    bombs.pop(0); kind = "bomb"; bombs_seen += 1
                elif goldens and t >= goldens[0]:
                    goldens.pop(0); kind = "golden"; golden_seen += 1
                v = fall_speed(m, p, kind == "golden")
                fastest = max(fastest, v)
                got = rng.random() < (bomb_catch if kind == "bomb" else catch)
                air.append((t + (FALL_TO_RIM if got else FALL_TO_MISS) / v, kind, got))
                if kind != "bomb":
                    dropped += 1
                last_spawn = t
        t += dt
    return dict(score=score, dropped=dropped, caught=caught, golden=golden_seen, bombs=bombs_seen,
                bombs_caught=bombs_caught, best_streak=best, fastest=fastest)


def summarise(rules, runs, seed):
    out = {}
    for mode, m in rules.items():
        out[mode] = {}
        for name, c, bc in PLAYERS:
            rng = random.Random(f"{seed}-{mode}-{name}")
            res = [play(m, c, bc, rng) for _ in range(runs if c < 1 else max(50, runs // 20))]
            scores = sorted(r["score"] for r in res)
            out[mode][name] = dict(
                mean=statistics.mean(scores), p10=scores[len(scores) // 10], p90=scores[(len(scores) * 9) // 10],
                max=scores[-1], dropped=statistics.mean(r["dropped"] for r in res),
                golden=statistics.mean(r["golden"] for r in res), bombs=statistics.mean(r["bombs"] for r in res),
                bombs_caught=statistics.mean(r["bombs_caught"] for r in res),
                best_streak=statistics.mean(r["best_streak"] for r in res), fastest=max(r["fastest"] for r in res))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runs", type=int, default=2000)
    ap.add_argument("--json")
    a = ap.parse_args()
    result = {"current": summarise(CURRENT, a.runs, 1), "proposed": summarise(proposed(), a.runs, 1),
              "rules": {"current": CURRENT, "proposed": proposed()}, "players": PLAYERS}
    for which in ("current", "proposed"):
        print(f"== {which}")
        for mode, rows in result[which].items():
            line = "  ".join(f"{n} {r['mean']:5.1f} ({r['p10']}-{r['p90']})" for n, r in rows.items())
            r = rows["Perfect"]
            print(f"  {mode:7} {line}   fruit {r['dropped']:.0f} golden {r['golden']:.1f} bombs {r['bombs']:.1f} fastest {r['fastest']:.2f}")
    if a.json:
        with open(a.json, "w") as f:
            json.dump(result, f, indent=1)


if __name__ == "__main__":
    main()
