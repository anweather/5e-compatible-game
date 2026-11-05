# The Hold: a castle on the hill, endless waves (2026-11-05)

The first real game loop (#152, Andrew, 2026-11-04 and 05): defend a castle against waves that get harder
until you fall, then come back a little stronger. The valley stays a sandbox. The Hold lives on the far side of
it for now, so it needs no map work, and moves to its own map after maps phase 8.

## The game

Raiders spawn at the foot of the hill and climb toward the castle. They hurt wizards and the castle. The waves never
end, each harder than the last, and a run ends when the castle falls. Your score is the wave you reached.

Each wizard holds one element in each hand. Choose well before the fight: changing means going back into the keep.
Each wave favours some elements (armoured raiders shrug off earth and fear lightning), so the group that covers
the most elements, and sees what's coming, does best. Chemistry stays the point: flood the road, freeze it, burn the
gate's approach.

## Where

South-west of Hollowmere (centred 222, 300), the land climbs from 0 m at about x 190 to a plateau at 45 to 49 m.

- **Castle:** on the crest, the plateau's east edge, 32 x 32 cells at x 120 to 151, y 352 to 383 (Andrew, 2026-11-05: on the skyline
  above the village). Ground 31.8 to 49.8 m: the foundations reach down to it, 18 m at the north-east tower. It moved from the flattest
  site, x 87, y 367, where the plateau's edge hid it from the valley floor. The gate faces east down the slope; a road runs from it to
  the foot at (196, 372). Its trees are gone by being laid over (`WorldBuildings.LayInto`): `cells.txt` and `things.json` don't change.
- **Seen from:** the foot of the slope best, up to 7% of the frame at (192, 360). Hollowmere and the lake shore see about 1% at most: the woods
  between hide all but a tower top or a stretch of wall, and the square sees 0.2%. The rendered frames with it and without it, in the same instant.
- **Spawners:** along the foot of the slope, from about (184, 340) to (206, 390), so raiders don't walk through the village, 48 cells north of the road's foot.
- **Slope:** about 40 m over 46 cells along the road. Raiders ignore height today (J-073: a mountain doesn't stop them, sight or
  spells), so they can climb it, but nothing slows them or blocks a spell on the way.
- **The brook behind the castle:** `west-brook` of the `home-valley` theme, 2 to 4 cells wide and cut 0.5 to 2 m into the plateau, from about
  (105, 260) south past x 57 to 68 to the map's edge. It runs about 50 cells behind the west wall.
- **Reach:** 2 chunks from a wizard at the castle should cover the foot of the slope. Phase 2 checks raiders at the far
  spawners still tick when everyone is on the walls.

## Rules for all of it

- **Additive.** The defense field keeps working as it is, and its golden doesn't move. If a shared piece must change, say so
  first. The sandbox is frozen: nothing is ported to it.
- **Data, not named code.** The castle is rows (J-055, J-057, J-058), raiders and adjectives are rows, and every wave number
  is in the rules. A spell's effect goes through the material verbs.
- **Lockstep.** Raider attacks, castle health, waves and a hand's element are all core state, changed only by
  commands, with integer state and the seeded RNG (D4, D9). Attacks use the 5E attack roll that strikes already use.
- **The sandbox's controls and spells don't change** until Andrew says the hands should spread.

## Phases

Sizes are the PM's rough guesses. Each phase lands alone with the valley still playable.

| # | Phase | Size | Where | Andrew judges |
|---|---|---|---|---|
| 1 | The castle | 4-6 days | Mac for rows, PC for the look | Is it grand? |
| 2 | The fight | 4-5 days | Mac, then PC for two peers | Is it fun with today's spells? |
| 3 | Hands | 4-5 days, beside 2 | PC (pad, animation) | Does it feel right on a pad? |
| 4 | Endless, tuned | 3 days | Mac | Do the waves make you choose? |
| 5 | Progress | 4-5 days | Mac | Do you want one more run? |
| 6 | Its own map | After maps phase 8 | PC | |

### 1. The castle

A grand set piece, standing on its own before any fight: curtain walls, towers, a gatehouse with a gate that can
open, a keep you can walk into, and a way up to the wall tops so wizards can fight from them.

- Rows in the valley's `buildings.json`, using and growing the building tables (`rules/buildings.json`, `builders.md`
  section 4). Expect new row kinds for round towers, battlements and a gatehouse. A mod should be able to build another castle.
