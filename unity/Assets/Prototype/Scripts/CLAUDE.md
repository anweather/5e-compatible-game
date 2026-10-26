# The view (`Scripts/`), networking (`Net/`) and Editor tools (`Editor/`)

`docs/ARCHITECTURE.md` sections 3 to 5 map these files, and its "Where new
things go" table is the recipe for adding a spell, material, prop or item.

## Dependency rules

- The view reads the core and sends it commands; the core never knows the
  view. Nothing in the view decides chemistry: anything a prop or item does
  to cells goes through `ChemEffects.Request` as an `EffectCommand` (applied
  at once solo, stamped into the host's next `NetChem` tick batch online).
- Networking may use the view; the view reaches networking only through its
  doors (`NetChem`, `NetProp`, `NetItem`, `NetWizard`), and solo none of
  them are used.
- Editor code may use everything; nothing may use editor code.
- Asset Store packs are optional at runtime: Starter Assets parts are
  reached by name and reflection (`WizardRig`, `WizardPuppet`,
  `SurfaceEffects`), and every pack prefab has a fallback.

## Conventions

- One MonoBehaviour per file, named like the file; a big component may be
  split into `partial` files by responsibility (`ChemGrid.*.cs`).
- A comment at the top of every file says what it's for, in plain English.
- Rule ids are referenced only through `RuleIds.cs` (`SpellIds`, `ItemIds`).
- Runtime singletons that every scene needs can start themselves with
  `[RuntimeInitializeOnLoadMethod]` (`FpsProbe`, `PauseMenu`) so no builder
  or scene change is needed.
- HUD and panels are IMGUI (`OnGUI`); while a panel is open it pauses the
  wizard's controls and frees the cursor the way `ModsPanel` does.
- This folder has no `.asmdef`: it compiles into `Assembly-CSharp`, so CI
  can't build it. Its checks are the Unity console's analyzer warnings
  (don't add any; the current count is the baseline in `docs/QUALITY.md`)
  and the probe.
- After changing a builder (`Editor/*Builder.cs`), rerun its menu item;
  in-scene `NetworkObject`s get their ids from the rebuild.
