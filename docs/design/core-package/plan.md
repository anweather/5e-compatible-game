# Chemistry Core Extraction + Punchy Integration
## Coding-agent handoff plan

**Status:** Proposed architecture and implementation plan; no repositories or files have been modified.

**Source projects inspected**
- Original game: `anweather/5e-compatible-game`, branch `dev`
- Punchy: `Skeleman/Punchy`, branch `main`
- Punchy baseline inspected: `db9ca83e7440cae997241b6fcddc1389f858f992`

## Executive recommendation

Create one standalone repository for the canonical Chemistry core and consume it as a versioned Unity package from both games.

- `chemistry-core`: the one canonical source of engine-independent simulation code.
- `5e-compatible-game`: remains an independent game and package consumer.
- Punchy fork: remains an independent game and package consumer, with a host-side adapter.

**Chemistry is the product. Unity projects are consumers of Chemistry. Punchy is one possible host, not Chemistry's new home.**

This is intentionally a reversible experiment. Do not merge the game repositories. Do not copy the core into Punchy. Do not make the original game depend on Punchy.

---

# 1. Non-negotiable architecture rules

1. Exactly one canonical copy of Chemistry source code. Never copy the core into Punchy or maintain a second implementation in the original game.
2. The core must not reference Punchy, UnityEngine, Unity.Entities, Unity.NetCode, or either game's presentation layer.
3. Both Unity projects consume a pinned version of the same package.
4. The original game must remain buildable and playable throughout the experiment.
5. Punchy-specific integration belongs in Punchy or a separate adapter package, never in the core.
6. Do not replace Punchy's terrain, cave, streaming, LOD, rendering, ECS, or player systems in the initial integration.
7. Do not redesign the simulation around Punchy's world model until a small adapter prototype proves what is necessary.
8. No integration work should require merging the two game repositories.

## Target repository layout

Create a repository provisionally named `anweather/chemistry-core` (confirm ownership and name before creation).

```text
chemistry-core/
├── package.json
├── README.md
├── CHANGELOG.md
├── LICENSE
├── Runtime/
│   ├── Chemistry.asmdef
│   └── canonical C# runtime sources
├── Tests~/
│   └── portable tests and fixtures
├── dotnet/
│   ├── Chemistry.Core.csproj
│   ├── Chemistry.Core.Tests.csproj
│   └── Directory.Build.props
├── samples/
├── docs/
│   ├── ARCHITECTURE.md
│   ├── API.md
│   ├── INTEGRATION.md
│   └── adr/
└── .github/workflows/ci.yml
```

The package should be both:
- A Unity Package Manager package.
- A standalone .NET library buildable and testable without Unity.

The Unity package and .NET project must compile the same source files. Never maintain independent copies.

Proposed package identifier: `com.anweather.chemistry-core`.

Use immutable Git tags such as `v0.1.0`, `v0.2.0`, and `v1.0.0`. Consumers should pin a tag or commit, never depend on a moving `main` branch. A local package path is acceptable during development but should not be the committed dependency for normal builds.

Illustrative dependency (not an existing package/release):

```json
{
  "dependencies": {
    "com.anweather.chemistry-core": "https://github.com/anweather/chemistry-core.git#v0.1.0"
  }
}
```

---

# 2. What belongs in the core?

Existing useful boundary:
- `unity/Assets/Prototype/Chemistry/Chemistry.asmdef` (`noEngineReferences: true`)
- `core/Game.Core/Game.Core.csproj`
- `core/Directory.Build.props`

The existing .NET project links C# sources from the Unity Chemistry directory instead of maintaining a second copy. Preserve this single-source principle after extraction.

## Initial extraction candidates

These are candidates to inspect and move while preserving namespaces and behavior:

| Area | Existing source files |
|---|---|
| World state | `World.cs`, `Cell.cs`, `WorldHash.cs` |
| Chunk storage | `Chunk.cs`, `ChunkCells.cs`, `World.Chunks.cs`, `ChunkStreaming.cs` |
| Simulation | `World.Sim.cs`, `World.React.cs`, `World.Spread.cs`, `World.StillWater.cs`, `World.Liquids.cs` |
| Spells | `World.Spells.cs`, `World.Combos.cs`, `Combos.cs`, `Effects.cs`, `SpellGroups.cs` |
| Tick and commands | `TickBatch.cs`, `JoinSnapshot.cs`, `RollingHash.cs` |
| Rules | `RuleSet.cs`, `RuleTables.cs`, `RuleSchema.cs`, `Rules.*.cs` |
| Persistence | `World.Save.cs`, `PlayerSave.cs`, `MapFile.cs`, `LandFile.cs` |
| Environment | `Sky.cs`, `World.Weather.cs`, `World.Time.cs` |
| Materials | `MaterialGroups.cs`, `TempMaterials.cs` |

