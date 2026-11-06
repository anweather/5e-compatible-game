Brief for the Mac: maps phase 5, the builder, branch `maps-builder` off `dev`. Read `docs/design/maps-as-data.md` section 1 and the order of work. About 4 to 5 days. Push as you go so the PC can judge.

**Do:** one run-time builder that turns arrays into `TerrainData`, behind a switch beside the baked chunks, default off, so the valley stays as it is until the PC has judged it. It reads the chosen map's folder from paths, not Unity assets. Two levels fill the same arrays:
- A map with `ground/` (the valley): heights, paint, trees and details from there.
- A map with no `ground/`: heights interpolated from `land`, paint from `CellPaint`, trees and grass scattered from a seed by the cell's terrain. No bake.

`ValleyGround` gets its chunks from the builder, not the scene; the builder replaces `Bind`'s run-time copy rather than adding to it, and `Recover` restores from the arrays. Prototypes (layers, trees, grass) stay assets, on one template `TerrainData`.

**Also:** the drawable range goes per map (`World.Land.cs`: `LandLowest`, `LandHighest` are valley constants). The valley's stay what they are. Everything landed since the design must carry over: the `TerrainRock` material, rock paint on steep stamped land, `LandRocks`, stamps easing and props riding on built terrain.

**Keep:** the core and `land.json` untouched byte for byte, no golden moves, the view decides nothing shared (D4). If one moves, stop and say why. The scene keeps its terrains until phase 6. Say if `ChemYardBuilder` or `ValleyBuilder` must change at all.

**Measure:** the limit is no more than 2 s added on a Windows player (baseline 7.37 s dev, 7.49 s with `ground/`). Give what you can from the Mac, and a `tools/probe.sh`-able way for the PC to get it. Also a way to flip the switch in a player.

**Tests:** arrays match `ground/`; a stand-in map with no `ground/` builds; built height at each cell centre within one unit of `land`. Stamps and `fell` on built terrain.

**Gates:** core build, tests, format, jscpd by real exit code, EditMode in a batch clone; the 8 `ApiKeyTests` are the known Mac failures.

**Report:** one short message when the switch works: the commit, gates, and a numbered list for the PC (look compared side by side, seams, start-up, frame cost, stamps, two peers). Don't merge it. The PC is profiling from dev and has no changes in `ValleyGround`, but ask before anything larger than this brief.
