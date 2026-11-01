Brief from the PM: Claude must be able to re-colour every visual through the MCP (Andrew, 2026-11-01: "keep working on customization via MCP, especially the visuals and skinning of colors"). His case: a blue fireball's case was blue, but the model wasn't. This is the core and MCP half. The Mac does spell effects and the PC does the world's colours, both in the view, reading what you add. Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree, so commits aren't authored as Andrew.

Setup: in ~/5e-compatible-game, run `git fetch`, then `git worktree add ../skin-looks-core origin/dev -b skin-looks-core` and work there. Read `docs/RULES_MCP.md` ("Looks"), `rules/looks.json`, `rules/README.md` ("Looks"), `unity/Assets/Prototype/Chemistry/Mcp/` and `LookOf` in `Rules.Check.cs`. Scripts/ is not yours: don't touch the view.

1. **Elements get a look.** An element's optional `"look": { "color": [r, g, b] }` is the colour of what that element makes in the world, whichever spell or reaction made it: fire's flames, water, cold's frost and so on. No family needed. Give each element in the rules its colour as drawn today (the view's values are in Scripts/; a read is fine). Same treatment as other looks: outside `Rules.Fingerprint`, kept by `Rules.ApplyPatch` when a replacing entry gives none, checked by `Rules.Check` and `validate`.
2. **Gases and covers:** make sure `look.color` is valid on them with no family drawn yet (a look-only change must apply), and the existing ones carry their colour as drawn today, so the view can read data, not ids.
3. **A `set_look` MCP tool:** changes the look of a spell, material or element in a draft, by id: colour, and any other parameter its family lists. It says which parameters that family takes, and refuses others with the list. The player says "blue"; Claude picks the numbers.
4. **Claude can see looks:** `describe` shows an entry's look and what it can change; `list looks` gives the families, their params and what takes no colour (`rules/looks.json` notes). Replace the Instructions' "Numbers are enough: looks follow them by themselves", which only holds for size: a colour change is a look, and a whole-element change ("blue fire everywhere") goes on the element, one spell's on the spell.
5. **Docs:** RULES_MCP.md "Looks" and rules/README.md, in the glossary's words.

Tests: a look-only draft leaves the fingerprint and the cell hash alone; a replacing entry keeps its look; `set_look` on a spell, a terrain, a gas and an element; a refused parameter. Rules change through the rules MCP's route (`docs/RULES_MCP.md`, D13) with goldens unmoved.

Checks: `dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes`, jscpd as in CLAUDE.md.

Deliver: commit and push `skin-looks-core`, not merged. Final message, 10 lines or fewer: the commit; the JSON shape of an element's look, for the view; test counts; anything that can't take a colour yet and why; the time.
