The eighth alpha of Spell Chemistry Valley, a 5E-compatible spell-chemistry sandbox. If you have 0.1.2 or later installed, it updates itself the next time you start it.

**This one is Windows only.** The Mac stays on 0.1.7 for now, and the next release brings it up to date. Until then, a Mac on 0.1.7 and a PC on 0.1.8 can't play online together.

## Install (new players)

**Windows:** download `SpellChemistryValley-win-Setup.exe` and run it. The game isn't code-signed yet, so Windows may warn ("Unknown Publisher", or SmartScreen's *More info → Run anyway*).

**macOS:** this release has no Mac files; install [0.1.7](https://github.com/anweather/5e-compatible-game/releases/tag/alpha-0.1.7) for now, and it updates itself when the Mac catches up. From 0.1.7's page: download `SpellChemistryValley-osx-Portable.zip`, unzip it and move **Spell Chemistry Valley** into Applications (keep it out of Downloads and Documents). It isn't notarized, so the first launch is blocked: open System Settings → Privacy & Security and click *Open Anyway*. The `.pkg` installer works too.

### Casting by elements
- Cast by combining elements: fire, water, earth and air, on the bumpers and triggers (keyboard Z X C V). One element casts a simple spell; two in a row make one of 16 combinations, each its own spell.
- A meter fills while you cast, and you walk slower. B cancels.
- Every combination you discover goes into your grimoire, a new page of the Esc menu.
- The d-pad cycles spells and casts them. The old spell wheel is still on Tab.
- The dodge roll moves to the right stick's click.

### The new spells
Every spell works through what things are made of, so they chain: one spell's leftovers are the next one's fuel. Each has its own look.
- **Fire:** Fireball scatters burning debris. Lava Pool runs downhill and crusts over so you can walk on it, and turns to rock in water. Scalding Steam rusts wet metal. Falling Star marks where it will land, then strikes a second later.
- **Water:** Geyser shoots a pond or river upward in a jet that throws whatever's on it, you included. Wave rolls over the ground, carries props along, puts out fires, knocks creatures back and leaves mud. Frost builds ice bridges over water, freezes wet creatures solid and glazes the ground slippery. Create Water brings burnt ground back to green.
- **Earth:** Forge bakes sand into glass and mud into brick, and heats metal red-hot. Rolling Boulder rolls downhill, smashes glass and ice, and dams streams. Plant Growth grows vines you can climb and walk on, and their roots crack stone walls when they die. Wall of Stone makes stepping stones over water; hold the cast for a ramp.
- **Air:** Lightning arcs to the nearest water or metal and runs through it, and its charge can power machines such as gates (none are placed in the valley yet). Rain Cloud waters the ground; lightning turns it into a storm and cold turns it into snow. Sand Blast scours rust, frost, snow and vines off, and wears walls down. Gust blows gas away and pushes oil across water. Whirlwind travels and picks up what it crosses: a fire tornado, a waterspout, a sandstorm.
- A cloud of dust flashes into a fireball in fire.

### Materials
- Glass and ice crack in a spiderweb on the first hit and shatter on the next. Village and cabin windows are glass and break the same way. Frozen creatures crack too.
- Wet metal rusts, Acid Splash eats through things, and dirt soaks into mud that slows you down.
- Hot metal stays hot and burns whoever touches it. Oil floats on water and burns across it.
- Every ground has a hardness: spells dig soil, sand and mud, but rock needs the pick.
- Light, on the d-pad, lights the ground around it for a while.

### The wizard
- A new look: a robe with a black fuzzy face and red eyes, under a wizard's hat or a cowl. Pick one in the Esc menu.

### Fixes
- The staff's crystal no longer flares into a bright starburst.
- Lightning in storms and meteor showers were often not drawn at all; now they are.
- Sand Blast no longer leaves the ground scorched black.

### Known issues
- After a long walk the robe can stretch out into a long tail behind the wizard (0.1.7 did this too). A fix is coming in the next release.

Bugs and the log: Esc → Open log folder, and send `Player.log`.
