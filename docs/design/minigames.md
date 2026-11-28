# Mini-games as add-ons: the seams

Golf now, frisbee and bowling next (D22). A mini-game is an add-on: its own core project (`Runtime/<Game>/`, the
`Chem.Play.<Game>` assembly), its own test project and its own view folder. It changes nothing in Sim
(`Runtime/*.cs`, the `Chemistry` assembly, `core/Game.Core`), and a change to it lands in about a minute because its
gates run only its own tests. This file is the seams that make that true, and a recipe for the next one.

Until this, golf was woven into Sim (`GolfSystem` owned by the world, its ball named by spells, strikes, the dragon,
acts, the save and the hash). A first move of it into `Chem.Play` worked with every hash the same, but stopped on
three things (`docs/design/architecture-review.md` step 9): the lockstep tests played golf with helpers only
`Chem.Lockstep.Tests` had; the rules MCP's rooms (compiled into Game.Core) placed golf, and Rules- and Sim-level tests
use them; and a mode's state could only live in Sim, because only Sim saves and hashes. Each seam below takes one of
those away.

## The contract: `WorldMode` (Sim, `Runtime/WorldMode.cs`)

A mode is a class deriving from `WorldMode`, made for one world. Its members are virtual with nothing to do by
default, so a mode overrides only what it uses; overriding one is how it subscribes. The world calls them, in the
order the modes were installed, at fixed places in the tick and the commands. No reflection, no registration by
name, no statics: the installed list is the world's own field.

- **Installing.** `world.Install(mode)` once per mode, before the world ticks; a mode of the same type twice throws.
  `world.InstallModesOf(other)` gives a joiner's world the modes of the world it replaces, each by `MadeFor(world)`,
  in the same order; a snapshot join does it before it checks the joined world against the host's checksum
  (`JoinSnapshot.Join(..., modesOf)`), since a mode's slot is in the checksum. `world.Mode<T>()` finds an installed one. `PlayModes` (the Hold, its progress, the defense
  field, the course) is a `WorldMode` too, the only seam the Hold has (`IWorldModes` is gone: "The Hold on it", below).
- **The slot (save, hash, text).** `SaveSlot(save)` writes the mode's keys into the world save, `SlotText()` its part
  of the casts checksum text (`CastText`), `SaveStart(start)` its part of the dragon's start, `KeepStart()` copies its
  start. The world calls each installed mode at one place: where golf's own calls were (the save between the
  machines and the dragon's start; the text between the boulders and the zones; the start's first key). Golf is the
  only slotted mode, so the save, `CastHash` and every join and lockstep hash are byte for byte what they were.
  A world loads before anything is installed on it, so `World.Load` keeps the parsed save until the first tick and
  `Install` hands it to the mode (`LoadSlot(save)`, which reads its keys and its start); a mode installed on a fresh
  world gets `null`.
- **Events** (what Sim raises; each a virtual on the mode):
  - `MapLaid(dir)`: a map's files were laid on the world (`world.MapLaid(dir)`, called where golf's `golf.json` was
    read: `GeneratedSource.MapWorld`, `ChemGrid`'s world makers, the tests' `BuiltWorlds`). The mode reads its own
    file there.
  - `ChunkReset(cx, cy)`: the dragon put a chunk back as it started, where it put the ball back.
  - `SpellStruck(body, spell, dir)`: a forcing spell (`force` in spells.json) reached a body's cell, from a cast,
    a wave or a boulder (`RaiderSystem.Force`, before the raider on that cell).
  - `SpellLanded(body, spell, cells, landed, dir, charge)`: any spell landed on cells holding a body (after the
    raiders are blasted, before the forced cells).
  - The body events below, from the body's step.
