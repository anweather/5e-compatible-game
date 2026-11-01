Brief from the PM: make the sound-design skill able to render every sound in docs/SOUNDS.md, not only the cast series (Andrew: "get each one better outside of Unity"). Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../synth-every-sound origin/dev -b synth-every-sound` and work there. Read `.claude/skills/sound-design/` (SKILL.md, vocabulary.md, synth.py, presets/) and `docs/SOUNDS.md` (your inventory, now on dev).

1. **Series for every family** in SOUNDS.md: spells and elements (impacts, bursts: fire, water, earth, air, cold, lightning, arcane), world and weather (rain, wind, thunder, splash, shatter), creatures (horn, roar), wizard and movement (punch, pick up, hit, footsteps: none exist yet), UI and feedback (none yet: a soft confirm, a refusal), mini games (golf strike, hole). Each family gets a standard series in `synth.py` the way the cast series works, so variants compare like for like. `synth.py --help` lists them.
2. **A preset per sound** in `presets/`, named in the vocabulary's words, that renders a first take of that sound procedurally, close in length and loudness to today's (SOUNDS.md has both). Not judged by you: it's a starting point Andrew refines. Grow `vocabulary.md` only with words you need (mark them proposed), keeping it under 40 words.
3. **Stdlib only**, still. Keep `synth.py` readable; split into modules beside it if it grows past about 600 lines.
4. Render every preset to `tools/sound-audition/new/<family>/<id>.wav` (git-ignored) plus one joined file per family, with the loudness and peak of each.

Checks: `python3 synth.py --help` and a render of every preset run clean; jscpd as in CLAUDE.md.

Deliver: commit and push `synth-every-sound`, not merged. Final message, 10 lines or fewer: the commit; the series added; presets made by family; anything that couldn't be made procedurally and why; the WAV folder; the time.
