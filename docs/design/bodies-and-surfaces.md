# Bodies and surfaces: creatures from parts, things on the water

Status: design, 2026-11-03, from Andrew's play tests of 2026-11-01 and 2026-11-02 (#73, #60). Design only. Builds
on `builders.md` (creatures as data, gaits by rule), `primitives.md`, `world-tools.md` and `docs/RULES_MCP.md`.
J-055, J-057, J-058: no centaur, dock or raft in code or base rules; each ends as one mod a player's Claude makes,
and a new primitive is added only for a whole family.

> "Make me a centaur." "A half-man, half-deer." "Build a dock on the house." "The dock sits on the lake bed."

## Part 1: bodies from parts (#73)

### 1. What's there, and why Claude said no

The data move is done (`builders.md` §2 "Landed as"): 68 species in `rules/fauna.json` and 93 shapes in
`rules/parts.json`, view only, out of the fingerprint (`Fauna.cs:1-6`). A species is a body and **slots**, each a
part hung from the body or from an earlier slot by `on` (`SlotDef`, `Fauna.cs:38-55`). A mod may already replace or
add rows in both tables (`ModPatch.cs:16`), and `ValleyFauna` rebuilds bodies at every rule switch
(`ValleyFauna.cs:168`). The parts a centaur needs exist: the deer's `hoof-hindleg` and `hoof-foreleg`, the human's
`human-torso`, `human-head`, `human-arm` and hair (`fauna.json:2`, `:695`). So a centaur is a row today. Claude
couldn't see that:
- **It can't read them.** `list` and `describe` have no `fauna` or `parts` table (`RulesTools.cs:71`,
  `Workbench.Read.cs:24-69`), and `outside_rules` says fauna has "nothing yet" (`Workbench.Outside.cs:13`).
- **No typed tool.** The only way in is hand-written patch JSON to `apply`: a 20-slot row in body units.
- **One coat a creature** (`CoatDef`, `Fauna.cs:15-22`; a segment can only fix one colour, `Fauna.cs:98`): a
  centaur's torso and face wear deer brown. That is what "half-man, half-deer didn't work" looked like (C2).

### 2. The design: graft

A species row may be built from others' slots. **`from`** lists donors; the core's `Fauna.cs` (no Unity) flattens
the row into an ordinary one when it's read, so the view, checks and `describe` see plain slots:

```json
"centaur": { "name": "Centaur", "size": [0.95, 1.05], "body": { "from": "deer" },
  "from": [
    { "species": "deer", "drop": ["neck", "head", "ears", "eyes"] },
    { "species": "human", "take": ["human-torso"], "at": [0, 0.45, 0.8], "scale": 1.1, "coat": "own" } ],
  "move": { "from": "deer" }, "behave": { "from": "deer", "flee": 8 }, "spawn": { "density": 0.05 } }
```

- **`take` or `drop`**: which donor slots come. A slot taken brings every slot hung `on` it, so `human-torso`
  brings head, eyes, hair and arms. Taken slots with no `on` hang from the new body at `at` (body units), turned by
  `dir` and sized by `scale`; ids that clash get the donor's id as a prefix.
- **`coat`** per donor: `"own"` paints that donor's slots from its own coats and hair (the human's tunic, skin and
  hair, picked by the animal's Individual), the default paints all from the row's coat. This is C2 done per
  graft, not per slot: one field, and "a blue-haired centaur" is still `coats` and `hair` on the new row.
- **`body`, `move`, `behave`** may say `from` a species and override fields.

**Walk-throughs.** The **centaur** is above: deer legs walk on the deer's poses; the human arms swing on theirs.
**Half-man, half-deer** read as a faun: `from: [{species: "human", drop: ["legs"]}, {species: "deer", take:
["hind"], at: [0, -0.3, 0]}]`, a biped on hoofed legs. Read as half and half side by side, it's not a body plan
and Claude should ask. **Six-legged bears** (Andrew, 2026-11-01): the bear with its `fore` slot taken twice, once
at a mid `at`. A **seahorse** or **flying fish** stays `builders.md`'s rows.

