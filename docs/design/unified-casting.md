# Unified casting: one way to cast in the world and the Hold

Status: **accepted** (Andrew, 2026-11-07: "Lgtm. Let's do it"), as D16. The build is three queued briefs:
`unified-casting-core` (core and rules), `unified-casting-view` (the controls, icons and spell list, after the core half
and `hold-ui` land) and `aimed-spells` (lock-on, pitch, Gust and Whirlwind).

**Core half built (2026-11-07).** Each hand has its own meter (`PlayerHands`): `ComboCommand.Hand` is a tap, `HandDown`
charges and `HandUp` lets go. A merge is `PlayerCast.Merge` while the hands hold it, and the hands are on everywhere
(`World.HandsOn`). Attuning outside the Hold takes `attuneTicks` (4, under a second). Fireball went from 8d6 to 4d6, half
the hit, then to 3d6 and 15 ticks, with Meteor resting both hands 90 ticks (`spell-balance`, 2026-11-08). Dust Storm and Sand Blast are out of the table, fire and air are swapped, and the `held` variants are gone. The
ordered `pairs` path (`LoadElement`) stays until the view half lands. The view still sends taps and attunes only in the
room, so until then free roam casts with the start hands (water and fire).

**Aimed spells built (2026-11-07, `aimed-spells`).** Lock-on and pitch are the view's (`SpellCaster.LockOn.cs`): aimed above
everything, the crosshair lands under where the camera looks; Lightning locks onto the raider nearest the crosshair within
14 degrees, the other aimed spells that hurt or shove within 5, aimed where the raider will be when the spell lands. In the
core: a line cast from up on a wall goes over it, and a line reaches flyers over what stops it (Lightning arcs to one a cell
beside its way); a tapped Gust knocks a raider 4 cells back (8 held), half that the second time, never a third, staggers it
a second and can throw it over a drop of 2 m or more (`World.CastShove`); Whirlwind lifts a creature in its eye for two
seconds, carries it, then flings it 6 cells and drops it (`World.LiftInTheEye`). Probe: `aimed` (`aimedclip` films it).

## Why

Andrew, after the 2026-11-06 playtest (`docs/playtests/2026-11-06-human-results.md`): keep the best of both.

- **From free roam, the feel.** You press, the element gathers, you hold and it charges, the pad rumbles, and it's clear
  what's happening.
- **From the Hold, the hands.** You only ever have two elements attuned, one per hand. The button says the range, and the
  element says what comes out.

Today these are two systems. Free roam has four element buttons, ordered pairs and a charge. The Hold has two hands and
gestures, and no charge (`docs/design/the-hold.md` section 3).

## Decided (Andrew, 2026-11-06)

1. **The Hold's buttons, with a charge.** On each hand the trigger is the **thrust** (short range) and the bumper is the
   **throw** (long range). Both on one hand is the **sweep** (an area spell). Holding a gesture charges it, using the
   world's charge (three levels, `chargeTicks` each, rumble on each level). **Releasing fires it.** A tap fires at charge 1.
2. **Hands merge while both charge.** If both hands are charging the same gesture, and the hand table has a row for their
   two elements, their icons **slam together** into the pair's icon while you hold. Letting go of either hand fires the
   merged spell, at the lower of the two charges. If they don't merge (a different gesture, or no row), each hand fires on
   its own release. So you can rapid-fire one hand while you charge a big spell on the other.
3. **Attune anywhere, from the spell list.** In free roam, open the spell list (Tab, or d-pad down) and set each hand. It
   takes a short moment, and not while you're mid-cast. **One element attuned means it's on both hands** until you attune
   a second, so fire alone makes both triggers a fire pair. The Hold keeps its own attuning place: the keep today, and per
   Andrew's idea one element per castle tower later.
4. **Design doc first,** then the PM puts the build in a lane.

## Controls

