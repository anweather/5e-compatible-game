Needs: unity
Area: unity/Assets/Prototype/Scripts/ (GroundBuilder's GroundFromLand, the valley bind, ChemGrid's RenderAll and
RenderCover, OnlineReset's rebuild path); no Chemistry/ changes
Branch: view-rebuild

# A fast start and rebuild on the nine (world squares; before the 3 x 3 playtest)

`cell-memory` (102fee93) shrank the core, but the PC's new WORLD BUILT log line shows the flat-1728 rebuild (5.0 s) is
mostly the view: cells 0.61 s, the valley bind 1.9 s, the draw 2.4 s (RenderAll's first pass over 3 million cells;
RenderCover's per-cell checks alone 1.1 s). Start-up adds GroundFromLand's 5.2 s (14.1 s total; the valley 5.1-5.5 s).
Every start, host and online reset pays the rebuild.

1. **Draw only what's near:** the first RenderAll pass and RenderCover for the active chunks around the players (reach
   2, or what the camera sees), the rest on first sight, spread over frames. Cover checks skip cells with no cover.
2. **The bind:** say what the 1.9 s binds per cell and make it per chunk, or lazy.
3. **GroundFromLand:** build each chunk's ground on first sight or across frames after start, not all 729 before play.
4. A rebuild that keeps the same map (online reset) reuses what it can.
Targets on flat-1728: start-up under 7 s, a rebuild under 2 s, no hitch over 50 ms while walking at sprint as chunks
draw. The valley no slower, shots from the town, castle and shore identical at 480 px. Probes `smoke`, `online`,
`hold`, `secondmap`, `groundbuild`. Layer 2 of `docs/TESTING_LAYERS.md`. Push the branch; remove this claim in the
last commit. The lead lands it.