This is a starting inventory, not permission to blindly move every file. The extraction agent must inspect dependencies and preserve test behavior.

## Explicit portability review

Some files are engine-independent but may encode assumptions specific to the original game:

- `TerrainGrid.cs`
- `CellHeightfield.cs`
- `CellPaint.cs`
- `CellContour.cs`
- `BiomeField.cs`
- `GeneratedMap.cs`
- `WorldPlan.*.cs`
- `Fauna.cs`
- `Buildings.cs`
- `Places.cs`
- `GameFolders.cs`

Classify every compiled source file as one of:

1. **Core domain:** portable rules or simulation behavior.
2. **Reusable world mathematics:** engine-free but potentially game-specific.
3. **Original-game feature:** useful to the original game, not necessarily universal.
4. **Host adapter:** belongs outside the core.

Favor compatibility over ambitious redesign in the first extraction. Split into smaller assemblies later only if there is a demonstrated benefit.

## Keep out of the initial runtime package

- MCP server and chat tooling.
- Unity editor-only builders and analyzers.
- Player input and Unity UI.
- Unity-specific rendering and particles.
- Punchy ECS systems.
- Punchy terrain and cave rendering.
- Game-specific networking implementations.

Important wrinkle: `Game.Core.csproj` currently links `Chemistry/Mcp/*.cs` and `Scripts/Playtest/Logic/*.cs` in addition to the main Chemistry sources. Do not lose these tools. Decide whether they become separate projects/packages and preserve their build/test coverage.

## Rules and content

The core should contain the rules engine and code that interprets rules. It should not silently assume every host uses the same game content.

Game-specific content such as:
- `rules/materials.json`
- `rules/reactions.json`
- `rules/spells.json`
- `rules/items.json`
- world maps, themes, buildings, and biome definitions

should remain owned by the consuming game unless explicitly designated shared content.

Both games may initially use compatible material and spell definitions, but should be able to diverge without forking the C# engine.

Create a small sample ruleset in the core repository for tests and examples. Do not make the core depend on the original repository's folder layout.

Review `GameFolders.cs`: it currently locates `rules/`, `world/`, and player data relative to a repository layout. The library should receive paths or content providers from its host rather than assuming Punchy has that layout.

---

# 3. Preserve the original game first

Before changing the original game:

1. Record the current `dev` commit as a baseline.
2. Run the current .NET build and tests.
3. Run Unity Chemistry tests.
4. Record test counts, golden hashes, and benchmark baselines.
5. Create a migration branch.
6. Extract canonical sources into `chemistry-core`.
7. Replace original-game local source with the package dependency.
8. Run the same checks again.

The original game must pass before Punchy starts using the package.

The original game should not retain a second copy under `unity/Assets/Prototype/Chemistry`. That directory should eventually contain only thin compatibility code, tests, or migration notes—not a second implementation.

If preserving history is practical, investigate a Git subtree split or equivalent. Do not let history migration block the first working package.

---

# 4. Integration architecture

The core owns simulation state and deterministic rules.

Each host owns its world representation, input, rendering, physics, networking, and host-specific persistence integration.

```text
                    chemistry-core
                  (one canonical source)
                          |
                 versioned Unity package
                    /             \\
                   /               \\
          Original game         Punchy fork
          -----------           -----------
          Unity view             Unity ECS
          NGO                    NetCode
          Existing world         TerrainSource
          Existing controls      Player controller
          Existing adapters      Streaming / LOD
                   \\               /
                    \\             /
                     host adapters
```

## World-model mismatch

Chemistry is a two-dimensional cellular simulation with 64×64 chunks.

Punchy has a continuous three-dimensional terrain and cave environment. Its `TerrainSource` is the world-wide source of truth for ground height, biome, rivers, water, and cave information. Its streaming system builds and renders chunks around the player.

These representations are not interchangeable.

Do not make the core understand Punchy's ECS. Do not make Punchy's terrain system depend on Chemistry's internal `Cell` representation.

Create a Punchy-side adapter with explicit responsibilities:

1. Convert Punchy world coordinates into Chemistry grid coordinates.
2. Sample terrain information from `TerrainSource`.
3. Create a bounded Chemistry simulation region.
4. Translate casts and other commands into core commands.
5. Observe changed cells or chunks.
6. Translate changes into Punchy-side visual/environmental updates.
7. Keep mapping stable and deterministic.

