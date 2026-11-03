# Events and placing: rules that react, spells that summon, scattering

Status: design, 2026-11-03, from Andrew's play test of 2026-11-02 (#76, #74, #75). Design only. Builds on
`world-tools.md` (G1 to G6), `primitives.md`, `builders.md` and `docs/RULES_MCP.md`. J-055, J-057, J-058: the
examples are tests of the tools, so no meteor-steps, child spell or lakeside grove goes in code or base rules. Each
ends as one mod a player's Claude makes.

> "A meteor at every step I take." "The dock spell, but laying a human child." "50 trees near the lake."

## 1. Where each stopped, and what's already there

| Ask | What Claude did | What stops it |
|---|---|---|
| #76 meteor per step | Offered thicker meteor showers | No rule can react to anything but an element on a cell. Yet the step **already reaches the core**: `WizardStrike` sends `StrikeCommand.Stand` whenever its wizard steps onto a new cell (`Scripts/WizardStrike.cs:72-80`), stamped like a cast and run in batch order (`TickBatch.cs:144`, `World.Strike.cs:135-150`), and stands are in `CastHash` (`World.Combos.cs:219`) |
| #74 child from a spell | "Can't add creatures from a spell" | A spell does cells only (`World.Spells.cs:213-231`, `Land` at `:477`). Its one placing field, `scatter`, lays a cover, from the world's RNG (`ScatterDef`, `RuleTables.cs:323`; `World.Spells.cs:275-289`). Things come only from files and mods: `ThingsNow` is a pure function of rules and seed (`World.Things.cs:16-27`). A `human-child` species exists (`rules/fauna.json:295`) |
| #75 50 trees | Three solid "buildings" of `tree`, read as roofless huts | A mod can lay cells only as a building. And a `tree` cell the land didn't bake isn't drawn as a tree: the view dresses only trees that grew (`CellDressing.Growth.cs:16-17`, `materials.json:11` `baked`) |

All three are one missing idea: **actions** (do this, there) that something other than an element can start, and
a seeded way to **pick cells** for them.

## 2. The design: one action list, three ways in

**Actions.** Two, each a row: `cast` (a spell at a cell, through `World.Cast`, so a meteor is the `meteor` spell
with its crater, fire and show) and `place` (below). They are started three ways: by a **spell** (its new `place`
field), by a **trigger** (a rule reacting to an event, §2c) and by a **world edit** in a mod (§2b). Nothing else.

**Pick: cells chosen the same on every peer.** One pure function in the core, `Placement.Pick(world, area, count,
spacing, onto, seed, salt)`. The candidates are the area's cells that pass `onto` (terrain tags, and today's site
rules: on the world, active, not blocking, dry unless what's placed floats, no building or thing), each scored by
`WorldHash.Hash4(seed, salt, x, y)` (`WorldHash.cs:29`), taken best first while keeping `spacing` cells apart.
No draw from the world's RNG (`World.cs:285`), so nothing else's randomness moves and every golden holds. An area
is a cell and `within`, a shape from a spell, or a place (`places()`, with `side` and `edge: "shore"` as
`find_site` has). Cost: a 32-cell radius is about 3 200 candidates, a sort and a pass, about 0.2 ms; it runs only
when an action does.

### 2a. `place` (#74, and #75's run-time half)

```json
"place": { "animal": "human-child", "count": 1, "within": 0 }
"place": { "thing": "scarecrow", "count": 3, "within": 2, "spacing": 1, "lasts": 3600 }
"place": { "terrain": "tree", "count": 5, "within": 4, "spacing": 2, "onto": ["soil"] }
```

Exactly one of four kinds of what:
- **`terrain` or `cover`**: laid fresh on the picked cells (as a building lays: no fire, gas or water), shared
  cell state, in the cell hash like any cast. A spell that grows a grove.
- **`thing`** (an `entities` or `items` row, a creature look allowed, G4): appended to a new **`World.Placed`**
  list, core state. Each gets `ThingId(chunk, 0x40000000 + n)`, `n` the world's running count, so ids never
  clash with the file's or a mod's (`WorldThings.With`). `ThingsNow` becomes file + mods + placed; `lasts`
  (ticks) removes it. `PlacedText()` joins `CastHash`, "" when empty, so old hashes hold; the room save keeps it;
  a late joiner gets it by replaying the casts. The view needs only one change: `ThingSpawner` diffs `ThingsNow`
  after any tick whose list changed, not only at rule switches, and the host spawns as it already does for a
  mod's thing (RULES_MCP "Things", steps 2 to 4).
