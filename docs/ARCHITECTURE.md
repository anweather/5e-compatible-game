# Architecture of the Unity prototype

How the code in `unity/Assets/Prototype/` is put together, written for
someone who doesn't read C# yet. It names files so you can find things;
you don't need to open them to follow it.

## The layers

Five layers. Each one only knows about the layers above it in this list.

| # | Layer | Where | What it does | Knows about Unity? |
|---|---|---|---|---|
| 1 | **Rules data** | `rules/*.json` (repo root) | What exists and how it reacts: materials, reactions, spells, sim numbers. Shared with the web sandbox | No (it's data) |
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
- `World.Spells.cs` turns a cast into the cells it touches.
- `World.React.cs` is the **rule interpreter**: "element X arrives at this
  cell", then it looks up `reactions.json` and applies the matching steps.
- `World.Sim.cs` is one tick: fire spreads, water flows, gas drifts,
  charge and wind decay.
- `World.Entities.cs` is what an entity does to cells: a barrel's blast of
  burning oil, a burnt crate's embers.
- `PropState.cs` is the rules side of a physics prop (hit points, burning,
  levitation, whether it floats), counted in ticks. It is the one file here
  that is *not* a port: the sandbox has no physics.
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

It is a line-by-line port of the TypeScript sandbox (`sandbox/src/`), and
the "golden" tests in `Chemistry/Tests/` check both give identical
results. That is why its semantics must not change without changing the
sandbox too. CI builds and tests it outside Unity (`core/`).

### 3. View (`Scripts/`)
| Area | Files |
|---|---|
| The grid's view | `ChemGrid.cs` (ticks the world, map legend, coordinates, log), `ChemGrid.Visuals.cs` (per-cell redraw, water, covers), `ChemGrid.Effects.cs` (fire, gas, charge), `GridTileBuilder.cs` (ground tiles and foliage), `MaterialCache.cs`, `FireLightPool.cs` |
| The world beyond the yard | `ValleyChemistry.cs` (burnt trees, scorched grass, the cabin burning down on the terrain) |
| The wizard | `PlayerControls.cs` (input bindings), `SpellCaster.cs` (aim, pick, cast), `CastPose.cs` (arm gesture), `SurfaceEffects.cs` (skid on ice, wade in water), `Knockback.cs` (shoved by blasts), `CameraZoom.cs`, `WizardRig.cs` (names of the Starter Assets parts) |
| Physics props | `PhysicsProp.cs` (a crate, barrel or boulder: reads its cell, floats, burns, explodes), `Forces.cs` (radial and line shoves), `SpellForces.cs` (Thunderwave, Gust of Wind, Levitate), `ChemEffects.cs` (the one door from physics into the chemistry) |
| The show of a spell | `SpellVisuals.cs` (data asset), `SpellProjectile.cs` |
| HUD | `EventLog.cs` (help, spell bar, "what happened" log) |
| Demos and probes | `DemoDirector.cs`, `WizardPuppet.cs`, `Showcase.cs`, `FlythroughCamera.cs`, `FpsProbe.cs` |
| Small shared helpers | `UrpMaterials.cs`, `Primitives.cs`, `ProceduralTextures.cs`, `ScrollingTexture.cs`, `Flicker.cs` |
| The old Fireline greybox | `PlayerController.cs`, `CameraFollow.cs`, `FireBolt.cs`, `Burnable.cs`, `Water.cs` (not used by the yard) |

### 4. Networking (`Scripts/Net/`)
`NetChem.cs` is **lockstep**: the host owns the clock and broadcasts, every
tick, the casts due on that tick. Every machine applies them to its own
`Chem.World` and steps it, so all worlds stay identical without sending
any cells. A checksum every 30 ticks catches a desync. `NetWizard.cs`
switches a spawned wizard on for its owner. `NetSession.cs` is the Play
Together panel (Relay join codes, or same-network IP). `NetDemo.cs` and
`NetDemoDirector.cs` run the 4-player scripted demo. Background:
`docs/MULTIPLAYER.md`.

### 5. Editor tooling (`Scripts/Editor/`)
Scenes are **generated by code**, never edited by hand:
`ChemYardBuilder.cs` (Prototype > Build Chemistry Yard) lays out the yard,
the wizard, the camera, lighting, networking and the spell visuals, and
calls `ValleyBuilder.cs` for the terrain, lake, sky, cabin and the
chemistry map around the yard, and `PropBuilder.cs` for the crates,
barrels and boulder. `PrototypeSceneBuilder.cs` builds the old
Fireline scene. `EditorAssets.cs` has the shared helpers and the Asset
Store pack paths. `DemoRecorder.cs` records clips, `PrototypeBuild.cs`
makes macOS/Windows builds, `AssetPacks/` restores the Asset Store packs.

## Design patterns, and where they are

| Pattern | In plain words | Where |
|---|---|---|
| **Pure core + view** | The rules engine knows nothing about graphics; the view only reads its state and draws it | `Chemistry/` vs. `ChemGrid*`, `ValleyChemistry` |
| **Rules as data + interpreter** | Behaviour is a table in JSON; one small engine reads the table | `rules/reactions.json` + `World.React.cs` |
| **Command / intent** | A cast is a small message, `(spell, x, y, direction)`, not a change to the world. Solo it is applied when the projectile lands; online it is queued, stamped with a tick and applied everywhere at once | `SpellCaster.Cast`, `NetChem.CastIntent` |
| **Lockstep simulation** | Share the inputs, not the results; a deterministic world does the rest | `NetChem.cs`, seeded RNG in `World` |
| **Dirty checking** | Each cell keeps a fingerprint of what's drawn and is only redrawn when it changes | `ChemGrid.View`, `ChemGrid.Key` |
| **Object pool** | A fixed set of lights moved to the biggest fires, never created per fire | `FireLightPool.cs` (and the aim markers in `SpellCaster`) |
| **Cache / flyweight** | One material per name, shared by thousands of tiles; per-tile shades go through property blocks | `MaterialCache.cs`, `GridTileBuilder.Paint` |
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
   `NetWizard` switches on `SpellCaster`). The view knows only one thing
   about networking: `SpellCaster` hands a cast to `NetChem` when online.
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
| A **material or terrain** | Add it to `rules/materials.json` (and reactions to `rules/reactions.json`). Test it in the web sandbox first. To draw it in the yard, add its colour and height to `GridTileBuilder.TerrainLook`, and a map character to `ChemGrid.Legend` if maps should use it |
| A **spell** | Add it to `rules/spells.json`. Add its id to `SpellIds` in `RuleIds.cs`, its look (glow, projectile, impact) to `ChemYardBuilder.BuildSpellVisuals`, and, to put it on the bar, to `SpellIds.DefaultBar` |
| A **new kind of cell effect** (e.g. a new gas) | Data first; then a `Render...` method in `ChemGrid.Effects.cs` with a prefab and a cube fallback, called from `ChemGrid.RenderCell`, and its state added to `ChemGrid.Key` |
| A **chemistry behaviour** that data can't express | Change the TS sandbox and `Chemistry/` together, with a golden test for both |
| A **physics prop** | Its numbers in `rules/entities.json`, a `PhysicsProp` placed by `PropBuilder` (Editor). Anything it does to cells goes through `ChemEffects.Request`, never straight to the world: solo it applies at once; online it is meant to become an intent (D4 in `DECISIONS.md`): the host stamps "barrel blew up in cell (4,7)" with a tick and sends it through `NetChem` like a cast (not built yet; online yards ignore these requests for now) |
| **Scenery** | In a builder (`ValleyBuilder`), never placed by hand |

