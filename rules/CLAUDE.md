# Rule tables

The single source of truth for what things do, loaded by the C# core (and
live-reloaded by the Unity Editor). The TS sandbox is frozen on its own
copy (`sandbox/rules-frozen/`, J-042), so a change here never touches it. `README.md` here
documents every file and the full condition and effect vocabulary of
`reactions.json`.

- **Order is meaning** in `reactions.json`: rules for an element run top to
  bottom, and `stop` ends the list for that square.
- A new key or value usually needs code in the checkers (`Rules.Check.cs`
  / `Rules.CheckItems.cs`), and anything new the interpreter must do goes
  in `World.React.cs`.
- After any change run `dotnet test core`. Tests read these files
  directly, and the examples in
  `docs/MODDING.md` must still pass the checks.
- `mods/` holds example live mods (`{"title", "idea", "patch"}`), not base
  rules.
- Adding a material, spell or item also needs Unity-side work (ids in
  `RuleIds.cs`, looks and builders); see "Where new things go" in
  `docs/ARCHITECTURE.md`.
- **At most three spells per element** on the bar (Andrew, 2026-10-27;
  `RadialTests.NoElementHasMoreThanThreeSpells`). A spell past three stays
  in `spells.json` with `"radial": false`, so mods, tests and demos can
  still cast it by id. A new element goes in `groups.json` together with
  its spells (a test pins the group list).
