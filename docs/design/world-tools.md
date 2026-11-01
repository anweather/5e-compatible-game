# World tools: tuning the living world from Claude and from the game

Status: design, 2026-11-01 (Andrew's asks of the same day). Builds on D13 and `docs/RULES_MCP.md`:
the same MCP, the same room, the same checks. Nothing here is built.

> "Make the house by the lake bigger. Or brick colored / patterned." "Maybe a rare golden squirrel
> might spawn in and you need to find it." "Embed a terminal or chat into the game so you can tune
> from the chat. Users own Claude, but the MCP is setup automatically."

## 1. Editing what's in the world

**What each is today**, and where size, colour and pattern come from:

| What | Made from | Shared state (lockstep, checksum) | View only |
|---|---|---|---|
| A **cabin** (the house by the lake is the second, on the lake's west shore) | C# constants: `ValleyShape.Cabins` (Editor), built by `ValleyBuilder.BuildCabins` into the scene | Its wall cells (`logwall`, `stone`), baked into the valley | Size, shape and colours, hard-coded in the builder (`CabinMaterial("Roof", ...)`). Not data at all |
| A **village building** | `world/villages.json` spec + seed, laid by `VillagePlan` (core) | Footprint, storeys, wall and roof material (cells that burn) | `BuildingMesher`: walls' pattern (timber frame, stone blocks, log), colours from small palettes by the building's seed |
| A **prop** (crate, barrel, boulder) | Kind in `rules/entities.json` (hp, mass, material); where in `world/things.json` | Kind and spot (the things fingerprint); what it does to cells, as `EffectCommand`s | Size and colour: the prefab `PropBuilder` bakes in the Editor. Position: host PhysX, synced as transforms (D4, D7) |
| A **creature** | Punchy species in `ValleyFauna`, densities in the scene | Nothing (raiders and goblins are core entities, a different thing) | Everything |

So today a player can't name the house by the lake, and "bigger" or "brick" reaches nothing but
code. Three gaps: buildings have no ids or data of their own, props' size and colour are baked into
prefabs, and nothing knows what "by the lake" means.

**Find by words.** Claude does the language; the game gives it places and candidates:
- `places()`: the named landmarks with their cells (the lake, the river, the falls, Hollowmere,
  Sunward Key, the yard, the court, the Gauntlet...), from a new `world/places.json`.
- `find(kind?, near?, within?)`: buildings, things and quarries (part 2), each with its id, a
  one-line description and its distance: `cabin#2  log cabin, 6 m from the lake's west shore`.
- `here()`: what the asking player stands by and looks at, so "make *this* bigger" works.
- `describe(id)` gives every field `set` may change, which are shared and which are looks.

**Ids.** Things keep `ThingId` (`4,5#2`). Buildings get one: `cabin#2`, `Hollowmere#4` (the
village and the building's place in its plan). Cabins move from `ValleyShape` into world data
(`world/buildings.json`, a list, so append, never insert), and a village building's id holds only
while its village's spec and seed do: an edit overrides one building **after** the plan is laid,
never by re-laying the village.

**Change it.** A **world edit** is one change to world data, carried in a mod's new `world`
section, beside its rules patch: `set(id, field, value)`, `add(thing)` (appended to its chunk's
list), `remove(id)` (a tombstone: the slot stays, so later ids don't move). Draft, `validate`,
`simulate`, `apply`, `revert` and `save` work on it as on rules (RULES_MCP.md). Fields fall in two:
- **Shape** (a building's footprint, storeys, wall material; a thing's kind or cell): shared.
  Applied at the switch tick by the core, which lays the new footprint's cells and clears the old
  (fresh, like the dragon's breath). In the things fingerprint and the cell hash.
- **Look** (colour, pattern, a prop's drawn size): view only, like a material's look; out of the
  fingerprint and the hash, so a look-only edit can't desync anyone.
"Brick" is both kinds: a `brick` terrain in `rules/materials.json` (it doesn't burn, earth breaks
it) is a rules entry, and a `pattern: brick` wall in a new look family is a look. "Brick coloured"
alone is a look.

**How it reaches every peer.** A mod with world edits goes in as today's `RoomCommand` (the
room's change mode decides who may), stamped by the host into a tick batch; every peer checks it
with `ModValidator` (the world checks: ids exist, footprints clear of other buildings, water and
the yard, budgets on cells changed) and applies it at that tick. Props are host objects: the host
respawns a reshaped thing with its same id, and every peer redraws. Undo is `revert`, which
recomposes the other mods; a revert re-lays the building as it was, fresh (what burned since is
lost: open question 3). **Late join** replays the room commands with the batches, as now. A
**world save** keeps the room's active mods (it already does), and the cells they changed are just
cells that differ from the source; the effective things go in the save beside `crackedThings`.
**Keep it in a mod:** `save` writes the mod with its world section to `rules/mods/`, and for our
own agents a `promote` writes the edits into `world/*.json` (appending) for the next build.

**New view work.** Buildings: `BuildingMesher` takes overrides (colour, wall pattern, roof) and
`VillageHouse` redraws on a switch; the cabins rebuilt at runtime from data by the same mesher
instead of the Editor. Props: a `look` on entities (family, colour, scale) read by `ThingSpawner`,
the prefab scaled and tinted at spawn, its collider with it. A redraw hook on every room switch,
like `ChemGrid.RedrawLooks`.

**Recommendation:** one path for everything: world edits in the mod format, through the room. First
slice: places and `find`/`here`, building ids with the cabins moved to data, and look-only edits
(colour, pattern) on buildings, which can't break lockstep. Shape edits next, with goldens.

**Questions for Andrew**
1. Should every world edit be revertable by anyone (Open room), or is a building's shape the
   host's even in an Open room?
2. Is brick a new material (fireproof, rules) or only a look on stone or log walls?
3. When a burnt house is reshaped or reverted, does it come back whole, or keep its damage?

## 2. Spawning

**Words.** "Critter spawner" becomes a **spawner**: an entry in a **spawn table** that says what
appears, where, when, how rare and how many. What it makes is either **ambient life** (view only,
as now) or a **quarry**: a creature that's the same on every peer, that players seek and
**claim**. The golden squirrel is a quarry; the deer around it are ambient life.

**Where the line falls.** A creature is a quarry if anything shared depends on it: it can be
claimed, scored, hurt by a spell, or it changes cells. Otherwise it is ambient life and stays per
peer (D9 allows view-only crowds, D12 keeps life in the view). One species can be both: squirrels
are ambient, the golden one is a quarry.

**In data.** Species (hp, speed, components such as `creature` and `quarry`, and a `look`: Punchy
species, colour, size) are entries in `rules/entities.json`, so a mod can add one. Spawn tables are
world data, `world/life.json` (the file WORLD_LIFE.md already plans), each spawner with a stable id
and a list to append to:

```json
{ "id": "golden-squirrel", "makes": "golden-squirrel", "quarry": true,
  "where": { "theme": "home-valley", "chunks": [[3,4],[4,4]], "on": ["grass", "tree"], "nearWater": false },
  "when": { "clock": "dawn", "weather": ["clear"] },
  "chance": 0.02, "every": 360, "group": [1, 1], "alive": 1, "cooldown": 7200, "once": false }
```

`where` narrows by theme zone, chunk list, then cell (terrain tags, near water, away from
wizards); `when` by clock window, weather and season; `chance` per roll, `every` ticks between
rolls; `group` how many at once; `alive` the cap; `cooldown` after a claim; `once` for a one-off in
a world. Ambient spawners take a density instead, and each peer rolls them itself, as `ValleyFauna`
does today.

**What the core holds (quarries only).** A `World.Life.cs` system with a fixed place in the tick,
after the movers: for each spawner with active chunks in `where`, every `every` ticks it rolls with
a draw from the world's seed, the spawner and the roll's number (`WorldHash`, as the dragon's path
is drawn), so adding a spawner never shifts another system's draws or the goldens. Only active
chunks roll, and chunk switches are commands, so every peer rolls the same; a catch-up doesn't
roll. A quarry is plain data (D9): its id (the spawner and the roll), its kind as an interned
integer, its cell, its next move tick, its state, who claimed it. It moves cell to cell on its own
draws, flees wizards by their stands, keeps to cells its components allow; the view tweens it and
draws it with the species' Punchy body. Quarries are in the checksum and the world save; in an
inactive chunk one is held as it is.

**Found and claimed.** A wizard within reach presses use: a `ClaimIntent` to the host, stamped as a
`ClaimCommand`; at that tick every peer checks the claimer's stand is within `claimReach` cells, and
the first claim in tick order wins. Log: "Ana found the golden squirrel". What a claim gives (a
grimoire entry, an item, a score) goes to the player save later (PLAYER_SAVE.md). A spell may also
catch one through the rules (a Web), since it's a core entity.

**MCP tools.** `list spawners`, `describe`, `add_spawner`, `set_spawner` (world edits, through
the room as in part 1), `simulate` with a spawner over N ticks ("how often does it appear at
dawn?"), and `spawn(kind, near)` for a one-off now (a world edit applied once, logged by name).
`find` lists live quarries only for the room's host, so a player's own Claude can't simply point
at the squirrel (question 5).

**Recommendation:** quarries in the core, with seeded rolls per spawner; ambient life untouched,
its densities moved into `world/life.json` per peer. First slice: the golden squirrel, one
spawner, claim by use, a probe with two peers comparing hashes.

**Questions for Andrew**
4. What does finding the golden squirrel give: a log line, an item, a grimoire mark, a score?
5. May a player's Claude tell them where a quarry is, or is that cheating (host's Claude only)?
6. Does a quarry stay in the world for its finder alone, or does a claim take it for everyone?

## 3. Chat in the game

The player's own Claude, with this game's MCP (`127.0.0.1:47777/mcp`) already connected.

**Anthropic's terms decide most of this.** The Claude Code legal page
(<https://code.claude.com/docs/en/legal-and-compliance>, read 2026-11-01) says: "Anthropic does not
permit third-party developers to offer Claude.ai login into their own applications, or to route
requests through Free, Pro, Max, or Ultra plan credentials on behalf of their users", and "sign-in
to a Claude account must complete through Anthropic's own flow". It does not prevent "an end user
from signing in to the unmodified Claude Code binary with their own Claude subscription", but
"running Claude Code in your products" needs the Commercial Terms, an unmodified binary, and each
user on their own credentials. The Agent SDK page
(<https://code.claude.com/docs/en/agent-sdk/overview>) repeats it: agents built on the SDK use API
keys, not claude.ai login. So a chat panel on the SDK can't use a player's Pro plan.

| | A. Their Claude Code in a terminal panel | B. One-click "Connect to Claude" | C. Chat panel on the Agent SDK |
|---|---|---|---|
| Player installs, signs in | Claude Code (Anthropic's installer), signs in by Anthropic's flow | Whichever Claude they use; nothing from us to sign in | Nothing extra, but an **API key** (pay per token): no Pro plan |
| Windows / Mac / Linux | All three: ConPTY on Windows, a pty elsewhere | Claude Code everywhere; Claude Desktop (Windows, Mac) through a stdio bridge or a desktop extension we ship, since its connectors can't reach 127.0.0.1; claude.ai on the web can't | All three, with Node or Python shipped beside the game |
| Steam Deck | Works in game mode: the panel is inside the game | Desktop mode only: Claude Code in Konsole, alt-tab back | Works |
| Controller, text | A terminal wants a keyboard: Steam's on-screen keyboard, pad shortcuts for quick asks | Typing happens outside the game | Our own text box: on-screen keyboard, canned asks, room for voice later |
| Security | Claude Code can also run shell commands: start it in an empty folder with only the game's tools allowed, and say so on first launch | Only the MCP, and whatever else that Claude already does | Only our tools, by construction |
| Terms | Allowed if the binary is unmodified, the player's own install and sign-in; confirm with Anthropic that launching it counts as fine | Clean: we touch no Anthropic sign-in | Allowed only with the player's API key |
| A release ships | A pty + VT renderer panel, `claude` found on PATH or an install link, a generated MCP config passed with `--mcp-config` | The button (runs `claude mcp add`, writes Desktop's config or the extension), the bridge, the token | The SDK and its runtime, key entry and safe storage |

**Security, whichever.** The MCP stays on 127.0.0.1 and refuses other Hosts and Origins (built).
World edits make it more powerful, so add a **connect token**: the game makes one per profile and
writes it into the config it registers (`--header`), and a stray local client without it is
refused. The first change from a newly connected client asks the player in the game ("Claude wants
to change the house by the lake: allow?"), and every change shows a toast with Undo. The Esc
menu's switch still closes the port.

**Multiplayer.** Each player's Claude talks only to their own game; its changes go out as intents
and the room's change mode decides (Open, Host only, Locked), as for rules. Nobody's Claude reaches
another peer. A Host-only room is how a host "tunes" alone; spawning a quarry by hand is the host's
in any room (it decides a hunt).

**Recommendation:** ship B first, now: it's small, it works with every Claude a player has, and it
touches no Anthropic credentials. Then build A as the in-game chat: the player's own, unmodified
Claude Code in a panel, started with only our tools, after asking Anthropic in writing that this
fits "running Claude Code in your products". Offer C only as a bring-your-own-API-key option, if
ever; never as a way round a subscription.

**Questions for Andrew**
7. Shall we write to Anthropic (sales contact on the legal page) before building A, or ship B and
   wait to see how much players ask for an in-game panel?
8. Is the in-game panel for everyone in a room, or only the host's?
9. Does the game ever pay for Claude itself (a key of ours for a demo)? That's our API bill, and
   with A it's ruled out: the terms forbid paying for Claude Code usage on players' behalf.

## Proposed ADRs (titles only)

- World data changes through the room as world edits in mods
- Quarries are core entities from seeded spawners; ambient life stays in the view
- Chat in the game uses the player's own Claude; the game never handles a Claude sign-in
