Needs: unity
Area: unity/Assets/Prototype/Scripts/ terrain and weather view (known-chunk meshes, far landmarks, haze, sea plane)
Branch: far-ring-view
Priority: after far-ring-core lands
After: a clip from the valley's spawn toward the spire and the volcano; then u2 tunes world/horizon.json if needed

# The far ring, view half (the PC)

`docs/design/world-squares.md` section 4 and its "On paper (2026-11-09)" notes; the paper is `tools/world-paper/`
(Horizon panel, its README has the numbers). The core half (`far-ring-core`) gives `KnownRing` (call `Want` with the
characters' chunks at each tick boundary, `Handover()`, then read `Version`, `Chunks`, `TryGet`: 9 × 9 heights, water
and dominant biome per known chunk) and `GeneratedMap.Far` / `SeenAt` with `world/horizon.json` (type, `seenFrom`
radius, height, radius, stand-in shape). All view, all float: nothing shared reads it.

1. **Known-chunk meshes** from the 9 × 9 heights, painted by dominant biome, no colliders, built off the main thread
   (a job or the existing chunk-terrain pattern), rebuilt only when `Version` changes. Edges meet (neighbours share
   samples). Generated and active chunks keep today's terrain; a known chunk's mesh goes when its chunk is generated.
2. **Haze:** exponential-squared, ~98% at `know` (k = 1.98 / draw distance; fogDensity about 0.00295/m at 672 m).
3. **Far landmarks** from `SeenAt`: stand-in meshes with their own haze, about 90% at their `seenFrom`, fading to the
   sky colour (not the fog colour), so they show past `know` without popping.
4. **A sea plane** to the horizon, fogged, so the skybox never shows under it past the coast.
5. Measure: the nine's frame (NET STATUS, about 31 ms now) must not get worse by more than 2 ms; say the mesh build cost.
Clip (≤ 30 s, 720p, daylight) from the valley's spawn turning toward the spire and the volcano.
