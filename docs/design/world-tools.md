# World tools: tuning the living world from Claude and from the game

Status: design, 2026-11-01 (Andrew's asks of the same day, and J-055). Builds on D13 and
`docs/RULES_MCP.md`: the same MCP, mods, room and checks. Built so far, in the core and the MCP
only: G1's `places()` and `here()` with `world/places.json`, and G2b's `where` and `mix`
(`LookScopes.cs`); the view doesn't draw scoped looks yet, and `find` isn't built.

> "Make the house by the lake bigger. Or brick colored / patterned." "Maybe a rare golden squirrel
> might spawn in and you need to find it." "Embed a terminal or chat into the game so you can tune
> from the chat. Users own Claude, but the MCP is setup automatically."

**The examples are tests of the tools, not features** (J-055: "Don't actually hard code it.
Let's see if we can make it happen via the existing systems"). No squirrel and no lake house in
code or base rules. Each example is walked through what exists today, step by step, to where it
stops; what's proposed is only the smallest general gap that stops it. When the gaps are built, the
test is a player asking their Claude and the MCP doing it as a mod.

**What exists today.** The MCP's tools (`list`, `describe`, the `add_`/`set_` drafts, `set_look`,
`tune`, `validate`, `simulate`, `apply`, `revert`, `save`); `apply` also takes patch JSON. A mod
patches the rule tables, `entities` and `items` included (`ModPatch.Tables`), never `world/`.
Looks are data on materials, spells and elements. `world/things.json` places props and items by
chunk; the host spawns each from a prefab of its kind, baked in the Editor (`PropBuilder`,
`ItemBuilder`), and a kind with no prefab doesn't spawn. The fauna (`ValleyFauna`) is Punchy's
species with densities from a table in `ChemYardBuilder`, each animal per peer; a species has
coats (colours with weights) and variants (`GlimmerSquirrel` is a variant of `Squirrel`, which
already lives in the valley at 1.5 a hectare).

## 1. Editing what's in the world: "make the house by the lake bigger, brick coloured"

| Step | Today | Stops because |
|---|---|---|
| 1. Find "the house by the lake" | No. The MCP sees only rules. The house is the second cabin, whose site is a C# constant (`ValleyShape.Cabins`, Editor) | The MCP can't read the world, and nothing names the lake |
| 2. Brick colour on every log wall | Half. `set_look logwall color` validates and switches on in the room | Cabins' meshes take colours from `ValleyBuilder` constants, village buildings' from `BuildingMesher`'s palettes; neither reads the wall's look |
| 3. Brick colour on that house only | No | A mod can't name one building: buildings have no ids, and mods can't reach `world/` |
| 4. Brick pattern | No (a `brick` terrain exists, drawn as ground) | The mesher's patterns are fixed per wall kind (timber, stone, log) |
| 5. Bigger | No | The footprint is wall cells baked with the valley, and the mesh is built at that size in the Editor |

**The smallest general gaps, in order:**
- **G1. Read the world.** Read-only tools over what the game already holds: `places()` (named
  landmarks with their cells, from a new `world/places.json`: the lake, the river, Hollowmere...),
  `find(kind?, near?, within?)` (things and buildings, with id, one line and distance) and
  `here()` (what the player stands by and looks at, for "make *this* bigger"). Buildings get ids
  (`cabin#2`; `Hollowmere#4`, the village and its place in its plan), which means the cabins'
  sites move from `ValleyShape` into world data beside the villages: data, not new content.
- **G2. Buildings draw from their materials' looks.** `BuildingMesher` (and the cabins built by it
  at runtime) colours walls and roofs from the look of their wall and roof terrains, with a
  `pattern` parameter on a new `wall` look family (logs, blocks, plaster, bricks). Then step 2
  works for every log wall, and "brick-patterned" is `set_look logwall pattern bricks`. It's the
  procedural-looks rule (RULES_MCP.md) applied to buildings, and redraws through `RedrawLooks`.
