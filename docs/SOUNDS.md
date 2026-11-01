# Sounds

Every sound the game can play, one row each, so each can be redone outside Unity (Andrew,
2026-10-31: "put all sounds in a file and we get each one better outside of Unity itself").
Made and described with the sound-design skill (`.claude/skills/sound-design/`, its words in
`vocabulary.md`). As of `dev` at `0605a46` (alpha 0.1.10), 2026-11-01.

**How a sound gets played.** A game sound has a name (`SoundIds`, `CastSoundTable`). The
**SoundBank**, built with the yard by `SoundBankBuilder`, holds its clips, mastered to sit
together: trimmed, brought to a loudness target (`SoundMastering`: RMS of the loudest 0.4 s, in
dB) and soft-limited. One of a sound's clips is picked at random each time, with a little pitch
jitter. A name the bank lacks, or has no clips for, falls back to `ProceduralSounds` (C#, made at
run time). Casting sounds go through `SoundVoices` (a pool of 24 3D voices, 40 m reach). The rest go
through `GameSounds.Play` (a 3D one-shot, 4 to 90 m) and `GameSounds.Loop`. Weather and the falls
use their own `AudioSource`s.

**Source** says where the clip comes from: a **clip** in `unity/Assets/Prototype/Audio/CC0/` (in
git, every machine has it), a **preset** rendered by the skill (`render_game.py`, saved as clips
in `Audio/CC0/cast-deep-hum-pulse/`), **C#** (synthesised at run time or when the bank is built),
or the **pack** (the Asset Store pack `Assets/Free Pack`, restored by Prototype > Restore Asset
Packs, never in git). Where a sound lists pack files and a machine hasn't restored the pack, it
plays only the rest.

**Loudness and peak** are of the audition renders below, at the level the bank masters them to
(or the C# clip's own level, peak 0.8, where there's no bank row). They don't include the playing
volume, distance or the Effects and Ambience settings. LUFS is integrated loudness (BS.1770, gated);
with several clips it's their mean, with the range after it. `synth.py` itself prints dBFS RMS,
not LUFS, so these were measured by the audition script. Peak is sample peak in dBFS. The bank's
soft limiter puts loud clips' peaks just under 0.

**Audition renders** (not in git): `tools/sound-audition/<family>/<id>.wav`, all of a sound's clips
in one file with 0.6 s between them, and `_joined-<family>.wav` with every sound of the family in
the order here, 1.2 s apart. Clips were decoded with the Vorbis library inside the Unity Editor's
FSBTool. The C# sounds were ported to Python line for line, with .NET's `System.Random` ported
too (checked against .NET: the same sequence), so they're the game's own noise. The scripts
(`tools/sound-audition/_scripts/`) aren't in git either.

## Casting

The wizard's own casting sounds (`CastSounds`, one per copy of every wizard, so other players'
casts sound the same from where they stand; `docs/SPELL_COMBOS.md`, "Sound"). Hums are loops held
every frame while the hands are in a cast. Every hum is mastered to -20 dB over the whole loop,
the slam to -21.4 and the whoosh to -27. A hum plays at loudness 0.69 at gathering and charge 1,
0.83 at 2 and 1 at 3.

