# Sound vocabulary

The words Andrew and Claude use for sounds. Each has a plain meaning, what to measure, and how the
generator (`synth.py`, beside this file) makes it. Status: **agreed** (Andrew confirmed it by a pick), **proposed**
(our guess, not yet confirmed). Amounts run 0 to 1 unless a unit is given.

## Body: what the sound is made of

| Word | Means | Measure | Recipe | Status |
|---|---|---|---|---|
| **hum** | a steady, tonal low sound with a pitch you could sing | tonal share of energy; fundamental 60-150 Hz | stacked sine or saw partials, low-passed | agreed: option B (2026-10-31) |
| **fifths** | hum notes stacked a fifth apart (and octaves), which sounds full, a little choral | interval 3:2 between partials | add partials at 1.5× and 2× the root | agreed: option B |
| **choir** | many slightly detuned voices on the same notes | detune spread, voice count | 4-8 copies detuned ±5-15 cents, slow vibrato | proposed (B was described as a low choir) |
| **rumble** | low, noisy, no clear pitch: rocks or thunder far off | low energy under 200 Hz; tonal share low | brown noise, low-passed under 150 Hz | proposed (option A; Andrew asked for "low and rumble") |
| **sub** | energy below about 60 Hz, felt more than heard | energy under 60 Hz | sine at 30-55 Hz | proposed |
| **grit** | a rough, buzzy edge on top of a sound | high-frequency noise share, distortion | soft clipping or added band-passed noise | proposed |

## Motion: how it changes over time

| Word | Means | Measure | Recipe | Status |
|---|---|---|---|---|
| **pulse** (throb) | the loudness beats in a regular rhythm | amplitude-modulation rate (Hz) and depth | AM at 3-8 Hz, depth 0-1 | agreed as a seasoning: "a little of C's pulse" (2026-10-31) |
| **accelerating** | the pulse or pitch speeds up as the cast builds | rate at start and end | AM rate rising per charge step | agreed with pulse (option C) |
| **rising** | the pitch goes up | semitones per step | pitch shift per charge step, about +1-3 semitones | agreed: "combine goes up in pitch a little" |
| **escalate** | each charge step is a notch bigger: louder, higher, denser | per-step change in loudness and pitch | step the gain and pitch with the charge | agreed (J-040) |
| **layer** | a second sound joins over the first rather than replacing it | both present after the join | mix, don't crossfade | agreed: "the second spell overlays on that" |
| **swell** | fades in slowly | attack time (ms) | attack 200-800 ms | proposed |

## Edges: how it starts and stops

| Word | Means | Measure | Recipe | Status |
|---|---|---|---|---|
| **snappy** | starts and stops very fast, clicky, light | attack under 5 ms, decay under 100 ms, little low end | short envelopes | agreed as a fault: "too snappy" (J-040) |
| **cut off** | stops dead at a moment instead of fading | time from the event to silence (ms) | gate at the event, 10-20 ms fade | agreed: charging cuts at release |
| **whoosh** | a short rush of air, noise sweeping past | duration; filter sweep direction | band-passed noise with a moving centre, 150-300 ms | agreed: "release is a short whoosh" |
| **thump** | a short, heavy, low hit | attack under 10 ms, energy under 120 Hz, decay 80-200 ms | pitched-down sine kick plus noise burst | proposed (each option added its own slam thump) |
| **oomph** | weight on an impact: more thump, more sub, a hold | low energy at the hit; length of the hold | thump + sub + 50-100 ms hold | agreed for the slam (J-040), a feel word too |

## Colour: per element

Each element shifts the same base a little, so you can hear which element is loaded
("first one makes a lower sound, slightly different by element type"). Proposed starting points:

| Element | Shift |
|---|---|
| earth | lower root, more rumble and grit |
| water | a slow wobble (vibrato), softer edges |
| air | more noise, more whoosh, less sub |
| fire | brighter partials, a crackle of grit |

## Measuring a variant

Report these for every variant: low energy share under 200 Hz (the cast sounds were 47% before,
A 87%, B 70%, C 77%); attack and decay (ms); cut-off after the event (ms); loudness against the
game's current sound (dB); pulse rate and depth when there's a pulse.