- **Acts.** `KnowsAct(kind, what)`, `Act(kind, what, cells)` (cells placed, 0 for refused) and `ActStays(kind)` (why
  it can't be taken back, or null). `World.Acts.cs` asks them for any kind it doesn't know itself, so the act
  "golf" (a ball, a cup) is golf's.

## Bodies (Sim, `Runtime/Body.cs`, `World.Bodies.cs`)

A **body** is a thrown or rolled thing the core steps: integer cell, direction, roll (cells left to roll), carry
(cells left in the air) and age. A mode owns its bodies (`WorldMode.Bodies`, in the mode's order) and keeps them in
its slot: the world never saves or hashes a body itself. Each tick, at the place golf's tick was (after the
boulders), the world steps every moving body of every mode, modes in install order:

- a body moves once every `BodyEvery(body)` ticks (golf: `sim.json` `golf.every`), while `BodyMoves(body)`;
- **in the air** it carries over water and hills, turning back off what blocks it (rock, a raised wall);
- **on the ground** it rolls on, paying a cell per step plus the climb (the land's height steps), getting the drop
  back going down, turns back off what blocks it (losing a cell of roll: `BodyBounced`), and when it runs out it
  runs on downhill, a cell at a time, while a lower neighbour is open;
- it stays where `BodyMayEnter(body, x, y)` says (golf: the course's box) on active cells in bounds; going past is
  `BodyLeft(body)`;
- after every move on the ground, and where a flight comes down, `BodyArrived(body)`: the mode looks at the cell
  (water? the cup?) and returns true when it took the body out of play;
- a step that ends with the body at rest raises `BodyStopped(body)`.

`body.Launch(dir, roll, carry)` starts one going. A frisbee's glide is a long carry with its own `BodyEvery`; a bowling
ball's hook is the mode turning `body.Dir` in `BodyArrived`; pins are bodies too, knocked by the ball's
`BodyArrived` on their cell. None of it is engine code.

## Targets

Golf's cup is a point in its own state, not a cell: making it a rules tag would put new data on a cell (and in the
cell hash), which isn't a pure refactor, so it stays the mode's. The one cell test a body's step needs, water, is
the cells' own (`World.InWater`). A mode that wants a tagged target (a bowling lane's gutter) reads a terrain's or a
cover's `tags` in `BodyArrived`, as anything else in the core reads tags.

## The rules MCP's rooms

`Workbench.Modes` (a `Func<World, World>`, set by whoever makes the workbench) installs modes on each room it makes.
Game.Core doesn't reference any mode. The rules MCP server (`core/Rules.Mcp`, `Program`) installs golf alone, so its
rooms place a ball and a cup as before and still throw on the Hold (as since cd990cc1); the game's chat goes to the
game's own world through its desk, so it has every mode. A test of golf in a room lives with golf
(`BallInCupTests`, in `core/Chem.Play.Golf.Tests`).

## Tests: the lockstep kit

`LockstepKit` (`unity/Assets/Prototype/Chemistry/Tests/LockstepKit.cs`; `core/Chem.Lockstep.Kit` in dotnet, a library
the test projects reference, with no tests of its own) is the late-join harness: `Stamp`, `HostRun`, `Host`, `Batch`,
`Parts`, `FirstDifference`, `SnapshotDifference`, `AssertJoinersMatch`, `Replayed`. `Chem.Lockstep.Tests` and a mode's
tests both use it, so a mode proves its own joins (golf's `GolfJoinTests`). Every Chemistry test file still compiles
into exactly one project (`core/Chem.Tests.props`).

## Golf on it

`Runtime/Golf/` is `Chem.Play.Golf` (its asmdef; `core/Chem.Play.Golf`), on `Chemistry` alone: `Golf.cs` (the
command and the state; the ball is a `Body`), `GolfMode.cs` (what was `GolfSystem`), `WorldGolf.cs`
(`world.Golf()`, `ApplyGolf`, `GolfWaiting`, `GolfMode.Of(world)`). Its own project, not `Chem.Play`, so a golf change
runs golf's tests and not the Hold's, and a Hold change doesn't run golf's. Its rule numbers stay in `sim.json`
`golf` (`GolfParams`, read with the rules). Its tests are `Golf*.cs` in the Chemistry tests (`Chem.Play.Golf.Tests`).
The view's half (`Scripts/Modes/GolfCourse.cs`, the probes) reads it through `world.Golf()` and draws through
`ChemGrid.GolfShow` as before.

Who installs it: the game's world makers (`ChemGrid.WithModes`), the playtest desk (`Playtest.Text`), the rules MCP
server's rooms, and golf's own tests. A world without it has no golf: no ball, no act, nothing in its save.

## The Hold on it (modes-out, 2026-11-28)

`PlayModes` (Chem.Play) owns the Hold's, its progress', the defense field's and the course's state, as `GolfMode` owns
golf's: `HoldSystem` (the site, its field and road, the run with each wizard's offers and picks, the plate, the
battered walls), `ProgressSystem` (each wizard's points between runs), `DefenseSystem` (the defense state) and
`CourseSystem` (the zones, the layout, the runs). Their save, text and hash are written by the mode, so a change to
them is a Play change and never touches `World.Save.cs`. Nothing moved in the bytes:

- **Places** (`SaveAt(place, save)`, `TextAt(place)`): this state was the world's before it was a mode's, and its keys
  sat at several places in the save and the text, not where a mode's slot is. So the world calls its modes at each of
  those places too: the save's head ("defense", which the world writes as null for a mode to fill), after the open
  world's fills ("hold", then "progress"), after the dragon ("course"); in the casts checksum text after the slots
  (the zones, the runs), after the stands (the Hold), before the hands (the points, the run's wizards); and the
  defense field's own hash (`World.DefenseHash`, "-" when no mode writes). A new mode writes in its slot, never at a
  place: the places exist so the Hold's bytes stay where they were.
- **What the world asks as it runs** (each a virtual, answered by the first mode that takes it, or by any): `Tick()`
  (the Rest stage after the raiders), `StartMoment`, `Apply(DefenseCommand)`, `Apply(CourseCommand)`, `ZoneAgainst`
  (a spell zone against a cast), `ReadsStand` (the stand keeper's), and the hands': `HoldsBack` and `Opens` (what a
  wizard's points lock in a run), `Bring` and `Pick` (the commands), `KitFor` (the kit's d-pad), `AttuneBarred`,
  `UpgradesOf` (the picks, which change cooldowns, range, power and health). `MapLaid` lays the map's `hold.json`.
- **Reads** from outside the core are `WorldPlay` extensions: `world.Hold()`, `HoldSite()`, `HoldRunning()`,
  `HoldLeft()`, `Defense()`, `DefenseLeft()`, `TowerOf`, `TowerGround`, `RunOf`, `SetHold`, `PointsOf`, `UnlocksOf`,
  `PointsAway`, `NextUnlock` (properties on the world before; a world without the modes reads null, false, 0 or
  everything unlocked, as it did). `World.AllPoints` is `ProgressSystem.AllPoints`, `HoldReadsStand` is `ReadsStand`.
- **A world without `PlayModes`** has none of it: `SetHold` throws (it's the mode's state), a defense start throws,
  a save's "hold", "defense", "course" and "progress" wait for the mode until the first tick and are dropped if none
  takes them (as golf's are). `HoldTests.AWorldWithoutItsModesWontQuietlySkipTheHold` proves the first two, and
  `SaveTests.ASaveOfALoadIsTheSaveAgain` installs the modes on the loaded world before it saves again.

What stayed in Sim, and why:

- **The dragon** (`DragonSystem`, `World.Dragon.cs`). Its save is one key with the change audit (`"audit"`:
  `[flights, auditAt]`, then `"dragon"`), its tick is the resumable dragon stage (`RunDragon`: a few chunk resets a
  tick, so `StepSome` can stop between them; no mode hook reaches inside a stage), its hash is a named part of every
  join snapshot and lockstep checksum (`JoinSnapshot.PartNames` "dragon"), its summons come from the Sim audit (the
  rules' `dragon.budget`), and every world has it, the rules MCP's rooms and any `new World` included: as a mode,
  each of those would need it installed or would behave differently. Moving it means a stage hook, a hash place and a
  split of the audit key, each its own change.
- **The raiders** (`RaiderSystem`). About forty calls in twelve Sim files reach them: spells (`RaiderAt`,
  `DamageRaider`, `LandOnRaider`, `ApplyRaiderElement`, `Force`, `Hurl`, `ShoveRaider`, `BlastRaiders`), strikes,
  reactions, flashes, waves, boulders, machines, buildings, the dragon's breath and the Rest stage itself (a cell a
  raider stands on); they're saved in the save's head ("nextEntityId", "raiders") and the rules' entity rows are read
  by the rules engine. The bodies' events cover a point strike and a landing, not damage, statuses, elements, blasts
  or blocking, so raiders as bodies would need a dozen new events: not a pure refactor.
- **Weather and buildings.** The weather is the world's physics (rain, wind, haste reach every cell), in the save's
  head and its own checksum part (sky). Buildings are laid from the map's rows and woven into the load's order and
  the dragon's start (`NoteBaseBuildings`, `BuildingsIntoStart`); the Hold's castle reads them. Neither is a mode.

## Adding a mini-game: bowling

A recipe to follow step by step. Nothing in `Runtime/*.cs` (Sim) changes; if a step seems to need it, stop and add a
seam to the contract first, as its own change, with its own proof.

1. **Words.** Add the game's new words to `docs/GLOSSARY.md` (lane, pin, frame...) before naming anything.
2. **The rules.** Its numbers go in `rules/sim.json` under its own key through the rules MCP (D13), with a params class
   beside `GolfParams` in `Runtime/RuleModes.cs` and a line in `RuleTables` that reads it (a Rules change, not Sim).
3. **The core project.** `packages/com.anweather.chemistry-core/Runtime/Bowling/` with
   `Chem.Play.Bowling.asmdef` (copy golf's: `Chemistry`, `noEngineReferences`, Newtonsoft), and
   `core/Chem.Play.Bowling/Chem.Play.Bowling.csproj` (copy golf's). C# 9, netstandard2.1, deterministic: integer
   state, the world's RNG only, ordered lists.
4. **The mode.** `BowlingMode : WorldMode` with `Install(world)`, `Of(world)` and `MadeFor`. Its state is plain data
   (`Bowling.cs`); the ball and the pins are `Body`s it owns (`Bodies`). Override what it needs:
   - `SpellStruck` / `SpellLanded` to bowl (`body.Launch`),
   - `BodyEvery`, `BodyMayEnter` (the lane), `BodyArrived` (a pin's cell: launch the pin; the gutter; the hook by
     turning `Dir`), `BodyStopped` (the frame's end), `BodyLeft`,
   - `KnowsAct` / `Act` / `ActStays` for placing a lane by an act,
   - `MapLaid` to read `world/maps/<id>/bowling.json` if a map has one,
   - `ChunkReset` and `KeepStart` / `SaveStart` if the dragon should put it back.
5. **The slot.** `SaveSlot` writes one new key (`"bowling"`), `SlotText` a `|w...` part, `LoadSlot` reads both (and
   the start). A new mode's slot comes after golf's, so an old save and every hash without bowling are unchanged; a
   world with bowling has new bytes only where bowling is played.
6. **Extensions.** `WorldBowling.cs`: `world.Bowling()` and the command methods the view and tests call.
7. **Tests.** `unity/Assets/Prototype/Chemistry/Tests/Bowling*.cs` (`[Category]` on each fixture), a
   `ChemTestsBowling` list in `core/Chem.Tests.props` (and in its Sim remove line), and
   `core/Chem.Play.Bowling.Tests` (references `Chem.Shared.Tests`, `Chem.Lockstep.Kit`, `Chem.Play.Bowling`; add its
   name to `core/TestFriends.cs`). Install the mode in each world a test makes. A golden of `SlotText` and
   `CellHash`; a save round trip; a late-join test with the kit (`Host`, `AssertJoinersMatch`, the joiner's world
   from `build` installs the mode); a dragon reset if it has one.
8. **The map.** `tools/workers/modules.json`: a `runtime-bowling` module (`Runtime/Bowling/**`, projects
   `Chem.Play.Bowling`) before `runtime-sim`, `core-bowling` and `core-bowling-tests`, `tests-bowling` (props
   `ChemTestsBowling`). Add both projects to `core/Game.Core.sln`. Run `node tools/workers/modules.mjs check` and
   `node --test tools/workers/*.test.mjs`.
9. **Install it.** One line where the game composes its modes: `ChemGrid.WithModes` (the view's world makers) and
   `Desk.Fresh` (`core/Playtest.Text`); `core/Rules.Mcp/Program.cs`'s `Modes` if the MCP's rooms should have it
   (then add `"stop": ["Rules.Mcp"]` to its module as golf's has). A joiner's world copies them.
10. **The view.** `unity/Assets/Prototype/Scripts/Modes/` (or its own folder and asmdef): a MonoBehaviour that draws
    `world.Bowling()`, joined by a hook it sets as it loads (as `GolfCourse` sets `ChemGrid.GolfShow`) or a
    `[GridAddOn]`; the asmdefs that use it (`Prototype.Modes`, `Prototype.World` for `WithModes`, the probes, the
    Editor tests, the Chemistry tests) reference `Chem.Play.Bowling`. A probe in `Probes/` with `[ProbeScenario]`.
11. **Land.** The gates for a `Runtime/Bowling/` file run its tests and the Unit smoke: about a minute.

## Proof of the move

What was proved when golf moved (2026-11-27, branch `minigame-seams`) is in `docs/design/architecture-review.md`
step 9's golf note.