| Id | Heard for | Triggered | Source | Length | How often | LUFS | Peak |
|---|---|---|---|---|---|---|---|
| `gather-fire` | fire gathering in a hand: 73.4 Hz hum, a stronger fifth | `CastSounds.Tick` → `Keep` (`CastSoundTable.HumOf`) | preset `cast-deep-hum-pulse`: `gather-fire.ogg` | 3.00 s loop | per cast, looping while the hands fill | -22.2 | -10.9 |
| `gather-water` | water gathering: 65.4 Hz, 3 Hz wobble | same | preset: `gather-water.ogg` | 3.00 s loop | per cast, looping | -22.7 | -10.6 |
| `gather-earth` | earth gathering: 55 Hz, more rumble | same | preset: `gather-earth.ogg` | 3.00 s loop | per cast, looping | -23.4 | -11.8 |
| `gather-air` | air gathering: 82.4 Hz, a stronger detuned voice | same | preset: `gather-air.ogg` | 3.00 s loop | per cast, looping | -22.4 | -11.4 |
| `gather-plain` | a d-pad cast gathering (no element): 61.7 Hz | same | preset: `gather-plain.ogg` | 3.00 s loop | per d-pad cast, looping | -23.4 | -10.5 |
| `charge-fire-1` | fire's hum once the hands meet, 3 semitones up | `CastSounds.Tick` → `Keep`, stage 1 | preset: `charge-fire-1.ogg` | 3.00 s loop | per cast, looping from the slam to the release | -21.8 | -11.1 |
| `charge-fire-2` | a held cast reaching charge 2: 4 semitones higher, faster pulse | stage 2 | preset: `charge-fire-2.ogg` | 3.00 s loop | per held cast, looping | -21.3 | -10.7 |
| `charge-fire-3` | full charge: 7 semitones higher, pulse 15 Hz | stage 3 | preset: `charge-fire-3.ogg` | 3.00 s loop | per held cast, looping | -21.1 | -11.1 |
| `charge-water-1` | water's hum, combined | stage 1 | preset: `charge-water-1.ogg` | 3.00 s loop | per cast, looping | -22.4 | -11.0 |
| `charge-water-2` | water, charge 2 | stage 2 | preset: `charge-water-2.ogg` | 3.00 s loop | per held cast, looping | -21.8 | -10.3 |
| `charge-water-3` | water, charge 3 | stage 3 | preset: `charge-water-3.ogg` | 3.00 s loop | per held cast, looping | -21.7 | -11.3 |
| `charge-earth-1` | earth's hum, combined | stage 1 | preset: `charge-earth-1.ogg` | 3.00 s loop | per cast, looping | -23.2 | -11.8 |
| `charge-earth-2` | earth, charge 2 | stage 2 | preset: `charge-earth-2.ogg` | 3.00 s loop | per held cast, looping | -22.1 | -10.7 |
| `charge-earth-3` | earth, charge 3 | stage 3 | preset: `charge-earth-3.ogg` | 3.00 s loop | per held cast, looping | -21.6 | -10.6 |
| `charge-air-1` | air's hum, combined | stage 1 | preset: `charge-air-1.ogg` | 3.00 s loop | per cast, looping | -21.6 | -11.4 |
| `charge-air-2` | air, charge 2 | stage 2 | preset: `charge-air-2.ogg` | 3.00 s loop | per held cast, looping | -21.2 | -11.3 |
| `charge-air-3` | air, charge 3 | stage 3 | preset: `charge-air-3.ogg` | 3.00 s loop | per held cast, looping | -21.0 | -11.6 |
| `charge-plain-1` | a d-pad cast's hum, combined | stage 1 | preset: `charge-plain-1.ogg` | 3.00 s loop | per d-pad cast, looping | -22.8 | -10.8 |
| `charge-plain-2` | d-pad, charge 2 | stage 2 | preset: `charge-plain-2.ogg` | 3.00 s loop | per held d-pad cast, looping | -22.2 | -11.1 |
| `charge-plain-3` | d-pad, charge 3 | stage 3 | preset: `charge-plain-3.ogg` | 3.00 s loop | per held d-pad cast, looping | -21.4 | -10.9 |
| `slam` | the hands meeting: a thump, 95 to 50 Hz | `CastSounds.Struck` (Beat.Slam) | preset: `slam.ogg` | 0.50 s | per cast | -26.9 | -12.3 |
| `release` | the spell let go: a short whoosh that cuts the hums off; lower and louder with charge (pitch 1, 0.93, 0.86; loudness 0.8, 0.9, 1). Six rows, one per `CastKind` (`release-bolt`, `-burst`, `-ground`, `-flow`, `-sky`, `-quiet`), all the same three clips for now | `CastSounds.Began` (Beat.Release) → `CastSoundTable.ReleaseOf` | preset: `whoosh-1..3.ogg` | 0.29-0.30 s | per cast | -26.6 | -9.2 |
| `cast` | a cast from the radial or the mouse leaving the hand (not Gust of Wind or Whirlwind, which are all whoosh already) | `SpellCaster.PlayCastEffect` (`GameSounds.CastOf`) | preset: `whoosh-1..3.ogg`, mastered louder, -22 dB | 0.29-0.30 s | per radial or mouse cast | -21.6 | -4.2 |
| `fizzle` | a cast cancelled: a falling sputter | `CastSounds.Began` (Beat.Fizzle) | C#: `ProceduralCast.Fizzle` | 0.45 s | per cancel | -21.8 | -8.3 |

## Spells and elements

Where a spell takes effect (`SpellCaster.Unleash` → `Land`, and `MeteorFall`'s landing for
spells from the sky), by element, else by what it does (`GameSounds.LandingOf`). Which spells
are which comes from `rules/spells.json` and the combos in `rules/combos.json`.

