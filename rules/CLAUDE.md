# Rule tables

The single source of truth for what things do, loaded by both the C# core
and the TS sandbox (and live-reloaded by the Unity Editor). `README.md` here
documents every file and the full condition and effect vocabulary of
`reactions.json`.

- **Order is meaning** in `reactions.json`: rules for an element run top to
  bottom, and `stop` ends the list for that square.
- A new key or value usually needs code in both checkers
  (`Rules.Check.cs` / `Rules.CheckItems.cs` and `validateRules` in
  `sandbox/src/rules.ts`), and anything new the interpreter must do goes in
  both `World.React.cs` and `sandbox/src/react.ts`.
- After any change run both suites: `dotnet test core` and, in `sandbox/`,
  `npm test`. Tests read these files directly, and the examples in
  `docs/MODDING.md` must still pass the checks.
- `mods/` holds example live mods (`{"title", "idea", "patch"}`), not base
  rules.
- Adding a material, spell or item also needs Unity-side work (ids in
  `RuleIds.cs`, looks and builders); see "Where new things go" in
  `docs/ARCHITECTURE.md`.
