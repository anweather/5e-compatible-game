# Feature speed: the baseline before the decoupling (2026-11-26)

Andrew asked for a "before" of the whole development cycle so the decoupling plan
(`docs/design/architecture-review.md`) can show whether it made feature work faster. This is it, taken from the
cost trailers every commit carries (`tools/workers/commit-cost.mjs`: `Worker-*`, `Host-*`, `Coordinator-*`).

**Cut-off:** dev at `6f86730c` (2026-11-25), the last commit before step 1 of the plan landed (`90e4a0b8`).

## Per week, features merged into dev

A feature is a merge on dev's first-parent line (or a straight commit carrying cost trailers), as
`tools/telemetry/feature-costs.mjs` counts it. Features with no code lines and the one 361.80M-ite outlier
(`branch`, 2026-11-23) are left out. ite = input-token equivalents (new input + 1.25 × cache write + 0.1 × cache read +
5 × output).

| Week (Mon) | Features | Median agent minutes per feature | Median cost per feature (k ite) | Median ite per code line | Failed tool calls |
|---|---|---|---|---|---|
| 2026-10-27 | 120 | 22 | 1,111 | 3,264 | 2.8% |
| 2026-11-03 | 126 | 54 | 1,934 | 7,139 | 2.2% |
| 2026-11-10 | 54 | 65 | 2,375 | 6,712 | 1.9% |
| 2026-11-17 | 11 | 52 | 1,698 | 7,823 | 2.1% |
| 2026-11-24 | 21 | 43 | 2,264 | 11,481 | 1.8% |

What it says: from the first week after 0.1.2 to the last week before the plan, a feature took about **2–3× the
agent time** (22 → 43–65 minutes, median) and **3.5× the cost per line of code** (3.3k → 11.5k ite).
Over the whole span, coordination (the coordinator's merge lines) was 37% of all spend.

## Landing gates

`tools/workers/gates.sh` on the full suite (cloud, 4 cores, 2026-11-26): **7 m 21 s** for any change, docs
included, plus up to 3 reruns when dev moved during the gates. `dotnet test core` alone: 334 s (one
`Game.Core.Tests`, before step 2).

After steps 1–3 (for comparison as the plan goes on): a docs or tools change 1 m 00 s in the cloud (48 s on the
Mac); a Land test change 6 m 07 s; a Runtime change 7 m 20 s. Step 5 is meant to bring a Land change down.

## Taking the "after"

```sh
node tools/telemetry/feature-costs.mjs --ref origin/dev --since <date the plan's last step landed> --csv after.csv
```

Then the same weekly medians from the CSV (columns `date`, `code`, `ite`, `minutes`, `tools`, `failed`), with the
same exclusions (no code lines; a single feature over 100M ite). Compare like with like: a week of feature work, not
a week of refactoring. Gate times: `time tools/workers/gates.sh <log>` with `GATES_FILES` set to a docs file, a
Land test, a Runtime Land file and a Sim file.

Caveats: agent minutes are wall time of the sessions between commits, so a session left waiting counts; the mix
of work changed (week 1 had many small UX passes, later weeks larger scale work), which by itself raises the
per-feature numbers. Cost per code line is the steadier measure.

## The Unity loop and the gates on the Mac: before step 10's assemblies (2026-11-27)

Andrew asked for a before and after of the developer experience and the test times for each kind of feature, around
step 10 (Prototype.Game into feature assemblies). All on the Mac (the M-series dev box), the Editor open on the main
checkout and otherwise idle, dev at `7dc22b48` (seams 1-4 in, the assemblies not yet split).

**Save to a ready Editor** (`tools/telemetry/unity-loop.sh`: a trailing newline added and taken away, then a new public
type added and taken away; seconds from the save until the Editor has compiled and reloaded; 4 body samples and 2 API
samples a file):

| File (kind of feature) | Body edit | API edit | Assemblies an API edit rebuilds |
|---|---|---|---|
| `ChemGrid.cs` (the grid) | 12.1-19.1 s (12.5 typical) | 14.4-14.9 s | 9: the game, its Editor and tests, Net, the probes (3), Assembly-CSharp (2) |
| `HoldShow.cs` (a game mode's view) | 10.6-13.3 s | 14.3-14.6 s | the same 9 |
| `SpellCaster.cs` (the wizard) | 12.2-13.2 s | 13.3-14.5 s | the same 9 |
| `ValleyFauna.cs` (fauna) | 11.6-13.8 s | 12.8-15.1 s | the same 9 |
| `ControlsMenu.cs` (a menu) | 12.7-13.6 s | 14.0-14.7 s | the same 9 |
| `Chat/GameChat.cs` (chat) | 11.3-13.4 s | 13.2-14.7 s | the same 9 |
| `Probes/ProbeRoomProgress.cs` (a probe) | 8.0-10.4 s | 9.0-10.1 s | 3: the probes' own |
| `Plans/TreeBudget.cs` (a plan) | 7.9-10.1 s | 13.7-15.2 s | 9: Plans and everything above it |

A body edit rebuilds only its own assembly (Unity skips dependents when the public surface is unchanged); the domain
reload is 3.3-4.4 s of every one, and the floor for any edit is about 8 s. **Play mode:** 4.8-4.9 s to enter (7.2 s the
first time after a compile) to a running grid 30 frames in, 1.1 s to leave. **EditMode suite** (`tools/editmode.sh`,
567 tests): 65 s.

**Landing gates** (`time tools/workers/gates.sh`, the Mac, nothing else running):

| Change | Before the plan (`6f86730c`, every gate for every change) | Now (`7dc22b48`, gates by module) |
|---|---|---|
| A docs file | 206 s | 75 s |
| A Land runtime file | 206 s | 86 s |
| A Land test file | 206 s | 85 s |
| A Play (game mode) file in the core | 206 s | 181 s |
| A Sim file (`World.cs`) | 206 s | 182 s |
| A view file (`HoldShow.cs`) | 206 s | 74 s (+ the Unity checks above) |

(The cloud's 4 cores took 7 m 21 s for the same "before"; the Mac is about twice as fast.) The 75 s floor is the build,
format and the workers' tests, which every change runs.