| Id | Heard for | Triggered | Source | Length | How often | LUFS | Peak |
|---|---|---|---|---|---|---|---|
| `fire` | fire catching where a spell lands: Fire Bolt (the fire single), Burning Hands, Flame Wave | `SpellCaster.Unleash` | pack `Dragon Spit Fire 1.wav`; C# `ProceduralFire.Burst(1..3)`, -16 dB | 1.20 s | per cast | -22.3 (-22.8..-21.8) | -2.0 |
| `explosion` | a fire burst: Fireball, Falling Star, Meteor, Molten Boulder; and a barrel blowing up (`PhysicsProp.ShowBreak`) | `SpellCaster.Unleash`; `PhysicsProp.ShowBreak` | pack `Explosion 1, 2, 8.wav`; clips `rubberduck/explosion, bang_01, bang_03.ogg`, -12 dB | 0.46-1.09 s | per cast | -16.1 (-16.5..-15.6) | 0.0 |
| `frost` | cold landing: Frost (water + air), Sleet Storm, Ray of Frost | `SpellCaster.Unleash` | clips `bart/ice.wav`, `bart/coldsnap.wav`, -18 dB | 1.16-1.50 s | per cast | -20.0 (-22.6..-17.5) | 0.0 |
| `splash` | water landing: Create Water (the water single), Wave, Flood; a Wave breaking (`ChemGrid.Movers.BreakWave`); the golf ball holed | `SpellCaster.Unleash`; `ChemGrid.BreakWave`; `GolfCourse.Update` | clips `rubberduck/splash_01, splash_02, plop_02.ogg`, -18 dB | 0.33-0.74 s | per cast | -18.1 (-19.0..-17.5) | 0.0 |
| `grease` | Grease landing: a wet, low squelch | `SpellCaster.Unleash` | C#: `ProceduralSounds.Squelch`, -20 dB | 0.45 s | per cast | -19.4 | -2.4 |
| `zap` | lightning: Lightning (air + fire), Shocking Grasp; a storm cloud's strike (`ChemGrid.RenderShedStrikes`) | `SpellCaster.Unleash`; `ChemGrid.RenderShedStrikes` | pack `Magic Spell_Electricity Spell_1.wav`; C# `ProceduralFire.Zap(1..3)`, -16 dB | 0.70 s | per cast; per storm-cloud strike | -19.0 (-19.9..-18.1) | 0.0 |
| `gust` | air landing: Gust of Wind (the air single), Whirlwind | `SpellCaster.Unleash` | C#: `ProceduralSounds.Gust`, -19 dB | 1.00 s | per cast | -19.1 | -8.2 |
| `thunder` | Thunderwave landing | `SpellCaster.Unleash` | pack `Cannon impact 9.wav`; clips `rubberduck/cannon_01, 02, 04.ogg`, -13 dB | 0.92-1.80 s | per cast | -17.4 (-19.6..-16.1) | 0.0 |
| `earth` | the ground moved: Raise Earth (the earth single), Wall of Stone, Stone Ramp, Earthen Trench, Move Earth, Stone Shape. Also the wizard's punch landing, the pick, a wizard hit (below) | `SpellCaster.Unleash`; see "Wizard and movement" | pack `Magic Spell - Turn to stone.wav`, `Earthquake 3 - Big.wav`, -16 dB. No CC0 clip: without the pack it's C# `ProceduralSounds.Rumble` (rendered) | 1.20 s (Rumble) | per cast, per punch, per dig, per hit | -18.0 | -1.9 |
| `arcane` | every other spell's landing: 9 of the 16 pairs (Scalding Steam, Lava Pool, Geyser, Mud, Forge, Plant Growth, Rolling Boulder, Rain Cloud, Sand Blast), all four d-pad casts (Hasten Time, Shapeshift, Light, Control Weather), and Levitate, Mage Hand, Fog Cloud, Dust Storm, Spike Growth | `SpellCaster.Unleash` | preset `arcane-whoosh`: `arcane-whoosh/arcane-whoosh-1..3.ogg`, -21.5 dB. The soft rush of air and no tone (J-054); it was six chiming JaggedStone clips | 0.58 s | per cast: the commonest landing | -21.6 | -4.5 |
| `blink` | Misty Step | `SpellCaster.Unleash` | pack `Magic Spell_Short Reverse_1.wav`, -19 dB. Without the pack, C# `ProceduralSounds.Shimmer` (rendered): breath over a 220 + 330 Hz pad | 1.00 s (Shimmer) | per cast (radial only) | -14.0 | -1.9 |
| `hiss` | Acid Splash eating in: a sizzle and pops | `SpellCaster.Unleash` | C#: `ProceduralSounds.Hiss` (no bank row) | 1.20 s | per cast (radial only) | -15.6 | -1.9 |
| `shatter` | a frozen raider struck: a crack and a scatter of clicks, no tone | `DefenseField.Shatter`; `ProbeLooks` | C#: `ProceduralSounds.Shatter` (no bank row) | 0.80 s | per frozen raider struck | -24.1 | -3.0 |

