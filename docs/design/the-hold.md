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
- **The castle cliff:** the castle stands on it (Andrew, 2026-11-07). The castle's pad reaches north of it at the castle's own
  height (`pad.more` in `buildings.json`: areas in half cells from `at`, each with its own `bank`) to x 106 to 150, y 302 to 349, so
  the ground from the gate to the lip is one height: out of the gate, north along the east wall, east to the lip. An area whose
  `face` is `crag` drops off over its bank as rock (`World.LayCrag`: moved as a stamp's land is, so it paints rock, grows
  `LandRocks` and loses its trees, but no reset takes it), 20 to 30 m down to the east and the north; on the west it meets the
  hill. A `clear` area loses the map's trees. The road leaves the gate on a landing at the gate's height (`flat`: 12 half cells
  before it starts down, `RoadGrade`), so the step out of the gate is level too. On the pad, outside the walls, stands the
  castle's stable (`stable#1`, stone in the castle's coursed finish, a slate roof, three stalls facing the castle, and a fenced
  paddock with its barrels and crates beside it in `things.json`). The canyon below the pad's north face is the map's stamp.
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
- **The brook behind the castle (dry since hold-east-path):** `west-brook` of the `home-valley` theme, 2 to 4 cells wide and cut 0.5 to 2 m into the plateau, from about
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
of it the hand's other button makes a sweep and the other hand's buttons join, each starting the meter again. A tap goes off `tapTicks` after its press and a held button
from its row's `castTicks` (casting-taps), so a lone trigger isn't held back. Different gestures go off together when the longer is done, each hand cooling on its own. `range` is in cells by gesture: the view pulls the target
back along its line (`HandsDef.Within`), so it's a limit on what the caster aims, and a spell from the caster's own cell is shaped by its own numbers. The hands' layout is on
during a run and in the attuning room (`PlayerControls.HandPad`); in the room a trigger moves that hand to the next element or nothing, so a wizard there can't cast. The kit
starts as the `dpad` spells. Hand spells aren't charged and don't join the grimoire. Keyboard: the clicks are the triggers, Q and C the bumpers. Stands in the room stay in a
joiner's history (`StandKeeper`), since attuning reads them.

`HandPose` puts the hands on the body, on every copy of a wizard, from the hands in the core: each arm winds up while its gesture is under way and goes out when the core fires it, so a thrust and a throw play on their own arms, and the same gesture on both is a two-handed version. An arm with no gesture stays down. `HandHeld` shows what each hand holds, one small particle system a hand, whenever that wizard's hands are on (`World.HandsOn`). The `handlooks` probe checks both peers show both wizards' hands.

### 4. Endless, tuned