- Real materials: stone walls in courses of blocks (a `coursed` finish on the row), a timber gate that burns.
- Look first at what the land does under it. Prefer building on the plateau as it stands, with foundations for the 3 to 5 m. If
  `land.json` must change, stop and say so: it moves the map hash.
- A road from the gate down the hill, and the keep's attuning room (phase 3 fills it).
- **Done when:** Andrew has walked it on the PC and likes it, and it holds the frame budget from the valley floor and the
  walls.

### 2. The fight

Today's spells, today's controls, the castle from phase 1.

- **Spawners:** rows naming a place, the kinds they send and their rate.
- **Raiders fight back:** they attack wizards in reach (attack roll, then damage dice), batter the gate and walls (the
  battering already exists), and go for the keep. Wizards go down as they do today and get up at the castle.
- **Castle health:** the keep has health, shown on the HUD and on the castle itself. It replaces lives. At 0, the run ends.
- **Adjectives:** a row that changes a raider: its numbers, its `weak` and `resists`, a status or a cover it leaves. Start with
  three: Armoured (resists earth, weak to lightning), Burning (lights the ground it crosses), Soaked (conducts lightning).
  The adjective shows in the raider's name above its health bar and in its look.
- **Start and end:** a plate in the castle starts a run. Raiders clear away when it ends, and the damage is mended.
- **A first endless curve,** rough: phase 4 tunes it.
- **Done when:** two peers play a run to a loss with no desync, and Andrew says if it's fun.

### 3. Hands

The pad already has an element on each shoulder button and trigger, and `combos.json` has the 4 × 4 table. So this is a change
of what the buttons mean, in a Hold loadout:

- **Each hand holds an element.** The right trigger casts the right hand, the left trigger the left.
- **The bumper modifies** that hand's spell. Both triggers together cast the two hands' combination. Mouse: left and right
  click are the hands, Shift modifies.
- **The D-pad stays the kit:** the spells every wizard has. The set is to choose in this phase.
- **Changing a hand's element happens only in the keep's attuning room,** so getting it right before the fight matters. Being
  downed lands you near it, which softens a bad choice.
- **Showing it:** each hand visibly holds its element at all times (fire in the hand, water round it, stones orbiting it, air
  curling off it), for the player and for their friends. `CastPose` already lights the casting hand. GPU first.

The spells, all existing, 28 of the 46:

| Input | From |
|---|---|
| One hand | `singles`: Fire Bolt, Create Water, Raise Earth, Gust of Wind |
| One hand, modified | Burning Hands, Ray of Frost, Stone Ramp, Shocking Grasp (to try) |
| Both hands, two elements | The pair in one order, e.g. air and fire: Lightning |
| Both, either modified | The pair in the other order, e.g. fire and air: Falling Star |
| Both, same element | Fireball, Wave, Wall of Stone, Whirlwind |
| Both, same, modified | Meteor, Flood, Earthen Trench, Thunderwave (to try) |

This needs a table in the rules (hand spells, by element and modifier) and a limit on casting: try a cooldown per hand,
with a two-hand spell locking both, so a big spell leaves you open. A one-trigger cast may have to wait a few ticks for the
second trigger: feel it on a real pad. **Done when:** Andrew has played a run on a pad and says which parts stay.

### 4. Endless, tuned

- **Waves by formula, from the rules:** count, health, kinds and adjectives grow with the wave number, per peer count.
- **Wave themes:** a wave leans on one or two adjectives, so no one hand set wins everything.
- **Warning:** between waves the castle shows what's next, in time to run to the keep and attune.
- **More adjectives and a boss every so often,** from the same rows. Raiders that fly or swim are a stretch.
- **Tuning:** a difficulty curve we can read: wave reached across test runs, the cheap playtester if it can drive this (it has no
  view). **Done when:** a good player's run lasts about the length Andrew picks, and a wrong hand set is felt.

### 5. Progress

Progress belongs to the player, not the world (J-038).

- **Inside a run:** between waves, choose one of three upgrades.
- **Between runs:** you start with one hand and one element. You unlock the second hand, modifiers, then other elements,
  then kit spells. Discoveries stay.
- **Later:** castle upgrades, and things worth finding on the hill.

### 6. Its own map

After maps phase 8, the Hold moves to a map of its own: the same rows on purpose-built land. Expeditions from the castle
(forest, fire mountain, water, trinkets that unlock elements) can grow from here.

## Open

- How long should a good run last?
- Is the castle mended between runs, or does damage carry through a session?
- Which spells make the kit?
- What does a player in the Hold do to anyone in the sandbox? Today a run is just something happening in the shared world.
- Should hands reach the sandbox if they feel good?
