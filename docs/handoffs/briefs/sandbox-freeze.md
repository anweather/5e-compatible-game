Brief from the PM: freeze the TypeScript sandbox (Andrew, 2026-10-31; JUDGMENT_CALLS J-042; D6 amended).

You're a worker in a VM, running headless, with no Unity: core and sandbox only. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../sandbox-freeze origin/dev -b sandbox-freeze` and work there. Read the root CLAUDE.md's "Invariants that span folders" (already updated: one engine) and sandbox/CLAUDE.md.

The decision: the C# core is the only engine that follows `rules/`. The sandbox keeps working exactly as it does today, but on a frozen snapshot, so new rules never break it and nobody ports anything to it again.
1. **Snapshot:** the sandbox reads its own copy of the rules (for example `sandbox/rules-frozen/`, copied from `rules/` at dev head) instead of `rules/`. Its tests, typecheck and build stay green on that copy. Say how big the copy is.
2. **Cross-engine goldens:** any C# test that checks the sandbox, or reads sandbox fixtures, keeps its C# side and drops the cross-engine requirement. The C# goldens stay. List what you changed.
3. **CI:** the sandbox workflow keeps running on the frozen copy; nothing else in CI depends on the sandbox matching the core.
4. **Docs:** update sandbox/CLAUDE.md, rules/CLAUDE.md, docs/ARCHITECTURE.md and any other doc that says a rule change must touch both engines (grep "both engines", "two engines", "mirror", "sandbox/test/terraform"). Leave the root CLAUDE.md and the ADRs alone; the PM did those. Short edits only.
5. **jscpd:** if the duplicate-code gate counted the sandbox against the core, say whether it still makes sense, and keep it passing.

Checks: `dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes`, `cd sandbox && npm ci && npm run typecheck && NODE_OPTIONS=--experimental-strip-types npm test && npm run build`, jscpd as in CLAUDE.md.

Deliver: commit and push `sandbox-freeze`, not merged. Final message, 8 lines or fewer: the commit; one line per item; the checks; the time.
