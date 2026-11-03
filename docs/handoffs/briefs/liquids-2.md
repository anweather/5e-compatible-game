Brief from the PM: the rest of P1 liquids (`hot`, `freezes`, `sets`). Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `liquids-2` off your `origin/liquids` (ac5041e5, waiting on the PC's compile and view) merged with the latest `origin/dev`. If `dependents-all` has landed by then, make liquids count as things a mod adds (you listed that).

Per `docs/design/primitives.md` §3 "P1 in detail": `hot` {for} (a liquid that burns what it touches for a while, then cools), `freezes` (into a terrain or cover at cold), `sets` (hardens into a terrain over time, e.g. a mod's molten rock or quick-setting mortar). All through the material verbs, no rule for one liquid. Tests with new goldens: a mod's lava-like liquid that sets into rock and burns grass on the way; a mod's syrup that freezes into a cover. Two peers with matching checksums. Existing goldens unchanged; base lava stays as is (J-062).

Constraints: deterministic core, nothing under `Scripts/`, new `.cs` under `unity/` gets its `.meta`, new methods under S3776 15 by your `/tmp` measure. Update `docs/RULES_MCP.md`. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, what's built, the results, gates, time.
