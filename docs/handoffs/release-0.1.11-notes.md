The eleventh alpha of Spell Chemistry Valley, a 5E-compatible spell-chemistry sandbox. If you have 0.1.2 or later installed, it updates itself the next time you start it.

**Saved a mod? Copy it out before you update.** Mods now live in your own data folder, safe from later updates, but this one update can still lose mods saved inside the old install. Copy them somewhere safe first.

**Windows only again.** The Mac stays on 0.1.7 for now, and there's no Linux build of this version. Only players on the same version can play online together.

## Install (new players)

**Windows:** download `SpellChemistryValley-win-Setup.exe` and run it. The game isn't code-signed yet, so Windows may warn ("Unknown Publisher", or SmartScreen's *More info → Run anyway*).

**macOS:** this release has no Mac files; install [0.1.7](https://github.com/anweather/5e-compatible-game/releases/tag/alpha-0.1.7) for now, and it updates itself when the Mac catches up.

### Play with Claude
- **Change the game by asking.** With Claude Code on your machine, press **Enter** to open the chat, and ask for bigger fireballs, blue fire or a new rule. Claude writes the change, checks it and switches it on while you play.
- **Talk to it.** Hold **H**, or both sticks in on a pad, say what you want, and let go. Your voice is turned into words on your own PC with Whisper, and everything it needs comes with the game. The update is larger this time because it brings the speech model.
- **Build by asking.** Ask for a house and it goes up in the world, built from parts. Ask for a wing, or for a golden squirrel: mods can add things to the world too, and they vanish when the mod is switched off.
- **Paint the world.** Colours for fire, gases, ground and water, by place, around you, for each player, or mixed.
- **Bring your own Claude.** In Settings > Claude setup, add your own Anthropic API key or point the game at your own Claude Code. Your key stays on your PC, and each session has a spend limit you control. Make a separate key for the game, with a low limit.
- It runs on the fast Haiku model by default, on your own Claude account. **F9** opens a terminal beside the game. See **Play with Claude** in the Esc menu.
- **Your room, your rules.** A host sets rule changes to Open, Host only or Locked. Everyone in a room plays the same rules, and late joiners get them too.

### The world
- Every animal and villager now varies, one to the next, and villages have their own mix of people, with workers at their posts.
- Animals pace about instead of shaking in a corner, and stay on their leash.
- Houses burn down to a ruin, wall by wall. Stucco burns about half as fast as log, and walls now stop you.
- Houses take the light properly: no more glowing walls at night.
- Mods can add new liquids that flow and show their own colour, like honey and paint, and ones that burn, freeze or set hard. Mods can also add new creatures, and moss that creeps over the ground and glows at night.

### Spells and controls
- **Tab** opens a spell list on the keyboard, **Q** changes element and **E** changes spell. The wheel stays for pads, and holds together above 1080p. 24 spells keyboard players couldn't reach are now on the list.
- The cast bar no longer leaps at its first tick.
- A spell with no colour of its own follows its element: set fire to blue and the Fireball goes blue too.
- No more chimes: the arcane landing is a plain whoosh.

### Menus
- The Esc menu is in sub menus, with a new **About and credits** page.
- The Play log has its own on and off switch in Settings.

### Under the hood
- Much faster loading, and less strain from rain and online play.
- Fixed: Claude's model being unavailable now says so plainly, and speaking can't freeze the game.
- The game is under the PolyForm Noncommercial licence; mods you make are CC-BY 4.0.
