# Sound vocabulary

The words Andrew and Claude use for sounds. Each has a plain meaning, what `synth.py` prints (or
nothing yet), and the generator's real parameters. Status: **agreed** (Andrew confirmed it by a pick)
or **proposed** (our guess). Values below are option B's (`presets/b-deep-hum.json`) unless noted.

## Body: what the sound is made of

| Word | Means | Measure | Recipe (preset key) | Status |
|---|---|---|---|---|
| **hum** | a steady, tonal low sound with a pitch you could sing | not printed (tonal share); pitch is `root_hz` per element, 55-82.4 Hz | sine at `root_hz` plus the fifths partials; `hum` 1 = B's level | agreed: option B (2026-10-31) |
| **fifths** | hum notes stacked a fifth apart, which sounds full, a little choral | none | partials at 1.5x, 2x, 3x the root, levels 0.22, 0.14, 0.05 times `fifths` | agreed: option B |
| **choir** | a second, slightly detuned voice on the same notes, which beats slowly | none | `choir` {amount 1, cents 10.4, voices 1}: one voice, +10.4 cents; no vibrato. `voices` above 1 is not used in any preset | proposed (B was described as a low choir) |
| **rumble** | low, noisy, no clear pitch: rocks or thunder far off | low energy under 200 Hz | white noise through two low-passes, `rumble` {amount 0.12, hz 140} | proposed (option A) |
| **sub** | energy below about 60 Hz, felt more than heard | none (only under 80 Hz is printed) | sine, `sub` {amount, hz 45}; off in B | proposed |
| **grit** | crackle on top of a sound | none | random noise pops, `grit` amount; off in B | proposed |

## Motion: how it changes over time

| Word | Means | Measure | Recipe | Status |
|---|---|---|---|---|
| **pulse** (throb) | the loudness beats in a regular rhythm | rate and depth, printed per charge step for the charged cast only | loudness modulation; `pulse.amount` is a share of option C's depth (0.55), so 0.3 = depth 0.165. Rate 7 Hz | agreed as a seasoning: "a little of C's pulse" (2026-10-31) |
| **accelerating** | the pulse speeds up as the cast builds | rates printed with pulse (7.5, 11.5, 15.0 Hz measured) | `accelerating: true` adds 4 Hz per charge step: 7, 11, 15 Hz. Only the charged series has steps; singles and the pair stay at 7 Hz | agreed with pulse (option C) |
| **rising** | the pitch goes up | none | `rising` {semitones 3, glide_ms 150}: one shift when the elements combine | agreed: "combine goes up in pitch a little" |
| **escalate** | each charge step is a notch bigger: louder, higher | none | `escalate` {semitones [4, 7], gain 1.2}: step n sits that many semitones above `rising`, gain 1.2 to the power n | agreed (J-040) |
| **layer** | a second sound joins over the first rather than replacing it | none | `layer` 1: level of the second element, mixed in | agreed: "the second spell overlays on that" |
| **swell** | fades in slowly | drone attack (ms), printed; reads higher than `swell` (235-260 ms for 120) | `swell` {ms 120}, linear fade-in | proposed |

## Edges: how it starts and stops

| Word | Means | Measure | Recipe | Status |
|---|---|---|---|---|
| **snappy** | starts and stops very fast, clicky, light | feel word, no parameter yet; closest: thump attack and decay, drone attack | short `swell`, short `thump.decay_ms`, small `thump.amount` | agreed as a fault: "too snappy" (J-040) |
| **cut off** | stops dead at a moment instead of fading | ms to silence after the event, printed (14 ms) | `cut off` {ms 15}, cosine fade at the release | agreed: charging cuts at release |
| **whoosh** | a short rush of air, noise sweeping past | duration, printed: 246, 253 and 409 ms (charged: let-go to hands landing) | low-passed noise whose cut-off sweeps 420 to 2400 Hz and back; `whoosh` {amount 0.32, peak_hz, rest_hz, tail_ms 140} | agreed: "release is a short whoosh" |
| **thump** | a short, heavy, low hit | attack and decay, printed (0 ms, 195 ms) | pitched-down sine, `thump` {amount 0.4, from_hz 95, to_hz 50, decay_ms 200}; no noise burst | proposed |
| **oomph** | weight on an impact: more thump, more sub, a hold | feel word, no parameter yet; closest: thump decay, low energy | raise `thump.amount` and `decay_ms`, lower `to_hz`, add `sub`; no hold | agreed for the slam (J-040), a feel word too |

## Colour: per element

Each element shifts the same base so you can hear which is loaded. `colour` in the preset:

| Element | Now (B) | Proposed |
|---|---|---|
| earth | root 55 Hz, rumble x2.2, no grit | add grit |
| water | root 65.4 Hz, 3 Hz pitch wobble | softer edges |
| air | root 82.4 Hz, detuned voice x1.8 | more noise and whoosh, less sub |
| fire | root 73.4 Hz, fifth x1.6, no crackle | brighter partials, a crackle of grit |

## Measuring a variant

`synth.py` prints: loudness (dBFS, and dB against the game's cast sounds) and peak; low energy under
200 Hz and under 80 Hz (B with 30% pulse: 72% and 16%); per cast, drone attack, thump attack and
decay, cut-off and whoosh in ms; pulse Hz and depth per charge step on the charged cast, when
there is a pulse; a warning if loudness is held by the peak.

Not printed yet, would like: tonal share, energy under 60 Hz, high-frequency share, detune spread.
