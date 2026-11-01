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
- **The players bring their own Claude**, and the MCP runs in their own game: we run no server
  for it. Lean budget.

## Tools

**Read**
- `list(table)`: ids and one-line summaries for materials, elements, reactions, spells, groups,
  sim parameters.
- `describe(table, id)`: the full entry, plus what refers to it.
- `explain(a, b)`: what happens when a meets b, and which rule decides it.
- `room()` (phase 3): the room's change mode and the mods on in it, with who added each.

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
6. Looks: every new terrain and spell names a look family the view already draws, and no entry
   names an unknown one (`rules/looks.json`). `ModBudgets.Load` carries the list, so rooms check it too.

**Simulate**: `simulate(draft, scenario, ticks)` runs the core headless with the patch on:
a named scenario (fire on grass, a flood, a storm) or a small described setup. It returns a
summary in words (cells changed by material, peak counts, tick time against the budget, the
cell hash) plus the same run without the patch for comparison.

**Save and apply**
- `save(draft, title, idea)`: writes a mod in the `rules/mods/` format (title, idea, patch).
- `apply(mod)` (phase 3): sends the mod into the game this MCP is attached to. In a room it goes
  as a rule-switch intent and follows the room's change mode (below). `revert(mod)`.

## Looks: procedural only

The view must draw any rule from its data:
- sizes, radii, heights and counts scale meshes, particles and decals from the numbers;
- a material's look comes from a **look family** plus parameters (color, glow, roughness,
  flow), never from a hard-coded id;
- effects (fire, steam, frost, lightning...) are families too, scaled by power and area.

**Where it lives.** The family list is `rules/looks.json`; each material's and spell's own look
is a `"look"` group on its entry in `materials.json` or `spells.json`. Not `world/looks.json`:
that's what a theme paints, a different idea. On the entry, because a mod's whole new thing then
travels as one patch entry, through the same patch format and rule switch. Looks stay out of
`Rules.Fingerprint`, so the fingerprint didn't move when they were added and a look-only mod
can't reach the netcode checksum; `Rules.ApplyPatch` keeps the old look when a replacing entry
doesn't give one, so a mod about numbers doesn't lose its drawing.

**How it redraws.** The view reads looks from `World.Rules` when it draws, so what it draws next
is from the rules in force on every peer. A switch also redraws what already stands if its
terrain's look changed (`ChemGrid.RedrawLooks`, from `Announce`, which solo and online share).
Sizes come from the numbers: cells, flames and craters always did, and a cast's impact scales
through `EffectScale.Of` (family `scaleBy` and `base`, with the charge).

**Done so far (phase 2):** `CellDressing`'s terrain table and dressing switch, and
`ChemYardBuilder`'s look per spell. `validate` checks the family list (`ModCheck.Looks`). What's
still keyed to an id, with what each needs: `docs/handoffs/procedural-looks-scan.md`. Check it with
`tools/probe.sh looksmod`.

**Limits today.** A `ground`-family material isn't painted in its own colour (`CellPaint` picks
layers by id), so a mod's new flat ground looks like the land under it; its colour reaches only
chips. A new cover or gas has no family drawn yet, so it needn't name one, and draws as an unknown
one always did.

## Change modes (chosen when a room is made)

Andrew, 2026-10-31: the point is fun, not policing. A room picks one when it's created:
- **Open:** anyone's change just appears for everyone, at the next agreed tick. Surprise is the
  game; the log says who changed what, and anyone can revert.
- **Host only:** only the host's changes go in; other players' are refused with a sentence.
- **Locked:** no changes; the rules are what the room started with.

Whatever the mode, **every peer validates every incoming change itself**, with the same checks in
the core, before it switches on. A change that fails on one peer fails on all (the checks are
deterministic), so a broken or hostile peer can't push anything the checks wouldn't pass.

## Where it runs

**The MCP ships with the game.** Each player's Claude talks to the MCP of their own copy of the
game, on their own machine. There is no hosted MCP server: what crosses the network is the mod
patch, as the existing rule-switch intent (client to host, the host schedules a tick, every peer
switches on), so lockstep holds and we run no servers for it.


