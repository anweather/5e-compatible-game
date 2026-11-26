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
