Brief from the PM: primitives step 4, P1 liquids, core only. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `liquids` off the latest `origin/dev` (products and temporary materials are on it; `cover-spread` and `building-gaps` wait on a compile, don't base on them).

Build P1 of `docs/design/primitives.md` §3 ("P1 in detail") and §5 step 4, core half: `Water` stays the amount, a new `int Liquid` is the kind, an index into a `liquids` table where 0 is water, so an untouched world is exactly today's (every existing golden holds; say so plainly if one can't). The `liquids` table, `Flow`'s rule by `thick`/`heap`, `touch`, and the flags in `Soak`/`Float`/`Cool`/rain. Then the interning step (a mod's liquid by id string on the wire, like temporary materials). J-062: base lava stays as it is; a mod's liquid lava comes first. A mod liquid is temporary like any mod material.

This is the big one (M to L). If it won't fit one run, land a clean first half (the kind, the table, flow by thickness, goldens) and list the rest; don't leave it half-wired. Tests: existing goldens unchanged, new goldens for a mod's honey spill (thick, slow, heaps) and a red paint that runs, two peers with matching checksums. List what the view needs (the coloured water mesh). Also fix the stale comment in `World.Weather.cs` that says the view doesn't read the snow names (it does now).

Constraints: deterministic core, integer state, nothing under `Scripts/`, new `.cs` under `unity/` gets its `.meta` (new GUID), every new method under S3776 15 by your `/tmp` measure. Update `docs/RULES_MCP.md`. Gates in CLAUDE.md, the diff stat before the push. Final message, 12 lines or fewer: the commit, what's built and what's left, the honey and paint results, tick cost on a full lake, gates, time.
