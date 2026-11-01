Brief from the PM: every game sound in one file, rendered outside Unity, so Andrew can redo them one by one (Andrew, 2026-10-31: "put all sounds in a file and we get each one better outside of Unity itself"; "the little chime thing is super annoying"). Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../sound-inventory origin/dev -b sound-inventory`, `git lfs pull` there, and work there. Read `.claude/skills/sound-design/SKILL.md` and `vocabulary.md` first: that skill is how sounds are made and described here (synth.py, presets, stdlib-only Python).

1. **The inventory: `docs/SOUNDS.md`.** Every sound the game can play, one row each: an id, what the player hears it for (in the game's words), where it's triggered (file and method), the source (a clip in `unity/Assets/Prototype/Audio/`, a rendered preset, or synthesised in C# at run time), length, and how often a player hears it (once, per cast, per footstep, looping). Find them all: AudioClip fields, PlayOneShot and AudioSource uses, `CastSoundTable`, sounds built in code (procedural horn, roar, tones). Group by family (casting, spells and elements, world and weather, creatures, wizard and movement, UI and feedback, mini games).
2. **The chime.** Andrew finds "the little chime thing" super annoying. List every short tonal sound (bells, pings, tones, sparkles, UI feedback, charge-level tones, golf, puzzles) as candidates, with when each plays, and rank them by how often a player hears one in normal play. Say which you think it is and why.
3. **Render them.** For each sound, a WAV in `tools/sound-audition/<family>/<id>.wav` (git-ignored; keep it out of the commit): convert clips with ffmpeg if it's on the VM (`sudo apt-get install -y ffmpeg` is fine); port a C#-synthesised sound to a `synth.py` preset when that's straightforward, or say it isn't. Also one joined file per family with a short silence between sounds and the order listed in SOUNDS.md, so Andrew can listen through a family in one go. Report loudness (LUFS) and peak for each, as synth.py measures.
4. **A first pass on the chime candidates:** for the top 3, render 2 calmer variants each with the sound-design skill (softer attack, lower, shorter, or no tone at all), named by what changes. Audio only.

Checks: docs only plus the presets you add; jscpd as in CLAUDE.md if you add code.

Deliver: commit `docs/SOUNDS.md` and any new presets on `sound-inventory` and push, not merged. Leave the WAVs in the VM and say their path, so the PM can copy them out (the board takes MP4, so the PM wraps them). Final message, 12 lines or fewer: the commit; how many sounds by family; the chime's likely identity; which sounds couldn't be rendered and why; the WAV folder; the time.
