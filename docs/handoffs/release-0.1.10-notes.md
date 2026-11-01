The tenth alpha of Spell Chemistry Valley, a 5E-compatible spell-chemistry sandbox. If you have 0.1.2 or later installed, it updates itself the next time you start it.

**Windows only again.** The Mac stays on 0.1.7 for now; a Mac on 0.1.7 and a PC on 0.1.10 can't play online together.

## Install (new players)

**Windows:** download `SpellChemistryValley-win-Setup.exe` and run it. The game isn't code-signed yet, so Windows may warn ("Unknown Publisher", or SmartScreen's *More info → Run anyway*).

**macOS:** this release has no Mac files; install [0.1.7](https://github.com/anweather/5e-compatible-game/releases/tag/alpha-0.1.7) for now, and it updates itself when the Mac catches up.

### The dragon
- Press **R** to summon a dragon. The sky turns red, it flies over the valley breathing a stream of fire, and its dive knocks you down and snuffs every fire it passes.
- Get under cover: shelter keeps you out of its breath.
- It's big: half again the size it was in testing, with a roar and a horn to warn you it's coming.

### A punch that lands
- A real punch: the arm reaches for what you hit.
- It's 2.5 times stronger: a crate flies a few metres, straight away from you.
- Punching another player is the 5E unarmed strike: an attack roll, 1 + Strength bludgeoning damage, and a cell of knock-back.

### Casting
- New casting sounds: a deep hum for each element (earth lowest, air highest), the second element layered on top, rising with each charge step, and a short whoosh when it's released.
- The slam lands heavier, with more weight when the hands meet.
- Fixed: under a heavy load (lots of fire, a storm), a cast could silently never go off.

### The world
- **The Gauntlet:** an obstacle course on the lake's south shore, with spell zones, checkpoints, a timer and your best times.
- Surf pushes you toward the nearest land.
- Fixed on Windows: the stutter when night fell.

### For testers
- **A frame-rate log:** the game now writes a small log of how smoothly it runs, a line a second. Press **F8** when something feels slow or wrong, and it marks that moment with a picture. Turn it off in the Esc menu. Send us the files and we can see exactly what happened.

### Under the hood
- Spell and material looks now come from the rules, so a mod that makes a fireball bigger draws it bigger, with no rebuild. This is the groundwork for changing the rules live, with Claude, in a coming release.