- **Phase 1, local:** a .NET MCP server (stdio) in `core/`, linking the core sources like the
  tests do, using the official C# MCP SDK (`ModelContextProtocol` on NuGet). Our own agents use
  it at once instead of editing rule JSON by hand.
- **Phase 3, in the game:** the same tools, served by the running game (or a small process it
  starts) on localhost, so a player adds it to their Claude once. `apply` goes into that game.

## Phases

1. **Local rules MCP** (Linux worker): read, draft, validate, simulate, save; tests in
   `core/`; every example in `rules/mods/` passes through it unchanged.
2. **Procedural looks** (Mac): id-keyed visuals found, look and effect families defined, the two
   biggest tables converted, a mod switched on mid-game rescales the view without a rebuild
   (`looksmod`). The rest is listed in `docs/handoffs/procedural-looks-scan.md`.
3. **In the game and live rooms**: the MCP served by the game on localhost, `apply` and
   `revert`, the three change modes chosen at room creation, every peer validating incoming
   changes, the log line per change, a mod gallery.
   - **Built, core half (2026-10-31, `live-rules`):** the room in the world state
     (`Room.cs`, `World.Room.cs`): a change mode set by `World.OpenRoom` when the world is made,
     saved with it (`World.Load` takes the budgets for a room's save) and in the checksum (through
     `CastHash`). A `RoomCommand` (switch a mod on, or revert one, naming the peer) rides in the
     tick batch (`TickBatch.Room`, `AddRoom` for a late joiner). At that tick every peer decides it
     itself: the change mode (`RoomPolicy.Refusal`), then `ModValidator` against its own rules in
     force, then the active mods composed again over the base in order. A refusal is the same
     sentence everywhere and changes nothing the checksum sees. The active mods keep id, title, who
     added it and the tick; a revert takes one off and recomposes the rest. One log line per change
     ("Ana switched on Big fireball (Open room)", "Revert refused: only the host can change this
     room"). The MCP has `apply`, `revert` and `room` against a world in memory. Tests:
     `RoomTests.cs` (two peers side by side, a hostile patch and a good one, hashes compared),
     `RoomToolsTests.cs`.
   - **Built, Unity half (2026-10-31, `live-rules-unity`):** every world is a room, solo too (an Open
     one); the host picks the mode in Esc > Play together before hosting (`NetSession.RoomMode`). A
     rebuilt world keeps the room and the mods on (`OpenRoom` takes them), and a host's solo mods
     become its room's starting mods, so they stay even in a Locked room. `RoomIntent`
     (`NetChem.Room.cs`): anyone asks with the patch, the host names the sender, may refuse at once
     (`RoomPolicy.Refusal`, a sentence every peer hears) and stamps the command for the next tick. A
     late joiner gets the room as its history starts, then the commands since. `RuleSwitcher`, and so
     the mods panel, goes through the room; in a room a batch's plain mod list changes nothing. Peers
     are "Player 1", "Player 2" until the game has names.
   - **The game serves the MCP:** the tools moved into `Chemistry/Mcp/` so Unity compiles them; both
     hosts read them from the `RulesTool` attributes. `RulesMcpServer` is HTTP on `127.0.0.1:47777/mcp`
     (the next port up for a second copy), one JSON answer per POST (`McpRpc`), on its own thread;
     `apply` and `revert` cross to the main thread (`GameRoomDesk`), go in as this player's commands,
     whoever the tool names, and wait for the room's sentence. It starts with the game and turns away
     a request with another Host or Origin, so a web page can't reach it. There's no sign-in: anything
     running as this user on this machine can use it. The Mods page shows the line to add it to Claude.
     Probes: `roomopen`, `roomhost`, `roomlocked`, `roommcp`, `roomclaude`.
4. **Assets**: not now. Procedural families come first, and may make most of it unnecessary.

## Open questions

- How much simulation a peer runs before switching on an incoming change: the checks only, or a
  short simulate too (it must finish inside the tick the host scheduled).
- The TS sandbox runs the same rules. If the MCP becomes the way rules change, do we freeze the
  sandbox? (already open in STATE)
- Budgets: start from what the perf work measures (storm at 20 fps, #24-#33).