## World and weather

| Id | Heard for | Triggered | Source | Length | How often | LUFS | Peak |
|---|---|---|---|---|---|---|---|
| `fire-loop` | a fire burning: roar, hiss and crackle, from each of the few fire lights (volume 0.55 + 0.15 × fire); also the dragon's breath | `FireLightPool` (made in the constructor, `Place` → `SetCrackle`); `DragonBreath` | C#: `ProceduralFire.Loop(1)`, -26 dB over the loop | 5.70 s loop | looping while fire burns | -27.0 | -9.0 |
| `rain-loop` | rain: fine hiss with drops (2D, 0.22 × rain × Ambience); Sleet Storm's hiss (3D) | `WeatherFx.Start`/`LateUpdate`; `SleetStorm` | C#: `ProceduralSounds.RainLoop` (no bank row) | 3.80 s loop | looping while it rains | -13.5 | -1.9 |
| `wind-loop` | wind gusting up and down (2D, 0.15 when there's wind); Sleet Storm's wind | `WeatherFx.Start`/`LateUpdate`; `SleetStorm` | C#: `ProceduralSounds.WindLoop` (no bank row) | 5.80 s loop | looping while there's wind | -18.7 | -1.9 |
| `falls-roar` | the waterfall's roar: the rain loop at pitch 0.55, 3D over 10 to 150 m | `WaterfallFx.Start` | C#: `ProceduralSounds.RainLoop`, pitch 0.55 | 6.91 s loop | looping near the falls | -14.0 | -1.9 |
| `thunder-clap` | thunder after a sky strike, at the speed of sound: a crack close by, a low roll far off; a new one each strike (0.7 loud near, 0.2 at 700 m) | `WeatherFx.Thunder` | C#: Punchy's `ThunderSynth.Generate` (third party); rendered at 300 m and 2.5 km | 5-16 s (7.9 and 12.8 rendered) | per lightning strike in a storm | -20.1 (-20.7..-19.4) | -0.4 |
| `break` | a crate or prop breaking; a window pane shattering | `PhysicsProp.ShowBreak`; `WindowPanes.Shatter` | clips `kenney/impactWood_heavy_000..004.ogg`, `rubberduck/wooden_01.ogg`, -17 dB | 0.31-0.39 s | per prop or pane broken | -19.0 (-20.9..-16.4) | 0.0 |

Also heard here, with clips above: a storm cloud's strike is `zap`, a barrel blowing up is
`explosion`.

## Creatures

| Id | Heard for | Triggered | Source | Length | How often | LUFS | Peak |
|---|---|---|---|---|---|---|---|
| `horn` | a war horn from the mountains as the dragon's warning starts: two low brass notes | `DragonView.Cues` (DragonPhase.Warning), at the listener | C#: `ProceduralSounds.Horn` (no bank row) | 4.50 s | once per dragon flight | -11.4 | -1.9 |
| `roar` | the dragon's roar: a growl sliding down, as it dives and from its perch | `DragonView.Cues` (Passes; perched while Leaving), at the listener | C#: `ProceduralSounds.Roar` (no bank row) | 3.20 s | twice per dragon flight | -14.3 | -1.9 |

Also heard here: the dragon's breath is `fire-loop`, a frozen raider struck is `shatter`. Raiders
and ambient life make no sound of their own.

## Wizard and movement

No sounds of its own: no footsteps, jumps, dodge roll, landing or swimming. These play `earth`:

| Heard for | Triggered | Plays |
|---|---|---|
| a punch landing | `ItemHolder.PunchLands` | `earth` |
| the pick biting the ground | `Pick.Land` | `earth` |
| the wizard taking a hit | `WizardStrike.Take` | `earth` |

So redoing `earth` changes these too. Without the pack, `earth` is the Rumble: a 1.2 s swell
for a punch.

## UI and feedback

None. Menus, the HUD, the grimoire, the cast meter and the event log make no sound. (A
"voice in your head" chime for Sending is a plan in `docs/MULTIPLAYER.md`, not built.)

## Mini games

None of their own. Spell golf's ball dropping in the hole plays `splash` (`GolfCourse.Update`).
The Gauntlet and the defense make none (a frozen raider struck is `shatter`).

## Removed in 0.1.10 (heard in alpha 0.1.9)

alpha 0.1.9 (2026-10-31 08:53) had the first casting sounds. 0.1.10 (20:04 the same day)
replaced them with the picked preset (`d723f4a`). The tonal ones are rendered in
`removed-0.1.10/`, for the chime question:

| Id | Heard for | Source | Length | LUFS | Peak |
|---|---|---|---|---|---|
| `charge-level-ping` | a held cast reaching charge 2 and 3: a struck tone, 440 + 880 Hz with a bright 1321 + 2212 Hz strike, at pitch 1.19 (523 Hz) and 1.5 (660 Hz) | C#: `ProceduralCast.Ping` (gone), -21 dB | 0.40-0.50 s | -22.4 | -7.1 |
| `gather-plain-0.1.9` | a d-pad cast gathering | clip `jaggedstone/magical_6_0.ogg`, -26 dB | 0.60 s | -25.3 | -14.9 |
| `release-sky-0.1.9` | the release of Falling Star, Meteor, storms | clips `jaggedstone/magical_3, 5.ogg`, -21 dB | 1.20 s | -22.0 | -10.5 |
| `release-quiet-0.1.9` | the release of quiet casts (Light, Shapeshift, Hasten Time, Plant Growth...) | clips `jaggedstone/magical_2, 7_0.ogg`, -24 dB | 1.00 s | -24.4 | -10.4 |

## Stand-ins (no bank built)

Only heard on a machine whose yard was built without a bank (each is made from its row's
`Make` the first time it plays, `GameSounds.Fallback`). Normal play never hears them, except
where noted above (`earth`, `blink` without the pack). Rendered in `stand-ins/`: `cast-standin`
(`ProceduralSounds.Cast`), `slam-standin` (`ProceduralCast.Sub`), the six release kinds' own
sounds (`ProceduralCast.Snap`, `Heave`, `Rush`), `hum-standin` (`ProceduralCast.ChargeLoop`),
and `ProceduralSounds`' fire, frost (a burst of plain noise), splash (with
bubbles), zap, thunder, explosion, break and fire loop.