- **Waves by formula, from the rules:** count, health, kinds and adjectives grow with the wave number, per peer count.
- **Wave themes:** a wave leans on one or two adjectives, so no one hand set wins everything.
- **Warning:** between waves the castle shows what's next, in time to run to the keep and attune.
- **More adjectives and a boss every so often,** from the same rows. Raiders that swim are a stretch.
- **Routes:** raiders take the road by default. Some climb the cliff and some fly, ignoring it (Andrew, 2026-11-05).
- **Barricades:** small wooden ones appear on the road at the start of a wave. Raiders break through them, and they burn.
- **Playtest moments:** canned setups as rows (a wave, a theme, hand sets, the keep's health) to jump straight to and play, in dev builds and the Editor (Andrew, 2026-11-05).
- **Run length:** a good run lasts about 10 minutes (Andrew, 2026-11-07; it was 20), a dial.
- **More players:** raiders scale by count, a boss by health (Andrew, 2026-11-05).
- **Tuning:** a difficulty curve we can read: wave reached across test runs, the cheap playtester if it can drive this (it has no
  view). **Done when:** a good player's run lasts about the length Andrew picks, and a wrong hand set is felt.

**As built.** `World.Hold.cs` runs the rows of `rules/hold.json`. A wave comes from the rules and the world's seed alone (`HoldPlan`, `WaveRoll`), so the same seed gives the same waves however a run got there, and a moment's wave is the one play reaches. New kinds and adjectives are rows of `rules/hold.json`, not `entities.json`, which the frozen sandbox's hash still matches: `cliff-stalker` climbs, `ash-wing` flies, and Frosted, Molten, Rooted and Gaunt join the three adjectives.

**Routes.** A raider's row says how it comes (`raider.route`). A road raider's way costs `offRoadCost` a cell off the road (the row's road the map's `hold.json` names); `climb` is the straight way raiders always had; `fly` goes straight at the heart over everything, and reaches a wizard on the walls. A flyer is on its cell like any raider, so anything that lands on the cell hits it, covers and burning ground too. Frozen or wet (`flying.lands`) brings it down for good, and it walks on from there. Raiders on different ways who meet head on swap cells.

**Dials,** in `rules/hold.json`. Turn the first three first.

| Dial | Start | If you raise it |
|---|---|---|
| `health` | 240 | The keep takes more: longer runs, more forgiving of a leak (left alone it falls in wave 6) |
| `growth` | 2 | Waves get big sooner: shorter runs, more crowd spells |
| `damagePerWave` | 4 | Late raiders hit harder (%, a wave): a sharper end |
| `hpPerWave` | 2 | Late raiders take longer to kill |
| `first` | 4 | A busier opening |
| `peerPercent` | 100, 210, 300, 380 | A wave for more wizards is bigger |
| `kindEvery` | 2 | New kinds come more slowly |
| `adjectiveFrom`, `adjectivePercent`, `adjectivePerWave`, `adjectiveMost` | 2, 34, 6, 90 | More raiders with an adjective, sooner: your hands matter more |
| `themeFrom`, `themePercent` | 3, 75 | Themes bite harder or sooner: a wrong hand set hurts more |
| `boss.every`, `boss.hpPerWave` | 5, 12 | Bosses rarer, or tougher late on (and the `dread` row: hp 700, damage 200, in percent) |
| `boss.peerHealth` | 100, 190, 270, 340 | A boss lasts longer for more wizards. Normal raiders scale by count (`peerPercent`), a boss by health, as there's only one |
| `warmup`, `betweenWaves` | 60, 90 | More time before wave 1, and in the next wave's warning (part of `waveTicks`): longer to reach a tower and attune |
| `waveTicks`, `waves` | 300, 10 | A wave every so many ticks, whoever is still standing, and the last wave, which wins the run when beaten: a longer run (0, 0: the old way, endless waves that come when the last is beaten) |
| `towerReach` | 3 | More of the ground round a tower attunes |
| `spawnEvery` | 9 | Raiders come in a thinner stream |
| `offRoadCost` | 40 | Road raiders stray less |
| `barricades.from`, `first`, `perWaves`, `most`, `radius` | 2, 1, 3, 4, 2 | More barricades: road raiders are held up longer, and fire has something to clear |
| `strength` | by terrain | Walls hold for more blows. A blocking terrain with no row can't be battered, so a wizard's wall needs one or it stops a run for good |
| `targetMinutes` | 10 | What the curve aims at; nothing in the game reads it |

Numbers for a tick are 6 a second. **Moments** are rows of `moments`; the list is F8 in dev builds and the Editor, going in is a `ComboCommand` that a host takes only in a dev build or the Editor (`NetChem.ComboChannel`), so a released player never runs one, whoever asks, and `MOMENT` names one for the probe. The curve is `core/Hold.Runner`: `dotnet run --project core/Hold.Runner -c Release -- --seeds 3` or `-- --moment cinders-right,cinders-wrong`. Its wizard stands on the wall by the heart and keeps casting what hurts, and never moves or plays chemistry, so it's a stand-in: water and earth look worse in it than they should.

**What waves look like.** `HoldShow` puts the warning on the HUD and the keep's banner, and a boss's name and long bar on the HUD; a boss is drawn bigger, on one cell like any raider. `HoldBarricades` draws crossed stakes on barricade cells, charred while they burn, and stops a wizard as a built wall does. `DefenseField.Routes.cs` draws a flyer's wings, height and shadow, drops it when the core lands it, and lays a climber flat on the slope. A cliff stalker is a large gila monster and an ash wing has its own few segments until there are models.

**As built, the hold-batch (2026-11-07, from the 2026-11-06 playtest).**

- **Timed waves.** A wave comes `waveTicks` (300, 50 s) after the last one's first raider, whoever is still standing, so pushing raiders back buys no time and leaving them up doesn't hold the next back; one still coming in does. The last `betweenWaves` (90, 15 s) of that is its warning, as before. `waves` (10) is the last wave, and beating it wins the run (`HoldPhase.Won`, the points for every wave, the castle mended). The HUD and the keep's banner show "Next wave in 0:42" (`World.HoldNextWaveIn`), and "Wave 3 of 10".
- **The keep at 240.** Measured with `holdcurve` (3 seeds a set): wizards that never cast (`--idle`) lose the keep in wave 6, about 4.5 minutes in, alone or two; the bots reach the last wave in 9 to 12 minutes with fire+water or fire+air and sometimes beat it, and fall in waves 7 and 8 with earth+air or water+air. Before, the two-peer bot runs went to the 40-minute cap.
- **The crystal.** The keep has no roof and no attuning room now, so from the walls you see the heart. `HoldCrystal` stands on it, a tall crystal drawn from code: a raider striking beside the heart turns to it (`DefenseField.FaceTheHeart`), each blow flashes it, and as the keep's health falls it cracks in three stages (a quarter, a half, three quarters gone), each with dark cracks, shards and the break sound, dimming and reddening; at 0 it shatters, and a new run brings it back. The probe is `crystal`.
- **Attuning at the towers.** The map's `hold.json` names four `towers`, the castle's corner towers, each with one element: fire, water, earth, air. A wizard on a tower's ground (`World.TowerGround`: the open cells within `towerReach` of it, the courtyard's corner beside it first) attunes a hand to that element only, in its moment (`attuneTicks`), in a run or not; in a run nowhere else; outside a run, anything anywhere, as D16 has it. On the pad, a hand's trigger at a tower attunes that hand if it holds something else, and casts if it already holds the tower's element. `HoldTowers` lays the ground in the element: tiles of its colour, embers rising, puddles and drips, stones and dust, leaves blown round. The probe is `towers`.