- **G3. A mod may carry world edits.** A `world` section in the mod format: set one field of a
  building or thing by id (`walls`, `look`). It goes through the room like any mod: validated by
  every peer, switched on at a tick, reverted, saved, replayed for a late joiner, kept in the world
  save's room. Look fields stay out of the fingerprint, as looks do. Step 3 works.
- **Bigger** (step 5) needs shape edits: a footprint field the core lays at the switch tick
  (clearing the old wall cells, laying the new, as the dragon's breath freshens a chunk), counted
  in the cell hash, with a golden. Not smallest; proposed only once G1 to G3 prove out.

**Recommendation:** G1 and G2 first (no lockstep risk: read-only and looks), then G3 for
one-building edits. Test: "make the house by the lake brick-coloured", asked of Claude, ends as a
mod in the room with no code naming a house.

**Questions for Andrew**
1. Is "every log wall turns brick" (G2 alone) a fair first answer, or must one house change alone?
2. Should the cabins become ordinary village buildings (one mesher, one data file)?
3. In an Open room, may anyone's Claude change a building, or only the host's?

### Scoped and mixed looks: "make the fire near me green"

Andrew, 2026-11-01: "make the fire in the area around the village blue. Or ... make the fire near me,
Green and as you move around that changes. Could also say maybe not an entire uniform color for all
the fire, but have different colors for the fire."

Today a look is one value for the whole world. **G2b** adds two general fields to any look, on
elements, covers, gases and terrains alike (fire is only the example):

- **`where`**: a scope. `{ "place": "hollowmere", "within": 20 }` (a G1 place), `{ "at": [x, y],
  "within": 12 }` (cells) or `{ "around": "me", "within": 10 }` (the asking player, from `here()`).
  Several scoped looks can be on at once: the latest that covers a cell wins, and the plain look
  is what's left. A scope's edge blends over a few cells.
- **`mix`**: several colours with weights in place of `color`. Each cell picks by a hash of its
  place, with no draw from the world's RNG, so every peer sees the same patchwork.

Scopes are worked out at draw time from where a cell is, not stored on the fire: a fire leaving
the circle changes back, and that's what makes "as you move around" work. This is a third case
beside J-052's: J-052 says what a fire remembers from a spell, `where` says what a place does
to whatever burns there. A look-scoped `where` wins over the element's colour, not over a colour a
spell chose. It goes through the room as a mod, like any look: view only, out of the fingerprint,
revertable. If the player named by `around` leaves, that look lapses.

**Cost:** a moving scope must touch only the cells at its edge each frame, within a budget. It can't
use the world-wide rule switch `look-switch` is timing. The test is "make the fire near me
green" asked of Claude, then walking through a burning forest at the lag fix's numbers.

## 2. Spawning: "a rare golden squirrel might spawn in and you need to find it"

| Step | Today | Stops because |
|---|---|---|
| 1. A golden squirrel kind | Yes. `apply` takes a patch adding an `items` row (`golden-squirrel`: flesh, mass 0.3, no use); `validate` passes it | No `add_item` draft tool: Claude writes the row by hand |
| 2. It looks like a golden squirrel | No | A thing is drawn by a prefab baked per kind; a new kind has none and doesn't spawn. Things have no `look` |
| 3. It appears somewhere | No | Only `things.json` places things, and a mod can't add to it |
| 4. The same one for every peer | Yes, once placed: things are host objects with stable ids, late joiners get them | |
| 5. Rare: maybe there, maybe not | No | A placed thing is always there |
| 6. Found: you pick it up | Yes, once placed: picking up an item is host-granted, and everyone sees who holds it | No log line names the finder |
| 7. It scampers | No | Things are still; the fauna moves, but per peer |

**The view-only route works further.** A coat on `Squirrel` with gold and a little glow and a
weight of 0.01 is a rare golden squirrel in the fauna, right now, if the species were data: it
stops because the fauna table and Punchy's coats are Editor data, not rules. But each peer would see
its own, at its own time: a sighting, never a shared hunt.

**The smallest general gaps:**
- **G4. Things drawn from data.** A `look` on entities and items rows, with a `creature` family
  (a fauna species, a coat colour, a scale) beside today's shapes. `ThingSpawner` dresses a
  generic registered prop or item prefab from the look when a kind has no baked one, so any mod's
  new kind spawns and syncs. `set_look` takes `entities` and `items`; `add_item` and `add_entity`
  join the drafts. Steps 1 and 2 work.
- **G5. Mods may add things.** In G3's `world` section, `add` appends a thing to its chunk's list
  (never inserts, so ids stay put). The effective things (base plus mods) feed the fingerprint and
  the spawner; a revert despawns what the mod added. Step 3 works: Claude picks the spot, so "it
  spawns in" is Claude choosing where and when. Step 6 gets a log line on first pick-up of a thing a
  mod added ("Ana found the golden squirrel", the name from the row).
- **G6. A seeded chance on a placed thing** (`chance`, decided once from the world's seed and the
  thing's id by `WorldHash`, no draw from the world's RNG, so every peer agrees). Step 5 works:
  Claude scatters five candidates at 0.2 each. Only if the test shows it's wanted.
- **The fauna as data** (the "critter spawners" Andrew asked for, say **spawners**): the
  `ChemYardBuilder` table and species' coats as a rules table a mod patches, view only and out of
  the fingerprint like looks. It's the view-only route above, and ambient life's knobs.
- **Not proposed:** a squirrel that moves the same on every peer (step 7). The host could move a
  creature-look thing as it moves props (D4 allows it: a synced transform, nothing on cells), but
  that's a behaviour to build, and the test doesn't need it.

**Recommendation:** G4 and G5. Test: "put a rare golden squirrel somewhere in the woods for us to
find", asked of Claude, ends as one mod (an item with a creature look, added at a spot it chose)
that every peer sees and one player picks up. Then the fauna as data for ambient spawners.

**Questions for Andrew**
4. Is a golden squirrel that sits still (and can be picked up) enough for the test, or must it run?
5. Should Claude tell a player where the thing it hid is, or keep it secret from that player?
6. Fauna as data: per room (a mod, everyone's table) or per player's own view?

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

**Security, whichever.** The MCP stays on 127.0.0.1 and refuses other Hosts and Origins (built). G3
and G5's world edits make it more powerful, so add a **connect token**: the game makes one per
profile and writes it into the config it registers (`--header`), and a stray local client without it
is refused. The first change from a newly connected client asks the player in the game ("Claude
wants to change the house by the lake: allow?"), and every change shows a toast with Undo. The Esc
menu's switch still closes the port.

**Multiplayer.** Each player's Claude talks only to their own game; its changes go out as intents
and the room's change mode decides (Open, Host only, Locked), as for rules. Nobody's Claude reaches
another peer. A Host-only room is how a host "tunes" alone, hiding things for the others included.

**Decided (Andrew, J-056):** bring your own Claude. "It's just like an IDE terminal where you can
just talk to Claude with an MCP configured." A plain terminal panel in the game (a pty and a VT
renderer, option A's panel) opening in a folder whose config already has this game's MCP and connect
token. The player runs their own Claude there and signs in by Anthropic's flow. The game doesn't start
Claude, ships no Claude, stores no credential and pays for nothing. B's config writer is the same
piece, so "Connect" for a Claude outside the game comes with it. C is dropped. A button that starts
`claude` for the player would be running Claude Code in the product, with the terms' conditions: not
in this design.

**Questions for Andrew**
Questions 1 to 6, 8 and 9 are answered in J-056. Still open: whether to ask Anthropic (sales contact on
the legal page) before the panel ships; with a plain terminal the player drives, that's a courtesy, not a gate.

## Proposed ADRs (titles only)

- The examples are tests of the tools: no named content in code or base rules (J-055)
- Mods may carry world edits: things appended, buildings and things changed by id
- Things, buildings and fauna are drawn from data, never from per-kind baked prefabs
- Chat in the game uses the player's own Claude; the game never handles a Claude sign-in