## Known debt

What this clean-up left alone, roughly in order of value.

- **Cognitive complexity in `Chemistry/`** (8 methods, up to 69 in
  `World.React.cs`). They mirror the TS sandbox line by line; splitting
  them means splitting the TS the same way, with the parity tests as the
  safety net. Then drop `S3776` from `WarningsNotAsErrors` in
  `core/Directory.Build.props`.
- **No `.asmdef` for `Scripts/`.** The view compiles into
  `Assembly-CSharp` with the Starter Assets tutorial scripts. An asmdef
  would enforce rules 2 to 4 above (see `QUALITY.md`, Recommendations).
- **The two demo directors** (`DemoDirector`, `NetDemoDirector`) script
  similar tours in two formats (a coroutine and a step table).
- **`ValleyBuilder` keeps its layout in static fields** set by `Dress`.
  Fine for a one-shot editor tool, but a small "valley plan" object would
  make its steps independent.
- **Rules folder lookup is written twice**: `ChemGrid.FindRulesDir` and
  the test helper in `Chemistry/Tests/` (the one jscpd clone).
- **Starter Assets via reflection** (`WizardRig`) breaks silently if the
  pack renames a field. A tiny interface asmdef next to the pack would
  make it compile-checked, at the cost of touching pack files.
- **The old Fireline scene** (`PlayerController`, `CameraFollow`,
  `FireBolt`, `Burnable`, `Water`, `PrototypeSceneBuilder`) predates the
  Chem core and duplicates its fire in MonoBehaviours. Delete it once no
  one needs the comparison.
