Brief from the PM: primitives step 3, P2 covers that spread. Max account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

**Never merge into `dev` or push to `dev`.** Push your branch only.

First run `git config user.name "u2 (worker VM)"` and `git config user.email u2@workers.invalid`. Fetch, then make branch `cover-spread` off the latest `origin/dev` (temp-materials and products-data are on it; `building-gaps` isn't yet and doesn't overlap).

Build P2 of `docs/design/primitives.md` §3: `spread` {every, chance, onto: tags, notOnto, reach} on a cover. On the soak pass a cover may lay itself on a neighbour, from the world's seeded RNG, in N, E, S, W order. It must cost nothing when no cover spreads, and be bounded when many do (a budget in `budgets.json`, and say what a whole active chunk of spreading moss costs a tick). Mods set it through the rules MCP (`set_material` or the cover row), within the checks. A temporary cover that spreads is swept like any other at its lapse.

Test, headless on two peers: §5's "glowing moss that creeps over the river stones" as one mod (a temporary cover that spreads only onto stone, never onto water or sand), with matching checksums and a new golden for the spread; plus a test that a world with no spreading cover is unchanged (existing goldens hold). Glow is view-only (P4) and not yours: leave `look.glow` as data if the look format already allows extra fields, otherwise list it for the view.

Constraints: deterministic core, nothing under `Scripts/`, new `.cs` under `unity/` gets its `.meta` (new GUID), every new method under S3776 15 by your `/tmp` measure (tests too). Update `docs/RULES_MCP.md`. Gates in CLAUDE.md, the diff stat before the push. Final message, 10 lines or fewer: the commit, what's built, the moss result and its cost a tick, gates, time.
