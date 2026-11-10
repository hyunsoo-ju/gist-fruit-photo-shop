# Instructions for AI coding agents

Read **[`Docs/DESIGN.md`](Docs/DESIGN.md) first, before anything else.** It is the design and build reference for this
project (지스트의 과일 사진가게 / GIST Fruit Photo Shop, called **FruitGame** in code): how every feature behaves, the
tuning values, the milestones and what's left, and the decision log explaining why things are the way they are.

- The English `Docs/DESIGN.md` is the source of truth. `Docs/DESIGN.ko.md` is a Korean translation that follows it:
  when the design changes, revise `DESIGN.md` in place first (no addendum chapters), then bring the translation in line.
- `README.md` (Korean, the default) and `README.en.md` are for staff running the booth and for first-time setup.
- The game does not run without the private assets kept out of git (see "First-time setup" in the README). Never commit
  them: `.gitignore` lists them, and their `.meta` files must stay in the repository.
- Unity 6000.6.7f1, URP. One game scene: `Assets/FruitGame/Scenes/Game.unity` (`Boot.unity` runs first in builds).
- In-game Korean is 해요체; the Korean README is 합니다체; the design doc is written in English.
- Never put API keys or passwords in the repository (narration uses `GOOGLE_TTS_API_KEY` from the environment).