- **`animal`** (a species of `fauna.json`, a mod's included): view only, as ambient life is. The core lists
  this tick's summons (`World.Summoned`, cleared each tick, as `NewMeteors` is: `World.Weather.cs:69`), and each
  peer's `ValleyFauna` brings one out on that cell with its Individual from `WorldHash`. It then lives as ambient
  life: it wanders, flees and sleeps on each peer's own (J-056: fauna is per room, view only).

On a spell, `place` sits beside `effect` and runs after its element, at its cells (`count` picked among them, or
around the aim with `within`). `scatter` stays as it is, so Falling Star's goldens hold.

**#74 walked through.** "Give me the dock spell, but laying a human child." Claude reads the dock spell
(`describe spells wooden-dock`, the mod's: a line, `effect: terraform`, `terrain: wood`), then `add_spell("child-dock", ...)`
with the same shape and look, no terraform, and `place: { "animal": "human-child", "count": 1 }`; `validate`,
`simulate`, `apply`. Cast: on every peer a child walks out of the light at the same cell, then plays as children
in Hollowmere do. If Andrew wants a child everyone sees in the same place and can bump, it's `thing` with an
`add_entity` row whose look is `{family: creature, species: human-child}` (question 1).

### 2b. Scattering in a world edit (#75)

The mod's `world` section (G3, G5) gains **`lay`**: `[{ "terrain": "tree", "cells": [[x, y], ...], "seed": 7 }]`.
At the switch tick every peer lays those cells fresh, skipping any that are no longer open (a cell that burnt or
got a thing since; the same skip on every peer, logged: "3 of 50 trees had no room"), and keeps what stood under.
A revert lays it back, as buildings do (`World.Buildings.cs`, RULES_MCP "Buildings"). Cell state, so it's in
the cell hash from that tick; the rows are in `Rules.Fingerprint`.

The picking happens once, when Claude asks: the MCP's **`scatter`** runs `Pick` against the real valley
(`GameRoomDesk`, as `find_site` does) and writes the cells into the draft. So the mod is the 50 cells: readable,
editable ("move them back from the water" is a new `scatter` with a new `seed`), and a revert is exact.

**#75 walked through.** "Put 50 trees near the lake." `places()` gives `lake`; Claude calls `scatter(terrain:
"tree", count: 50, near: "lake", edge: "shore", within: 12, spacing: 2)`; the reply says "50 trees on soil
around the lake, 2 cells apart, the nearest 1 cell from the water". `validate`, `apply`. Every peer lays 50
`tree` cells. They block, burn to stumps and topple to Move Earth as any tree, since they are the `tree` terrain.
**The view's half:** `CellDressing` dresses a `tree` cell as one of its chunk's valley trees whenever the land
didn't bake a tree there, not only when one grew (`IsGrownTree`, `CellDressing.Growth.cs:16`). Bushes are
`brush` the same way. No grove is in code; "50 bushes in the meadow" is the same call.

### 2c. Triggers (#76)

A new rules table, **`rules/triggers.json`**, empty in the base rules; a mod adds rows:

```json
"meteor-steps": { "on": "step", "who": "Andrew", "every": 3, "chance": 1,
                  "do": [{ "cast": "meteor", "at": "ahead", "by": 2, "delay": 6 }] }
```

- **`on`**: the event. **`step`** first: a player's stand moves to another cell (`ApplyStrike`, a `Stand` whose
  cell differs from the stored one; not a first stand, not while downed). The event carries the player, the cell
  and the way they moved. Later events go in only when an ask needs one, each a place in the core that already
  runs on a command: `cast` (a player casts), `enter` (a step into a place), `strike`, `found`.
- **`who`**: a player's name (the asking player, from `here()`, for "every step **I** take") or `"each"`.
- **`at`**: `here`, `ahead` or `behind` by `by` cells along the step, or `around` within N (through `Pick`).
- **`do`**: one or two actions. `cast` takes `delay` (ticks): the cast goes through the delayed path Falling
  Star uses (`AimStar`, `World.Spells.cs:238`), so a target mark glows, then the meteor lands where the player
  was heading. A meteor with no delay lands at once.
- **`every`** (ticks between firings, per player and trigger) and **`chance`** (from `WorldHash` of the seed,
  the trigger, the tick and the player: no RNG draw). The cooldowns are core state: `TriggerText()` in
  `CastHash` ("" with none) and in the save.

**Lockstep and D4.** A trigger fires inside `ApplyStrike`, in batch order, and its actions run there as a cast
does. The step's cell is the player's own input, as a cast's position is: physics decides nothing shared. Every
peer runs the same batches (`TickBatch.Prepare`), so the same meteors land; a late joiner replays them.
**No chains:** an action raises no event. But a meteor's knockback can move a player, which is a new step: that's
a loop only `every` bounds, which is why `every` has a floor. The view learns of triggered casts from a list the
core keeps this tick (`World.Triggered`, as `NewMeteors`), and `SpellVisuals` plays each cast's show.

**#76 walked through.** "Make a meteor fall at every step I take." `here()` says who's asking; Claude calls
`add_trigger("meteor-steps", on: "step", who: "Andrew", do: [{cast: "meteor", at: "ahead", by: 2, delay: 6}])`;
`validate` notes the floor ("at most one every 3 ticks: two a second at a run"); `simulate(walk: true)` walks
a loop on the stand-in valley for 600 ticks and reports 180 meteors, 40 cells burning, 1.3× slowdown; `apply`.
Andrew runs and the valley burns behind him. "Only when I run" or "only in the woods" is `onto` or a later event.

## 3. D9 and the tables

`triggers.json` is read by the core, so it's in the fingerprint; at each rule switch the rows are indexed by event
kind into an array (seam 2: integer kinds in hot state, the table in the rules), so a step with no trigger costs
one array read. `place` and `lay` name rows by id like every table; the checker refuses a dangling one. Placed
things have integer ids. Actions enter only as commands: the trigger runs inside a command, never on its own.

## 4. Budgets (`budgets.json`, base rules only)

- `triggers`: 4 a mod, 2 actions each, `every` at least 3 ticks, at most 4 firings a tick in the room (later
  ones that tick are dropped, the same on every peer), cast spells at most level 7 (Meteor).
- `place`: `count` at most 8 an action, `within` at most 8; at most 64 placed things live in a room (the oldest
  lapses first), `lasts` at most 21 600 ticks (an hour); at most 24 summoned animals on a peer (the view's cap).
- `lay`: 256 cells a mod, `within` at most 32 for `scatter`.
- `simulate`'s 2× slowdown gate as now, with a scripted walk when the draft has a `step` trigger.

## 5. What Claude gets

`list triggers`, `describe triggers <id>`; `add_trigger`, `set_trigger`, `remove`; `add_spell` and `set_spell`
take `place`; `scatter(what, count, near | at, within, spacing?, side?, edge?, seed?)` writing `lay` (terrain or
cover) or `add` (things) rows; `simulate(walk?)`. `outside_rules` drops "area fill" and "player movement". Tool
descriptions name the three asks' shapes, as `add_item`'s names the creature look, so Haiku finds them.

## 6. New golden tests (none move)

- **`StepTriggerGolden`**: a meteor-steps mod, a scripted 40-step walk as `Stand` commands; `CellHash` and
  `CastHash` pinned after 300 ticks, and a `Timeline` replay matches cell for cell.
- **`PickGolden`**: a digest of `Pick`'s cells for fixed seeds and areas (pure, so cheap and exact).
- **`LayGolden`**: 50 trees laid by a mod: `CellHash` pinned; reverted, back to the starting hash.
- **`PlaceGolden`**: a spell placing 3 things: `CastHash` pinned, `ThingsNow` ids; lapsed after `lasts`.
- Every existing golden holds, since the new texts are empty in worlds without these. The sandbox is frozen (J-042).
- **By a real client**, as the golden squirrel was: each of the three asks by `claude -p`, ending as one mod.

## 7. Order of work

| Branch | What | Effort | Unity |
|---|---|---|---|
| `place-core` | `Pick`, the `place` field, `World.Placed`, `World.Summoned`, budgets, `PickGolden`, `PlaceGolden` | days | no |
| `world-lay` | `lay` in the world section, the switch and revert, `scatter`, `LayGolden` | days | no |
| `place-view` | `ThingSpawner` diffs per tick; `ValleyFauna` brings out summons; trees dressed where none baked | days | yes |
| `triggers-core` | `triggers.json`, `step`, `cast` and `place` actions, cooldowns, `add_trigger`, walk in `simulate`, `StepTriggerGolden` | days | no |
| `triggers-view` | triggered casts' shows from `World.Triggered` | hours | yes |
| `asks-test` | the three asks by `claude -p`, then in a play test | hours | yes for play |

The first two and `triggers-core` are independent and can go to different workers; the views wait on their cores.

## Words to add (`docs/GLOSSARY.md`, proposed)

**action** (`cast` or `place`, what a spell, trigger or world edit does), **trigger** (a `triggers.json` row
reacting to an **event**), **step** (a player's stand moving to another cell), **summon** (`place` of an animal or
thing by a spell), **scatter** (picking cells over an area by `Pick`), **lay** (a world edit setting cells).

## Questions for Andrew

1. A summoned creature: alive, or the same for everyone? (a) alive as ambient life: it starts on the same cell
   on every peer, then each peer's copy wanders on its own; (b) a still thing everyone sees in one place, can push
   and pick up; (c) both, the mod choosing. *Recommend (c), building (a) first: it's view only and a child that
   plays is what the ask means.*
2. "Every step I take": whose steps? (a) only the asker's, by name; (b) everyone's; (c) the mod says, the asker's
   by default. *Recommend (c).*
3. How hard a cap on triggers? (a) a floor of 3 ticks between firings and 4 a tick in a room; (b) no floor,
   only `simulate`'s slowdown gate; (c) a floor the room's host can lift. *Recommend (a): a meteor every step at
   a run is already two a second.*
