# FruitGame — detailed design & build reference

A 2.5D webcam minigame. The player's own (background-removed) webcam image stands in the scene. A basket follows their **face left and right**, and they catch fruit falling **one at a time** from the treetops. Each caught fruit joins a growing pile behind them.

This document is the reference for building the game. Points from the handwritten plan (`unity plan.pdf`) are marked **[plan]**. Answers to the first round of open questions are marked **[decided]** (see §14). Everything else is a decision made here to fill gaps, marked **[filled in]**, and can be changed. Numbers in the tunables table are starting values to tweak during playtesting.

![Screen layout](images/01-screen-layout.svg)

---

## 1. At a glance

| | |
|---|---|
| Engine | Unity 6000.6.7f1, URP 17.6, Input System 1.20 |
| ML runtime | Unity Inference Engine (`com.unity.ai.inference` 2.6.1, already installed) |
| Target | **macOS and Windows** standalone (runs in the Editor too). Everything chosen here (WebCamTexture, Inference Engine, URP) is cross-platform; anything that turns out macOS-only gets a Windows fallback or is flagged. |
| Screen | **Portrait 3:4** **[plan]**, reference 1080 × 1440, windowed |
| Control | Face x position from the webcam **[plan]**; keyboard/mouse fallback for testing **[filled in]** |
| Style | Low-poly, flat-shaded, bright and minimalist **[plan]**. **Being restyled to pixel / voxel; see §16.** |
| Session | **60 s timed round** **[decided]**. A lives counter is coded in but switched off, as a fallback. |
| Audience | **Accessibility first** **[decided]**: per-player calibration of head movement, so even a small comfortable range controls the full basket travel |

---

## 2. Scene layers (front to back)

The plan stacks the scene as parallel planes along Z. The camera is a **perspective camera with a narrow FOV**, so the depth difference gives a gentle 2.5D parallax without looking like a full 3D game.

![Depth layers](images/02-depth-layers.svg)

