Brief for the Mac: the Hold, phase 4, endless and tuned. Read `docs/design/the-hold.md` (phases 2 to 4). Core, rules and tests only; no GPU or pad needed.

**Base:** branch `hold-waves` off `hold-hands` (4cac979c) with dev merged in, because both change `Hold.cs` and `World.Hold.cs`, and a wrong hand set can't be felt without hands. It lands after `hold-hands`. The PC is running the hands checks now and may push to that branch: merge it again when it moves.

**Goal:** waves that get harder in a way we can read and tune by editing `rules/hold.json`, with nothing in code naming a number, kind or adjective.

**Build:**
- **Waves by formula, from the rules:** count, health, damage, kinds and adjectives grow with the wave number. Peer count at the run's start scales them, from a rules row. Same seed, same waves.
- **Wave themes:** rows that lean a wave on one or two adjectives, chosen by the seeded RNG with no theme twice running, so no one hand set wins everything.
- **More adjectives:** about four more, as rows, so each of fire, water, earth and air has raiders it's good and bad against. Use what the chemistry already does. Say what you chose.
- **A boss** every so many waves, from the same rows: a raider with a bigger row, its own name and health bar. A flyer or swimmer is a stretch: leave it out.
- **Warning:** core state saying what the next wave is, set long enough before it to reach the keep and attune. The dial is the time, and the PC does the HUD and the castle's sign later.
- **Dials:** every number above is a named field, and `about` says what each does. A short section in `the-hold.md` lists them: name, start value, what you'd feel if you raised it.

**The curve:** a headless runner in core: a scripted wizard with a fixed hand set plays seeded runs with no view, and prints the wave reached, the time and the keep's health at each wave, by hand set and peer count. Say how good the scripted wizard is, as it's only a stand-in for a player. Use the cheap playtester only if it's plainly less work.

**Start target:** a good run lasts about 20 minutes (a dial, and Andrew may change it). A hand set that's wrong for a wave should do clearly worse on that wave.

**Keep:** the same rules as `the-hold.md`: core state, commands only, seeded RNG, no golden moved, words in the glossary first ("boss", "theme" and anything else new). Raiders' existing kinds stay as they are in the sandbox. Keep the test suite fast: the runner isn't a gate, and any test of it stays under about 2 s.

**Tests:** the formula, themes never repeating, peer scaling, the warning's timing, two worlds matching over several waves.

**Report:** one short message: the commit, gates with exit codes, the curve as a table, the dials you'd turn first, and a numbered list for the PC: what the warning and the boss must show. Don't merge it. Ask before anything much larger than this brief.
