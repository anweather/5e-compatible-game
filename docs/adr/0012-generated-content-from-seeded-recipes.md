# D12. Generated content from seeded recipes (2026-10-26)

**Status:** Proposed

- **Context:** the valley looks empty: four primitive cabins, ground
  animals and fireflies. We want villages, fish, birds and wizards that
  look like wizards, and want to add more without an art pipeline or
  Asset Store packs, which every machine has to own and restore. Punchy
  (credited) shows the approach working: houses, animals and critters
  all built from primitive shapes, with variety from a seed. Our
  constraint is lockstep (D7): whatever players can affect has to be
  identical on every machine.
- **Decision:**
  - Buildings, villages, ambient life and wizards' looks are
    **generated**: a recipe in `world/*.json`, plus a seed, goes through
    a builder made of primitive shapes with vertex colours. No textures
    and no imported models for these.
  - **Layout goes in the core, looks go in the view.** A village's plan
    (square, lanes, footprints, building kinds) is computed in `Chem` from
    integer cells and `WorldHash`, because footprints are wall cells that
    burn and break. Meshes, colours and details are rebuilt by each
    machine from the same spec and never affect the tick.
  - **Ambient life is view-only:** fish, birds, critters and particles
    are per machine and read cells, but never write them. Villagers start
    out that way too; they become core entities when they matter to play
    (the same open question as the tower-defense critters, BACKLOG §17).
  - **The scene keeps specs, not meshes:** generated meshes are built
    when the scene loads, so nothing generated goes into git.
  - "More life" is a recipe change: a density, a new entry, or a new
    builder with its entry (`docs/WORLD_LIFE.md`).
- **Why:**
  - Variety costs data, not art.
  - Every machine builds the same world without shipping assets.
  - Layout that's already in the core is ready for runtime chunk
    generation (D11).
  - Keeping looks in the view keeps tick state small and keeps the
    golden hashes stable when we restyle.
- **Reopen if:** generated looks can't reach the quality we want (then
  bring in a modular art kit, generated layout still placing it), or
  ambient life starts to matter to the game (say, fish you can catch),
  and then that layer moves into the core.
- **Sources:** Punchy `Scripts/Flora/FloraGenerator.cs` (houses as
  seeded variants), `Scripts/Terrain/Landmarks/LandmarkType.cs`
  (settlements), `Scripts/Ambience/CritterSystem.cs`; `docs/WORLD_LIFE.md`.