| | Pad | Keyboard and mouse |
|---|---|---|
| Left hand: thrust, throw, sweep | LT, LB, LT + LB | Left click, Q, left click + Q |
| Right hand: thrust, throw, sweep | RT, RB, RT + RB | Right click, E, right click + E |
| Charge | Hold the button(s) | Hold |
| Fire | Release | Release |
| Cancel | B / Circle | B (today's element keys Z X C V go) |
| Kit (Hasten Time, Shapeshift, Light, Control Weather) | D-pad ← → pick, ↑ cast | [ ] pick, T cast |
| Hands menu (attune: each stick its hand's element, let go) | Hold D-pad ↓ | Tab (the spell list's hands tab) |
| Spell list (attune, grimoire, every spell) | Esc menu: Game, Spells | Tab |
| Menu | Menu button | Esc, then arrow keys or WASD and Enter on every page |

Today's keyboard throw on **C** clashes with Earth. With element keys gone, **E** is free for the right throw. The **4-tick
window** stays: a sweep is the second button of a hand inside it.

**Order no longer matters.** Free roam's 16 ordered pairs become the hand table's 10 unordered pairs, two gestures each.
Pairs that differ only by order survive as one pair's thrust and throw (fire+water: Scalding Steam and Geyser).

## The icons

Each hand shows one icon, low on the screen where the element orbs are now: left hand on the left, right hand on the
right.

- **Element and gesture in one glyph.** The element sets the shape and colour (a flame, a drop, a stone, a curl). The
  gesture is a mark on it:
  - **Thrust:** a short, fat burst right at the hand.
  - **Throw:** the glyph stretched into a streak or arrow.
  - **Sweep:** an arc or ring around it.

  At rest the icon shows the element only. The mark appears as soon as a gesture starts, so you see what's coming before
  you let go.
- **Charge** is a ring around the icon that fills in three steps, one per charge level, each with its rumble beat.
- **The merge:** when the hands' charges merge, the two icons fly together and slam into the pair's own glyph (Lightning's
  bolt, Steam's cloud) with a beat and a rumble. When they don't merge, they stay apart and go out on their own.
- **Cooldown** greys the icon and refills it. A locked gesture shows a lock and a short buzz, instead of today's nothing.
- The Hold's top line ("Left: X   Right: Y") goes, since the icons say it.
- **Bottom left:** the d-pad cross stays as the kit. In the Hold, the spell selector at the bottom is hidden (Andrew: "I
  don't think I'm gonna use it").

## The spell table: fewer, and each one does something

One table serves the world and the Hold (`rules/hold.json` `hands` today; it moves to a shared rules file when built).
Andrew: "we don't need two spells that do the same thing", and the dust spells were confusing. A row stays only if it
does a job no other row does: damage close, damage far, area, control (push, slow, hold), terrain, or utility (water,
growth, light).

**Singles** (element × gesture): keep all 12. They're the base everyone learns.

| Element | Thrust (short) | Throw (long) | Sweep (area) |
|---|---|---|---|
| Fire | Burning Hands | Fire Bolt | Flame Wave |
| Water | Create Water | Ray of Frost | Sleet Storm |
| Earth | Raise Earth | Earthen Trench | Spike Growth |
| Air | Shocking Grasp | Gust of Wind (must push, see below) | Thunderwave |

**Pairs** (same gesture on both hands): today's 20, proposed down to 17.

| Pair | Thrust | Throw | Change |
|---|---|---|---|
| Fire + fire | Fireball | Meteor | Fireball tuned down (about 56 a hit, nothing else gets used): 3d6, 15 ticks. Meteor 24 ticks and its own 90-tick cooldown |
| Water + water | Wave | Iceberg | **Wow spell** (J-078): Iceberg takes the throw from Flood, at Meteor's cost |
| Earth + earth | Wall of Stone | Earth Spires | **Wow spell** (J-078): Earth Spires takes the throw from Stone Ramp, at Meteor's cost |
| Air + air | Whirlwind | Tornado | **Cut** Dust Storm. Whirlwind must lift and throw, or it's cut too. **Wow spell** (J-078): Tornado, at Meteor's cost |
| Fire + water | Scalding Steam | Geyser | |
| Fire + earth | Forge | Lava Pool | |
| Fire + air | Thunder Strike | Lightning | **Swapped** so Lightning is the long one: a lock-on spell with pitch. **Wow spell** (J-078): Thunder Strike takes the thrust from Falling Star, at Meteor's cost |
| Water + earth | Mud | Plant Growth | |
| Water + air | Frost | Rain Cloud | |
| Earth + air | ~~Sand Blast~~ | Rolling Boulder | **Cut** Sand Blast (dust) |

Free roam's held variants go: Wall of Stone at full charge no longer turns into Stone Ramp, and Frost no longer turns into
Sleet Storm. They're rows of their own now, and **charge only makes a spell stronger:** more damage, size or reach by
level, from each spell's own numbers, never a different spell. Sweep pairs stay unbuilt, as the Hold doc says: a later
tier, arriving as discoveries and unlocks.

Fixes this table depends on:

- **Gust of Wind pushes raiders back,** and can knock one off the cliff, with diminishing returns (once or twice per
  raider).
- **Whirlwind** has to visibly do something.
- **Aimed spells lock on a little:** Lightning auto-targets, and Ray of Frost and the other aimed spells pull toward an
  enemy near the crosshair. Every aimed spell gets **pitch,** so flyers can be hit. Lock-on is a view helper that picks a
  target cell, and the core still gets an ordinary cast, so lockstep is unaffected.

## The spell list, Tab and the d-pad

**One list, two ways in:** Tab and d-pad ↓ open the same spell list. It shows the two hands (where you attune), then the
table above for what you've attuned, then the kit and the grimoire's discoveries. The radial stays a debug layout (F2).

## Fixes that go now, outside this proposal

- **The "hold LB" text.** The rules MCP tells modders a new spell is "on the spell radial: hold Tab (LB on a pad)". That's
  wrong twice: LB is Fire, and Tab opens the spell list. It's in `unity/Assets/Prototype/Chemistry/Mcp/Workbench.Draft.cs`
  lines 201 and 210, its test `LiveStateTests.cs:363`, `docs/ARCHITECTURE.md:160`, `docs/SPELL_RADIAL.md` and two
  comments (`SpellCaster.cs:13`, `SpellRadialHud.cs:6`). It should say Tab opens the spell list, which is where a new
  spell appears. When this proposal lands, it changes again to how a spell sits in the hand table.
- **The menu by keyboard:** only the spell list page takes keys today (`MenuControls.cs:37-40`). Every page should take
  arrows or WASD and Enter.

## In the core

Casts stay commands, and charge is already integer ticks in `PlayerCast`, so this is a merge of two paths rather than new
machinery.

- `World.Hands.cs` gains the charge from `World.Combos.cs` (`HeldCharge`, release fires).
- The merge is a check while both hands hold the same gesture.
- The element path (`LoadElement`, the ordered `pairs` in `combos.json`) retires once free roam uses hands.

Golden tests: a charged hand cast, a merge at the lower charge, one hand firing while the other charges, and two peers
with different hands staying in lockstep.

## Open

- **What's attuned in free roam to start,** and whether the Hold's unlocks (elements, the second hand, gestures) apply in
  the world too, or the world has all four elements from the start.
- **Cooldowns:** today 12 ticks a hand and 30 for a pair. With charge, should a tap's cooldown be shorter than a full
  charge's?
- **Charge levels per gesture:** does a thrust charge as far as a throw?
- **The grimoire:** world-only spells (Hasten Time and the like) stay in the kit. Does anything else need a home outside
  the hand table?

## As built, pad-hands-menu (2026-11-08)

The playtest called the d-pad-down spell list "completely bonkers" (M1) and the screen too wordy (M2). Now:

- **Hold d-pad ↓ for the hands menu** (`HandsMenu.cs`, choosing in `Chemistry/HandsPick.cs`): two hands, the rules'
  elements round each clockwise from up; the left stick points the left hand, the right stick the right; letting go
  attunes the hands that changed, the second once the first is done (the core attunes one at a time); B shuts it without
  attuning. The wizard stands and the camera stays while it's open. In a Hold run an element a hand can't take there is
  dimmed: away from the towers nothing changes, at a tower only its element. Free roam and the Hold use the same menu.
- **The spell list moved** to the Esc menu (Game, Spells) on a pad; Tab still opens it on the keyboard. The hand table,
  the kit's list and the discoveries live there only. Without the hands (no hands in the rules) d-pad ↓ still opens it.
- **The HUD's element icons** show only while a hand is at work (its buttons held or its gesture under way), and smaller;
  the wizard's own hands show what they hold the rest of the time. The kit cross's book is two plain hands.
- **Text cut:** the cast bar's cancel hint; the "You cast …" lines on screen (the console keeps them); the log's swim,
  wade, skid, Misty Step and wall-nudge lines and the attuned line; the shapeshift's change-back key; the Esc menu's
  button hints; the spell list's explanations. Shortened: the Hold's wave line, the tower line, the offer's hint, the
  attune refusals and the locked lines.
