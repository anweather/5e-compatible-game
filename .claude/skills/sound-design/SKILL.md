---
name: sound-design
description: Design, describe and generate game sounds for Spell Chemistry Valley outside Unity, using a shared sound vocabulary (vocabulary.md) that Andrew and Claude agree on. Use whenever Andrew describes a sound ("lower, more rumble", "like a choir", "too snappy"), asks for sound options or variants, judges a sound, or when any worker makes, tunes or reviews cast, spell, creature or ambient sounds. Turns words into measurable parameters, renders audio-only variants with tools/sound, puts them on Andrew's board to pick from, and grows the vocabulary from each verdict.
---

# Sound design

Describing a sound is hard, so we keep one shared vocabulary: `vocabulary.md` beside this file.
Every word in it has a plain meaning, a number you can measure, and a recipe for the generator.
Andrew describes a sound in those words; we render it; he picks; the words get sharper.

## The loop

1. **Translate.** Map Andrew's description onto vocabulary words, each with an amount
   (`hum 0.8, pulse 0.2 accelerating, snap 0`). Words he uses that aren't in the vocabulary yet:
   guess a meaning, say the guess in one line, and add it to the vocabulary as **proposed**.
2. **Render variants, audio only.** No video. Use `tools/sound/` (Python, no Unity; its README has
   the command): a JSON of parameters per variant, rendered as the standard series below. Make 2-4
   variants that differ along the one or two words in question (for example pulse at 15%, 30%,
   45%), and name each by what changes ("B + pulse 30%"), not A/B/C alone.
3. **Measure.** For each variant, report the vocabulary's numbers (low energy under 200 Hz, attack,
   cut-off, loudness against the current game sound). Equal loudness across variants, so the choice
   isn't "the loud one".
4. **Put them on the board** (`docs/STATE.md`, "Andrew's board"): upload as assets
   (`asset: true`), play with `<audio controls>`. If the board refuses an audio type, wrap it with
   `ffmpeg -f lavfi -i color=c=black:s=320x180 -i in.wav -shortest -c:v libx264 -tune stillimage -c:a aac out.mp4`.
   One line per variant: its words and amounts. Don't listen and judge on Andrew's behalf.
5. **Record the verdict.** In `JUDGMENT_CALLS.md` (as for any call), and in the vocabulary: a
   word Andrew confirmed becomes **agreed**, with the variant that showed it as its example; a
   word he rejected or corrected gets fixed. Keep the parameters of the picked variant in
   `tools/sound/presets/` under a name in the vocabulary's words.
6. **Build it in** only after a pick, as data (the game's sound tables), never by hand-tuning in
   Unity; the host does that part.

## The standard series

Every cast-sound variant renders the same moments, as separate files and one joined file, so
variants compare like for like: an earth single, a water + air pair, a charged fire (each charge
step audible), the release. Other families (creatures, weather, impacts) define their own series
in `tools/sound/README.md` the first time they're made.

## Who can do it

Anyone with Python: the PM, a Linux VM (cheapest), or the host. Rendering needs no Unity and no GPU.
Brief a worker with "use the sound-design skill" plus Andrew's words; the skill and vocabulary
carry the rest. Building a picked sound into the game is the host's (DesktopUnity's).

## Keep it small

The vocabulary is a working list, not a textbook: a word earns its place when Andrew uses it or
picks by it. Merge near-synonyms into one word with the others noted. Under 40 words.