| Layer | What | Z (world) | Notes |
|---|---|---|---|
| **L0** | Score counter **[plan]** + round timer | UI, Screen Space – Overlay, inside a "Frame" rect fitted to the 3:4 viewport | Cartoon, minimalist, 2.5D stylised **[plan]**. Score top-left, per the sketch; timer top-right in the same style. |
| **L1** | Foreground **[plan]** | −1.1 … −0.6 | Low-poly trees and shrubbery **[plan]**. **Four small trees** (two pines, a cypress, a small oak) toward the bottom corners **[decided]**, with recoloured Kenney bushes, grass and flowers along the bottom edge. |
| **L2** | Falling fruit + basket **[plan]** | 0 | The gameplay plane. All gameplay maths happen here, in 2D (x, y). |
| **L3** | Webcam person, background removed **[plan]** | 2 | Unlit textured quad: camera colour × segmentation mask. |
| **L4** | Background fruit pile **[plan]** | ≈ 3.0 … 4.2 | Caught fruit stacks up with physics, "between webcam person and removed background" **[plan]**. Built ahead of schedule (§7). |
| **L4b** | Distant trees **[decided]** | 5.2 … 5.4 | Two oak canopies framing the top corners plus two pines, every colour pulled 60–70 % toward a light-blue haze (#A9C6DD) so they read as far away, even against webcam footage. They don't cast or receive shadows. |
| **L5** | Webcam background, "image processed bokeh" **[plan]** | 6 | The same camera frame, heavily blurred, so the real room shows as soft bokeh. |

**Camera [filled in]:** position (0, 0, −12), looking +Z, vertical FOV 30°. `PortraitLetterbox` keeps it at 3:4 in any window size (black bars from a second camera) and stretches the HUD frame to match. At z = 0 the visible area is about **4.8 m wide × 6.4 m tall**; one world unit is one metre. Quads behind z = 0 are scaled by `(12 + z) / 12` so they still fill the frame exactly.

**Lighting [filled in]:** one warm directional light (about 50° down, from the upper left) with soft shadows, plus a light-blue/soft-green gradient ambient. Fruit and trees use flat-shaded lit materials. The person and bokeh quads are **unlit**, so the webcam image keeps its true colour and doesn't pick up scene shadows.

---

## 3. Core loop

1. **Boot**: open the webcam and load the ML models.
2. **Calibrate**: three quick poses (see §5). Skipped automatically if a saved calibration exists, but can always be redone.
3. **Countdown**: 3 · 2 · 1.
4. **Playing**: exactly **one fruit falls at a time** **[plan]**. The next fruit spawns only after the current one has been caught or missed **[plan]**, with a 0.6 s pause in between **[filled in]**.
5. **Game over** when the **60 s timer** reaches 0:00 **[decided]**: show the score, the per-fruit tally and the best score, then play again. A fruit that is mid-air at 0:00 is allowed to land (caught or missed) before the round ends, so the last catch is never stolen.

![Game states](images/05-game-flow.svg)

### Rules
- **Caught** = points for that fruit type (all 1 for now) **[decided]**. The fruit pops into the basket, then a copy of it drops onto the background pile (L4).
- **Missed** = no penalty in timed mode; it only costs time. The miss is counted in the stats. The fruit falls off-screen behind the bottom bushes.
- **Round timer** **[decided]**: 60 s, shown top-right. It pauses whenever the game pauses (face lost, settings open). The last 10 s pulse.
- **Lives (coded, off)** **[decided]**: a `LivesCounter` component and lives HUD exist and are tested, but `GameConfig.endCondition = Timer` keeps them inactive. Switching to `Lives` (or `TimerOrLives`) makes 3 misses end the round, with no other code change.
- **Difficulty ramp** **[filled in]**: driven by elapsed round time. Fall speed goes from 2.7 to 3.8 m/s and the drift cap from 0.7 to 1.2 m/s along an ease-in curve over the 60 s. It stays easy on purpose: the plan says the drift is "capped to make game easy" **[plan]**.
- **Best score** is saved in PlayerPrefs.

### Fruit types & scoring hooks [decided]
Every fruit is its own type, even though they're all worth the same for now, so variety-based scoring can be added later without a refactor:
- A `FruitType` ScriptableObject per fruit (Apple, Banana, Orange, Pear, Lemon, Mango, Grapefruit, Pineapple, Melon) holds its id, display name, prefab(s), points (= 1), spawn weight, collider shape and icon.
- Score goes through one `ScoreRules.PointsFor(FruitType, context)` function. Today it returns `type.points`; later it can apply combos, streaks or rarity.
- `RoundStats` tracks caught and missed counts **per type**, and the Game Over screen shows them as a small icon tally.

---

## 4. Falling fruit

![Fruit motion](images/03-fruit-motion.svg)

| Property | Spec |
|---|---|
| Spawn position | Random x in the band ±2.1 m, at y = +3.6 (just above the top edge, so fruit appears to drop out of the canopy) **[plan: randomised falling position]** |
| Vertical speed | **Constant** **[plan]**: 2.7 m/s at the start, ramping to at most 3.8 m/s *(raised after playtest 1)* |
| Sideways speed | **Random but capped** **[plan]**: \|vx\| ≤ 0.7 m/s at the start, up to 1.2 m/s, wide enough that wall bounces are common and visible *(widened after playtest 1)* |
| Walls | Invisible walls at x = ±2.25 reflect vx (in both phases below), so fruit never leaves the screen sideways **[filled in]** |
| Spin | **Random rotational velocity** **[plan]**: 120–240 °/s around a random axis, fast enough that you can read the fruit's shape as it falls *(was 15–45 °/s)* **[decided]**. Cosmetic: the fall itself is scripted, and spin only matters once a fruit bounces. |
| Size | Every fruit is normalised to about **0.88 m** across (`fruitSize` × the type's `sizeScale`), whatever its model scale *(0.45 m in M1, 0.55 m in M2, ×1.6 on request)* **[decided]** |
| Types | Apple, Banana, Orange, Pear, Lemon, Mango, Grapefruit (common); Pineapple and Melon (rarer and a bit bigger). All worth 1 point for now. |

### Two-phase movement with real basket collisions [decided after playtest 1]
Each fruit is a Rigidbody with a sphere collider, locked to z = 0.

1. **Scripted fall**: every physics step its velocity is held at `(vx, −vy)`, so the "constant downward speed" stays exact. The assist (§6) nudges vx and the walls reflect it.
2. **Knocked**: the moment it touches the basket (rim, side wall or floor edge), the scripted fall stops and physics takes over. The fruit bounces (bounciness 0.45) and falls under `knockedGravity` (9 m/s²). The assist no longer applies, so a basket that arrives late *bangs the fruit away* instead of swallowing it. The basket is a kinematic Rigidbody moved with `MovePosition`, so a fast-moving basket also shoves fruit sideways.

### Catch / miss detection
- **Caught**: the fruit's centre is inside the tub (below the rim, between the tapered inner walls), detected by a trigger inside the basket. That covers clean drops *and* lucky bounces off the rim that end up inside.
- **Missed**: the fruit's centre drops below the **miss line** (y = −2.6) without being caught, or a knocked fruit is still unresolved after 4 s (a safety net in case it balances on the rim).
- **Bump feedback**: every basket contact plays the "tink" and wobbles the basket (sound throttled to once per 0.12 s).
- Because the walls are solid, a fruit (radius ≈ 0.22 m) only drops in cleanly within about ±0.25 m of the basket centre. Nearer the edges it clips the rim and it's up to physics whether it falls in or out. The bigger assist (§6) compensates.

---

## 5. Basket & face control

| Property | Spec |
|---|---|
| Axis | **Locked to webcam face tracking on the x axis** **[plan]**; y is fixed with the rim at −2.0 (roughly chest height, under the chin as in the sketch) |
| Size | Mouth **1.6 m** wide (a third of the screen), 0.8 m tall, scaled up with the ×1.6 fruit so a clean drop still has about ±0.3 m of room. The model tilts 5° toward the camera *(was 12°, which looked too obviously slanted)*. |
| Travel | x clamped to ±1.45 m so the basket never leaves the screen |
| Movement | **Scaled**, so the player doesn't have to move their face all the way **[plan]** (see mapping below). The player's *own comfortable range* maps to the *full* basket travel **[decided]**. |

![Face to basket mapping](images/04-face-to-basket.svg)

### Mapping, per frame [filled in]
1. `u` = horizontal centre of the tracked face box in the **mirrored** camera image (0…1). Mirroring means moving left moves the basket left, like a mirror.
2. `t = (u − u_centre) / (u_centre − u_left)` when the face is left of centre, and `(u − u_centre) / (u_right − u_centre)` when it is right of centre. Each side uses its **own** measured range, so a player who can turn further one way than the other still reaches both edges **[decided]**. The **Sensitivity** setting (0.6×–1.6×) scales on top.
3. Dead zone: `|t| < 0.04 → 0`, so small head jitter at rest doesn't move the basket.
4. `x = limit · softclamp(t)`. This is linear through the middle and eases into ±limit, so overshooting your calibrated range doesn't feel like hitting a wall.
5. A **One-Euro filter** on x: smooth when still, responsive when moving fast.
6. `MoveTowards` with a max speed of 18 m/s *(doubled after playtest 1)* to absorb tracker glitches.

> Note: the basket deliberately moves further than the face does on screen, so it won't sit exactly under the person's chin. That is intended **[decided]**: crossing the whole camera shot with your head is unviable, especially for players with limited neck or upper-body movement.

### Calibration stage [plan: "perhaps a pre-game calibration stage later" → decided: core feature]
Calibration measures how far **this** player can comfortably move their head, so they get the finest possible control over the basket **[decided]**. It runs before the first round and is always available from Settings and from the Game Over screen.

1. **"Look straight at the camera"**: a 1 s steady hold records `u_centre`.
2. **"Move your head left as far as is comfortable"**: records `u_left`, the furthest *held* position (not a momentary peak).
3. **"Move your head right as far as is comfortable"**: records `u_right`.
4. **Try it**: a short practice where the basket follows the new mapping and the player can accept or redo it.

Accessibility details **[decided / filled in]**:
- "Steady" means the face position varies by less than 0.01 over 1 s, shown as a filling ring around the face. The tolerance and hold time are settings, for players with tremor.
- Left and right ranges are stored **separately**, and there is no symmetric `min()`.
- A very small range is accepted; there is only a floor of 0.03 (u units) per side to keep the maths stable. Smaller ranges just mean higher effective sensitivity, and the dead zone and One-Euro filter scale with the range so it stays steady.
- If the face box is under 12 % or over 45 % of the frame width, a prompt says "move closer" or "move back".
- Prompts use large text plus arrows; optional voice/sound cues are a polish item.
- Results are saved in PlayerPrefs, so a returning player can skip straight to playing. Press **C** to recalibrate. Defaults if skipped: 0.5 / 0.35 / 0.65.

### Face tracking robustness [filled in]
- With several faces, track the largest box, but stick with the previous target while it still overlaps it (IoU > 0.3).
- Face lost: the basket holds its position. After **1 s** the game **pauses** with a "Can't see you 👀" overlay, then resumes 1 s after the face returns.
- Keyboard ← → / A D and mouse x drive the basket when no webcam is present or when **F2** is toggled (for testing).

---

## 6. Assist attractor

"Physics slight attractor with capped radius for assist" **[plan]**, specified as **[filled in]**:

- Only acts on the current falling fruit, only during its **scripted fall** (never once it has been knocked by the basket), only while it is **above the rim**, and only within **R = 1.8 m** of the basket mouth centre *(raised from 1.0 m after playtest 1)*.
- It adds a **horizontal** acceleration toward the basket's x: `a = strength · (1 − d/R)`, with a strength of 4 m/s² *(was 1.5)*, and capped so vx never exceeds the drift cap + 1.0 m/s.
- It never touches vertical speed, so the "constant y speed" rule holds.
- It noticeably pulls a near-miss toward the mouth, but a basket that's too late still gets hit and knocks the fruit away. It can be tuned to 0 in settings ("Assist: Off / Low / Normal").

---

## 7. Background fruit pile

"Collected fruits stack up with collision → should put care so there are no physics glitches" **[plan]**. Placed on L4, between the person and the bokeh **[plan]**.

**How it works [decided]:** when a fruit is caught, the falling fruit is consumed by the basket (pop + sparkle). A **pile copy** of the same fruit type then drops from above, behind the person, into a **very narrow invisible rectangular container**. The container is only about one fruit deep, so the fruit stacks like a 2D heap but still looks 3D. Nothing stacks inside the basket. Over the round a colourful heap builds up behind the player, partly hidden by the foreground bushes.

### The container [decided / filled in]
- Invisible box colliders: a floor (y = −3.6) on a ground strip, left and right walls at x = ±3.0 (near the screen edges at that depth), and **front and back walls `pileDepthFactor` (1.15) × the largest fruit's diameter apart**. That's ≈ 1.27 m now that fruit are 0.88 m, so two fruit can never sit side by side in depth.
- Built by `FruitPile` at runtime from `GameConfig`; caught fruit are added by `GameStateMachine`, and the pile is cleared at the start of each round and on Main menu.
- Elongated fruit (banana, pear, pineapple) get a capsule fitted along their longest axis; the rest get spheres.
- **Background look [decided 2026-10-27]:** pile fruit must never be mistaken for a falling fruit. They use `Fruit_Atlas_Far`, a copy of the fruit atlas with the same light-blue haze as the distant trees baked in (`FruitAtlas_Far.png`, made with `magick FruitAtlas.png -fill "#A9C6DD" -colorize 65%`). They neither cast nor receive shadows. Falling fruit keep the full-colour atlas.
- *Stress-tested 2026-10-27: 50 fruit dropped in bursts. It capped at 40, 36/40 were frozen once settled, none escaped the container, and the heap reached y ≈ −0.9.* At 0.88 m a full pile is tall and busy behind the falling fruit; lower `pileMax` if it competes for attention.
- Fruit keep full 3D rotation and tumble naturally between the walls, which is what makes the pile read as 3D rather than flat sprites.
- Drop point: a random x across the container, from y = +3.6, so the copy falls through the frame behind the person.

### Anti-glitch measures [filled in]
| Measure | Why |
|---|---|
| Simple colliders: sphere for round fruit, capsule for banana and pear, never mesh colliders | Stable stacking, cheap |
| Narrow front/back walls instead of a frozen Z axis; optionally a soft Z-centring force if a fruit jams diagonally | Keeps the 2D heap while rotation stays free; nothing tumbles toward the camera |
| Interpolate, collision mode **Continuous Dynamic** | No tunnelling or jitter at render rate |
| `maxDepenetrationVelocity = 1.5` | Overlaps resolve gently instead of fruit "exploding" out |
| Spawn only where `Physics.CheckSphere` finds space; otherwise retry another x up to 5 times, then spawn higher | Never spawn inside another fruit |
| Physics material: friction 0.8, bounciness 0.05 | Settles quickly |
| **Freeze when settled**: speed < 0.05 m/s for 1.5 s → `isKinematic = true` | The pile stops simulating; a big pile costs nothing and can't creep |
| Cap of **40 pile fruits**; past that the oldest shrinks to nothing over 0.3 s, and every frozen fruit is woken so the heap settles into the gap | Bounded cost, no floating fruit |
| Its own physics layer `Pile` (layer 8, for both fruit and container walls), set to ignore every other layer at runtime | Can't interact with gameplay fruit or the basket |
| Solver iterations 10 / velocity iterations 4 for the pile bodies; fixed timestep 1/60 | Better stacking stability |

---

## 8. Webcam pipeline

![Webcam pipeline](images/06-webcam-pipeline.svg)

| Stage | Spec **[filled in]** |
|---|---|
| Capture | `WebCamTexture` with 1280×720 at 30 fps requested; device selectable in settings |
| Prep | A blit mirrors it and centre-crops to 3:4 into a 540×720 RenderTexture |
| Face detection | **BlazeFace** (short-range) through Unity Inference Engine, 128×128 input, GPU backend. Unity publishes a ready-made version on Hugging Face (`unity/inference-engine-blaze-face`); **check the exact repo name and model format at milestone M3**. Runs every camera frame, with async readback so it never stalls rendering. |
| Person segmentation | **MediaPipe Selfie Segmentation** (256×256 square variant), ONNX or LiteRT, through Inference Engine. Gives a 0…1 person mask. **Check model availability and licence (Apache-2.0) at M4.** |
| Mask cleanup | 1-pass blur for soft edges, a small erode against haloing, and a temporal lerp (0.6) to stop flicker |
| Person quad (L3) | Unlit shader: `rgb = cam`, `alpha = smoothstep(0.4, 0.6, mask)` |
| Bokeh quad (L5) | Unlit shader on a downsampled (1/4) camera frame: two-pass Gaussian or Kawase blur at a large radius, 15 % desaturation, slight brightening and a light tint toward the scene palette. Optional: a few soft circular highlights from the brightest areas for a stronger "bokeh" look. |
| Fallback | If segmentation fails to load: the person quad shows the full frame and the bokeh layer is hidden. If there's no camera at all: the bokeh layer becomes a static soft gradient, the person layer is hidden, and the keyboard controls the basket. |

**Cross-platform notes [decided: macOS + Windows]**
- WebCamTexture, Inference Engine (GPU compute: Metal on macOS, DX12/Vulkan on Windows) and URP all run on both platforms. Use the GPU backend, falling back to CPU if the GPU backend fails.
- macOS needs camera permission: set **Player Settings → Camera Usage Description**, and allow Unity/the built app under System Settings → Privacy → Camera.
- Windows needs no player setting, but the camera can be blocked under Settings → Privacy → Camera; show a clear message if `WebCamTexture` produces no frames.
- Webcams report different resolutions and orientations (`videoRotationAngle`, `videoVerticallyMirrored`); the prep blit handles both.
- Shaders are HLSL/ShaderGraph only, with no platform-specific code paths.

---

## 9. UI & feedback

**Score counter (L0) [plan: cartoon minimalist 2.5D stylised → filled in]**
- Top-left, as in the sketch. A big rounded heavy font (e.g. *Fredoka* or *Baloo 2*, both OFL) in TextMeshPro: white fill, thick magenta outline (#C2185B), and a hard drop shadow offset down-right so it reads like an extruded sticker.
- On +1: scale-pop 1 → 1.25 → 1 over 0.2 s, plus a small "+1" that floats up from the basket.
- **Timer**: top-right in the same sticker style (`0:42`), pulsing during the last 10 s.
- Lives: three small fruit icons under the score; hidden while lives are off.

**Depth cues: keeping the play layer readable [decided 2026-10-27]**
- **Rim light on live fruit:** falling fruit use `Fruit_Atlas_Live` with the custom `FruitGame/Fruit Rim` shader (simple lit + fresnel rim, `_RimStrength` 0.7, `_RimPower` 2.5). Shadows and depth are borrowed from URP Lit, so the fruit still casts shadows and feeds depth of field. Pile fruit keep their hazed material.
- ~~Ground shadow under the falling fruit~~: tried (a soft ellipse at rim height with a light halo) and **removed at your request**; it didn't fit the look.
- **Far-layer blur:** Gaussian depth of field in `GamePostProfile`, from 14.5 m to 18 m from the camera (max radius 1). The play plane (12 m) and the person (14 m) stay sharp, the pile (~15.6 m) softens slightly, and the distant trees (~17 m) soften more.
- Rejected as hurting the look: a smaller pile, a lower pile cap, and the ground shadow.

**Juice [filled in]:** a catch squashes the basket vertically for 0.12 s and bursts a few leaf-coloured particles. A miss plays a soft thud and a brief red tint on the lost life icon. A rustle sound plays from the canopy as each fruit spawns, to telegraph it.

**Audio [filled in]:** pop (catch), tink (rim graze), thud (miss), rustle (spawn), and a gentle looping ambience. All short, one mixer, nothing loud.

**Screens: fully mouse-operable for a booth [decided after playtest 1].** Nobody should need to look at the keyboard; keys stay as shortcuts only. All buttons are large (≥130 px in the 1080×1440 frame) rounded rectangles in pink (primary) or slate (secondary).

| Screen | Contents | Keyboard shortcut |
|---|---|---|
| **Start menu** | "Catch the fruit!", a hint, big **Play** button | Space / Enter |
| **Playing** | Score, timer, **II** pause button top-centre | P / Esc to pause |
| **Pause menu** | Dimmed game, **Resume**, **Main menu** (abandons the round) | P / Esc / Space to resume |
| **Game over** | "Time's up!", score (same size as before), best, **fruit tally in two small columns**, missed count, **Play again**, **Main menu** | Space = play again |

Planned: the calibration overlay (prompts plus a steady-ring) and a small Settings panel (sensitivity, recalibrate, steady-hold tolerance and time, assist, camera device, mirror), both mouse-operable. A debug overlay on **F1** shows the face box, the mask, the mapping value t and FPS.

---

## 10. Art & assets

### Approved downloads (2026-10-26) → `~/Documents/3D Assets/`
| Folder | Contents |
|---|---|
| `Kenney Nature Kit (recoloured)/` | Nature Kit 2.1 OBJ models (CC0). Greens remapped to the tree pack (grass #68AB42, leafsGreen #5A9522, leafsDark #46741A) and stones to warm grey; originals are listed in its README.txt |
| `FruitGame Extras/Audio/` | The first pick of each sound group: catch_pop, rim_tink, miss_thud, spawn_rustle, pile_land, countdown_tick, round_over (Kenney, CC0; mapping in CREDITS.txt) |
| `FruitGame Extras/Fonts/` | Fredoka variable font + `Fredoka-Bold.ttf` (a static 700 instance made with fontTools), OFL |
| `Downloads/` | Untouched original downloads + `_previews/` |

The sounds and Fredoka-Bold are already imported into `Assets/FruitGame/`; the Nature Kit gets imported in M2.

### Provided packs (`~/Documents/3D Assets/`)
| Pack | Use |
|---|---|
| **Low Poly Food** (FBX / GLTF / OBJ, gradient-atlas `Texture.png`) | `Fruits/*/<Name>_01.fbx` and `_02.fbx` (two variants each) for falling and pile fruit. Slices are unused (possible future "splat" on miss). |
| **PolyOne Free Fruits** (from Photo Booth 2, `Art/Models/FruitsPolyOne/`) | Grapes, cherries, dragon fruit, coconut, avocado, mangosteen, watermelon (two variants), on their own gradient atlas (downsized to 2048) plus a hazed copy for the pile. |
| **Low Poly Trees (Lite)**: Bare, Pine, Oak, Maple, Cypress | Oak and Maple for the canopy in the top corners; Pine and Cypress partly visible at the side edges. The licence forbids redistribution, so don't push them to a public repo. |

### Gaps to fill → downloaded or made into `~/Documents/3D Assets/`, shown for a yes/no, then imported **[decided]**
Downloaded packs go into the asset folder first and are previewed straight away. Only approved items get imported into the project.

| Asset | Plan |
|---|---|
| **Basket** ✅ | Modelled ourselves: `FruitGame Extras/Basket/make_basket.py` generates `Basket.obj/.mtl`, a flat-shaded 10-sided tapered tub with a chunky rim and two bands in alternating wicker terracottas (#D2603E / #C4552F, bands #8E3B23, inside #7A2F1B). In game it's tilted 12° toward the camera so the mouth reads, and scaled to `basketMouthWidth`/`basketHeight` so it lines up with the physics walls. |
| **Bushes / shrubbery, grass, rocks** ✅ | Kenney *Nature Kit* (CC0), recoloured: bushes, grass, flowers, a mushroom group and a rock along the bottom edge |
| **Pile floor** | A simple low-poly ground strip in muted earth tones, mostly hidden behind the foreground |
| **Font** | Fredoka (Google Fonts, OFL); Korean glyphs fall back to Jua (OFL, from Photo Booth 2) |
| **SFX** | Kenney audio packs (CC0): pop, tink, thud, rustle |

Palette: leaf greens #3D7F3A / #5AA64F / #82C068, basket #D2603E / #8E3B23, score #C2185B, sky tint #9CC3D9. Fruit keep their own atlas colours.

---

## 11. Tunables (`GameConfig` ScriptableObject)

| Key | Default | Range |
|---|---|---|
| `endCondition` | `Timer` | `Timer` / `Lives` / `TimerOrLives` |
| `roundDuration` | 60 s | |
| `fallSpeedStart` / `fallSpeedMax` | 2.7 / 3.8 m/s | eased over the round |
| `driftCapStart` / `driftCapMax` | 0.7 / 1.2 m/s | |
| `spinDegPerSec` | 120–240 | |
| `spawnXRange` | ±2.1 m | |
| `nextFruitDelay` | 0.6 s | |
| `lives` | 3 | used only when the end condition includes lives |
| `basketMouthWidth` / `basketHeight` / `basketTaper` | 1.6 m / 0.8 m / 0.75 | the colliders are built from these, and the model is scaled to match |
| `fruitSize` | 0.88 m | |
| `basketXLimit` | 1.45 m | |
| `rimY` / `missY` | −2.0 / −2.6 | |
| `sensitivity` | 1.0 | 0.6–1.6 |
| `deadZone` | 0.04 | in t units, so it scales with the calibrated range |
| `calibMinRangePerSide` | 0.03 | |
| `steadyTolerance` / `steadyHoldTime` | 0.01 / 1.0 s | adjustable for accessibility |
| `pileZ` / `pileDepthFactor` | 3.6 / 1.15 × largest fruit | |
| `pileFloorY` / `pileHalfWidth` / `pileSpawnY` | −3.6 / 3.0 / 3.6 | |
| `oneEuroMinCutoff` / `oneEuroBeta` | 1.2 / 0.4 | basket x in metres |
| `softClampKnee` | 0.8 | |
| `faceReturnGrace` | 1.0 s | |
| `basketMaxSpeed` | 18 m/s | |
| `knockedGravity` / `fruitBounciness` / `fruitFriction` | 9 m/s² / 0.45 / 0.3 | physics after a basket hit |
| `knockedTimeout` | 4 s | |
| `assistRadius` / `assistStrength` | 1.8 m / 4 m/s² | 0 = off |
| `faceLostPauseDelay` | 1.0 s | |
| `pileMax` | 40 | |
| `pileFreezeSpeed` / `pileFreezeTime` | 0.05 m/s / 1.5 s | |
| `bokehBlurRadius` / `bokehDesaturate` | large / 0.15 | |

---

## 12. Project layout & scripts

Files marked ✅ exist (M1); the rest are planned.

```
Assets/FruitGame/
  Art/        Fonts/ ✅ (Fredoka-Bold + SDF asset + Sticker material)  Materials/ ✅ (greybox + Palette/)  Rendering/ ✅ (GamePostProfile)
              Models/ ✅ Fruits/ (9 × _01/_02 FBX + FruitAtlas.png)  Trees/ (Oak, Pine, Cypress)  Nature/ (recoloured Kenney OBJ)  Basket/
  Prefabs/    Art/ ✅ (generated: Fruits/, Trees/, Nature/, Basket/)
  Audio/      ✅ 7 clips
  Config/     GameConfig.asset ✅  FruitTypes/<Name>.asset ✅ (9 types)
  ML/         blazeface.(onnx|sentis)  selfie_segmentation.(onnx|tflite)
  Prefabs/    Fruit_<Type>.prefab  PileFruit_<Type>.prefab  Basket.prefab  Bush_*.prefab
  Scenes/     Game.unity ✅ (generated)
  Shaders/    FruitRim.shader ✅  PersonCutout.shader ✅  MaskSmooth.shader ✅  BokehBlur.shader ✅  BokehBackdrop.shader ✅
  Scripts/
    Core/      GameConfig ✅, GameStateMachine ✅, SaveData ✅, RoundTimer ✅, LivesCounter ✅, PortraitLetterbox ✅
    Webcam/    WebcamSource ✅, FaceTracker ✅, PersonSegmenter ✅, PersonCutout ✅, BokehBackdrop ✅
    Control/   BasketInputSource ✅, FallbackInput ✅, BasketInputRouter ✅, FaceToBasketMapper ✅, OneEuroFilter ✅,
               CalibrationData ✅, CalibrationController ✅
    Gameplay/  FruitType ✅, ScoreRules ✅, RoundStats ✅, FruitSpawner ✅, FallingFruit ✅,
               BasketController ✅, BasketAssist ✅, FruitPile ✅, PileFruit ✅
    UI/        GameHUD ✅ (score, timer, lives, messages, game over), FaceDebugOverlay ✅ (F1), CalibrationUI, SettingsUI
    Feedback/  AudioFeedback ✅, JuiceFx
    Editor/    ArtLibrary ✅ (model → prefab helpers, palette and haze materials)
```

**How the pieces connect (M1):**
- `BasketInputSource` is the seam for control. `FallbackInput` (keyboard ←/→ A/D, mouse while it's over the frame) implements it today; the face mapper (M3) will be a second implementation, and `BasketController` doesn't care which one it gets.
- `GameStateMachine.Confirm()` / `TogglePause()` are public, so a face gesture can drive them later (Space/Enter and P call them today).
- `GameState.Calibrate` exists but is unused until M3.
- **The scene is now hand-maintained [decided 2026-10-27].** The code scene builder was run one last time and then retired; its source is kept for reference in `Docs/archive/GreyboxSceneBuilder.cs.txt` (outside `Assets`, so it isn't compiled). `Game.unity` is the source of truth: edit it in the Editor, and Claude edits the open scene live and saves it. `ArtLibrary` stays as an editor utility for turning new models into prefabs.
- **OBJ colours:** Unity reads OBJ `Kd` as linear and washes it out, so `ArtLibrary` rebuilds OBJ materials as shared palette materials with the intended sRGB colours (`Art/Materials/Palette/Obj_*`). Tree FBX colours import correctly and are kept.

Scene hierarchy: `Main Camera`, `Directional Light`, `L1_Foreground`, `L2_Play` (Basket, FruitSpawner, Walls), `L3_Person` (quad), `L4_Pile` (floor, bounds, pile root), `L5_Bokeh` (quad), `UI` (Canvas), `Systems` (state machine, webcam, trackers).

---

## 13. Milestones

| # | Goal | Done when |
|---|---|---|
| **M1** ✅ | Greybox loop | The keyboard moves a placeholder basket; fruit spawns one at a time with constant vy, capped vx and wall bounce; catch/miss, per-type score and stats, the 60 s timer, Game Over and restart all work; lives are coded but off. *Verified 2026-10-27 in a full Editor round: 28 fruit resolved, catches within the mouth and misses outside it, the round ended on the timer after the last fruit landed, and lives hit 0 without ending the round.* |
| **M1.1** ✅ | Playtest 1 fixes | Basket physics collisions, bigger assist, tuning, mouse-only menus (start / pause / main menu / game over), two-column tally. *Verified 2026-10-27 in the Editor: rim hits bounce fruit away or occasionally in, every button path works when invoked, and the tally fits. Real mouse clicks still need a manual check (simulated clicks can't reach an unfocused Game view).* |
| **M2** ✅ | Art pass | Real fruit, trees, bushes, basket model, lighting and the styled score counter; the 3:4 portrait frame is set up. *Done 2026-10-27: Low Poly Food fruit (2 variants each, shared atlas), oak canopies in both top corners (the maple was dropped because its red-orange competed with the fruit), recoloured Nature Kit shrubs and flowers along the bottom, our own basket model, a warm sun + trilight ambient, and a global post volume (saturation +10, contrast +6, soft bloom, vignette 0.2, no tonemapping). Fruit enlarged to 0.55 m and the mouth to 1.25 m. A full round ran in the Editor with no errors. L3–L5 (person, pile, bokeh) are still placeholders for M4/M5.* |
| **M3a** ✅ | Webcam in | `WebcamSource` (Systems) picks a camera (`GameConfig.preferredCameraName`, else the first non-built-in one, e.g. an iPhone via Continuity Camera, else any) and keeps a mirrored, centre-cropped 3:4 RenderTexture (540×720). `WebcamBackdrop` shows it, unblurred for now, on the L5 quad (unlit `Webcam_Backdrop` material), with a plain colour fallback if there's no camera or no frames after 3 s. The placeholder person is hidden. *Verified 2026-10-27 with an iPhone (Continuity Camera) at 1280×720.* |
| **M3b** ✅ | Face detection | `FaceTracker` (Systems) runs BlazeFace short-range (`ML/blaze_face_short_range.onnx` + `blaze_face_anchors.csv`, Apache-2.0, from `unity/inference-engine-blaze-face`) on the GPU through Inference Engine. The 3:4 frame is pillarboxed into 128×128; score filtering and NMS are baked into the graph as in Unity's sample. It tracks one face, the largest, but sticks with the previous one while IoU ≥ 0.3, and outputs `HasFace`, `FaceRect`/`FaceCenter` in webcam UV (mirrored, bottom-left origin, matching the screen), `Score` and `LastSeenTime`. `FaceDebugOverlay` (F1) draws a green box plus camera/score/rate status. The backdrop quad was resized to fill the view exactly so the UV lines up with the screen. *Verified 2026-10-27 with the iPhone camera: 94 % score, ~130 detections/s, box on the face.* |
| **M3c** ✅ | Face → basket | `FaceToBasketMapper` implements §5's mapping: per-side range from `CalibrationData` (PlayerPrefs; defaults 0.5 / 0.35 / 0.65 until M3d), × `sensitivity`, a rescaled dead zone (0.04), a soft clamp past the 0.8 knee, then a One-Euro filter (min cutoff 1.2, β 0.4), with the basket's 18 m/s cap last. `BasketInputRouter` is the basket's input: the face whenever the webcam is live, else mouse/keyboard; F2 forces mouse/keyboard. `GameStateMachine` pauses with "Can't see you!" after 1 s without a face during a round, and resumes 1 s after the face is back (a manual pause still waits for Resume). *Verified 2026-10-27: the mapping maths checks out (u 0.575 → t 0.48 → 0.70 m), the face-lost pause resumed on its own, and a face-controlled round caught 5 out of 5 in its first 15 s.* |
| **M3d** ✅ | Calibration | `CalibrationController` (Systems) runs look straight → head left → head right; each pose is captured once the face stays within ±`steadyTolerance` (0.015 u) for `steadyHoldTime` (1 s), shown as a pink ring filling around the face. Left and right must clear the centre by `calibMinRangePerSide`, and a face narrower than 12 % or wider than 45 % of the frame gets "move closer / back". Then comes **Try it!** (the basket follows the new range) with **Looks good** (saves to PlayerPrefs) and **Redo**; **Back** / Esc cancels and keeps the old calibration. It runs automatically on the first Play with a live camera and no saved calibration; re-run it with the start menu's **Calibrate** button or C. *Verified 2026-10-27: a live player calibrated (L 0.19 / C 0.50 / R 0.82), accepted, and the round started on the new range.* |
| **M3** ✅ (M3a–d) | Face control | Webcam in, BlazeFace running, mapping + One-Euro filter, the calibration stage, face-lost pause. Playable with the face. |
| **M4a** ✅ | Segmentation | `PersonSegmenter` (Systems) runs MediaPipe Selfie Segmentation (`ML/selfie_segmentation.onnx`, Apache-2.0, ONNX from `onnx-community/mediapipe_selfie_segmentation`; input 1×3×256×256, pixels 0–1) on the GPU. Frame → tensor → model → `Mask` RenderTexture (R = person), with no CPU readback; the 3:4 frame is stretched into the square input and back, so mask UV = webcam UV. The F1 overlay shows a live mask thumbnail and the rate. *Verified 2026-10-27 (MacBook camera): ~220 masks/s, ~21 % of the frame is person, 0.98 inside the tracked face box, 0 in the top corners.* |
| **M4b** ✅ | Cut-out | `PersonCutout` on the L3 "Person Cutout" quad (z = 2, scaled to fill the view so UVs match) shows webcam colour × mask through `FruitGame/Person Cutout`, with alpha = smoothstep(0.4, 0.65, mask). `Hidden/FruitGame/Mask Smooth` blurs each new mask 3×3 and blends it 60 % with the previous one, so edges are soft and don't flicker. The shader draws at queue Geometry+500 (after the opaque scene, blending over the pile and backdrop) and writes depth only where the person is (plus a DepthOnly pass), so depth of field keeps the player sharp and the layers behind soft. Hidden until the camera and mask are live. *Verified 2026-10-27: a clean cut-out with soft hair edges, the basket and bushes in front, and the pile and far trees behind.* |
| **M4c** ✅ | Bokeh backdrop | `BokehBackdrop` on the L5 "Bokeh Backdrop" quad: `Hidden/FruitGame/Bokeh Blur` pass 0 weights the webcam colour by "not person" (mask smoothstep 0.15–0.45) as premultiplied rgb·w / a = w at ¼ resolution; five Kawase passes (offsets 0.5 … 4.5) blur it; then `FruitGame/Bokeh Backdrop` divides rgb / a, so only room pixels are averaged and the player doesn't smear in, and grades it (15 % desaturate, ×1.08 brighten, 12 % tint toward #A9C6DD). Fallbacks: a plain #B9D3DD until the camera is live, and it still blurs (without masking) if segmentation isn't running. This replaces the temporary `WebcamBackdrop` (deleted). *Verified 2026-10-27: a soft, evenly filled room with no halo of the player.* |
| **M4d** ✅ | High-res matting | Replaced the 256×256 MediaPipe mask (hair and jacket edges flickered) with **Robust Video Matting** (ResNet-50, fp32, `ML/rvm_resnet50_fp32.onnx`, **GPL-3.0**: fine for running at the booth; distributing the game would require GPL-3.0 compliance). RVM is a video matting model with four recurrent state tensors carried between frames, so edges stay temporally stable. `PersonSegmenter` bakes `downsample_ratio` 0.5 into the graph, feeds the frame at 576×768 (each side × 0.5 must be a multiple of 32), outputs a 576×768 alpha matte, and copies r1o … r4o into the next frame's inputs (reallocated when their shape changes). MediaPipe remains as a fallback if no RVM model is set. The cut-out was retuned for a true matte: temporal blend 0.85, blur 0.5 texel, edge smoothstep 0.05–0.75. *Verified 2026-10-27: no errors; a sharp, strand-level hair edge; ~1 % of the person area changes between frames when still; ~26 mattes/s (the game frame rate follows this).* |
| **M4e** ✅ | Matting off the critical path | Running RVM whole every frame held the game to ~26 fps (on an Apple M5, on battery, Low Power Mode off, so the battery wasn't the cause). `PersonSegmenter` now advances RVM with `Worker.ScheduleIterable`, a slice of layers per frame. The slice adapts (×0.85 when a frame runs over 110 % of the target, ×1.05 when under 95 %) to hold `targetFrameRate` (60), and the cut-out reuses the last finished matte while the camera colour updates every frame. No separate process needed. *Verified 2026-10-27 (Editor): the game at ~115–125 fps, the matte at ~21/s (~42 layers/frame), face tracking ~53/s, no errors.* |
| **M4** ✅ (M4a–c) | Person & bokeh | The segmentation mask drives the person quad; the blurred bokeh background; the fallbacks. |
| **M5** ✅ *(pulled forward)* | Fruit pile | Caught fruit stacks in the narrow L4 container with every anti-glitch measure; stress-tested with 40+ fruit. *Done 2026-10-27, before M3/M4 on request.* |
| **M6** *(moved after M15)* | Polish | Assist tuning, juice, audio, settings, debug overlay; macOS and Windows builds with camera permission. Now scheduled after the pixel/voxel redesign; see §16. |
| **M7a** ✅ | Photo capture | `PhotoBooth` (Systems): Game over → **Photo** (third button in the row) → "Strike a pose!" + **Take photo** → 3·2·1 → white flash + sound → off-screen 1080×1440 render of the game camera (no UI; basket and fruit spawner hidden; pile switched to full-colour `Fruit_Atlas` via `FruitPile.SetPhotoMode`) → preview with **Save** (PNG to `~/Pictures/FruitGame/FruitGame_<date>_<time>_<score>.png`), **Retake**, **Done** (back to the start menu; pile cleared). New `GameState.Photo`. *Verified 2026-10-27: a 1080×1440 capture, basket hidden, pile in full colour, a 1.1 MB PNG saved (test file deleted), Done restores the menu.* |
| **M7b** ✅ | Frame | `PhotoFrame` ("Photo Frame" object, parked at y = 1000 with its own disabled camera and a fixed 1080×1440 target) composes the final PNG on an off-screen canvas: a white sticker background, the scene inset 1000×1030 inside a pink #C2185B keyline (cropped to fit, trimming mostly from the top via `cropFromBottom` 0.2), `GameConfig.photoCaption` along the top (`{date}` → yyyy.MM.dd; placeholder "EVENT NAME · {date}"), and along the bottom the **LAB LOGO** placeholder · the sticker-style score + "Best n" · the **SCHOOL LOGO** placeholder. Assign the real logos as Texture2D on `PhotoFrame` (`labLogo`, `schoolLogo`); they replace the placeholders and keep their aspect ratio. *Verified 2026-10-27: a correct framed 1080×1440 composition.* |
| **M7c** ✅ | Beauty filter | Built into `FruitGame/Person Cutout`, so it touches only the player and shows live on the pose screen as well as in the capture. `_Beauty` = `GameConfig.beautyStrength` (default 1, 0 = off) while the booth is open, else 0. Steps: 5×5 bilateral (edge-preserving) blur blended in up to 75 % on skin, using a YCbCr skin detector so eyes, lips, glasses and hair stay crisp; brightening ×1.12 weighted toward skin (30 % elsewhere); contrast 0.97; a faint highlight glow; a warm-pink midtone shift on skin; cheek blush (`_BlushStrength` 0.22) placed from the tracked face box (40 % up, 22 % / 78 % across, radius 14 % of its width). A first version greyed out hair and clothes (global contrast cut + shadow lift), so the effect is now skin-weighted. *Verified 2026-10-27 with a side-by-side render: smoother, brighter, warmer skin; hair and outline keep their depth.* |
| **M7c2** ✅ | Beauty filter v2 (GPUPixel) | Replaced with a port of **GPUPixel**'s beauty filter (github.com/pixpark/gpupixel, **Apache-2.0**; credits in `Art/Beauty/NOTICE.txt`): (1) guided smoothing: `Hidden/FruitGame/Beauty Prep` builds a half-res box-blurred mean and a blurred squared high-pass (Δ 14) in gamma space, and the cut-out pulls each pixel toward the mean by (1 − var/(var + 0.1)) × brightness gate × `_BlurAlpha` (0.9), which keeps edges; (2) a 3×3 high-pass sharpen (`_Sharpen` 0.3); (3) whitening: GPUPixel's 4-LUT chain baked offline into `beauty_skin_lut.png` + `beauty_light_lut.png` (standard 512×512 64-level LUTs; the chain read live on the GPU gave red/cyan artefacts), blended by `_Whiten` 0.6 and faded out on near-black pixels so hair stays natural; (4) makeup from **BlazeFace's six keypoints** (`FaceTracker.Keypoints`): soft blush (`_BlushStrength` 0.14, halved after feedback) and a lip tint on the mouth point gated by redness (`_LipStrength` 0.45). Overall strength is still `GameConfig.beautyStrength`. *Verified 2026-10-27 with side-by-side renders: porcelain, even skin, rosy lips and cheeks, crisp eyes and glasses, dark natural hair. Known nit: bright glasses glare picks up a slight cool tint.* |
| **M7c3** ✅ | Face reshape | `FaceMesh` (Systems) runs **MediaPipe Face Mesh v2** (478 points incl. irises; `ML/face_mesh.onnx`, MIT export of an Apache-2.0 model) on a square crop (1.8× the BlazeFace box), 256×256 NHWC on the GPU, and maps the points back to webcam UV with 0.5 smoothing. It only runs while the photo booth is open. *Checked: nose/eyes/mouth within ~0.01 u of BlazeFace.* `FruitGame/Person Cutout` warps the sampling UV, colour and mask alike, with GPUPixel's maths: **eye enlargement** (centre = mid of eye corners 33/133 and 263/362, radius ≈ eye width; `GameConfig.bigEye` 0.4) and **V-line slimming** (jaw points 172/136/397/365 pulled 22 % toward a point between chin 152 and nose 1; radius 0.85 × eye gap; `GameConfig.faceSlim` 0.4). Makeup now also uses mesh points (cheeks 50/280; lips 13/14, width 61–291), falling back to the BlazeFace keypoints. All placements ease in over time (`makeupFollow` 10/s), so nothing jumps between detections. *Verified 2026-10-27: larger eyes, slimmer jaw, half-strength blush; glasses frames bend slightly near the eyes (inherent to eye enlargement).* |
| **M7d** ✅ | Sharing | The preview now has **Print** and **Get it on your phone** (both save the PNG first). **Print**: `PhotoShare.Print` sends the saved PNG to the default printer (macOS `lp -o fit-to-page`, optional `printerName`; Windows shell "print" verb) and shows "Couldn't print (is a printer set up?)" on failure. **Phone**: `PhotoShare` runs a small `HttpListener` server (port 8765) that serves each published photo at an unguessable link `http://<LAN IP>:8765/p/<id>` (a mobile page with a Save button) and `/p/<id>.png`; links expire after 15 min, and nothing leaves the local network. *Verified 2026-10-27: page 200, PNG 200 (1080×1440, ~0.98 MB), unknown id 404; print fails gracefully (this Mac has no printer configured).* **QR code:** `QrCode.cs`, a self-contained encoder written in-project instead of a third-party DLL (byte mode, ECC level M, versions 1–10, Reed–Solomon, all 8 masks scored by the standard penalty rules; registers itself as `PhotoBooth.QrEncoder`). The phone card shows the QR plus the link as text. *Verified: four test strings (v1, v3, v4 and v7, i.e. multi-block plus version info) and the booth's own QR all decode with macOS Core Image; the booth QR's link served the page and PNG (200).* **Email fallback not built** (QR is workable). Booth notes: phones must be on the same Wi-Fi as the computer, venue Wi-Fi with client isolation will block it, and macOS may ask to allow incoming connections the first time. |
| **M7** ✅ (M7a–d) | Photo booth | After a round: a framed souvenir photo of the player with that round's fruit pile, a Korean-style beauty filter, lab + school logos and the score/best score, saved locally. Design in §15. Needs M4 (person cut-out) and M5 (pile). |

---

## 14. Decisions log

| # | Question | Decision |
|---|---|---|
| 1 | End condition | **60 s timer.** A lives counter is coded in but unused, as a fallback (`endCondition` switch). |
| 2 | Background pile | A **copy** drops behind the person into a **very narrow invisible rectangular container**, so fruit stacks like 2D but looks 3D. Nothing stacks in the basket. |
| 3 | Basket vs. face | The basket **may move away from under the chin**. Crossing the whole shot with your head is unviable, and the game is aimed at **accessibility**. Per-player **calibration** of left/right head range gives the finest control. |
| 4 | Fruit variety | All worth the same for now, but **each fruit is tracked as its own type**, so variety-based scoring can come later. |
| 5 | Bushes / SFX / basket | **Download CC0 packs first** into `~/Documents/3D Assets/` and **show them immediately** for a yes/no. |
| 6 | Platform | **macOS and Windows**; macOS only is acceptable if something can't be made cross-platform. |
| 7 | Fruit vs basket (playtest 1) | Fruit get **real physics collisions with the basket**: hitting the rim or a side bangs them away; the assist radius and strength go up so the attractor is noticeable. |
| 8 | Tuning (playtest 1) | Faster initial fall (2.7 m/s), wider sideways range (0.7–1.2 m/s) so wall bounces are visible, basket top speed doubled (18 m/s). |
| 9 | Booth UI (playtest 1) | **Everything mouse-operable**; the pause menu gets **Main menu**. |
| 10 | Game over layout (playtest 1) | Same-size score; a smaller **two-column** fruit tally so long runs don't overflow. |
| 11 | Ordering (2026-10-27) | More **UI tweaks are coming from you later**; M2 (art pass) goes first. Keep UI code easy to restyle (GameHUD + builder helpers). |
| 12 | Art tweaks (2026-10-27) | Fruit ×1.6 (0.88 m), with the basket scaled up to 1.6 m to match; spin 120–240 °/s; basket tilt 5°; four small foreground trees; hazy light-blue distant trees. |
| 13 | Fruit pile | Built now rather than waiting (M5 pulled ahead of M3/M4). |
| 14 | Scene builder | Retired; the scene is edited directly and stays visible in the Editor. |
| 15 | Pile readability | Pile fruit hazed like the distant trees (65 % toward #A9C6DD), with no shadows, so they read as background and never compete with the falling fruit. |
| 16 | Depth cues | Keep the rim light on live fruit and the slight depth-of-field blur on the far layers. The ground shadow was tried and then scrapped completely. A smaller pile and a lower cap were rejected as hurting the look. |
| 17 | Photo booth (2026-10-27) | New milestone M7: a photo with the last round's pile, a Korean-style beauty filter, an outline frame with the lab's and school's logos, and the best score (§15). |
| 18 | Photo booth details | Placeholder logos until the files arrive (ask when M7 starts); print first, QR download second, email as a fallback; event caption yes; pile in full colour. |
| 19 | Segmentation quality | The background separation must be as seamless as possible, so quality over performance: **RVM (GPL-3.0)** chosen over MODNet (Apache-2.0) for its temporal stability. |
| 20 | Beauty filter direction | Much stronger, SNOW/SODA/Douyin-flavoured (not Douyin-strong). No budget for paid SDKs (Banuba, DeepAR, Tencent Effect, FaceUnity), so port **GPUPixel** (Apache-2.0): skin + makeup first, face reshape (needs face mesh) later. |
| 21 | V-line off | The face slimming looked unnatural: `GameConfig.faceSlim` = 0 (eye enlargement stays at 0.4). The code remains, so it can be re-enabled with a value > 0. |
| 22 | Gentler beauty | Every individual beauty setting halved from the tuned values: smoothing 0.9→0.45, sharpen 0.3→0.15, whitening 0.6→0.3, blush 0.14→0.07, lips 0.45→0.225 (`Person_Cutout` material), eye enlargement 0.4→0.2 (`GameConfig.bigEye`). Overall `beautyStrength` stays at 1; face slim stays 0. |
| 23 | Bokeh off (for now) | `BokehBackdrop` got a **Bokeh** switch (default off): the backdrop shows the plain, sharp camera frame, ungraded and unmasked. The scene's far-layer depth of field (14.5–18 m) still softens it slightly. Tick **Bokeh** on the "Bokeh Backdrop" object to bring the blurred room back. |
| 24 | Assets from Photo Booth 2 (2026-10-28) | Brought over from the sister project `~/Documents/Unity/Photo Booth 2`: **(a)** the official GIST school logo (`Art/Logos/GIST_Wordmark.png`, the wide 광주과학기술원 wordmark) as `PhotoFrame.schoolLogo` (`GIST_Logo.png`, the 지스트 mark, is also there but unused); its slot was widened from 250 to 330 px so the wordmark prints about 64 px tall instead of 48 and still clears a 3-digit score; the lab logo is still a placeholder. **(b)** the *Jua* font (OFL) as a dynamic TMP fallback on Fredoka-Bold SDF, so Korean text renders. **(c)** 7 new fruit types from *PolyOne Free Fruits*: grapes, cherries, dragon fruit, coconut, avocado, mangosteen and watermelon. Apple, banana, orange and pineapple were skipped as duplicates. Their models use their own atlas, so `FruitType` gained optional `atlas`/`atlasFar` textures that the live, haze and photo materials swap in. The haze copy `PolyOneAtlas_Far.png` is baked at 72 % (vs 65 % for the Low Poly Food atlas) because the PolyOne colours are more saturated. **(d)** a score-over-time step graph on the game-over panel (`ScoreGraph`, adapted from Photo Booth 2's `ScoreGraphGraphic`), fed by `RoundStats.History`. The panel grew to 1230 px tall to fit the graph and up to 16 tally lines. |
| 25 | Pixel / voxel redesign (2026-10-28) | The look moves from low-poly to pixel/voxel, and the game is titled **지스트의 과일 사진가게**. Font: **Neo둥근모 Pro** (OFL) for both Korean and English, replacing Fredoka and Jua. Models are voxelised from the existing ones; rendering is a hybrid of voxels and a gentle low-res pixel render; the player stays sharp with a pixel outline; depth uses dithering; the UI has keycap-style pixel buttons with true-3D voxel title, score and timer. Roadmap M8–M15 in §16, with M6 moved to the end. |

---

## 15. Photo booth mode (M7) [decided 2026-10-27 / details filled in]

A souvenir photo at the end of a round: the player, in front of the fruit pile they just caught, softened with a Korean-style beauty filter, in a branded frame with the lab's and school's logos and their scores.

### Flow (all mouse-operable, like every other screen)
1. **Game over → "Take a photo" button** (a third button next to Play again / Main menu).
2. **Pose screen:** the live scene stays up with the round's pile still in place (it isn't cleared until the next round starts). The filter is already applied, so players can see how they'll look, with a "Get ready" prompt and a big **📸 Take photo** button.
3. **3 · 2 · 1 countdown** (the countdown tick), then a white **flash** and a shutter sound.
4. **Preview:** the framed photo is shown frozen, with **Save**, **Retake** and **Done**.
5. **Save** writes a PNG to `~/Pictures/FruitGame/` (Windows: `Pictures\FruitGame\`) named `FruitGame_<date>_<time>_<score>.png`, then returns to the start menu. **Done** returns without saving. **Print** saves and prints it; **Get it on your phone** shows a QR code (or the email fallback).

### What's in the photo
| Element | Spec |
|---|---|
| Canvas | Portrait **1080 × 1440** (the game's 3:4), rendered offscreen at full resolution regardless of window size |
| Background | The scene behind the player: the hazed distant trees and **that round's fruit pile, un-hazed for the photo** so the catch shows in full colour, plus the bokeh webcam backdrop |
| Player | The person cut-out from M4 (segmentation mask) with the beauty filter applied; the basket and falling fruit are hidden |
| Foreground | The small foreground trees and bushes, for depth |
| Frame / outline | A rounded white "sticker" border (≈ 40 px) with a thin pink (#C2185B) inner keyline, matching the score counter's style |
| Logos | **Lab logo** and **school logo** in the bottom band of the frame, left and right, at equal visual height (≈ 90 px). Supplied as PNG/SVG with transparency |
| Scores | Bottom band, centre: **this round's score** (big, sticker style) and **"Best ⟨n⟩"** (smaller), plus a small per-fruit icon strip if there's room |
| Caption | Optional one-line title along the top band (e.g. the event name and date) |

### Korean-style beauty filter ("뽀샤시" look)
Applied only inside the person mask, then blended back, so the scene isn't touched:
1. **Skin smoothing:** an edge-preserving blur (bilateral or guided filter) at ~50–60 % blend, restricted to a soft skin mask (YCrCb skin range ∩ person mask, eroded away from the eyes and mouth using BlazeFace's six keypoints).
2. **Brightening + soft glow:** lift exposure by about 0.2 EV, lower contrast slightly, and add a gentle bloom ("soft focus") on highlights.
3. **Tone:** a slight warm-pink shift in the midtones and a touch of desaturation in the shadows; a clean, airy palette.
4. **Blush:** two very soft pink radial gradients on the cheeks, placed from the eye and mouth keypoints, at about 15 % opacity.
5. **Optional, off by default:** subtle eye enlargement / face slimming (a mesh warp from keypoints). Some people find this uncomfortable, so it stays off unless you want it.

All of it runs on the GPU as a few full-screen passes on the captured frame, so there's no extra ML model beyond M3's BlazeFace and M4's segmentation. Strength is one slider (0 = off) in `GameConfig`.

### Privacy (booth use)
- Photos are only saved when the player presses **Save**; nothing is uploaded anywhere.
- A small "Photos are saved on this computer" note sits on the preview screen.
- An optional `photoAutoDeleteDays` setting cleans old photos out of the folder at startup.

### Decisions for M7 (2026-10-27)
1. **Logos:** use **placeholders** ("LAB LOGO" / "SCHOOL LOGO" boxes) until the real files arrive. **Ask the user for the logo files (PNG with transparency or SVG) when M7 starts.**
2. **Getting photos to people:** **printing** is the main option (the preview gets a **Print** button that sends the PNG to the system's default printer). As a secondary option, try a **QR code download** (the game serves the photo over local Wi-Fi / a small upload endpoint). If that turns out to be too much hassle, fall back to **email** (player types an address on an on-screen keyboard; sent through a configured SMTP account).
3. **Caption:** **yes**, an event name and date along the top band (placeholder text until the user gives the wording).
4. **Fruit pile in the photo:** **full colour**.


---

## 16. Redesign roadmap: pixel / voxel style [decided 2026-10-28]

The game is being restyled from flat low-poly to a **pixel / voxel** look, and it gets a proper title: **지스트의 과일 사진가게**. Gameplay, face control, the webcam pipeline and the photo booth stay as they are; this roadmap changes how everything *looks*. Earlier art decisions (Fredoka, Jua, the faded pile atlases, the sticker-style score) are superseded as each milestone lands.

### Direction (decided)

| Area | Decision |
|---|---|
| **Font** | **Neo둥근모 Pro** (NeoDunggeunmo Pro v1.020 by Dalgona, based on Jungtae Kim's public-domain 둥근모꼴; **SIL OFL 1.1**). One font for both scripts: all 11,172 Hangul syllables (16 × 16) plus Latin (8 × 16). Source: `~/Documents/3D Assets/NeoDunggeunmoPro-Regular.ttf`. It replaces Fredoka and Jua everywhere. |
| **3D models** | **Voxelise the existing models** with an editor tool instead of remodelling by hand: every fruit, the basket, trees and bushes keep their shapes and colours, rebuilt from blocks. |
| **Rendering** | **Hybrid**: voxel models *plus* a gentle low-resolution render of the game layers (about ½ resolution, point-upscaled), so it reads as pixel art without heavy shimmer. |
| **Player** | The webcam cut-out stays **sharp** (it carries the beauty filter and the photo) and gets a 1–2 px dark pixel outline so it sits in the pixel world. |
| **Depth** | Distance is shown with **ordered dithering** toward the sky colour instead of faded colours. |
| **UI** | **Two tiers**: buttons and panels are crisp pixel art with a "keycap" depth edge (lift on hover, sink on press); key elements (title, score, timer) are **true 3D voxels** on a UI camera. |
| **Title** | **지스트의 과일 사진가게** (from Photo Booth 2), with a smaller English line underneath; working text "GIST Fruit Photo Shop". |

### Milestones

Run in this order. **M8** is a decision gate: it settles block size and render resolution before the big conversion. **M6** (polish and builds) moves to the end so it polishes the final look.

| # | Goal | Done when |
|---|---|---|
| **M8** | Style test | A throwaway test scene with 3 fruit, the basket, the pile and a button, rendered three ways: **A** voxel models at full resolution, **B** today's models with a low-res pixel render, **C** the hybrid. Includes a quick test of the dithered distance fade and the person outline. Screenshots are reviewed and the final settings picked (blocks across a fruit, render scale, outline on/off, palette limit on/off). Nothing in `Game.unity` changes. |
| **M9** | Pixel font | Neo둥근모 Pro imported as a TMP font asset with crisp pixels (point-filtered raster atlas, no smoothing) and set as the only UI font; Fredoka and Jua removed. Every font size is a multiple of 16 px on the 1080 × 1440 canvas, so glyph pixels stay square and even. The score's pink sticker outline becomes a pixel outline plus a hard down-right shadow, one pixel step thick. All screens (menu, calibration, HUD, pause, game over, photo booth) checked for fit with both Korean and English text. |
| **M10** | Voxel models | An editor **voxeliser**: it samples a model on a 3D grid, takes each block's colour from the model's texture, and builds one merged block mesh (adjacent faces merged to keep the triangle count low). It's applied to all 16 fruit types, the basket, trees, bushes and the ground strip, with a per-object block size and hand fixes where small details (stems, leaves) get lost. Fruit sizes, colliders, the basket's physics walls and the pile container are unchanged. The pile switches from the faded atlases to the dither fade, so `FruitType.atlas`/`atlasFar` become unnecessary for voxel fruit. |
| **M11** | Pixel render & world | Game layers drawn into a low-res target (the scale picked in M8) and point-upscaled. Camera and object positions snap to the pixel grid to limit shimmer on spinning fruit. Optional palette limit and 1 px outlines on models. Dithered distance fade on the pile and far trees. The person cut-out stays at full resolution with a pixel outline from its matte. Backdrop options: the live camera (current), or Photo Booth 2's pixel orchard illustration (also used on the title screen). Post-processing (bloom, depth of field, vignette) is re-tuned or dropped to suit pixel art. |
| **M12** | Title & logo | The title **지스트의 과일 사진가게** built as **3D voxel lettering** extruded from the Neo둥근모 glyphs (reusing the M10 block mesher), lit like the scene, with a slow bob and a light sweep. An **icon**: a voxel camera whose lens is a fruit. A **start-screen intro**: the title's blocks drop in like fruit and stack into the letters. **Flat pixel exports** (transparent PNG at several sizes) for the README, the photo frame, signage and the app icon. The English subtitle is confirmed with the user. |
| **M13** | 3D UI | **Buttons:** a pixel face over a darker block "lip", like a keycap; on hover the face lifts 1 px, on press it sinks and the lip disappears; large hit areas and clear hover states keep the mouse-only booth easy. **Panels:** pixel 9-slice with the same thick edge (Photo Booth 2's pixel buttons as reference). **Score:** voxel digits that pop and spin on each catch. **Timer:** voxel digits, possibly a voxel hourglass. True-3D elements render on a dedicated UI camera so they can be lit and rotated, while flat UI stays crisp. |
| **M14** | Photo frame restyle | The photo frame in the new style: pixel border and keyline, Neo둥근모 caption and score, the flat title logo, and the lab and school logos. The phone page served by `PhotoShare` and the QR card are restyled to match. The player stays sharp in the photo; optionally offer a "pixel me" version. |
| **M15** | Korean localisation | Every in-game string moved into one string table, with Korean as the main language in **해요체** (friendly in-game tone); English kept as a fallback or toggle. Text fit re-checked on every screen. |
| **M6** *(moved)* | Polish & builds | Assist tuning, juice, audio, a settings screen, the debug overlay off by default for the booth; macOS and Windows builds with camera permission prompts. |

**Dependencies:** M8 gates M10 and M11. M9 can start any time. M12 needs M9 (glyphs) and M10 (the block mesher). M13 needs M9 and M10. M14 needs M9 and M12. M15 needs M9.

### Open questions
1. **English subtitle** wording under the Korean title (working: "GIST Fruit Photo Shop").
2. **Lab logo** file, which is still a placeholder in the photo frame.
3. **"Pixel me" photo option:** offer a pixelated version of the player in the photo, or always keep them sharp?
4. **Bold text:** Neo둥근모 Pro has a single weight. Emphasis comes from size, colour and the pixel outline/shadow rather than a fake bold, which would break the pixel grid.
