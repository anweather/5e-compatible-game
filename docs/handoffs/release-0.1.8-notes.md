<!-- Draft notes for alpha-0.1.8 (PM, 2026-10-30), from dev b33c6bd. Lines marked [C] only
     hold if the looks pass runs before the release (Option C); drop or reword them for Option B. -->

The eighth alpha of Spell Chemistry Valley, a 5E-compatible spell-chemistry sandbox. If you have 0.1.2 or later installed, it updates itself the next time you start it.

**This one is Windows only.** The Mac stays on 0.1.7 for now, and the next release brings it up to date. Until then, a Mac on 0.1.7 and a PC on 0.1.8 can't play online together.

## Install (new players)

**Windows:** download `SpellChemistryValley-win-Setup.exe` and run it. The game isn't code-signed yet, so Windows may warn ("Unknown Publisher", or SmartScreen's *More info → Run anyway*).

**macOS:** download `SpellChemistryValley-osx-Portable.zip`, unzip it and move **Spell Chemistry Valley** into Applications (keep it out of Downloads and Documents). It isn't notarized, so the first launch is blocked: open System Settings → Privacy & Security and click *Open Anyway*. The `.pkg` installer works too.

### Casting by elements
- Cast by combining elements: fire, water, earth and air, on the bumpers and triggers (keyboard Z X C V). One element casts a simple spell; two in a row make one of 16 combinations, each its own spell.
- A meter fills while you cast, and you walk slower. B cancels.
- Every combination you discover goes into your grimoire, a new page of the Esc menu.
- The d-pad cycles spells and casts them. The old spell wheel is still on Tab.
- The dodge roll moves to the right stick's click.

### The new spells
Every spell works through what things are made of, so they chain: one spell's leftovers are the next one's fuel.
- **Fire:** Fireball scatters burning debris. Lava Pool runs downhill and crusts over so you can walk on it, and turns to rock in water. Scalding Steam rusts metal. Falling Star marks where it will land, then strikes a second later.
- **Water:** Geyser shoots a pond or river upward, throwing whatever's on it. Wave rolls over the ground, puts out fires, knocks creatures back and leaves mud. Frost builds ice bridges over water, freezes wet creatures solid and leaves the ground slippery. Create Water brings burnt ground back to green.
- **Earth:** Forge bakes sand into glass and mud into brick, and heats metal red-hot. Rolling Boulder rolls downhill, smashes glass and ice, and dams streams. Plant Growth grows climbable vines, and their roots crack stone walls when they die. Wall of Stone makes stepping stones over water; hold the cast for a ramp.
- **Air:** Lightning jumps to the nearest water or metal and runs through it, and the charge can open a gate. Rain Cloud waters the ground; lightning turns it into a storm and cold turns it into snow. Sand Blast scours rust, frost, snow and vines, and wears walls down. Gust blows gas away and pushes oil across water. Whirlwind travels and picks up what it crosses: a fire tornado, a waterspout, a sandstorm.
- A cloud of dust flashes into a fireball in fire.
- [C] Each new spell has its own look: the geyser's jet, the wave, frost, the rolling boulder, vines, stepping stones and lightning's arcs.

### Materials
- Glass and ice crack on the first hit and shatter on the next. Village windows are glass and break.
- Wet metal rusts, Acid Splash eats through things, and dirt soaks into mud that slows you down.
- Hot metal stays hot and burns whoever touches it. Oil floats on water and burns across it.
- Every ground has a hardness: spells dig soil, sand and mud, but rock needs the pick.
- Light, on the d-pad, lights the ground around it for a while.

### The wizard
- A new look: a robe with a black fuzzy face and red eyes under a wizard's hat or a cowl. Pick one in the Esc menu.

### Known issues
- On bulging ice, cracks look broken up, and frozen creatures show cracks as colour rather than a web.

Bugs and the log: Esc → Open log folder, and send `Player.log`.
