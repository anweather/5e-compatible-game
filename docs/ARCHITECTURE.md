# Architecture of the Unity prototype

How the code in `unity/Assets/Prototype/` is put together, written for
someone who doesn't read C# yet. It names files so you can find things;
you don't need to open them to follow it.

## The layers

Five layers. Each one only knows about the layers above it in this list.

| # | Layer | Where | What it does | Knows about Unity? |
|---|---|---|---|---|
| 1 | **Rules data** | `rules/*.json` (repo root) | What exists and how it reacts: materials, reactions, spells, sim numbers. Shared with the web sandbox. Next to it, `world/things.json`: where the props and items stand, per chunk | No (it's data) |
| 2 | **Chem core** | `Chemistry/` (namespace `Chem`) | Loads the rules and runs the world: a grid of cells, one tick at a time. Same results on every machine | **No**, the compiler forbids it |
| 3 | **View** (presentation) | `Scripts/` | Draws the world, reads input, plays the show (gestures, projectiles, particles), moves the wizard | Yes |
| 4 | **Networking** | `Scripts/Net/` | Lets friends share one world: sends cast intents, not cells | Yes |
| 5 | **Editor tooling** | `Scripts/Editor/` | Builds the scenes, records demos, makes player builds, restores Asset Store packs. Never runs in the game | Yes (Editor only) |

```
 rules/*.json ──► Chem core ──► View ──► Networking
   (data)        (Chemistry/)  (Scripts/)  (Scripts/Net/)
                        ▲          ▲            ▲
                        └──────────┴────────────┴── Editor builders (Scripts/Editor/)
```

### 1. Rules data
Everything a designer might tune lives in JSON: a spell is an element, a
shape and a power; a reaction is "when *fire* reaches a cell where
*frozen* is true, *melt*". See `rules/README.md`. The Unity code never
hard-codes what a spell *does*. It only refers to rules by id, and those
ids are listed once in `Scripts/RuleIds.cs` (`SpellIds.FireBolt`, ...).

### 2. Chem core (`Chemistry/`)
- `RuleTables.cs` reads the JSON into typed tables.
- `World.cs` holds the cells (`Cell.cs`), the seeded random number
  generator, the log and the tick counter.
- **Chunks** (`Chunk.cs`, `World.Chunks.cs`; `docs/WORLD_STREAMING.md`).
  Cells are stored in 64×64 chunks (`Chunks.Size`, set once as
  `Chunks.Shift`) keyed by world-wide chunk coordinate:
  `Chunks.ChunkOf(cell)` is `floor(cell / 64)` and `Chunks.LocalOf` the
  remainder, negatives included. Each chunk also keeps the per-cell sim state
  that isn't `Cell` state (fire generation, per-tick marks) and links to its
  loaded neighbours. The world still has an extent (`W`×`H`: the valley's
  576×576 cells, one chunk per chunk terrain, 9×9); `At`, `Idx` and `Cells` (a row-major array of the same cell
  objects) work as before. **Active set:** `SetActive(cx, cy, bool)`,
  `SetAllActive`, `IsActive`, `ChunkCoords`, `ActiveChunks`.
  `ChunkStreaming` plans it from the players' cells (the 3×3 around each
  wakes, a chunk sleeps once no player is within 2) as `ChunkSwitch`es that
  travel in `TickBatch.Chunks` like any other command; the view draws and
  scans only active chunks. `Step` visits only active
  chunks, as row segments in world row-major order (sorted, never the
  store's order), so with every chunk active a tick is exactly the old
  single-grid tick and all goldens hold. A paused chunk is frozen: nothing
  the simulation does writes to it (ticks, reactions, spells, blasts, earth
  shaping), and fire, water and gas beside it treat it as a wall. Map
  building (`Paint`, `AddWater`, `SetCover`) still writes anywhere.
  `ChunkHash(cx, cy)` fingerprints one chunk like `CellHash` does the world
  (whose value is unchanged).
- `World.Spells.cs` turns a cast into the cells it touches.
- `World.React.cs` is the **rule interpreter**: "element X arrives at this
  cell", then it looks up `reactions.json` and applies the matching steps.
- `World.Sim.cs` is one tick: fire spreads, water flows, gas drifts,
  charge and wind decay.
- `World.Entities.cs` is what an entity does to cells: a barrel's blast of
  burning oil, a burnt crate's embers, a smashed lantern's spilled oil.
  `Effects.cs` wraps these as a value, `EffectCommand` (burn, embers,
  explode or spill, a cell and an entity or item id), applied by
  `world.ApplyEffect`: what a physics prop hands the chemistry, solo at once
  and online stamped into a tick batch like a cast.
- `Items.cs` reads `rules/items.json`, things to carry (glowing balls, a
  bullseye lantern, a spyglass, sending stones, a Dancing Lights mote): their
  light in SRD feet, their use, and when one breaks (`ItemDef.BreaksAt`).
  `Rules.CheckItems.cs` checks them with the same messages as the sandbox,
  which carries the table through mods but has no items on its board. Not a
  port: like `PropState`, it is the rules side of Unity physics.
- **Things from data** (`WorldThings.cs`, `docs/WORLD_STREAMING.md`): the
  props and items of `world/things.json`, per chunk, each with a stable id
  (`ThingId`: chunk + index in the chunk's list, never a runtime counter),
  checked against the rules, with a `Fingerprint` for the netcode checksum.
  `EffectCommand.Thing` names the prop an effect came from by that id. Not
  a port: the sandbox has no props on its board.
- `PropState.cs` is the rules side of a physics prop (hit points, burning,
  levitation, whether it floats), counted in ticks. It is the one file here
  that is *not* a port: the sandbox has no physics.
- **Chunk terrains** (`docs/WORLD_STREAMING.md`, part 1), the maths the
  view needs to draw the cells on 9×9 chunk `Terrain`s, kept here so it is
  tested without Unity. Like `PropState`, not a port (the sandbox has no
  terrain). `TerrainGrid.cs` is the layout: chunk corners, 257 or 65
  heightmaps (4 or 1 samples per cell), grid cell ↔ world cell ↔ chunk,
  sample and alphamap texel, and world point → cell (`TerrainGrid.Valley()`
  is the valley's). `CellHeightfield.cs` turns cells into each sample's
  offset from the baked ground: a cell's `(elevation + Height) ×
  stepHeight`, the mean of the cells touching a sample on a cell border (so
  banks slope), the highest wall for a border touching a raised wall (walls
  stand full height over their whole cell), and on a 257 edge facing a 65
  chunk the in-between samples interpolated so seams match exactly. It keeps
  a target and an eased current per chunk; `Update(world, changedCells)`
  gives the dirty sample rectangles (neighbour chunks included) and
  `Advance(dt)` the ones that moved, for `SetHeightsDelayLOD`. `CellPaint.cs`
  maps a cell's terrain, cover and scorch to weights of the valley's
  terrain layers (by name) for its 4×4 or 1×1 texel block, batched as one
  rectangle per chunk. Each texel blends the cells around a point nudged by
  smooth noise, and the fade into the base slides with broader noise, so a
  scorch or the bare pad has a soft, wobbly edge that ignores cell borders
  (a cell still only affects its 3×3 neighbourhood). Arrays are Unity's
  order: `[z, x]` (and `[z, x, layer]`).
- `ValueNoise.cs` (smooth, seeded, repeatable noise) and `CellContour.cs`
  (a rounded, wobbly outline around a patch of cells as a triangle mesh,
  by marching triangles over the cells' blended values; a per-cell wobble
  factor holds the edge still where it must meet a bank) are view maths
  kept here so they are tested without Unity. Not ports.
- `World.Terraform.cs` is earth shaping: a spell with `"effect":
  "terraform"` raises or digs its cells (`Cell.Height`, steps on top of
  the terrain's elevation). Water flow reads elevation plus height, raised
  ground spills its water without losing any, and a cell raised to
  `sim.height.blockAt` counts as blocking (`IsRaisedWall`). `CellHash`
  writes the height only when it isn't 0, so older goldens still hold.
- **Live mods** (`docs/LIVE_LAB.md`), the sandbox's patch format:
  `ModPatch.Parse` reads one, `rules.WithMods(mods)` applies them in order
  and checks the result (`Rules.Mods.cs`, `Rules.Check.cs`; same merge and
  same messages as `sandbox/src/rules.ts`), and `world.ApplyRules(rules)`
  switches a running world over between ticks, keeping its cells.
  `rules.Fingerprint` is a stable hash of the rules in force, for the
  netcode checksum next to `world.CellHash()`. A late joiner rebuilds the
  rules as `baseRules.WithMods(roomMods)`. Examples are in `rules/mods/`.
- **Switching rules in a running game** (`docs/LIVE_LAB.md`): `RuleSet.cs`
  is the rules in force as a value (base rules, the ordered switched-on
  mods as id + JSON text, the effective rules), so every switch builds and
  checks a new one and a refused switch leaves the old one untouched.
  `RuleSet.BaseOf` folds a scene's tuning into the base as a mod.
  `TickBatch.cs` is one tick's lockstep inputs: a rule switch, casts and
  props' effects (applied in that order, then a step); `RunOn` is the one
  place they are applied, live or replayed, and `Timeline` rebuilds the
  batches from a host's history (casts, rule switches, effects) for a late
  joiner.
  `ModCatalog.cs` lists `rules/mods/*.json` with titles and ideas.

It is a line-by-line port of the TypeScript sandbox (`sandbox/src/`), and
the "golden" tests in `Chemistry/Tests/` check both give identical
results. That is why its semantics must not change without changing the
sandbox too. CI builds and tests it outside Unity (`core/`).

### 3. View (`Scripts/`)
| Area | Files |
|---|---|
| The grid's view | `ChemGrid.cs` (ticks the world, map legend, coordinates, log), `ChemGrid.Rules.cs` (the rules in force; puts a switch in force between ticks and raises `RulesChanged`), `ChemGrid.Visuals.cs` (per-cell redraw: dressing, blocker, water, covers), `ChemGrid.Effects.cs` (fire, gas, charge; flames jittered by a cell hash so a burning front isn't a row), `ChemGrid.Shaping.cs` (earth shaping: reshaped ground eases to its new height with a puff of dust, carrying what stands on it), `CellDressing.cs` (what stands on a cell: brambles, a charred stump, rubble, a stone block; seeded per cell so nothing repeats), `CellDressing.Built.cs` (a bridge's plank deck on two beams, a riveted iron or bronze plate shifted and turned off the grid, a raised wall's grey crags), `CellBlockers.cs` (pooled invisible boxes on blocking cells: trees, brambles, stone, raised walls), `CellWaterMesh.cs` (one surface over the wet cells, and one for ice with a collider; never above the land around it), `CellSlickMesh.cs` (oil and grease as one glossy film, level over a pit's lip and shed on steep ground), `ContourSurface.cs` (the mesh both draw: a `CellContour` outline with heights, vertex colours fading the edge, world UVs), `MaterialCache.cs`, `FireLightPool.cs` |
| The ground | The valley's 9×9 chunk terrains are the ground for every cell, the old yard included. `ValleyGround.cs` keeps runtime copies of the chunks the cells reach and draws the cells on them: heights from `CellHeightfield` (baked base plus each cell's offset, eased) and paint from `CellPaint` (one texel block per cell, detail grass cleared off bare ground). `ValleyChemistry.cs` routes cell changes to it and does the rest on the terrain: burnt trees (the yard's trees are terrain trees too), scorched grass, the cabin burning down, the lake (pits under the lake's surface) |
| The wizard | `PlayerControls.cs` (input bindings), `SpellCaster.cs` (aim and cast the selected spell), `SpellRadialHud.cs` (the spell radial: hold Tab or LB, element then spell; the model is `Chemistry/SpellRadial.cs`), `CastPose.cs` and `CastGestures.cs` (the casting gestures: a meteor pulled down, a fireball thrown, earth raised or pressed, a shove, a thrust, a reach; IK keys over time, a held wind-up while charging, the spell released at the gesture's release), `SurfaceEffects.cs` (skid on ice, wade in water, climb one step), `Knockback.cs` (shoved by blasts), `CameraZoom.cs`, `WizardRig.cs` (names of the Starter Assets parts) |
| Physics props | `ThingSpawner.cs` (spawns the props and items of `world/things.json` per chunk from one prefab per kind, at rest on their cells; online the host spawns them as NetworkObjects and clients adopt them by stable id; `SpawnChunk` / `UnloadChunk`), `PhysicsProp.cs` (a crate, barrel or boulder: reads its cell, floats, burns, explodes; online a client's copy is a puppet of the host's), `Forces.cs` (radial and line shoves, optionally only what this machine owns), `SpellForces.cs` (Thunderwave, Gust of Wind, Levitate; solo when a cast lands, online when its tick batch runs), `ChemEffects.cs` (the one door from physics into the chemistry: applied solo, stamped by the host online) |
| Things to carry | `Carryable.cs` (an item from `rules/items.json`; a `PhysicsProp` on the ground, kinematic in hand; adds the behaviours its row asks for), `ItemHolder.cs` (the wizard's hand: pick up, drop, throw, use, the hint by the crosshair; online every wizard has one, and pick-up and letting go are granted by the host), `ItemUse.cs` (base of the behaviours), `ItemLight.cs`, `Fragile.cs` (smash and spill through `ChemEffects`), `Spyglass.cs`, `SendingStone.cs` with `PingMarker.cs`, `DancingLights.cs`, `Aiming.cs` (the crosshair ray, shared with `SpellCaster`), `MageHand.cs` (the spell: fetches a light item from 30 ft into the hand, or sets the held one down there, through `ItemHolder.PickUpFromAfar` and `SetDownAt`; online the host grants up to `NetItem`'s `MaxGrantReach`) |
| The show of a spell | `SpellVisuals.cs` (data asset), `SpellProjectile.cs` |
| HUD | `EventLog.cs` (help, the spell chip, "what happened" log), `PauseMenu.cs` (Esc / Start: Resume, Play together, Mods, Credits, Quit; other code adds sections with `PauseMenu.AddSection` and sub-pages with `AddPage`; buttons drawn with `PauseMenu.Button` get controller focus through `MenuNavigator`, `MenuControls` and `MenuActivator`), `ModsPanel.cs` (the menu's Mods page; M opens it), `CodePicker.cs` (join code by controller) |
| Live rules | `RuleSwitcher.cs` (decides what to switch to and sends it: queued solo, stamped by the host online, asked of the host by a client), `RulesWatcher.cs` (Editor and dev builds: reloads saved `rules/` files, debounced, on the main thread). `ChemGrid` adds both at runtime, so scenes need no rebuild |
| Demos and probes | `DemoDirector.cs`, `WizardPuppet.cs`, `Showcase.cs`, `FlythroughCamera.cs`, `FpsProbe.cs` |
| Sound | `GameSounds.cs` (which clip a spell makes when cast and where it lands, a break or a blast; one-shot and looping 3D sources), `ProceduralSounds.cs` (the clips, made in code: no recordings yet). Fires crackle from the `FireLightPool` lights |
| Small shared helpers | `UrpMaterials.cs`, `Primitives.cs`, `ProceduralTextures.cs`, `ScrollingTexture.cs`, `Flicker.cs` |
| The old Fireline greybox | `PlayerController.cs`, `CameraFollow.cs`, `FireBolt.cs`, `Burnable.cs`, `Water.cs` (not used by the yard) |

**Earth shaping.** One step of `Cell.Height` is `ChemGrid.stepHeight`
(0.5 m, the same step a pit is deep), so the ground, the water surface
(ground plus depth) and the water flow all use one scale: a heap is 0.5 m,
Move Earth's hill 1 m, an Earthen Trench 1 m deep, and a Wall of Stone
(height 3, `sim.height.blockAt`) a 1.5 m block of rock with crags on top and
an invisible blocker to 2.7 m, so it can't be jumped. The terrain is the
ground: `CellHeightfield` moves its samples at `stepHeight` per
`shapeSeconds` (1 m/s), and the cell's dressing, blocker and anything
standing on it ride along. The wizard steps up one step
(`SurfaceEffects.climbStep`, 0.55 m) and jumps out of a trench. Props and
the wizard stand on the terrain colliders everywhere, so pits, the pond and
trenches are real hollows. `ChemGrid.GroundTop` (valley base + offset),
`SurfaceY` (+ what stands on the cell) and `WaterSurfaceY` (+ water depth;
the lake level for lake cells) are the one height function. Height is cell
state, hashed in `CellHash`, so online it arrives through lockstep like
everything else.

### 4. Networking (`Scripts/Net/`)
`NetChem.cs` is **lockstep**: the host owns the clock and broadcasts, every
tick, the casts due on that tick (with the caster's position), the effects
its physics props had on cells (`EffectIntent`: a barrel's blast, a
lantern's spill, a burning crate's fire) and, when the rules change, the
new mod list (`RuleChangeMsg`). Every machine runs them as a `TickBatch` on
its own `Chem.World`, so all worlds stay identical without sending any
cells. A checksum every 30 ticks (cell hash and rules fingerprint) catches
a desync and says whether the rules differ. A late joiner gets the casts,
rule switches and effects and replays them. When a live batch's casts run,
their shoves run too: the host moves the props, each player their own
wizard. `NetWizard.cs` switches a spawned wizard on for its owner and
passes a shove worked out elsewhere (a barrel blast on the host) to it.

**Physics props and items are host-authoritative** (D4): each is a
`NetworkObject` spawned by the host's `ThingSpawner` from
`world/things.json`, with a server-authority `NetworkTransform`. `NetProp.cs`
carries the thing's stable id (`Chem.ThingId`, set before it spawns, so late
joiners get it too), makes a client's copy a kinematic puppet and replicates
only what the eye needs (burning, levitating, hit points); a broken prop is
despawned and destroyed everywhere. Effects (`EffectIntent`) name the prop
by that id, and the checksum includes the things file's fingerprint. `NetItem.cs` keeps who holds an item (the holder
wizard's object id; the host grants pick-ups and flies throws), whether its
light is uncovered, the Dancing Lights mote's position (from the holder),
and carries a sending stone's message to the machine of whoever holds the
other stone. `NetCourt.cs` keeps the ball court's score: the host's physics
decides goals (`BallCourt`) and every scoreboard shows the host's count.
Measured numbers are in `docs/MULTIPLAYER.md` §7. `NetSession.cs` is playing together:
the Play together section of the Esc menu (Relay join codes, or same-network
IP) and a one-line status with the join code while online. `NetCommandLine.cs`
lets a player build host (`-host-online`) or join (`-join-code CODE`) from the
command line and logs a `NET STATUS` line every 10 s, for two-machine tests. `NetDemo.cs` and
`NetDemoDirector.cs` run the 4-player scripted demo. Background:
`docs/MULTIPLAYER.md`.

### 5. Editor tooling (`Scripts/Editor/`)
Scenes are **generated by code**, never edited by hand:
`ChemYardBuilder.cs` (Prototype > Build Chemistry Yard) lays out the yard,
the wizard, the camera, lighting, networking and the spell visuals, and
calls `ValleyBuilder.cs` for the terrain, lake, sky, cabin and the
chemistry map around the yard. `PropBuilder.cs` and `ItemBuilder.cs` build
one prefab per prop and item kind (`Generated/Things/`, registered as network
prefabs through `Generated/ThingNetworkPrefabs.asset`); where they stand is
data (`world/things.json`), spawned at runtime by `ThingSpawner`, so nothing
is placed in the scene. `ValleyShape.cs` is the valley's shape as functions of a point;
`ValleyTerrain.cs` builds the chunk terrains from it (`TerrainGrid.Valley()`:
257 heightmaps on the 3×3 chunks around the play area, 65 elsewhere; seam
corners rounded to stored height steps so a 257 edge meets a 65 edge
exactly) and bakes the starting cells into them with the runtime's own
`CellHeightfield` and `CellPaint`, from `ChemGrid.MapWorld`. Full-detail
and coarse chunks are separate terrain groups, since Unity only stitches
LOD between neighbours of one resolution. `PrototypeSceneBuilder.cs` builds the old
Fireline scene. `EditorAssets.cs` has the shared helpers and the Asset
Store pack paths. `DemoRecorder.cs` records clips, `PrototypeBuild.cs`
makes macOS/Windows builds, `AssetPacks/` restores the Asset Store packs.

## Design patterns, and where they are

| Pattern | In plain words | Where |
|---|---|---|
| **Pure core + view** | The rules engine knows nothing about graphics; the view only reads its state and draws it | `Chemistry/` vs. `ChemGrid*`, `ValleyChemistry` |
| **Rules as data + interpreter** | Behaviour is a table in JSON; one small engine reads the table | `rules/reactions.json` + `World.React.cs` |
| **Command / intent** | A cast is a small message, `(spell, x, y, direction)`, not a change to the world. Solo it is applied when the projectile lands; online it is queued, stamped with a tick and applied everywhere at once. A rule switch travels the same way | `SpellCaster.Cast`, `NetChem.CastIntent`, `TickBatch` |
| **Immutable value + observer** | The rules in force are a value replaced whole at a tick boundary; props and items listen for `ChemGrid.RulesChanged` and re-read their rows | `RuleSet`, `ChemGrid.Rules.cs`, `PhysicsProp.ReadRules` |
| **Lockstep simulation** | Share the inputs, not the results; a deterministic world does the rest | `NetChem.cs`, seeded RNG in `World` |
| **Dirty checking** | Each cell keeps a fingerprint of what's drawn and is only redrawn when it changes | `ChemGrid.View`, `ChemGrid.Key` |
| **Object pool** | A fixed set of lights moved to the biggest fires, never created per fire; blocker boxes handed back and reused | `FireLightPool.cs`, `CellBlockers.cs` (and the aim markers in `SpellCaster`) |
| **Cache / flyweight** | One material per name, shared by every cell's dressing; per-cell shades go through property blocks | `MaterialCache.cs`, `CellDressing.Paint` |
| **Data assets** | How a spell *looks* is an editable asset, separate from what it *does* | `SpellVisuals.cs` (ScriptableObject) |
| **Builders** | Code that generates scenes and assets, so a rebuild always gives the same result | `ChemYardBuilder`, `ValleyBuilder`, `PrototypeSceneBuilder` |
| **Fallbacks (strategy-ish)** | Each effect has a prefab version and a plain-cube version, chosen by whether the pack is present | `ChemGrid.Effects.cs`: `RenderFire`/`FallbackFire`, `RenderGas`/`FallbackGas`, `RenderCharge` |
| **Ground effects** | Cells change how the wizard moves, read from the world each frame, without editing the Starter Assets controller | `SurfaceEffects.cs` (slide, wade) |
| **Adapter by name** | Starter Assets scripts are reached by name and reflection, so our code compiles even before the packs are restored | `WizardRig.cs`, `WizardPuppet.cs`, `SurfaceEffects.cs` |

## Dependency rules

Who may use whom. Breaking one of these is a design bug even if it compiles.

1. **`Chemistry/` uses nothing but .NET and Newtonsoft.Json.** No
   `UnityEngine`, no mutable statics, all randomness from the world's
   seeded generator, no culture-dependent string calls. The Chemistry
   `.asmdef` sets `noEngineReferences`, so the compiler enforces the first.
2. **The view (`Scripts/`) may read and command the core**, never the
   other way round. The core reports back only through its state and its
   log.
3. **Networking may use the view** (`NetChem` drives `ChemGrid`,
   `NetWizard` switches on `SpellCaster`). The view knows networking only
   as a door it hands a request to when online: `SpellCaster` and
   `ChemEffects` to `NetChem`, `PhysicsProp` and `Carryable` (and the item
   behaviours) to their `NetProp` / `NetItem`, `Knockback` to `NetWizard`.
   Solo none of them is used.
4. **Editor code may use everything; nothing may use editor code.**
5. **Asset Store packs are optional.** Runtime code reaches them by name
   or through prefab references set by a builder, and every missing
   prefab has a fallback. Never edit pack folders (`Starter Assets`,
   `UnityTechnologies`, `TerrainSampleAssets`, `TerrainDemoScene_URP`) or
   `Generated/`.

## Conventions

- **Naming:** types and methods `PascalCase`; fields `camelCase`
  (Unity's inspector style); constants `PascalCase`. One MonoBehaviour per
  file, named like the file (Unity requires it). A big component may be
  split into `partial` files by responsibility (`ChemGrid.*.cs`).
- **Comments** at the top of every file say what it is for, in plain
  English.
- **Small methods.** The analyzers warn above cognitive complexity 15;
  split into well-named helpers rather than suppressing.
- **Analyzer warnings are the bar.** `docs/QUALITY.md` explains them;
  `unity/Assets/Default.globalconfig` lists every rule switched off, each
  with its reason.
- **Rebuild, don't hand-edit, generated scenes.** Change the builder and
  re-run its menu item.

### Where new things go

| You want to add... | Do this |
|---|---|
| A **material or terrain** | Add it to `rules/materials.json` (and reactions to `rules/reactions.json`). Test it in the web sandbox first. To draw it, give it layer weights in `CellPaint` (or it reads as bare earth), its colour and height in `CellDressing.TerrainLook` (and parts in `CellDressing` if something stands on it), and a map character to `ChemGrid.Legend` if maps should use it |
| A **spell** | Add it to `rules/spells.json`. Add its id to `SpellIds` in `RuleIds.cs`, its look (glow, projectile, impact, and a `gesture` if the one `CastGestures.For` picks from its rules doesn't suit) to `ChemYardBuilder.BuildSpellVisuals`, and a `group` in its spells.json entry: the spell radial's element it sits under (`rules/groups.json`; every spell in the rules is in the radial). A spell that moves its caster runs only on the caster's machine, from `SpellForces` (see `MistyStep.cs`) |
| A **new kind of cell effect** (e.g. a new gas) | Data first; then a `Render...` method in `ChemGrid.Effects.cs` with a prefab and a cube fallback, called from `ChemGrid.RenderCell`, and its state added to `ChemGrid.Key` |
| A **chemistry behaviour** that data can't express | Change the TS sandbox and `Chemistry/` together, with a golden test for both |
| A **physics prop** | Its numbers in `rules/entities.json`, a shape in `PropBuilder` (Editor), which builds its prefab and makes it networked (`NetProp`), and where it stands in `world/things.json` (append to a chunk's list so ids don't shift). Anything it does to cells goes through `ChemEffects.Request` as an `EffectCommand`, never straight to the world: solo it applies at once; online the host stamps it into the next `NetChem` tick batch (D4 in `DECISIONS.md`) and every machine applies it at that tick. A new kind of effect goes in `Chem.EffectKind` and `World.ApplyEffect`. Then rebuild the yard (Prototype > Build Chemistry Yard) so its prefab is built and registered |
| A **thing to carry** | Its row in `rules/items.json` (light, use, breaks; checked in both `Rules.CheckItems.cs` and `validateRules`). Its id in `ItemIds`, a shape in `ItemBuilder` (Editor), and a spot in `world/things.json` (a pair shares a `link`). A new `use` needs an `ItemUse` behaviour, added in `Carryable.AddBehaviours`, and the use name in both checkers. If what it does should show on other screens, give `NetItem` the state or message (as for the light, the mote and sending stones); a use only its holder sees (the spyglass) needs nothing |
| **Scenery** | In a builder (`ValleyBuilder`), never placed by hand |

## Known debt

What this clean-up left alone, roughly in order of value.

- **Cognitive complexity in `Chemistry/`** (4 methods, up to 69 in
  `World.React.cs`). They mirror the TS sandbox line by line; splitting
  them means splitting the TS the same way, with the parity tests as the
  safety net. Then drop `S3776` from `WarningsNotAsErrors` in
  `core/Directory.Build.props`.
- **No `.asmdef` for `Scripts/`.** The view compiles into
  `Assembly-CSharp` with the Starter Assets tutorial scripts. An asmdef
  would enforce rules 2 to 4 above (see `QUALITY.md`, Recommendations).
- **The two demo directors** (`DemoDirector`, `NetDemoDirector`) script
  similar tours in two formats (a coroutine and a step table).
- **The valley builders keep their layout in static fields**
  (`ValleyShape.Plan`, `ValleyTerrain`'s chunk list). Fine for one-shot editor tools, but a small "valley
  plan" object would make their steps independent.
- **Rules folder lookup is written twice**: `ChemGrid.FindRulesDir` and
  the test helper in `Chemistry/Tests/` (the one jscpd clone).
- **Starter Assets via reflection** (`WizardRig`) breaks silently if the
  pack renames a field. A tiny interface asmdef next to the pack would
  make it compile-checked, at the cost of touching pack files.
- **The old Fireline scene** (`PlayerController`, `CameraFollow`,
  `FireBolt`, `Burnable`, `Water`, `PrototypeSceneBuilder`) predates the
  Chem core and duplicates its fire in MonoBehaviours. Delete it once no
  one needs the comparison.
