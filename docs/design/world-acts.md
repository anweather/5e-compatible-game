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

## Next: the ball in the cup

The north star: "spawn a cup, spawn a ball, hit the ball into the cup, and fireworks go off".

- **Named acts.** A trigger with `on: "called"`, and a tool to fire one at a place: for a custom thing worth defining once,
  like "my fireworks". For 20 trees, `act` is enough.
- **More events.** Triggers know only `step`. This needs something like "this thing entered that cell".
- **A ball anyone can place.** `World.Golf.cs` has a ball, a tee and a hole, hard-coded. Physics can't decide shared
  state (D4), so this generalises the golf ball's own simulation: rolling balls, not any physics object.

## Later

- **One animal** ("the turkey near me"). Animals live in each peer's view, so there's nothing shared to name. It needs a
  way to promote one into shared state with its own row. The hardest design question here.
- **The water level.** The lake's level is fixed in the view (`ValleyChemistry.lakeLevel`), not a rule.
- **Real mountains.** Ground runs from -6 to +8 height steps (about -3 m to +4 m). Hills and trenches can come to `act`
  first; a mountain needs a bigger range.
- **Undoing animals.** It needs the view to take back what it brought out.

## Also seen in the logs

Claude said there's no dragon (retest: it may be an older build). Rainbow meteors were refused though the spell has a look
colour. A creature's scale stops at 4 for `add_entity`-style requests but deer went 5× and children 10×. Buildings can't
stand on water, so all three dock requests became workarounds. Claude's notes listed a mod the game didn't have.
