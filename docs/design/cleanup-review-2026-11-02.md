# Code base cleanup: ranked review (2026-11-02)

A read-only review for deep modules: a small interface hiding real complexity. Ranked by how much
future work gets cheaper per unit of effort. Nothing is changed yet; the Editor and tests weren't run.
Inputs: `docs/handoffs/cleanup-inputs-2026-11-02.md`, `docs/design/second-map-2026-11-02.md`.

Spot-checked by the PM: the `FindRulesDir` copies, the 41 `ChemGrid` lookups (`FindObjectOfType` and
friends), 142 files naming `ChemGrid`, the Fireline list in `docs/ARCHITECTURE.md`. Probes are 103 files
on dev. The rest is as the review reported it. `dotnet build core` gave 80 distinct S3776 warnings.

| # | Module | What it fixes | Effort | Risk | Branches |
|---|---|---|---|---|---|
| 1 | `GameFolders` in `Chem` (`Rules`, `World`, `PlayerData`, `Mods`), roots injected by the view; a plain `ClaudeFolder` | `FindRulesDir` written three times (`ChemGrid.cs:300`, `Workbench.cs:51`, a test helper); `RuleSwitcher.cs:35` reaching a MonoBehaviour's static | Hours to a day | None for determinism | One |
| 2 | `CommandChannel<TIntent>`: `Request`, `Stamp(tick)`, `Deliver(tick)`, `HistoryForJoiner`, a compaction hook for strikes | Combos, strikes, defense and dragon each repeat the same set in `NetChem.cs:393-486`; 974 lines, 32 changes since October 1 | Days | High: wire ordering; needs online and room probes | One channel at a time |
| 3 | `ClaudeRun.Explain(failure, asked)` returning text and a `modelRefused` flag | `ClaudeChat.cs:127` runs `ModelRefused` then `Pointed`, which repeats the check (`ClaudeRun.cs:161-173`) | Hours | None | One, follows 1 |
| 4 | A Probes assembly with `internal` and `InternalsVisibleTo` | 103 `Probe*.cs` in `Scripts/` compile into players and force public API only they use (`NetSession.Host`/`Join`) | Days | Compile order; needs the Editor. Must stay available in test players: a dev define, not Editor-only | After 5 |
| 5 | A narrow `ChemGrid.Current` (`World`, `Rules`, `Request(EffectCommand)`) | 41 `ChemGrid` lookups by search, 142 files naming it | Days, then a week or more | Low, view only | Lookups only |
| 6 | An immutable `MapPlan` through the builders, `MapId` in the room and late-join history | `ValleyShape`/`ValleyTerrain` static plan fields in 12 Editor files; no map identity on the wire | A week or more | High: checksum, handshake, goldens; bakes aren't repeatable, so no diffing | Plan first, wire later. Andrew's call on shipped maps |
| 7 | One `RuleSchema` per table, read by validators and the MCP | Field and verb lists copied in `RuleFields.cs`, `ModValidator`, `Rules.Check*`, `Workbench.*` | A week or more | Medium | One per table |
| 8 | Split `World.React.cs:324` (complexity 80) and `:172`, `World.Terraform.cs:151`, `World.Buildings.cs:231` | Line-by-line ports | Days | Medium: `CellHash` goldens catch it | One per method |
| 9 | Delete the old Fireline scene | Dead greybox code (`docs/ARCHITECTURE.md:158`). Check `FireBolt`'s 32 mentions first: maybe a name clash | Hours | None for determinism | One |

## Leave alone

- The 14 `ChemGrid` partials and the 40+ `World.*.cs` partials: organised by topic, mostly data.
- The two demo directors, `RuleIds.cs` (the repetition gives compile-time safety), the large tests,
  the probe registry text, the sandbox.
- The Mac's voice and play-log notes: bugs, not structure. They need issues of their own.

## Not read

`Playtest/`, `Perf/`, most probe bodies, the Editor builders other than by search, the bodies of
`World.*`, `Rules.Check*` and `Workbench.*`, `PauseMenu`, `WizardOutfit`, the `Mcp` server code.
