Brief from the PM: bring Punchy's newer life into the game and make the valley's life more varied (Andrew, 2026-11-01: "pull the latest animals and behaviors from punchy including the people and the other races of humanoid too. Children and birds are part of that as well. Let's add more variety to the life. Size, skin color, shape, hair color"). Gameplay lane, in the Editor.

This picks up J-035 and backlog 17-19. Upstream is `C:\Users\Andrew\Development\Punchy` on the PC, or https://github.com/Skeleman/Punchy (main, 50da96e). Our base is b183507, 31 commits behind. Permission to modify all of Punchy is J-022: keep the attribution in Credits.cs and record each change in the file's header and the Punchy README. Skip upstream's netcode (M0-M6 and the Netcode for Entities work): life stays view-only here, so physics or randomness in it must never reach shared state.

**Changed 2026-11-01 (J-058):** creatures become data, not Editor assets, and we diverge from Punchy's code while keeping its style. Do branch 1. Branches 2 to 4 wait for the data format in `docs/design/builders.md`, then land as data rows, not new `.asset` files.

One small branch each, off dev, gated and tested in the valley, with a clip for Andrew's board:
1. **`punchy-mill`** (upstream 7ee4511): cornered and leashed animals mill about. First record the base b183507 in the Punchy README.
2. **`punchy-people`** (f3141f6): individual villagers, hairstyles, workers at their posts; `HumanChild`; the changed Human, Dwarf, Elf, Goblin and Ogre; Hair*, Beard* limbs; StyleGroup. Children and each humanoid people appear in the valley. Upstream's other new work on creatures comes with it where it helps.
3. **`life-variety`**: every animal and person differs: size, shape, skin or coat colour, hair colour, per individual, from its own seed so a peer sees the same one where that's cheap. Ranges in data, so the MCP can reach them later (`docs/handoffs/briefs/world-tools-design.md` is designing that; don't build the MCP side).
4. **`punchy-water`** (c52d9ff): fish, ducks, dolphins, eels, lily pads, reeds; Habitat Swims/Floats/Leaps; water depth through ICreatureGround.

Birds: Duck comes with 4; Heron, Quail, Ptarmigan, Turkey and Roadrunner are already vendored, and gulls are ambient life. Check they all actually appear in the valley and say which don't, and whether any bird flies.

Words: "critter" is retired, say ambient life or fauna. Mind the frame budget: the lag fix just landed (docs/perf/2026-11-01-load.md), so report the valley's numbers with a crowd before and after 2 and 3.

Report after each branch: the commit, gates, clip, what was left out.
