# The Punchy proof of concept (MVP)

Agreed with Andrew on 2026-11-12. Punchy's builder has given permission for this experiment.

**The MVP:** load Punchy's world with our chemistry engine running in it, and do two things:
1. Cast a fire spell on Punchy's grass, and the fire spreads through it and burns it off.
2. Cast a freeze spell on Punchy's water, and it turns to ice.

Nothing more: no multiplayer, no caves, no saving, no terraforming.

## Does lift and shift still work? Yes

`plan.md`'s Phases 3 and 4 (import the package, then a fire cast on a bounded surface patch) only need the package
boundary. They don't need a neutral core (`audit.md` 6.A).
- **The rules:** our `rules/` already has both reactions. Grass burns (`catch 0.35`, `leaves dirt`), and cold freezes
  water to ice. Punchy can load a copy of our rules with `Rules.Load(dir)`, so no rules changes are needed.
- **The extra code** (golf, the dragon, the Hold) goes along as code Punchy never calls.

## What Punchy is like (origin/main 50da96e)

- **Terrain** is a 2.5D heightfield computed from a function. `TerrainSource` (an ECS singleton) answers
  `SampleGround(xz)` with height, biome, water depth and water surface. It's an 8 km island; chunks are 64 m, and the
  nearest level of detail is 1 m per quad, which matches our 1 m cells.
- **Grass isn't made of entities.** `GroundDetailSystem` (a MonoBehaviour) scatters GPU blades per 16 m tile, from
  `TerrainSurface.GrassDensity`. The blades are drawn procedurally and regenerated whenever a tile streams in. There's
  no API to remove a patch.
- **Water** is a sheet mesh per 64 m chunk at `WaterSurface`. There are no per-cell liquids.
- **Spells:** none, and no aiming either. Input comes from Input System actions, read in `PlayerInputSystem`. The
  terrain has no colliders, so aiming means marching a ray against `TerrainSource`.
- **Packaging:** there are no asmdefs, so Assembly-CSharp will see an auto-referenced `Chemistry` assembly. Newtonsoft
  3.2.2 is already there, as a dependency of other packages, so the package must declare it rather than ship its own
  DLL. Unity is 6000.5.8, ahead of our 6000.3.

**The biggest mismatch:** Punchy has no stored, changeable world layer, because everything is a function of position.
So the chemistry grid is an overlay. It's seeded from `TerrainSource` in a window around the player, and its changes
are fed back to each renderer.

## The bridge (Punchy side, outside the package)

1. **Seed:** make a `new World(rules, 256, 256)` window around the spawn. Sample `SampleGround` on a 1 m grid:
   grass density above 0 gives a grass cell, and water depth above 0 gives water. Our cell (x, y) maps to Punchy's
   (x, z) plus the window's corner.
2. **Tick** the World in a MonoBehaviour at our tick rate.
3. **Cast:** new Fire and Freeze input actions; the ray-marched ground point becomes a `CastCommand` (fire-bolt or
   fireball, frost).
4. **Show** the result through a player-centred chemistry texture (burnt, burning, ice per cell), set as a global the
   way `WeatherSystem` sets `_PunchyWetnessMap`:
   - `GroundDetail.hlsl` hides or chars the burnt blades,
   - `TerrainSurface.hlsl` darkens the scorched ground,
   - `Water.shader` draws ice, or a flat ice mesh sits just above the water surface.
   - Burning cells get particles.
5. **Find what changed:** the core has no per-cell change journal yet. For a 256 × 256 window, compare the burning,
   cover and liquid layers each tick, or per chunk. `plan.md`'s change journal waits until it's needed.

**Done when:** in a Punchy play session, a fire cast burns a patch of grass that spreads and leaves scorched ground,
and a freeze cast turns pond water to ice. Removing the bridge assembly restores Punchy exactly as it was.

## Controller-first casting (Andrew, 2026-11-12)

D16's hands with a charge (`docs/adr/0016-*`, `docs/design/unified-casting.md`) on a pad, in Punchy's play mode:
LT / LB the left hand's thrust / throw, RT / RB the right hand's, trigger + bumper on one hand the sweep, hold to
charge, release to fire; both hands charging one gesture merge into their pair's spell. Hold D-pad up for an element
radial (right stick picks, for the hand whose trigger is held, or the left by default). Punchy rebinds three things:
inventory LB → D-pad down, doors RB → Interact (Y), the weather toggle RT → a long press of Select. Menus keep the
bumpers for tabs. Keyboard keeps H / J / L for now.