Start with a small outdoor surface patch. Map Chemistry's two grid axes onto Punchy's horizontal XZ plane and obtain surface height from Punchy terrain sampling. Cave-floor integration is a later milestone because caves require a different sampling and coordinate policy.

Do not pretend this solves arbitrary 3D simulation.

## Adapter API concept

This is conceptual, not a prescribed final API:

```csharp
public interface IHostWorldAdapter
{
    GroundSample SampleGround(WorldPosition position);
    void ApplyChanges(IReadOnlyList<CellChange> changes);
}
```

The core should not know what a Punchy world position means. The adapter owns mapping and translates into core-owned data types. Prefer explicit value types and commands over exposing mutable internal collections.

## Change reporting contract

The host needs to know:

- Which cells changed.
- Which chunks became dirty.
- Which tick produced the change.
- Whether changes affect terrain, material, liquid, gas, temperature, or effects.
- Whether a change is reversible or requires a snapshot.

Do not make Punchy poll every cell every frame. Use a change journal, changed-cell collection, or chunk snapshot API. Test it against the existing deterministic tick model before making it public.

---

# 5. Punchy systems to preserve

Keep these as the default Punchy implementations:

| Punchy system | Direction |
|---|---|
| `TerrainSource` | Preserve |
| Terrain baking and heightmaps | Preserve |
| `TerrainStreamingSystem` | Preserve |
| Chunk mesh generation and pooling | Preserve |
| LOD and far-ring behavior | Preserve |
| `CaveSystem` and cave baking | Preserve |
| Player movement and camera | Preserve |
| ECS architecture | Preserve |
| Existing multiplayer world sync | Preserve initially |
| Input System action asset | Extend carefully |

Useful inspected paths:
- `Assets/Game/Scripts/Terrain/TerrainSource.cs`
- `Assets/Game/Scripts/Terrain/TerrainStreamingSystem.cs`
- `Assets/Game/Scripts/Terrain/Caves/CaveSystem.cs`
- `Assets/Game/Scripts/Player/PlayerInputSystem.cs`
- `Assets/Game/Scripts/Net/World/WorldSync.cs`
- `Packages/manifest.json`

Punchy uses Unity Entities and NetCode 6.5.0. The original game uses Netcode for GameObjects 2.13.3. Do not unify those networking systems in the first integration.

The core remains independent of both. Each host has its own command transport and replication strategy.

---

# 6. Spells and controls

Bring the spell system over as a complete experience, but keep input and presentation outside the core.

## Core responsibilities

Preserve and test:
- Spell definitions and validation.
- Elements and combinations.
- Spell shapes and targeting.
- Charged casts.
- Deterministic spell application.
- Terraforming and material changes.
- Weather and time effects.
- Delayed casts and persistent effects.
- Whirlwinds and other simulation-owned effects.
- Command and tick behavior.

`World.Spells.cs` handles the cell side of spell casting. Its comments exclude pushes, damage, levitation, and carrying. Those behaviors need a separate audit; do not assume the spell file contains the entire spell experience.

## Host responsibilities

Each game owns:
- Input bindings.
- Hand gestures and animation.
- Camera aiming.
- Casting UI.
- Projectiles, particles, and sound.
- Player movement changes.
- Creature interactions.
- Physics-driven pushes and carrying.
- Networking transport.

## Punchy controls

Keep Punchy's movement scheme. Add spell actions rather than transplanting the original player controller.

Current Punchy Input System baseline:
- Move: left stick / WASD.
- Look: right stick / mouse.
- Jump: A / Space.
- Sprint: left stick press / Shift.
- Walk: right stick press / Ctrl.
- Crouch: B / C.
- Attack: X / mouse left.
- Interact: Y / E.
- Previous/Next: D-pad left/right / 1 and 2.

Original Chemistry controls include hand casting, charging, cancellation, spell-kit selection, and hand attunement.

Implement these as new actions and a casting state machine. Do not assume buttons are unused. Punchy's attack, interact, crouch, and spell cancellation require explicit conflict resolution.

The exact layout is an integration decision validated in-game, not a core-library concern.

---

# 7. Phased implementation plan

## Phase 0 — Baselines and safeguards

**Goal:** Make the experiment reversible before changing architecture.

Tasks:
- Record original-game commit.
- Record Punchy upstream commit.
- Create a fork and dedicated integration branch only after ownership and permission are agreed.
- Establish build/test baselines for both projects.
- Confirm core .NET target and Unity compatibility.
- Document dependencies and rules-loading assumptions.

Acceptance:
- Both original repositories remain check-out-able at baseline commits.
- Baseline tests are recorded.
- No upstream Punchy branch is modified.
- No original-game gameplay code is removed.

## Phase 1 — Extract canonical core

