---
name: sound-design
description: Design, describe and generate game sounds for Spell Chemistry Valley outside Unity, using a shared sound vocabulary (vocabulary.md) that Andrew and Claude agree on. Use whenever Andrew describes a sound ("lower, more rumble", "like a choir", "too snappy"), asks for sound options or variants, judges a sound, or when any worker makes, tunes or reviews cast, spell, creature or ambient sounds. Turns words into measurable parameters, renders audio-only variants with the skill's own stdlib-only Python generator, puts them on Andrew's board to pick from, and grows the vocabulary from each verdict.
---

# Sound design

**No chimes (Andrew, J-054):** no bells, pings, tinkles or sparkles in any game sound. A whoosh is fine.

Describing a sound is hard, so we keep one shared vocabulary: `vocabulary.md` beside this file.
Every word in it has a plain meaning, a number you can measure, and a recipe for the generator.
Andrew describes a sound in those words; we render it; he picks; the words get sharper.

The generator lives in this skill too, so it works on any machine with Python 3 and nothing else
(standard library only: no pip installs, no Unity, no GPU):

- `synth.py`: renders presets to WAV and prints the vocabulary's measures.
  `python synth.py presets/<name>.json --out <dir>` (run `python synth.py --help` for every series and word).
  Beside it: `layers.py` (one layer per word), `families.py` (every game sound's family, series and today's
  length and loudness from `docs/SOUNDS.md`), `dsp.py` (WAV, filters, LUFS, flatness).
- `presets/*.json`: parameters keyed by vocabulary words. Two kinds:
  - **cast presets** (the cast series): need `root_hz`, `swell`, `"cut off"`, `whoosh` and `thump` (a short
    example alone stops with an error listing them), so copy `presets/b-deep-hum.json`. Keys: `root_hz`,
    `hum`, `fifths`, `choir`, `rumble`, `sub`, `grit`, `pulse` {amount, accelerating}, `rising`, `escalate`,
    `layer`, `swell`, `"cut off"`, `whoosh`, `thump`, `colour`, plus `name`, `says`, `seed`.
  - **sound presets** (every other sound): `sound` names its id in `docs/SOUNDS.md` and the rest are layers
    (`thump`, `crack`, `ring`, `whoosh`; `hum`, `sub`, `rumble`, `hiss`, `buzz`, `growl`, `grit`, `drops`) and
    whole-sound words (`swell`, `tail`, `"cut off"`, `rising`, `pulse`, `gusting`, `dull`). Length defaults
    to today's; loudness is always today's. There's one first-take preset per game sound, named in the
    vocabulary's words (`fire-whoosh-rumble-grit`); copy it to make a variant.
  A picked variant is saved here under a name in the vocabulary's words.
- Render every sound preset at once: `python synth.py $(grep -l '"sound"' presets/*.json) --out
  tools/sound-audition/new` writes `<family>/<id>.wav` and `<family>/_joined-<family>.wav` (git-ignored)
  and prints each sound's loudness and peak. Variants of one sound go to separate `--out` folders.

## The loop

1. **Translate.** Map Andrew's description onto vocabulary words, each with an amount
   (`hum 1, pulse 0.2 accelerating, thump 0.2`). Words he uses that aren't in the vocabulary yet:
   guess a meaning, say the guess in one line, and add it to the vocabulary as **proposed**.
2. **Render variants, audio only.** No video. Copy a preset, change the words in question, and
   render it with `synth.py` in its family's standard series below. Make 2-4
   variants that differ along the one or two words in question (for example pulse at 15%, 30%,
   45%), and name each by what changes ("B + pulse 30%"), not A/B/C alone.
3. **Measure.** For each variant, report the numbers `synth.py` prints (low energy under 200 and
   80 Hz, attacks, cut-off, loudness against the current game sound). Equal loudness across variants, so the choice
   isn't "the loud one".
4. **Put them on the board** (`docs/STATE.md`, "Andrew's board"): the board doesn't accept .wav.
   Wrap each into an MP4 with a still picture, writing to a short path (Windows' ffmpeg fails on very
   long ones): `ffmpeg -f lavfi -i color=c=black:s=320x180 -i in.wav -shortest -c:v libx264 -tune stillimage -c:a aac C:/tmp/out.mp4`.
   Upload as assets (`asset: true`) and play with `<audio controls>`.
   One line per variant: its words and amounts. Don't listen and judge on Andrew's behalf.
5. **Record the verdict.** In `JUDGMENT_CALLS.md` (as for any call), and in the vocabulary: a
   word Andrew confirmed becomes **agreed**, with the variant that showed it as its example; a
   word he rejected or corrected gets fixed. Save the picked variant's preset in
   `presets/` under a name in the vocabulary's words.
6. **Build it in** only after a pick, as data (the game's sound tables), never by hand-tuning in
   Unity. `render_game.py <preset> --out unity/Assets/Prototype/Audio/CC0/<preset>` renders the
   parts the game holds and plays (it needs ffmpeg for the .ogg): a seamless 3 s loop per element
   and stage, the thump, the whooshes. `CastSoundTable` lists them; the bank sets their level, and
   `docs/SPELL_COMBOS.md`, "Sound", has the figures it was matched by.

## The standard series

Every variant of a family renders the same moments, as one file per sound (and a joined file), so variants
compare like for like. The cast series: an earth single, a water + air pair, a charged fire (each charge step
audible), the release, as separate files and one joined file. The others (`synth.py --help` lists them and
their sounds): **landing** for casting and spells (three takes with the bank's pitch jitter), **world** (a loop
twice end to end so the seam is heard; a one-shot near, again and far), **creature** (near, then far),
**wizard** (four takes at a walking pace), **feedback** for UI (twice fast, then alone: it must bear repeating),
**golf** for mini games. A new family gets its series in `families.py` the first time it's made.

## Who can do it

Anyone with Python: the PM, a Linux VM (cheapest), or the host. Rendering needs no Unity and no GPU.
Brief a worker with "use the sound-design skill" plus Andrew's words; the skill and vocabulary
carry the rest. Building a picked sound into the game is the host's (DesktopUnity's).

## Keep it small

The vocabulary is a working list, not a textbook: a word earns its place when Andrew uses it or
picks by it. Merge near-synonyms into one word with the others noted. Under 40 words.
