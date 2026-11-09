Needs: core
Area: unity/Assets/Prototype/Chemistry/ (World.cs ApplyRules/Sanitize/TerrainOf, the chunk switch, the snapshot and hashes that carry chunk state), its tests, docs/GLOSSARY.md (new entries only), docs/WORLD_STREAMING.md (ask the lead before editing; put the text in your report otherwise)
Branch: rule-switch-by-chunk

# A rule switch touches active cells now, and other chunks when they activate (J-086, #190)

Andrew, 2026-11-09: "Rule switch should affect active chunks (active cells) and then all future chunks." A switch's
cost must follow the active region, never the map's size.

Today (`World.ApplyRules`, after `perf-rule-switch-181`, ece6d649): the terrain cache is stamped with the rules
(`Cell.DefRules`, `rulesStamp`), so most switches touch no cell. But when the new rules could leave a cell unfit
(`World.Unfits`: a terrain, cover or gas dropped, fewer liquids, a new height range), it still runs
`foreach (var c in Cells) Sanitize(c)` over every cell the world holds: 2,985,984 on the nine (500-985 ms),
about 40 million on the 12 x 10.

## What to build

- On a switch that unfits, sanitize only the cells of **active** chunks, on the switch's tick.
- Each chunk remembers which rules it was last made to fit (an integer in the chunk's state, D9). A chunk that
  **activates** with an older number is sanitized then, before its first tick. A chunk generated or loaded later is
  made under the rules of that moment. Every peer activates the same chunk on the same tick, so this stays in
  lockstep (D4); prove it.
- Reading an inactive chunk that hasn't been made to fit yet must be safe and the same on every peer: the view
  draws inactive chunks (the drawn and known tiers), and `TerrainOf` and the other accessors must give a cell that
  would be sanitized the answer sanitizing would give, without writing it. No throw, no null terrain.
- The number is shared state: it goes wherever a chunk's state goes (the late joiner's snapshot or replay, saves,
  the checksum or hash that compares peers), so a late joiner sanitizes the same chunks on the same ticks as the
  host.
- Meaning changes slightly, on purpose: a chunk inactive across two switches is made to fit the rules current when
  it activates, not each set in turn (a terrain dropped then brought back survives in a chunk nobody was near).
  Say so in the GLOSSARY entry and test it.

## Check

- Tests: a switch that drops a terrain changes active chunks' cells on that tick and no inactive chunk's; the
  inactive chunk changes on activation and equals what the old full pass gave for a single switch; two peers and a
  late joiner agree (the existing late-join replay tests, with a switch before the join); reading an unfit inactive
  cell gives the fallback. Every fixture needs its `[Category]` (Unit or Simulation); no `Does.Contain(object)`.
- Goldens (`CellHash`, FNV) unchanged where no switch happens. If a hash's layout must change to carry the
  number, say so plainly and update the goldens in the same commit.
- Measure: `ApplyRules` for a terrain-dropping switch on the nine and on the 12 x 10 plan's size, before and after
  (under dotnet), with one character's reach active.

## Done

`dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes` and the
duplicate gate pass. Push the branch; it has Unity C#, so the PC compiles it and the lead lands it. Report the
numbers, what the hash or snapshot change was (if any), and anything in the view that reads cells in a way the
fallback doesn't cover.
