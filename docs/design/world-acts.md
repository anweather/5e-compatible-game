# Acts: what happens once, and what's left to build

Andrew, 2026-11-04, after reading his play sessions. "Add 20 trees near me" made a mod, and asking again made another.
41 prompts in the chat records: 25 changed something, 6 answered, 10 refused. Most of what disappointed came down to one
thing: **a mod is a rule that keeps holding, and many requests aren't rules. They're events.**

## Built (branch `mcp-act`)

- **`act` and `undo_act`.** An **act** is something that happens once, now, for everyone, with no mod: lay a terrain or
  cover, place a thing, bring out an animal, cast a spell. `Workbench.Act` picks cells as `scatter` does, from the world it
  reads, and sends them as `RoomChange.Act`, so every peer does the same at the same tick. The room's change mode decides
  it, as for a mod. `World.Acts.cs` keeps the last 32 so `undo_act` can take back ground and things. Ground goes back to
  what stood under unless something has changed it since. Animals live in each peer's view (J-056) and a cast is over, so
  those stay. `ActText` joins `CastHash` and the save, "" until an act, so no golden moves. `scatter` stays, for a mod to
  keep or share.
- **Superseded mods.** Green fire, Blue fire and Pink fire all stayed on, each hiding the last. `apply` now takes off an
  earlier mod of the same player's, made in this chat, when the new one says everything it said (`Workbench.Superseded`,
  value by value, rows by id). A list that isn't rows by id compares by content, so two scatters stay two. A saved mod
  someone switched on from the Mods page is never touched.
- **`set_species`** takes `color`, `accent`, `glow` and `density`: blue turkeys, and `density` 0 so that "turn all deer
  into centaurs" stops new deer. Every animal of that species changes; one can't be picked out.
- **`simulate`'s words.** It told Claude to "cut it down before applying it", which is why meteors on steps were refused,
  though `apply` never times a mod. It now says the number is a guide, that fire spreading costs far more than the cast
  that lit it, and to tell the player and apply unless they say otherwise. Whether meteors on steps run well is unmeasured.

## Built: the ball in the cup (branch `ball-in-cup`)

The north star: "spawn a cup, spawn a ball, hit the ball into the cup, and fireworks go off". Andrew, 2026-11-04: anyone
places a ball and a cup, and everyone takes turns "golfing", which is using any spell to move it toward the cup.

- **Placing.** `act` takes `golf-ball` and `cup` (`ball` alone stays the court's physics ball). It's the golf ball's own
  simulation, not a physics object (D4). Each placing starts a round. There's one round in a world, so placing moves the
  valley's course, and only a dragon's reset brings it back. Neither can be taken back with `undo_act`.
- **Any spell.** A spell with no force that lands on the ball sends it away from where it landed, `pushRoll + level ×
  levelRoll` cells, times a held charge, less the cells it landed off the ball, so a blast beside it is a tap. Spells with
  force go on as they did. The ball still rolls only the grid's four ways, so a placed cup takes a ball within `cupReach`.
- **Turns.** Who just hit it waits while someone else is at the course (`CastCommand.By`, from the `Caster` already on the
  wire), for at most `turnTicks`, so nobody stalls the game by walking off. There's no order among three or more. A
  delayed spell, a wave or a boulder lands with no player, so it isn't turn-checked and takes no turn.
- **Fireworks.** The view sets them off over the cup on every peer when the ball goes in.
- **`holed`.** A second trigger event: `add_trigger(on: holed)` fires at the cup for whoever sank it.

## Built: named acts (branch `named-acts`)

A **named act** is a one-off defined once and fired again by name at a place. It's a trigger with `on: "called"`, in a mod,
so a mod that's on has it and a saved mod carries it; `call_act(id, near)` fires it. The trigger shape fits with one bend:
a step or holed trigger's `do` rows are casts and places run inside a strike, from a player's stand, but a called trigger's
rows are what `act` takes (`{"act": "tree", "count": 5, "within": 8}`, `{"act": "land", "radius": 10, "rise": 12}`), because
a firing happens at a place someone names, through the room. `Workbench.PlanAct` is one path for both tools: `call_act`
works out every row against the world as it is, with `act`'s limits, and sends one `RoomChange.Act` holding the parts, so the
change mode decides it once and every peer does it at one tick. A part with no room is skipped, and the verdict says so.
`undo_act` takes one firing back whole: all that can go, or nothing if land can't go back yet; casts, animals and golf stay.
`who` says who may call it, everyone by default. Up to `triggers.maxActs` rows, `lay.maxCells` cells a firing. A named act
can't call another. Without a called trigger nothing hashes or saves differently.

## Next

- **Playing again.** After holing out, a new round needs the ball placed again. Placing only works through the chat.
- **More events.** "This thing entered that cell", for things other than the golf ball.

## Later

- **One animal** ("the turkey near me"). Animals live in each peer's view, so there's nothing shared to name. It needs a
  way to promote one into shared state with its own row. The hardest design question here.
- **The water level.** The lake's level is fixed in the view (`ValleyChemistry.lakeLevel`), not a rule.
- **Real mountains.** Built as land stamps (`docs/design/maps-as-data.md`): `act("land")`, on `land`, not a cell's height.
- **Undoing animals.** It needs the view to take back what it brought out.

## Also seen in the logs

Claude said there's no dragon (retest: it may be an older build). Rainbow meteors were refused though the spell has a look
colour. A creature's scale stops at 4 for `add_entity`-style requests but deer went 5× and children 10×. Buildings can't
stand on water, so all three dock requests became workarounds. Claude's notes listed a mod the game didn't have.
