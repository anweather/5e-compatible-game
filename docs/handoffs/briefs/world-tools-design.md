Brief from the PM: a design, no code, for tuning the living world through the MCP and from inside the game. Pro account.

You're a worker in a VM, running headless, with no Unity. Your final message is your full report, and it goes straight back to the PM. Before that final message, stop every background task you started, and don't send anything after it.

First run `git config user.name "u1 (worker VM)"` and `git config user.email u1@workers.invalid` in your worktree. Setup: in ~/5e-compatible-game, `git fetch`, then `git worktree add ../world-tools-design origin/dev -b world-tools-design`. Read `CLAUDE.md`, `docs/GLOSSARY.md`, `docs/RULES_MCP.md`, D4, D9, D12 and D13 in `docs/adr/`, `world/things.json`, `docs/ARCHITECTURE.md`'s "where new things go", and how the room's live rules work.

Andrew, 2026-11-01:
- "use the MCP connection to change the behaviors and attributes of things in the world too. Both things that are in world. Like Make the house by the lake bigger. Or brick colored / patterned. And things that are going to spawn into the world. Like maybe a rare golden squirrel might spawn in and you need to find it."
- "We need 'critter spawners' at some point too." (Critter is a retired word: propose the glossary word.)
- "I wanna think about how we embed a terminal or chat into the game so you can tune from the chat. Users own Claude, but the MCP is setup automatically"

Andrew, correcting this brief, 2026-11-01: "Golden squirrel was an example of something you could do via MCP. Don't actually hard code it. Let's see if we can make it happen via the existing systems" (J-055). So the examples here (the squirrel, the house by the lake) are tests of the tools, never features: no named content in code or base rules. Start from what the rules MCP, mods, looks, `things.json` and the fauna already do, say how far each example gets through them today, step by step, and propose only the smallest general gap that stops it.

Write `docs/design/world-tools.md`, under 200 lines, three parts, each ending in a recommendation and open questions for Andrew:

1. **Editing what's in the world.** What a house, a prop and a creature are today, and where each one's size, colour and pattern come from; which are shared state and which view only. MCP tools to find one by words ("the house by the lake"), change it, undo it, keep it in a mod. How it reaches every peer through commands (lockstep, stable ids: append, never insert) and what a late join and a save see. What needs new view work.
2. **Spawning.** Spawn tables and life spawners in data: what, where (theme, chunk, cell), how rare, when, how many. A find-it creature like the golden squirrel must be the same on every peer, while ambient life is view only: say where that line falls, how it's found and claimed, and what the core must hold. Use the world's seeded RNG and D9's seams.
3. **Chat in the game.** The player's own Claude, with this game's MCP already connected. Compare at least: launching the player's installed Claude Code in an in-game terminal panel with the MCP config passed for them; a one-click "connect" that registers the MCP with whatever Claude they have, and they chat there; a chat panel built on the Agent SDK. For each: what the player must install and sign in to, Windows, Linux, Steam Deck and Mac, controller and text input, security (the MCP stays on 127.0.0.1; what a stray client may do, consent), multiplayer (who may tune, the host?), what a release must ship. Check Anthropic's current published terms on third-party products using a Claude.ai login or subscription limits, cite the page, and let that shape the recommendation; don't assume it.

Add the new words to `docs/GLOSSARY.md`. Propose ADRs by title only.

Checks: docs only, so none. Deliver: commit and push `world-tools-design`, not merged. Final message, 12 lines or fewer: the commit; the recommendation for each part; the questions for Andrew; the time.