## The chime

**Done (J-054): "No more chimes. Whoosh is ok."** `arcane` is now the `arcane-whoosh` preset, `shatter`
lost its glassy partials, and `frost-standin` is plain noise. A sweep of every bank clip for a peak
over 500 Hz that holds steady found two more, `rubberduck/wooden_02` and `_03`: a wood block ringing
at 1.3 kHz for 130 to 170 ms, against under 45 ms for everything else but `zap`'s buzz. They're out of
`break`. Left in, as not chimes: `splash` and `blink`, and `zap` (70 to 186 ms at 750 Hz, an electric
buzz). The three first-take presets with a short high ring, `golf-strike-crack-ring-short`,
`hole-ring-thump` and `pick-crack-ring-rumble`, aren't in the game; they need another word before they
are. What follows is the search as it was, before this.

Andrew: "the little chime thing is super annoying" (2026-10-31). Every short tonal sound, the
commonest first. Tonal means a sound with pitches you could hum back, measured as spectral
flatness over its loudest 93 ms: 0 is a pure tone, 1 is noise.

| Rank | Sound | When | How often in normal play | Tonal? |
|---|---|---|---|---|
| 1 | `arcane` (JaggedStone's "magical" clips) | the landing of 13 of the 24 combo and d-pad casts, and five radial spells | most casts | yes, strongly: flatness 0.01-0.10, bell-like peaks at 0.5-6 kHz, 1.4-1.6 s with a ringing tail |
| 2 | `charge-level-ping` (0.1.9 only) | each charge step of a held cast, levels 2 and 3 | every held cast, twice | yes: a struck bell, 523 and 660 Hz (flatness 0.04) |
| 3 | `splash` (`plop_02`'s plop, the splashes' droplets) | Create Water, Wave, Flood, a wave breaking, golf | every water single and pair | partly: flatness 0.04, peaks 2.2-2.7 kHz |
| 4 | `shatter` | a frozen raider struck | a few times a defense wave | the tail, 3.5-6.2 kHz tinkles; the crack is noise (0.80 over its loudest part) |
| 5 | `blink` (Shimmer without the pack) | Misty Step | rare (radial only) | a soft 220 + 330 Hz pad under breath |
| 6 | `frost` | Frost, Sleet Storm, Ray of Frost | per cold cast | no: icy noise (0.26-0.53) |
| - | hums (`gather-*`, `charge-*`) | every cast | every cast | tonal, but a low drone (55-140 Hz), not a chime |
| - | `frost-standin` | only with no bank | never | yes, chimes, but never heard |

**My guess: `arcane`.** It's the only short, bright, bell-like sound still in the game, and the
landing sound of most casts: every pair except Fireball, Falling Star, Wave, Frost, Wall of
Stone, Lightning and Whirlwind, and every d-pad cast. So a player trying combos hears it every few
seconds, and its 1.5 s ringing tail outlasts the cast. In 0.1.9 the same JaggedStone sparkle was
also on the d-pad's gathering and the release of quiet and sky casts, so a d-pad cast chimed
twice. The runner-up is 0.1.9's `charge-level-ping`, literally a little struck chime on every
charge step. It shipped that morning and was taken out in 0.1.10 that evening. If Andrew was
playing 0.1.9 when he said it, it may already be gone. Playing him `spells/arcane.wav` and
`removed-0.1.10/charge-level-ping.wav` settles it.

**Calmer variants (first pass, audio only)**, in `chime-variants/`. Each is matched in loudness to
the sound it replaces; the splash ones are 1 dB under, held by the peak:

| Variant | What changes | LUFS | Peak |
|---|---|---|---|
| `arcane-lower-5-shorter-0.6s-swell-60ms` | the same six clips, 5 semitones lower, cut off at 0.6 s, a 60 ms swell in, highs off above 3 kHz | -21.6 | -7.3 |
| `arcane-no-tone-whoosh-0.6s` | no tone at all: the vocabulary's whoosh alone (synth.py's `add_whoosh`, 300 to 1800 Hz), 0.6 s, three seeds | -21.6 | -2.2 |
| `splash-no-plop-swell-30ms` | the two splashes only (no plop), a 30 ms swell in, highs off above 4 kHz | -19.1 | -0.4 |
| `splash-lower-3-shorter-0.4s` | all three, 3 semitones lower, cut off at 0.4 s, highs off above 3 kHz | -19.1 | -0.4 |
| `shatter-no-tinkle` | the crack and scatter only, no glassy partials | -24.1 | -3.0 |
| `shatter-lower-12-shorter-tail-0.1s` | the tinkles an octave lower (1.8, 2.4, 3.1 kHz) and dying in 0.1 s, not 0.3 | -24.1 | -2.9 |