**As built, hold-bodies (2026-11-07, from the same playtest).**

- **Raiders from the valley's animals.** Four more kinds in `rules/hold.json` `raiders`, each a role: the **blight deer** (a deer: as fast and frail as any), the **gloom wolf** (a wolf: bites the most often), the **barrow bear** (a black bear: the slowest and toughest, resists cold and earth) and the **storm bison** (a bison: comes fast and hits hardest, seldom). A raider row says how it looks: `raider.body`, the `fauna.json` species drawn for it, and `raider.size`, a percent of that species' size, so `DefenseField` reads bodies from the rules, not a list in code (the ash wing has none: it has its own wings). The map's spawner sends nine kinds now, a new one each wave (`kindEvery` 1), round and round, so the waves are mixed from wave 2 and the last has them all. `holdcurve` moved little: idle wizards still fall in wave 6 or 7, about 4.7 minutes in; fire+water wins two seeds of three.
- **The folk off the road in a run.** A job in `jobs.json` can name `inARun`, the stop its people keep to while a Hold run is on: the guards and porters to the square, the fishers to the lake house. When a run starts each walks there from wherever it is, off the raiders' road (`FolkJobs.HomeFrom`), and stays; after the run it takes its round up again when the round next brings it to that stop, so no one jumps. View only, like the rest of the jobs. The village's own folk never come near the road. The probe is `holdbodies` (and its clip).

**As built, hold-east-path (2026-11-08, from the 2026-11-08 playtest C3, B4 and W1).**

- **A second road, from the south-east.** The castle has a south gate now, opposite the stable: a timber gate in the outer
  south wall, a passage in the inner one, and two gate towers (`hold#1` in `buildings.json`). Its road (`east`, joins
  `s0`-`s5`, graded dirt like `down`) runs from it south-east toward the lake to the second spawner, `hold-east-foot` in
  `places.json` (about (212, 438) to (226, 458)). `hold.json` lists two spawners with the same kinds; the core takes them in
  turn, so every wave comes half by the foot's road to the east gate and half by the east road to the south gate. Raiders
  keep to roads (`offRoadCost`), so each half uses its own. `HoldEastTests` holds it; `AMixedWaveHashesAsItDid` moved.
