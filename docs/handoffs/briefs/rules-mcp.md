Brief from the PM: the rules MCP server, phase 1 (docs/RULES_MCP.md; ADR D13).

You're a worker in a VM, running headless, with no Unity: core only. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../rules-mcp origin/dev -b rules-mcp` and work there. Read docs/RULES_MCP.md (all of it), docs/MODDING.md ("The patch format", "Rules"), and `unity/Assets/Prototype/Chemistry/ModPatch.cs`. Skim the rest only as you need it.

The PC is shared with a Unity perf job: keep test runs few and targeted (`--filter`), and run the full suite once at the end.

Build phase 1: a local MCP server, stdio, in .NET.
1. **Project:** `core/Rules.Mcp/` in `core/Game.Core.sln`, linking the Chemistry sources the way `Game.Core` does, using the official C# MCP SDK (`ModelContextProtocol` on NuGet; pin the version). Nothing new in `unity/` or `sandbox/`.
2. **Read tools:** `list`, `describe`, `explain` over the tables in `rules/`.
3. **Draft tools:** `add_material`, `set_material`, `add_reaction`, `set_reaction`, `add_spell`, `set_spell`, `tune`, `remove` (only what the draft added). A draft is a mod patch held in memory under an id; tools compose onto it. Pick the exact parameters from the tables' real fields.
4. **`validate`:** shape, references, the design rules, determinism and budgets, as listed in the design doc. Reuse `ModPatch`'s checks; add the rest as a validator in the core, so the game can call it too. Every failure is one plain sentence in the glossary's words. Budgets that need numbers: pick conservative ones, put them in the rules data (not constants in code), and say what you picked.
5. **`simulate`:** run the core headless with the draft on, for N ticks, on a named scenario (start with 3: fire on grass, a flood, a storm) and the same run without it. Return a short summary in words: cells changed by material, peak counts, ticks per second against the budget, the cell hash.
6. **`save`:** write a mod in the `rules/mods/` format (title, idea, patch).
7. **Tests** in `Game.Core.Tests`: every mod in `rules/mods/` passes `validate` unchanged; one bad patch per check fails with the right sentence; simulate is deterministic (same hash twice); a round trip draft → save → parse gives the same patch.
8. **Docs:** a short "Using the rules MCP" section in docs/MODDING.md: how to add the server to Claude Code (`claude mcp add ...`) and an example conversation.
9. **Read-only scan for phase 2** (no Unity needed, just grep): list places in `unity/Assets/Prototype/Scripts/` where a look or effect is keyed to a specific rule id (a material, spell, reaction or element name in a switch, if or dictionary of prefabs). Write it to `docs/handoffs/procedural-looks-scan.md`: file:line, the id, and a one-line guess at the family it belongs to. Don't change any of it.

Checks: `dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes`.

Deliver: commit and push `rules-mcp`, not merged, with the cost lines the hook adds. Final message, 10 lines or fewer: the commit; one line per item; the budgets you picked; the tests; the time.