**Goal:** Create one independently buildable package.

Tasks:
- Create `chemistry-core`.
- Move canonical C# source into it.
- Preserve namespaces and public API initially.
- Create Unity package manifest and runtime assembly definition.
- Create standalone .NET project compiling the same sources.
- Move/adapt portable tests and fixtures.
- Separate MCP and playtest tooling where appropriate.
- Document external content and path requirements.

Acceptance:
- `dotnet build` succeeds.
- `dotnet test` succeeds.
- Unity imports the package in a clean test project.
- Package has no UnityEngine or Punchy dependencies.
- Original game consumes package without duplicate source.
- Deterministic test outputs match baseline.

Do not combine this phase with a major simulation refactor.

## Phase 2 — Convert original game into package consumer

**Goal:** Prove extraction did not damage the existing game.

Tasks:
- Add pinned package dependency.
- Remove duplicate local runtime source.
- Preserve original game's view, input, networking, and content.
- Adapt paths/rules loading that assumed old repository layout.
- Keep existing tests running against package.

Acceptance:
- Original game builds and runs.
- Existing Chemistry tests pass.
- Spell behavior and golden hashes remain unchanged.
- Original game can update package version independently.
- Clean checkout restores package without special local setup.

This is the first major rollback checkpoint.

## Phase 3 — Establish Punchy integration without changing gameplay

**Goal:** Import and initialize core inside Punchy.

Tasks:
- Create Punchy fork integration branch.
- Add pinned Chemistry package.
- Add required Newtonsoft dependency if runtime uses it; Punchy's inspected manifest did not list it.
- Create Punchy-side integration assembly.
- Initialize a small Chemistry world from Punchy-provided configuration.
- Log core tick and chunk information.
- Do not alter terrain or player movement yet.

Acceptance:
- Punchy builds with package installed.
- Chemistry initializes and advances deterministically.
- No core code references Punchy types.
- Removing integration assembly restores original Punchy behavior.

## Phase 4 — First real world interaction

**Goal:** Prove the world models can communicate.

First demonstration:
1. Spawn player in existing Punchy world.
2. Identify bounded surface patch.
3. Initialize Chemistry grid for that patch.
4. Cast charged fire spell.
5. Apply resulting changes to simulation.
6. Observe fire and heat spreading through compatible materials.
7. Reflect changed material/effect in Punchy scene.

Do not begin with an entire island or cave network.

Acceptance:
- Coordinate mapping is documented and repeatable.
- Cast produces expected core changes.
- Changed cells are reported without scanning entire world.
- Punchy displays the result.
- Core test reproduces simulation outside Unity.

## Phase 5 — Expand bridge

Only after first demonstration:
- More material types.
- Water and gas interactions.
- Terraforming.
- Terrain visual updates.
- Cave-floor mapping.
- Chunk activation/deactivation coordination.
- Persistence of modified areas.
- Performance profiling.

Do not assume Punchy streamed chunks and Chemistry active chunks should be identical. Define simulation activity relative to render visibility, player proximity, and persistence.

## Phase 6 — Spells, controls, multiplayer

Tasks:
- Add casting state machine.
- Add spell selection and hand attunement.
- Integrate charged casts and cancellation.
- Add visual/audio effects.
- Adapt pushes, damage, levitation, and carrying to Punchy entities.
- Define host-authoritative command submission.
- Define deterministic tick ordering and replication.
- Handle late joiners and modified chunk state.

Acceptance:
- Two clients agree on core tick results.
- Host and clients agree on command ordering.
- Late joiner receives correct modified world state.
- Visual effects do not become authoritative simulation state.
- Punchy's own world synchronization continues to work.

---

# 8. Reversibility safeguards

These are mandatory.

## A. Pin versions, never track a moving branch

Both games use released core tags. Updating core is a deliberate dependency update. If a new core version breaks Punchy, revert the package version in Punchy; the original game remains on its known-good version.

## B. Keep Punchy-specific code outside core

No Punchy namespaces, Unity Entities types, ECS systems, or terrain classes in core. If Punchy needs a new behavior, decide whether it is a generally useful simulation capability. If yes, add a host-neutral API. If it is only rendering/world integration, keep it in the adapter.

## C. Maintain three consumer checks

Every core change should be checked against:
- Core .NET tests.
- Original game's Unity project.
- Punchy integration project's Unity build.

Core compilation alone is insufficient.

## D. Use compatibility tests

Maintain canonical scenarios:
- Fire spreads through a known material arrangement.
- Water flows and reacts with heat.
- Charged spell affects expected cells.
- Terraform command produces expected height changes.
- Chunk deactivation freezes simulation as expected.
- Two peers given same commands produce same hash.

