Brief from the PM to the Mac: P5 mix, then a cheap-model playtester (Andrew, 2026-11-04). Frugal: one Unity job at a time, no clips, short reports, no sub-agents except the playtester itself.

Never push to `dev` or merge. Push your branch; the PM lands it. Keep off anything mod-tooling the PC is changing: if a part needs a change in `RulesMcpServer` or the mod room beyond what's named here, say so before making it.

## Part 1: P5 mix (branch `mix` off dev)

`docs/design/primitives.md` §4 "Mix", answers in J-062. Rows sharing `mix: "paint"` that meet (a liquid touching another, a cover laid on another) derive `a+b` (ids sorted) with no reaction row: a pure function of the two rows. Colour mixes as `Colors.cs` does, numbers are the mean rounded down, tags are those both have, it lapses with the later parent. Fixed tick order, so every peer appends it at the same moment to the table and the room's mix list; saves and joins carry the list. Cap 16 derived mixes: past it the arriving one stops as at a wall.

Done means: red and blue paint make purple with no row per pair (a mod's rows only, nothing in code naming paint); goldens for it; every existing golden holds, or say why not; a save and a late join keep the mix; `docs/RULES_MCP.md` and the primitives step 5 note updated. Deterministic core, nothing under `Scripts/` unless the view needs it to draw the colour. Gates in CLAUDE.md "Commands" plus EditMode.

Report: commit, gates, anything left. Then go on to part 2 without waiting.

## Part 2: the playtester on a cheap model (branch `playtester-cheap` off dev)

Andrew wants the playtest bot back, driven by Sonnet or even Haiku, exploring the world to build up the issue backlog, and wants to know what that costs. Start from `playtest/DESIGN.md` and `docs/PLAYTESTS.md`, `core/Playtest.Runner`, `playtest/known-issues.json`. The existing bot is scripted, and Claude only triaged after; here the cheap model chooses what to try. Its Windows parts (Task Scheduler, `.ps1`) don't apply on the Mac.

1. Find the cheapest way for a model to see and act: text state first (probe JSON, world queries, logs), a 480 px shot only when text can't answer. Editor via `tools/probe.sh` or a Mac player, your choice; say why.
2. Run the same 20-minute exploring session once with Haiku and once with Sonnet (`--model`, on this machine's existing sign-in; no API key, never sign in to anything). Goals: walk the world, cast things at things, try spell pairs and materials, look for what's wrong.
3. A finding needs evidence: steps, seed and the state or log that shows it, reproduced once by replay or a second try. Unreproduced ones go in a separate list marked unconfirmed. Check each against open issues (`gh issue list`) and `known-issues.json`.
4. Don't file issues: findings go in `playtest/findings/` on the branch; the PM files them.

Stop at the two runs, or sooner if a run shows the approach can't work: the point is a measurement, not a schedule. If Max's limit stops you (out until Nov 5, 4pm Phoenix, if this machine is on it), push and say so.

Report, a few lines: per model, tokens in and out, API-equivalent dollars, minutes, findings confirmed, unconfirmed, false alarms, duplicates; cost per confirmed new finding; which model you'd run and how often.
