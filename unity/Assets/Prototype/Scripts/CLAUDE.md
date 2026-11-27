# The view (`Scripts/`), networking (`Net/`) and Editor tools (`Editor/`)

`docs/ARCHITECTURE.md` sections 3 to 5 map these files, and its "Where new
things go" table is the recipe for adding a spell, material, prop or item.

## Dependency rules

- The view reads the core and sends it commands; the core never knows the
  view. Nothing in the view decides chemistry: anything a prop or item does
  to cells goes through `ChemEffects.Request` as an `EffectCommand` (applied
  at once solo, stamped into the host's next `NetChem` tick batch online).
- Networking may use the view; the view reaches networking only through its
  doors (`NetChem`, `NetProp`, `NetItem`, `NetWizard`, `NetCourt`), and solo none of
  them are used.
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
- Assemblies: this folder and `ThirdParty/Punchy` compile into
  `Prototype.Game`; `Editor/`, `Perf/Editor/` and `Punchy/Terrain/Editor/`
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
