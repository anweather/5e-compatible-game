# Rules MCP: changing the game through checked tools

Status: design, 2026-10-31 (Andrew: "let's do it"). Decision: ADR 0013.

## The idea

Players (and our own agents) change the game by talking to their own Claude, which calls
**tools on an MCP server we provide**. The tools are typed and every change is checked before
the game sees it. Nobody hand-writes rule JSON, so nothing gets missed: a typo, a dangling id,
a reaction that floods every cell, a rule that breaks lockstep.

It is a front door to what already exists, not a new system. Today a **mod** is a JSON patch
over `rules/` (`docs/MODDING.md`, `ModPatch.cs`); it is checked against the engine and switched
on for everyone in a room at the same tick. The MCP server produces those same patches.

## Principles

- **Only patches.** The server never edits files or runs code in the game. Its output is a mod
  patch, the format we already check and switch.
- **Typed operations, not raw JSON.** `add_reaction(a, b -> c)`, not "paste this table".
- **Check everything, explain every failure** in one plain sentence, in the game's words
  (`docs/GLOSSARY.md`), so the caller's Claude can fix it and retry.
- **Simulate before anyone sees it.** The C# core runs without Unity (`core/`), so the server
  runs a change headless for N ticks and says what happened.
- **Everything the view draws is procedural.** A new rule that makes something bigger, hotter
  or wider must show up scaled in the game with no code change and no rebuild (see "Looks").
- **The players bring their own Claude.** We pay only for the server's checks and short
  simulations: lean budget.

## Tools

**Read**
- `list(table)`: ids and one-line summaries for materials, elements, reactions, spells, groups,
  sim parameters.
- `describe(table, id)`: the full entry, plus what refers to it.
- `explain(a, b)`: what happens when a meets b, and which rule decides it.
- `room_mods()` (phase 3): the mods on in a room.

**Draft** (each returns a draft id and the patch so far; drafts compose)
- `add_material`, `set_material(id, field, value)`
- `add_reaction(a, b -> result, chance?, ...)`, `set_reaction`
- `add_spell` / `set_spell` (through groups and material verbs only)
- `tune(param, value)`: a sim parameter, within the bounds the rules declare
- `remove(table, id)`, only for things the draft itself added

**Check**: `validate(draft)` returns every problem, one sentence each:
1. Shape: the schema of each table.
2. References: every id named exists (or is added by the same draft).
3. Design rules, as code (Andrew's standing rules):
   - every spell acts through material verbs (`rules/materials.json`), not a rule for one spell;
   - no new element for one spell;
   - spells stay inside the 5E-compatible frame (SRD 5.2.1 levels, shapes, damage dice).
4. Determinism: integer state only, no new randomness outside the world's seeded RNG.
5. Budgets: caps on spread rates, chain-reaction depth, new entries per mod, so a mod can't
   flood every cell or stall the tick.
6. Looks: every new thing names a look family the view already draws procedurally.

**Simulate**: `simulate(draft, scenario, ticks)` runs the core headless with the patch on:
a named scenario (fire on grass, a flood, a storm) or a small described setup. It returns a
summary in words (cells changed by material, peak counts, tick time against the budget, the
cell hash) plus the same run without the patch for comparison.

**Save and apply**
- `save(draft, title, idea)`: writes a mod in the `rules/mods/` format (title, idea, patch).
- `apply_to_room(mod, room)` (phase 3): asks the room's host; on approval it switches on at a
  tick for every peer, like the Experiments panel. `revert(mod, room)`.

## Looks: procedural only

The view must draw any rule from its data:
- sizes, radii, heights and counts scale meshes, particles and decals from the numbers;
- a material's look comes from a **look family** plus parameters (color, glow, roughness,
  flow), never from a hard-coded id;
- effects (fire, steam, frost, lightning...) are families too, scaled by power and area.

Phase 2 audits the view for anything keyed to a specific id (a `switch` on `"fireball"`, a
prefab per material) and lists what must become a family. That work needs Unity and goes to the
host or the Mac; the MCP's `validate` then checks against the family list, so a mod can never
ask for a look the game can't draw.

## Where it runs

- **Phase 1, local:** a .NET MCP server (stdio) in `core/`, linking the core sources like the
  tests do, using the official C# MCP SDK (`ModelContextProtocol` on NuGet). Our own agents use
  it at once instead of editing rule JSON by hand.
- **Phase 3, hosted:** the same server over HTTP with sign-in, as a connector for players'
  Claude. Game hosts connect out to it (like a lobby), and room changes travel as the existing
  rule-switch intents, so lockstep holds.

## Phases

1. **Local rules MCP** (Linux worker): read, draft, validate, simulate, save; tests in
   `core/`; every example in `rules/mods/` passes through it unchanged.
2. **Procedural looks audit** (host or Mac): find id-keyed visuals, define the look and effect
   families, and confirm that switching a mod on mid-game rescales the view without a rebuild.
3. **Hosted and live rooms**: HTTP transport, sign-in, rate limits, the host's approval or a
   room vote, revert, a mod gallery.
4. **Assets**: not now. Procedural families come first, and may make most of it unnecessary.

## Open questions

- Who may change a live room: the host only, a vote, or mod rooms only? (phase 3)
- The TS sandbox runs the same rules. If the MCP becomes the way rules change, do we freeze the
  sandbox? (already open in STATE)
- Budgets: start from what the perf work measures (storm at 20 fps, #24-#33).
