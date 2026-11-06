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
  site, x 87, y 367, where the plateau's edge hid it from the valley floor. The gate faces east down the slope. Its road leaves Hollowmere's south lane, runs through the woods
  to (176, 345), and climbs in three hairpins to the gate. Its trees are gone by being laid over (`WorldBuildings.LayInto`): `cells.txt` and `things.json` don't change.
- **The road:** cut into the hill, not laid on it. A road marked `graded` is level across and climbs evenly, with the land leaning back
  to where it was over three cells either side (`RoadGrade`). The cut comes from `buildings.json` and `land.json` at load, so every peer has
  the same one, a save carries it to a late joiner, and it lies under the stamps, so the dragon's reset keeps it. Its banks are bare earth.
- **Rocks on the hill:** `rocks.json`, drawn only, as `LandRocks` draws a crag's.
- **The clearing:** wide grass roads and a few stumps in the castle's own row, along the line from Hollowmere to the castle (Andrew,
  2026-11-05). It takes 45 of the map's 3,473 tree cells, keeps the ground the bake drew, and lies under the castle's road. It's ground, so it
  wears and burns as ground does: a row stands by what it built, not by its roads.
- **Seen from:** the shared rendered frames with it and without it, in the same instant. From the square 4.6% of the frame, a castle on the skyline
  over the roofs; from Hollowmere's west edge 7.3%; from 33 of 49 spots in the village, 29 of them over 1%. The foot of the slope up to 10%; the
  lake shore under 1%, as the clearing doesn't look that way. Raiders' routes are the same with it, and their field stops short of the village.
- **Spawners:** along the foot of the slope, from about (184, 340) to (206, 390), so raiders do not walk through the village (`hold-foot` in `places.json`).
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

**As built.** `rules/hold.json` has the numbers and adjective rows, a map's `hold.json` the castle's row, three of its joins (`heart`, `plate`, `rise`) and the
spawners, each a place in `places.json` and the kinds it sends. `World.Hold.cs` is the run. A wizard on the plate starts one, read from Stand commands, so there's no
start command. Raiders use the field's list, so spells reach them as ever, but their way is `World.Hold.cs`'s: it can cost a way through a wall, by blows (`strength`). A
wizard on a blocking cell is on the walls and no raider reaches it. An adjective's raider is an entity of its own, `kind~adjective`, so `World.Raiders.cs` and
`World.Defense.cs` are unchanged. A defense field started during a run isn't refused, and its march moves all raiders: don't run both.
A late joiner gets a run by replaying stands, so `StandKeeper` keeps every stand of a run and those on the plate.

### 3. Hands

**The button is the gesture; what's in the hand is what comes out** (Andrew, 2026-11-05, J-075). Once you've felt it, you know
what any hand will do: a trigger is short range, a bumper long range, both an area.

| Input | Gesture | Empty hand | Element in hand |
|---|---|---|---|
| Trigger | Thrust forward | Punch | Short-range spell |
| Bumper | Arm up, throw | Throw | Long-range spell |
| Trigger and bumper, one hand | Sideways sweep | Shove or swing | Area spell |

**Two hands:** the same gesture on both makes something new, from the table. Different gestures do each hand's own thing
at once, with no table entry, and the chemistry decides what they make.

Spells stay unique per element and combination, never one spell re-skinned.

| Element | Short | Long | Area |
|---|---|---|---|
| Fire | Burning Hands | Fire Bolt | Flame Wave |
| Water | Create Water | Ray of Frost | Sleet Storm |
| Earth | Raise Earth | Earthen Trench (a thrown stone would suit better) | Spike Growth |
| Air | Shocking Grasp | Gust of Wind | Thunderwave |

- **Thrust plus thrust, throw plus throw:** the 16 pairs of `combos.json` and a few same-element extras (Meteor, Flood).
- **Sweep plus sweep:** about 14 spells still to design, a third tier that can arrive as discoveries and unlocks.
- **Range is a real limit,** from the rules. Spells have none today.
- **A hand is a slot.** It holds an element or nothing today. A weapon can sit there later under the same rule: sword and spell on
  different gestures, you swing and cast; on the same gesture, fire on the sword. Not built yet.
- **The D-pad stays the kit:** the spells every wizard has.
- **Changing a hand's element happens only in the keep's attuning room,** so getting it right before the fight matters. Being
  downed lands you near it, which softens a bad choice.
- **Showing it:** each hand visibly holds its element at all times (fire in the hand, water round it, stones orbiting it, air
  curling off it), for the player and for their friends. `CastPose` already has thrust, overhand and shove gestures. GPU first.
- **A limit on casting:** a cooldown per hand, with a combined spell locking both.
- **To test on a pad:** trigger and bumper on one hand is awkward with one finger on both. If it's bad, holding the trigger
  could become the sweep.

**Done when:** Andrew has played a run on a pad and says which parts stay.

**As built.** (Andrew's rule, 2026-11-05: the button is the gesture, the element is what comes out.) `rules/hold.json` `hands` has the table: a row for each thing a hand can
hold, `empty` and the four elements, by gesture (thrust, throw, sweep), and a row for two of them together, by thrust and throw. A gesture with no combined row, like every
sweep today, leaves each hand to its own. An `empty` row is an act; a later weapon would be rows too, and nothing in code knows what a slot holds. Empty sweep is a punch for
now, and an empty hand's act takes the window's 4 ticks, since it can't go before a sweep might come. The map's `hold.json` names the attuning room by two of the row's joins
(`k1`, `k6`). `World.Hands.cs` is the hands: `ComboCommand`s `Hand` and `Attune`, so they go the way of the rest. The first press starts the cast meter; inside `window` ticks
of it the hand's other button makes a sweep and the other hand's buttons join, each starting the meter again. A spell's own `castTicks` hold the window open, so a lone
trigger isn't held back. Different gestures go off together when the longer is done, each hand cooling on its own. `range` is in cells by gesture: the view pulls the target
back along its line (`HandsDef.Within`), so it's a limit on what the caster aims, and a spell from the caster's own cell is shaped by its own numbers. The hands' layout is on
during a run and in the attuning room (`PlayerControls.HandPad`); in the room a trigger moves that hand to the next element or nothing, so a wizard there can't cast. The kit
starts as the `dpad` spells. Hand spells aren't charged and don't join the grimoire. Keyboard: the clicks are the triggers, Q and C the bumpers. Stands in the room stay in a
joiner's history (`StandKeeper`), since attuning reads them.

### 4. Endless, tuned

- **Waves by formula, from the rules:** count, health, kinds and adjectives grow with the wave number, per peer count.
- **Wave themes:** a wave leans on one or two adjectives, so no one hand set wins everything.
- **Warning:** between waves the castle shows what's next, in time to run to the keep and attune.
- **More adjectives and a boss every so often,** from the same rows. Raiders that swim are a stretch.
- **Routes:** raiders take the road by default. Some climb the cliff and some fly, ignoring it (Andrew, 2026-11-05).
- **Barricades:** small wooden ones appear on the road at the start of a wave. Raiders break through them, and they burn.
- **Playtest moments:** canned setups as rows (a wave, a theme, hand sets, the keep's health) to jump straight to and play, in dev builds and the Editor (Andrew, 2026-11-05).
- **Run length:** a good run lasts about 20 minutes (Andrew, 2026-11-05), a dial.
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

- Is the castle mended between runs, or does damage carry through a session?
- Which spells make the kit?
- What does a player in the Hold do to anyone in the sandbox? Today a run is just something happening in the shared world.
- Should hands reach the sandbox if they feel good?