## E. Keep integration behind an explicit boundary

Punchy integration should be removable without editing core. Do not replace `TerrainSource` or make terrain streaming depend on Chemistry.

## F. Keep independent histories and branches

Recommended:
- Core: `main`, feature branches, tagged releases.
- Original game: `dev`, package-update branches.
- Punchy fork: upstream-tracking `main` plus `chemistry-integration` branch.

Never make the Punchy fork the only copy of integration work.

## G. Rollback checkpoints

- End of Phase 2: original game fully works against package.
- End of Phase 3: Punchy works with integration disabled.
- At every later point: Punchy experiment can be stopped without rewriting or extracting Chemistry again.

---

# 9. Licensing and collaboration

Before publishing/distributing the combined game:
- Agree with friend on permission to fork and modify Punchy.
- Agree on ownership of new integration code.
- Agree on distribution rights.
- Add an appropriate license to the new core repository.
- Preserve notices from dependencies and upstream code.

The inspected Punchy repository had no detected license at the time of inspection. Public visibility alone does not establish permission to redistribute or relicense code. Resolve this with the repository owner before treating the fork as a distributable product.

---

# 10. Suggested coding-agent assignments

Use separate agents with narrow ownership. Do not let multiple agents modify the same architecture boundary simultaneously.

| Agent | Assignment | Deliverable |
|---|---|---|
| Architecture agent | Audit source dependencies and classify files | File-by-file extraction map |
| Core extraction agent | Create package structure and move canonical source | Buildable `chemistry-core` |
| Test agent | Preserve and expand deterministic tests | Test suite and baseline report |
| Original-game agent | Convert original Unity project to package consumer | Working original-game build |
| Punchy integration agent | Add package and initialization adapter | Punchy builds with core loaded |
| World bridge agent | Prototype coordinate mapping and changed-cell application | Small fire/material interaction demo |
| Integration reviewer | Review boundaries, dependencies, rollback safety | Acceptance report |

Do not start Punchy world bridge until core package and original-game consumer are proven.

---

# 11. First implementation tickets

Create these tickets in order.

### Ticket 1 — Audit core source dependencies
- Enumerate every file compiled by `Game.Core.csproj`.
- Identify Unity APIs, filesystem assumptions, external dependencies, and game-specific types.
- Produce keep/move/split/adapt table.
- Do not move files yet.

### Ticket 2 — Create chemistry-core repository
- Create package and .NET project skeleton.
- Establish CI.
- Do not change simulation behavior.

### Ticket 3 — Move canonical source
- Move source once.
- Update original game's build to reference new package/location.
- Remove old duplicate source only after package consumer builds.

### Ticket 4 — Port portable tests
- Preserve deterministic outputs.
- Keep tests that genuinely require Unity in Unity test assemblies.

### Ticket 5 — Release first package version
- Publish immutable development tag.
- Record commit SHA and compatibility.

### Ticket 6 — Convert original game
- Pin package.
- Build and run original game.
- Confirm test parity.

### Ticket 7 — Integrate package into Punchy
- Add package and dependencies.
- Add isolated initialization component.
- Do not modify terrain streaming.

### Ticket 8 — Prototype world bridge
- One small grid patch.
- One charged fire spell.
- One visible material interaction.
- No multiplayer yet.

### Ticket 9 — Go/no-go review
- Is core genuinely reusable?
- Is mapping acceptable?
- Is integration sufficiently isolated?
- Does Punchy provide enough benefit to continue?

---

# 12. Go/no-go decision

**Continue with Punchy if:**
- Package is shared cleanly.
- Adapter is small and understandable.
- Terrain and simulation coexist without competing ownership.
- Visual result is compelling.
- Performance is acceptable.
- Multiplayer path looks feasible.

**Pause or abandon Punchy integration if:**
- Chemistry needs major Punchy-specific changes.
- 2D/3D mapping is too restrictive.
- Simulation and terrain cannot agree on world ownership.
- Networking requires coupling core to Punchy NetCode.
- Adapter becomes a second world engine.
- Integration costs more than terrain/streaming benefits justify.

If stopping, keep core repository and original game package consumer. Remove or archive Punchy integration branch. Original game remains a first-class project.

## Definition of success

The first success is not a complete combined game. It is:
- One canonical Chemistry codebase.
- One independently buildable and testable core package.
- Two Unity projects consuming the same pinned package.
- Original game still working.
- Punchy still working with integration disabled.
- One small, visible Chemistry interaction working inside Punchy's world.
- No core dependency on either host.

This lets us choose the better game foundation later without losing simulation work or duplicating it.
