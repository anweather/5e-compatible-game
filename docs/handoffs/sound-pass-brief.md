# Brief: the sound pass, for 0.1.9 (on the PC, after UX pass 4; the MacBook profiles overnight)

Andrew, 2026-10-30: sound ships in 0.1.9; proximity voice (Vivox) waits for 0.1.10.
Pull `dev`. Read COORDINATION.md (lean), docs/BACKLOG.md item 8 and docs/SPELL_COMBOS.md.
Build on what exists: `GameSounds.cs` (the SoundBank, `SoundIds`, SoundBankBuilder's recipe),
the CC0 packs under `unity/Assets/Prototype/Audio/CC0` (kenney, bart, jaggedstone, rubberduck;
credits in CREDITS.md), and the sounds already wired in WeatherFx, WaterfallFx, FireLightPool and
SleetStorm. CC0 or self-made sounds only, each credited. Run it as short fresh sub-agents per part,
and merge after each part.

## 1. Inventory
List what plays today and what's silent. Of today's casts, materials and events, which have
no sound?

## 2. Every cast and material event gets a sound, driven by the rules' events (the log and
reactions), not by the view guessing
- Casting: an element's sound as each is loaded; a charge building (#7: rising as you hold);
  the release.
- Each kind of effect: fire catching and crackling (louder with intensity, a loop per burning
  area rather than per cell), water splashing and flowing, freezing, steam hissing, glass and ice
  cracking then shattering, stone raised and broken, the boulder rolling, the wave's crash,
  the geyser's jet, lightning's crack and the charge humming through water, wind, the punch's
  thump, the golf ball's hit and the hole's drop, the gate opening.
- The rules decide what happened; the sound only plays it. Online, every player hears the same
  events, because they come from the lockstep world, not from one player's view.

## 3. Distance and loudness
- Every sound is 3D at its cell, with a loudness class: small (a footstep, a crackle) carries
  about 15 m, medium (a cast, a splash) about 40 m, big (an explosion, Falling Star, a storm's
  lightning, a dust flash) across the valley, about 150 m or more.
- Other wizards' casts are heard from where they stand.
- Under water, everything is low-passed (muffled). Behind a wall it's optional: say whether it's
  cheap enough to do now.
- A mixer with groups: effects, ambience, UI, and voice later; volume sliders in the Esc menu.

## 4. Cheap
- Pooled audio sources, a cap on voices at once, the nearest and loudest kept, the rest
  culled. One loop per burning area, not one per cell. Measure the cost with the wildfire
  probe: no worse than now.

## Checks
- A `sounds` probe: each event plays its sound, loudness falls with distance as classed, the
  voice cap holds in a wildfire, the underwater muffle works, no console errors.
- EditMode passes, no new warnings.
- The bot's `audio-silent` detector should stop firing where there's now sound.
- Listen yourself on the Mac's speakers, and say what sounds wrong.

## Deliver
- Merged to dev per part.
- A short clip with sound for the reel.
- CREDITS.md updated.
- Report to the PM in 8 lines or fewer after each part.