Two of these became presets: `arcane-no-tone-whoosh-0.6s` is `arcane-whoosh`, and `shatter-no-tinkle` is `shatter-crack-grit`. The rest were made from the clips. `synth.py` now has a series
for every family and a first-take preset for every sound (below).

## First takes, made procedurally

Every sound above except the cast series' own (hums, slam, whooshes: `cast-deep-hum-pulse`) has a
first-take sound preset in `.claude/skills/sound-design/presets/`, matched to its length and
loudness here, plus the missing ones: wizard (`punch`, `pick`, `pick-up`, `hit`, `footsteps`), UI
(`confirm`, `refusal`) and mini games (`golf-strike`, `hole`) at proposed levels. They're starting
points for Andrew to refine, not picks. Rendered (not in git) to `tools/sound-audition/new/<family>/`
with `synth.py $(grep -l '"sound"' presets/*.json) --out tools/sound-audition/new`; families and
series are in `synth.py --help`. The removed 0.1.10 sounds and the stand-ins have none.

## Couldn't be rendered

The Asset Store pack's files (not in git, not on the VM): `Dragon Spit Fire 1` (fire),
`Explosion 1, 2, 8`, `Magic Spell_Electricity Spell_1` (zap), `Cannon impact 9` (thunder),
`Magic Spell - Turn to stone` and `Earthquake 3 - Big` (earth), `Magic Spell_Short Reverse_1`
(blink). For `earth` and `blink` the stand-in each falls back to is rendered instead. The rest
also have CC0 or C# clips, which are rendered.