**What looks wrong first.** Until gaits are by rule (`builders.md` §3), legs move by their own poses and
`phase`: a centaur's front pair keeps the deer's phase, which is right; a faun's hoofed hind legs step as a
deer's rear pair, half a cycle apart, which reads as a walk. The torso bobs with the deer's back (§3's
"torso held to world up" fixes it later). A torso set too far back clips the withers: `describe` prints the
flattened slots' world positions so Claude can nudge `at`.

### 3. How Claude asks for one, and where it shows

`list fauna` and `list parts` (id, name, role, slots); `describe fauna deer` (the flattened row, each slot's role
and where it sits). `add_species(id, name, from, size?, coat?, move?, behave?, spawn?)` and `set_species(id,
field, value)`; `validate` flattens and checks (every part exists, `on` resolves, budgets). Headless, `simulate`
can't draw, so `describe` gives a text shape: "4 legs in 2 pairs, 2 arms, 1 head; 2.1 m tall, 1.6 m long".

**Where it appears**, three ways, all existing or from `events-and-placing.md`: **ambient life** through `spawn`
(a density, near water or among another species, as the human child's `among: human` at `fauna.json:295`); a
**summon** spell placing an `animal` (that design's §2a); a **still thing** with a creature look
(`{family: creature, species: centaur}`, RULES_MCP "Things"), the same everywhere. View only, so no golden moves.

**Budgets** (`budgets.json`, new `fauna`): 4 species a mod, 3 donors a graft, 40 slots after flattening (a slot
is a few capsule instances; 120 animals × 40 is the crowd cost `builders.md` §3 gates), `scale` 0.25 to 4.

### 4. Order of work

| Branch | What | Effort | Unity |
|---|---|---|---|
| `fauna-read` | `list` and `describe` for `fauna` and `parts`; `outside_rules` fixed | hours | no |
| `graft` | `from` flattening in `Fauna.cs`, checks, budgets, `add_species`, `set_species`; `GraftTests` pins the centaur's flattened row | days | no |
| `graft-coats` | `coat: own` drawn: each slot keeps its donor's palette (`CreatureBodies`) | days | yes |
| test | "make a centaur" and "a half-man half-deer" by `claude -p`, then in play | hours | yes for play |

`fauna-read` alone may be enough for Claude to hand-write a centaur; ship it first and watch.

## Part 2: things on the water's surface (#60)

### 5. What's there

The dock spell is terraform laying `wood` ("Wooden bridge", `ground`, a `deck` look of planks on two beams:
`materials.json:18`, `CellDressing.Built.cs:17`). Terraform raises the cell's **ground** (`World.Terraform.cs:16-33`;
`Stepping` already caps it on water at `:29`), so in the lake the planks sit on the bed. Building and thing sites
refuse water outright (`World.Buildings.cs:298`). What does sit on water: ice (`Frozen`, walkable through its
own collider, `CellWaterMesh.cs:352`) and covers with `float`, which drift (`World.Sim.cs:280`, `Float` at `:371`).
The core holds no lake level: a lake cell is its bed plus `Water` units (`World.cs:212-215`), and the drawn
surface is the view's (`ChemGrid.Visuals.cs:398`).

### 6. The design: a deck

A cell gains a second walkable layer **at its water's surface**: the **deck**. Two fields on `Cell`:
- `Deck`: a terrain id, or null. Any terrain whose row has a **`deck`** verb may be one: `"deck": {}` on `wood`.
- `DeckAt`: the water units above the cell's ground where it stands, set to the cell's `Water` when laid.

**Laying one.** A terraform spell (or a world edit's `lay`, or `place` with `terrain`) whose terrain has `deck`
lays it as a deck on a wet, unfrozen cell instead of raising the bed; on dry ground it lays terrain as today. So
the existing dock spell, unchanged, docks: on the shore it's a boardwalk, over the water a pier at the surface.
A building's or thing's site may be on a deck (it's open ground), and a thing whose `PropState` floats may stand
on open water (a crate, a raft prop).

**What it does in the chemistry**, one rule: **a deck is the cell's top**. Elements and reactions meet the deck's
terrain, and `water` reads false under it (`World.React.cs:180-181`), so fire burns a pier by `wood`'s `burn` and
the lake doesn't put it out; burnt or broken, the deck's `leaves` is ignored and the deck is gone, and the water
is the top again. Water flows, soaks and freezes beneath as before; a `float` cover stops at a deck's edge.
Walkers and raiders count a decked cell as ground (not `deepWater`), and it doesn't block. When the water falls
below `DeckAt`, a deck stands on posts at its height; the view draws them. A deck with `"deck": { "floats": true }`
rides the water: `DeckAt` follows `Water`, and lands on the bed when it dries.

**Rafts and lily pads**, without more core. A **raft** you ride is a floating prop (physics props already float:
`PropState`), host-run as every prop is (D4): an `entities` row with a deck look (G4 gains a `deck` family beside
`creature`). A drifting deck that moves cell by cell is possible (it's `Float` for decks) but not proposed
(question 3). **Lily pads** are ambient life already (`fauna.json:924`, `habitats: floats`); as shared cells they
are a `float` cover once covers are drawn from data (`primitives.md` §5, step 1).

### 7. Cell state, the checksum and the saves

`Deck` and `DeckAt` join `Cell` (`Cell.cs:6-54`; `CopyFrom` and `Reset` at `:72-81`), about 8 bytes on a 120-byte
object, likely none after padding. `AddCell` and `AppendCell` (`World.Chunks.cs:295-345`) add them only when set,
as `Height` and `Life` are, with new tags (`,d<id>`, `,e<n>`), so **every `CellHash` and FNV-1a golden holds**.
The save's positional tail (`World.Save.cs:319`, `:342-347`) appends both after `Cracked`, only when set, so old
saves load deck-less. D9: `Deck` is an id string like `T` until terrain ids are interned, then an index. The
sandbox is frozen (J-042). New goldens: **`DeckGolden`** (the dock spell into a pond, 300 ticks of rain and a fire:
the deck burns, the water under it doesn't steam, `CellHash` pinned) and **`DeckFallsGolden`** (a pond drained
under a deck: on posts; a floating deck lands).

**Walk-through #60.** "Build a dock off the house into the lake." Claude makes the dock spell as before
(terraform, `terrain: wood`, a line from the player), or a `lay` of `wood` cells along a line from
the house. Over the shore it's ground; over the lake each cell becomes a `wood` deck at the surface. Andrew walks
out on it, a fireball sets it burning, it falls in, and the lake is open there again.

### 8. Order of work

| Branch | What | Effort | Unity |
|---|---|---|---|
| `site-refusals` | #60's side bug: `SiteProblemOf`'s `ModException` in `RulesMcpServer.Update` becomes a refusal | hours | no |
| `deck-core` | the fields, hash and save, the `deck` verb, terraform and `lay` onto water, the top rule in reactions, fire, flow, pathing; floating things' sites; `DeckGolden`, `DeckFallsGolden` | week+ | no |
| `deck-view` | planks at `WaterSurfaceY` with a walkable box (as ice's), posts when the water drops | days | yes |
| `raft-look` | a `deck` look family for things; a raft prop by `add_entity` | days | yes |

`deck-core`'s risk is the top rule touching every pass that reads `Water`; the deck goldens and the existing ones
running unchanged are its gate.

## Words to add (`docs/GLOSSARY.md`, proposed)

**graft** (a species row built from other species' slots, `from`), **deck** (a walkable terrain at a cell's water
surface, `Cell.Deck`), **donor** (a species a graft takes slots from).

## Questions for Andrew

1. A centaur's colours: (a) each half keeps its donor's (human skin, tunic and hair; deer coat); (b) one coat for
   the whole body; (c) a colour per slot, any. *Recommend (a) as the default, (c) later if asked: it's what makes
   a centaur read as one.*
2. A dock when the lake drops: (a) it stays at its height on posts; (b) it rides the water down; (c) the row says.
   *Recommend (c), moored by default: that's a dock; `floats` makes a pontoon.*
3. A raft: (a) a floating prop you stand on, smooth, run by the host's physics; (b) deck cells drifting a cell at a
   time, shared, chunky; (c) not yet. *Recommend (a) after the dock: props already float, and it rides waves.*
