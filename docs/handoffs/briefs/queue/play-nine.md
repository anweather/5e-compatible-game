Needs: unity
Area: unity/Assets/Prototype/Scripts/ (start-up and the map picker, the ground and biome painting, the coast dressing,
the menu's map choice), unity/Assets/Prototype/Scripts/Editor (a build that bakes the nine); no Chemistry/ changes
except a call into GeneratedMap
Branch: play-nine

# Play the nine (world squares phase 6, the first slice; the PC; the 3 x 3 playtest)

Landed: maps phase 7 (`-map`, sea from land), `world-plan`, `chunk-gen` (`GeneratedMap.Bake` /
`EnsureBaked(world/maps/nine)` writes the 1728 x 1728 folder, the valley at chunks 0..8, 18..26), `cell-memory`,
`view-rebuild` (flat-1728 starts in 6.1 s, rebuilds in under 2 s). Rings stay deferred (J-080): all nine at load.

1. **Bake at start:** `-map nine` (and the Editor's `Temp/map`) calls `GeneratedMap.EnsureBaked` before loading, into
   the player's data folder in a build (`worlds/nine/<world hash>/`), the gitignored folder in the Editor. Fix #169 on
   the way (a relative map path).
2. **Spawn in the valley** where the valley's spawn is today (shifted by the valley's new rows), so the town, castle
   and Hold work as now.
3. **Draw the other eight:** their biomes painted (the ground looks for each biome from the plan), their trees and
   boulders, rivers with water, the sea from land (phase 7). The valley's beach props and the river current line stay
   valley-only (phase 7's leftover): only place them where the valley is.
4. **A menu choice** for the map (Valley or The Nine) on Host, so Andrew can start it from a build, and the joiner
   gets the host's map (it's a pure function of the world file: same seed, same folder; refuse on a hash mismatch).
5. **Two peers on the nine:** the PC hosts, a second player joins over loopback, both walk from the valley into a
   second square: players=2, desync false, frame mean/p95, memory per peer. Watch free RAM; under 3 GB, stop.
Done: a 30 s 720p clip walking from the valley into the next square (Andrew's first look), shots of three squares at
1280 px checked at 480 px, probes smoke, online, hold, secondmap, groundbuild, crossing on the valley unchanged, and
the Unity-side EditMode tests. Push each step; git rm this claim in the last commit. The lead lands it.
