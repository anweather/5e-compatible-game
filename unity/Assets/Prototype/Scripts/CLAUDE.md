# The view (`Scripts/`), networking (`Net/`, `Online/`) and Editor tools (`Editor/`)

`docs/ARCHITECTURE.md` sections 3 to 5 map these files, and its "Where new
things go" table is the recipe for adding a spell, material, prop or item.

## Dependency rules

- The view reads the core and sends it commands; the core never knows the
  view. Nothing in the view decides chemistry: anything a prop or item does
  to cells goes through `ChemEffects.Request` as an `EffectCommand` (applied
  at once solo, stamped into the host's next `NetChem` tick batch online).
- Networking may use the view; the view reaches networking only through its
  doors, and solo none of them are used. The grid's peers (solo, host or client;
  this peer's names; caught up; the intents to send) are the `IPeers` port:
  ask `Peers.Of(grid)`, never `NetChem` (its adapters are `SoloPeers` and
  `NetChem`; `PeersTests`). `Net/` is the session, the `Prototype.Net` assembly,
  and the game can't reference it: a game file that names `NetChem`,
  `NetSession` or `NgoLockstepTransport` doesn't compile. Needing one means a
  member on `IPeers` (with `SoloPeers`' answer). A thing's own network half is
  the game's, a door by name in `Online/` (`NetProp`, `NetItem`, `NetWizard`,
  `NetCourt`); it reaches the session through `IPeers` too. Keep a send where it
  was and in the same order when routing it: the port changes who you ask, not when.
- Editor code may use everything; nothing may use editor code.
- Asset Store packs are optional at runtime: Starter Assets parts are
  reached by name and reflection (`WizardRig`, `WizardPuppet`,
  `SurfaceEffects`), and every pack prefab has a fallback.

## Conventions

- One MonoBehaviour per file, named like the file; a big component may be
  split into `partial` files by responsibility (`ChemGrid.*.cs`).
- A comment at the top of every file says what it's for, in plain English.
- Reach the grid in play through `ChemGrid.Current` (`World`, `Rules`,
  `Request`, or `.Grid` to draw on it), never by searching the scene for a
  `ChemGrid`. It is null outside Play mode, so Editor code and EditMode
  tests that open a scene still find the grid in it.
- Don't search the scene (`FindAnyObjectByType`, `FindObjectsByType`) for a type that has a registry: ask
  it (`Registry.cs`, `RegistryTests`). A type searched for in more than a one-off startup call gets one. A type joins with a static
  `Registry<T>` field, `registry.Join(this)` first in `Awake` and `registry.Leave(this)` first in `OnDestroy`
  (a `NetworkBehaviour` overrides `OnDestroy` and calls the base), and hands out `InPlay` (one) or `All` (a new
  array; `AllByInstanceId` for a caller that sorted by instance id): only those on active GameObjects, a disabled
  one included, as Find* does. A caller that asks what's near a place asks by chunk: the type keeps its chunk with
  `registry.Place` every physics step and callers ask `Near(chunk, chunk)` and test positions themselves
  (`PhysicsProp`, J-086). Registries empty as Play starts (`Registries.ForgetAll`; domain reload is off, J-023),
  so nothing needs clearing by hand; add the type's case to `RegistryTests.Types`. Edit mode runs no Awake: code
  that also runs in the Editor (`[ExecuteAlways]`, builders, `LandBaker`) keeps its search (`Village`'s sky).
- Rule ids are referenced only through `Plans/RuleIds.cs` (`SpellIds`, `ItemIds`).
- Runtime singletons that every scene needs can start themselves with
  `[RuntimeInitializeOnLoadMethod]` (`FpsProbe`, `PauseMenu`) so no builder
  or scene change is needed.
- HUD and panels are IMGUI (`OnGUI`); while a panel is open it pauses the
  wizard's controls and frees the cursor the way `ModsPanel` does.
- Never write a controller button's name ("A", "LB", "○") into a hint:
  use `PadGlyphs` (`Text("{South} choose")`, `Label(PadButton.East)`),
  which names it as printed on the pad used last (Xbox, PlayStation,
  Switch, Steam).
- Assemblies: this folder (with `Chat/`, the Claude, rules MCP and speech
  files, and `Online/`) and `ThirdParty/Punchy` compile into
  `Prototype.Game`; `Net/` into `Prototype.Net` (it references the game; the
  game never references it; the Editor tools, probes and tests do); `Editor/`, `Perf/Editor/` and `Punchy/Terrain/Editor/`
  into the Editor-only `Prototype.Game.Editor` (`Editor/AssetPacks/` keeps
  `Prototype.AssetPacks.Editor`; `Editor/Playtest/` holds `PlaytestBuild`);
  `Editor/Tests/` and `Playtest/Editor/` into the test assembly
  `Prototype.Game.Tests`. The probes are apart: `Probes/` is
  `Prototype.Probes` (it references the game; the game never references it),
  `Probes/Editor/` the Editor-only `Prototype.Probes.Editor` (the CLI),
  `Probes/Editor/Tests/` `Prototype.Probes.Tests`. The view's plans are apart too: `Plans/` is
  `Prototype.Plans` (plain C# a MonoBehaviour draws from: budgets, tables, menu logic, mesh arrays; it references
  only `Chemistry`, `Chem.Land` and UnityEngine's maths, and the game references it), `Plans/Tests/`
  `Prototype.Plans.Tests`, which `core/Prototype.Plans.Tests` also runs under dotnet with the `core/UnityMath` shim
  (`docs/design/architecture-review.md` step 6). A file goes in `Plans/` only if it names no game type and no engine
  API past `Vector2`/`Vector3`, `Mathf`, `Color`/`Color32` and `Debug.Log` (grow the shim for one more, never for a
  `Mesh` or a MonoBehaviour): keep the `Mesh` call in the game, as `FarMeshesMesh.ToMesh` does for `FarMeshes`. A
  new folder named `Editor` here joins an Editor-only assembly with an
  `.asmref`, or its code goes into player builds. A new package the code
  uses goes into the asmdef's references. CI still can't build these
  (only the Editor does). Its checks are the Unity console's analyzer warnings
  (don't add any; the current count is the baseline in `docs/QUALITY.md`)
  and the probe.
- There are two wizards: the solo one in the yard and the `NetWizard`
  prefab for online play. Give a wizard a new part only in
  `ChemYardBuilder.AddWizardParts`, which both are built from;
  `WizardPartsTests` fails if their parts ever differ.
- After changing a builder (`Editor/*Builder.cs`), rerun its menu item.
  Props and items aren't placed in the scene: `ThingSpawner` spawns them
  from `world/things.json` per chunk, with stable ids (`ThingId`: chunk +
  index), from prefabs the builders make in `Generated/Things/`.