- **The brook behind the castle is dry.** It was the `home-valley` theme's `west-brook` (see Where): 2 to 4 cells of river
  water in a 1.5 m channel, never reaching the lake, with its ground climbing as it ran south. A theme river can now be
  `"dry": true` (`ThemeSchema`, `BpRiver.Dry`): it lays out as before, so the land and its draws don't move, but fills no
  cell and cuts no channel. Taking the feature out instead reshaped a third of the map (the theme's draws shift). The yard
  build rewrote `cells.txt` (the brook's cells grass, a few by the tarn lake, about 90 trees elsewhere moved with the
  draws); `land.json` was re-baked along the old course only (within 6 cells, up to 2.5 m: the channel filled; a bake of the
  whole land on the Mac also takes in the castle cliff's and the canyon's stamps, which the land doesn't hold), and the
  ground pack (`ground/`) exported again, with 42 cells of the land north of the castle cliff set to it, so
  `GroundLandTests` holds on every cell. The better river comes with the world plan (W1).
- **Wading slows a little.** `SurfaceEffects` scales wading by depth: 0.92 at the water's edge to 0.78 nearly
  swimming-deep (was 0.55 at any depth). The `holdeast` probe measures 83% of walking speed on a wade into the lake.

### 5. Progress

Progress belongs to the player, not the world (J-038).

- **Inside a run:** between waves, choose one of three upgrades.
- **Between runs:** you start with one hand and one element. You unlock gestures, the second hand, then other elements,
  then kit spells. Discoveries stay.
- **Later:** castle upgrades, and things worth finding on the hill.

**As built.** `rules/hold.json` has the rows; `World.Progress.cs` runs them and `HoldProgress.cs` is the save. Inside a run, as a wave is beaten each wizard gets `offer` upgrades by the seed, its wave and its name, only rows it can use with what's unlocked (what an open hand holds, a gesture it has, both hands for a row with `"hands": "both"`), so it may be fewer; `ComboCommand.Pick` takes one, and one left open is picked for it as the next wave starts. Upgrades are percents on a number a wizard has, kept in `Hold.Wizards`, so they end with the run. A hand spell's damage goes by who cast it (`CastCommand.By`); burning and covers it leaves behind aren't scaled.

Between runs, a player's save is one number, **points**: `pointsPerWave` for each wave survived, added up. What's unlocked follows from the points and the rows, in order, so a rule change can't leave a save disagreeing with them. `ComboCommand.Bring` tells every peer; `ProgressKeeper` sends it whenever the world doesn't know, so a new world, a reset or a host's world all hear it, and writes what's been earned. A wizard nobody has brought points for has everything, which keeps bots and tests whole, and so does anyone in a moment, which also earns nothing. Dev builds and the Editor bring everything; F7 plays as the save says. **A save that won't load:** it's kept beside the new one as `.broken`, the last good copy is used if there is one, the player is told, and the file is never written lower than it was. Hosts believe what a player brings: it's a friends' game.

**On screen.** `ProgressKeeper` is the player's side, in three parts. Between waves the offer is a panel of cards, each upgrade's name and what it does in words
(`UpgradeDef.Words`, from its numbers), with the seconds until one is picked for you; 1, 2 and 3 or a click take one, and on a pad the d-pad chooses and takes
(`PlayerControls.OfferPad` turns the kit's d-pad off while the offer is open). During a run the picks so far are listed at the side. A run's end says the wave
reached, the points earned, anything newly unlocked and what's next. The Esc menu's Game page "The Hold: your progress" lists every unlock, yours or how many points
away (`World.PointsAway`, `NextUnlock`), with F7's switch in dev builds. A locked hand or gesture says so when pressed, with the points to it, and the press goes
nowhere; at a tower whose element you haven't unlocked the HUD names the points to it (the attuning room did this before the hold-batch); the HUD's hands line says "locked". Health an upgrade adds (Tough) is
named on the health bar, which glows as it grows. F7 never writes a dev build's everything into the save: nothing is saved until the world has the points just
brought. The probes are `progress` (one peer from a fresh save) and `roomprogress` (two peers with different points, checksums compared).

| Dial | Start | If you raise it |
|---|---|---|
| `offer` | 3 | More choice between waves |
| `upgrades` rows | 12 | Bigger percents make a run longer: they're worth about half a run again in the curve |
| `pointsPerWave` | 1 | Everything unlocks sooner |
| `unlocks` costs | 57 in all | A longer road to everything |

### 6. Its own map

After maps phase 8, the Hold moves to a map of its own: the same rows on purpose-built land. Expeditions from the castle
(forest, fire mountain, water, trinkets that unlock elements) can grow from here.

## Open

- Is the castle mended between runs, or does damage carry through a session?
- Which spells make the kit?
- What does a player in the Hold do to anyone in the sandbox? Today a run is just something happening in the shared world.
- Should hands reach the sandbox if they feel good?
