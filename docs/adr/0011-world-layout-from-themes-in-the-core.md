# D11. World layout from themes, in the core (2026-10-27)

**Status:** Accepted

- **Context:** the world is one hand-shaped valley, baked in the editor
  into Unity terrains and a cell map that every machine loads the same.
  Punchy's biome noise (credited, `ThirdParty/Punchy`) now shapes the land
  around it, also baked. Neither scales to the world `WORLD_STREAMING.md`
  aims at: chunks generated from the world seed when a player first goes
  there, the same on every machine. Punchy's noise is float maths with
  sine and simplex noise, and its rivers come from a whole-map drainage
  simulation, so it can neither give the same bits everywhere nor build
  one chunk on its own. Andrew's other game, PocketTactics, has an open
  world generator built for exactly this: a **theme** (JSON goals: "a
  river through a central lake, mountains far north", with mix-ins)
  solved per seed into a parametric **blueprint** (noisy ellipses for
  zones, mountains and lakes; meandering width-profiled curves for
  rivers; soft influence fields), which any chunk asks about its own
  cells, in any order, bit-identically in four JavaScript engines.
- **Decision:**
  - **Port the PocketTactics theme engine to the Chem core**
    (`Chemistry/WorldHash.cs`, `WorldNoise.cs`, `Theme*.cs`), in our cells: its hashing and value noise,
    its geometry, the theme schema with mix-ins and validation, the
    blueprint (zones, mountains, lakes and islands, rivers, influences;
    roads, towns and goal scoring later), and the per-cell steering (which
    look, lake or river here). Themes are data in `world/themes/`.
  - **The layout is the theme's, the 3D look is the noise's:** a theme
    says where mountains, lakes, rivers and looks go; height and surface
    detail come from Punchy-style relief inside that layout.
  - **Floats as PocketTactics uses them:** the generator may use doubles,
    but only exactly rounded operations (`+ - * /` and `Math.Sqrt`), its
    own series sine, cosine and arctangent (`dsin`, `dcos`, `datan2`),
    and its own hashing and value noise, never `Math.Sin` and co or
    `System.Random`. .NET doubles on x64 and ARM64 are IEEE 754 with
    exact rounding, so this gives the same bits on the PC and the Mac.
    Pinned by goldens: the pure parts against values from PocketTactics'
    own TypeScript, the blueprint and steering against our own.
  - Its output is integer cells and terrain ids. Floats never reach tick
    state, so D3, D4 and the integer rule for the tick are untouched.
  - Until chunks generate at runtime, the editor bakes from it like
    today; Punchy's float noise stays editor-only for the look.
- **Why:** it already solved our problem (chunk-local, order-free,
  deterministic, goal-driven layout, mix-ins), it's heavily tested, it's
  Andrew's own code, and porting a design proven in four engines is far
  cheaper than inventing one. Punchy's generator looks better but cannot
  run per chunk or across machines, so it supplies the look, not the
  layout.
- **Reopen if:** a golden differs between the PC and the Mac (then move
  the generator to fixed point), or runtime generation turns out to need
  rivers that follow the ground downhill (then the blueprint carves the
  height, rather than drawing the water on top of it).
- **Sources:** PocketTactics `docs/world-themes.md`,
  `src/core/world/theme/` (`geometry.ts` on exact maths, `blueprint.ts`,
  `steer.ts`, `schema.ts`); Punchy `Assets/Game/Scripts/Terrain/`.
