# Rules MCP: changing the game through checked tools

Status: design, 2026-10-31 (Andrew: "let's do it"). Decision: ADR 0013.

## The idea

Players (and our own agents) change the game by talking to their own Claude, which calls
**tools on an MCP server we provide**. The tools are typed and every change is checked before
the game sees it. Nobody hand-writes rule JSON, so nothing gets missed: a typo, a dangling id,
a reaction that floods every cell, a rule that breaks lockstep.

It is a front door to what already exists, not a new system. Today a **mod** is a JSON patch
over `rules/` (`docs/MODDING.md`, `ModPatch.cs`); it is checked against the engine and switched
on for everyone in a room at the same tick. The MCP server produces those same patches.

## Principles

- **Only patches.** The server never edits files or runs code in the game. Its output is a mod
  patch, the format we already check and switch.
- **Typed operations, not raw JSON.** `add_reaction(a, b -> c)`, not "paste this table".
- **Check everything, explain every failure** in one plain sentence, in the game's words
  (`docs/GLOSSARY.md`), so the caller's Claude can fix it and retry.
- **Simulate before anyone sees it.** The C# core runs without Unity (`core/`), so the server
  runs a change headless for N ticks and says what happened.
- **Everything the view draws is procedural.** A new rule that makes something bigger, hotter
  or wider must show up scaled in the game with no code change and no rebuild (see "Looks").
- **The players bring their own Claude**, and the MCP runs in their own game: we run no server
  for it. Lean budget.

## Tools

**Read**
- `list(table)`: ids and one-line summaries for materials, elements, reactions, spells, groups,
  sim parameters; `list looks` gives the look families, the parameters each takes, and what
  takes no colour; `list fauna` each species' name, slot count (or what it's a variant of) and
  where it comes out; `list parts` each part's role, segments, length and who wears it.
- `describe(table, id, draft?)`: the full entry in force, which mods on changed which of its values,
  what refers to it, for a spell, material or element its look (its mix and scoped looks too) and what
  `set_look` may change, and for a spell how a player reaches it; with a draft, the entry as the draft has it.
  For a species (`fauna`) it adds the shape in a line ("4 legs in 2 pairs, 1 head, 1 tail; 1.5 m tall,
  1.8 m long, 0.4 m wide") and every slot flattened, below; for a part, its segments and every slot that wears it.
- `explain(a, b)`: what happens when a meets b, and which rule decides it.
- `room()` (phase 3): the room's change mode and the mods on in it, with who added each.
- `whats_on()`: what's on now in plain words: the room, each mod and what it changes from what
  was under it, the clock and the weather.
- `outside_rules()`: what no tool can change yet because it isn't in the rules, with the nearest
  thing for each (`Workbench.Outside.cs`, the one place that says so; when one becomes data, take
  its line out).
- `time()`, `weather()`: the clock and the weather.
- `places()`: the world's named places (`world/places.json`): each one's id, name, kind and
  cells. Villages, courses, puzzles and the water bodies are generated from the world files;
  the river and the falls are written by hand. Read-only; mods can't change them.
- `here(within?)`: where the player asking stands (name and cell), the place they're in, the
  places near them with distance and direction, and what they look at. The game says who is
  asking through an `IHere` (`Workbench.Here`); a headless run has a stand-in, the host at the
  middle of the valley's grid, or at `--here x,y` given to `core/Rules.Mcp`. `--applied <file>` appends each patch going to the room (chatbench's record).

- `find(kind?, near?, within?, side?, edge?)`: what stands in the world, nearest first: buildings (with the id
  a mod edits one by: `cabin#2`, `Hollowmere#4`) and things. `near` is a place, a building, `"me"` or `[x, y]`.
- `find_site(width, depth, near?, side?, edge?, within?)`: cells a building of that size can stand on: dry open
  ground, clear of things and buildings, out of the villages and courses. `near "sea"`, `edge "shore"` is the
  beach; `near "lake"`, `side "north"` the lake's north shore.
- `scatter(what, count, near?, within?, spacing?, side?, edge?, seed?, onto?, draft?)`: picks `count` open cells near
  something by `Placement.Pick` from the world's seed, now, and writes them into a draft: a terrain or cover as a `lay`
  row, a thing as `add` rows. "50 trees near the lake" (see "Laying cells").
- `act("land", near, radius, rise, profile, rough, along)` moves the land now, for everyone: a **land stamp**, a shape applied to height. `profile` is flat, dome, cone, ridge, canyon or cliff, `rise` metres (below 0 a hollow; a canyon's or cliff's wall height), at most `land.maxRadius` and `land.maxRise` in `budgets.json`; one stamp an act. `rough` is 40 when left out, 0 for flat, so a plain ask isn't a smooth slab. Land a stamp makes steep paints as bare rock and grows `LandRocks` by itself. It keeps off water, buildings and settled ground (a place of kind village, course, puzzle, court, field, tower, bridge or landmark), is refused past what can be drawn (no land below -17 m or above 272 m, so a deep hollow in low ground is refused), and stops nothing: raiders, sight and spells pass over. `undo_act` takes it off exactly, in any order; a save and a late joiner keep it, and the dragon's reset takes all stamps back.
- `call_act(id, near)` fires a **named act**: a trigger with `on: "called"` in a mod that's on, each `do` row what `act` takes, as one firing `undo_act` takes back whole (`docs/design/world-acts.md`). Define it with `add_trigger(on: called, do: [...])`; saving the mod keeps it.
- With no game attached these read a stand-in of the valley (`StandInValley`: the land, water and buildings from
  the map's folder, no trees and no yard), and say so. The server's `--map <id>` picks the map; the valley when left out.

**Act** (`docs/design/world-acts.md`)
- `act(what, count?, near?, within?, spacing?, side?, edge?, seed?, onto?, lasts?)`: something that happens once, now, for
  everyone, with no draft and no mod: a terrain, cover, thing, animal or spell near the player or a place, or `golf-ball` and `cup` for a round of spell golf. `undo_act(act?)`
  takes back ground and things, the player's last if none is named; animals and casts stay.

**Draft** (each returns a draft id and the patch so far; drafts compose)
- `add_material`, `set_material(id, field, value)`: a terrain, cover, gas or liquid (see "Liquids"); a mod's own material is temporary and may `lapse` (see "Temporary materials"), and a liquid or cover may name a `mix` group (see "Mix")
- `add_reaction(a, b -> result, chance?, ...)`, `set_reaction`
- `add_spell` / `set_spell` (through groups and material verbs only)
- `add_item(id, name, mass?, material?, look?, extra?)`, `add_entity(id, name, hp?, mass?, material?, look?, extra?)`:
  a new kind of thing with the look that draws it; `add_thing(kind, at, chance?, rot?)` puts one in the world (see "Things")
- `add_species(id, name, basedOn?, scale?, color?, accent?, glow?, behave?, density?, from?)`, `set_species(id, scale?, behave?, from?, color?, accent?, glow?, density?)`:
  a kind of animal that moves, built on one or grafted from others' slots, and a change to one's size, habits or donors (see "Animals")
- `set_look(id, param, value, table?, where?, by?, spells?)`: one parameter of the look of a spell, terrain,
  cover, gas, element, entity or item, a `mix` of colours, with `where` a colour or mix that holds only
  round a place, a cell or the player asking, and with `by` one that colours only what a
  player casts (see "Looks")
- `tune(param, value)`: a sim parameter, within the bounds the rules declare. The dragon's fields are sim
  (`dragon.*`, `World.Dragon.cs`), each bounded in `budgets.json`; `dragon.budget` is 0 (or `never`) by default, so it comes only
  when summoned; a number brings it by itself past that many changed cells. `list dragon` and `describe dragon` say what each field does (`Workbench.Dragon.cs`, #77)
- `remove(table, id)`, only for things the draft itself added
- `add_building(at, width, depth, style?, front?, id?, fill?)`, or a whole `row`; `add_wing(building, width?, depth?, side?)`;
  `set_building(id, field, value)`; `add_building_part(table, id, row)` for a style, opening, floor, roof shape or detail
  (see "Buildings")

**The rules in force.** Every read, draft, `validate`, `simulate` and `apply`'s diff goes by the base
rules with every mod on in the room over it, in order (`Workbench.Live.cs`), not by the base, because
mods stack and what's showing is what Claude must build on. A set_ writes one field into
the draft. A mod holds only the fields it names, so mods stack and a revert undoes only its own; where a
later mod sets the same value, `revert` says that one stands.

**Refusing at once.** `set_material`, `set_spell`, `set_reaction` and the add_ tools refuse an unknown
field, condition or effect when it's set (`RuleFields.cs`), since the engine would ignore a misnamed one;
`set_spell` and `add_spell` also refuse a number past the budget's caps and a group the radial doesn't
have. A dangling reference is still left to `validate`, because a draft may name what it adds next. The
caps and the hue-only rule are also in the tool descriptions and the instructions (a test holds them to
`rules/budgets.json`). `add_spell` says whether a player can reach the spell and how (the radial, a combo).

**The clock and the weather** aren't rules, so they aren't mods. `set_time(time)` moves the clock
forward to the next time the day reaches it, and `set_weather(weather, minutes?)` holds a weather
(up to `RoomPolicy.MaxHoldMinutes`), `natural` letting it go. Each is a `RoomCommand`
(`RoomChange.SetTime`, `SetWeather`; `World.RoomSky.cs`), so it rides in the tick batch and a late joiner's
history, the change mode decides it on every peer at the same tick, and what it sets is what Hasten Time and
Control Weather set: `ClockAhead` and `WeatherSet`, already in the save and `SkyHash`.

**In a running game**, `GameRoomDesk` is an `ILiveDesk`: `Now()` reads `WorldNow.Of(grid.World)` on the main
thread, null between scenes, and `Workbench.Refresh` calls it at the start of every tool call. `Decide` sends
`command.As(switcher.Me)`, and `RoomIntent` carries the `RoomChange`, so a clock command stays one on its way
through the host. A desk that isn't an `ILiveDesk` reads the base rules and says so, and `time`, `weather`,
`set_time` and `set_weather` refuse with a sentence.

**Check**: `validate(draft)` returns every problem, one sentence each:
1. Shape: the schema of each table.
2. References: every id named exists (or is added by the same draft).
3. Design rules, as code (Andrew's standing rules):
   - every spell acts through material verbs (`rules/materials.json`), not a rule for one spell;
   - no new element for one spell;
   - spells stay inside the 5E-compatible frame (SRD 5.2.1 levels, shapes, damage dice).
4. Determinism: integer state only, no new randomness outside the world's seeded RNG.
5. Budgets: caps on spread rates, chain-reaction depth, new entries per mod, so a mod can't
   flood every cell or stall the tick.
6. Looks: every new terrain and spell names a look family the view already draws, and no entry
   names an unknown one (`rules/looks.json`). `ModBudgets.Load` carries the list, so rooms check it too.

**Simulate**: `simulate(draft, scenario, ticks)` runs the core headless with the patch on:
a named scenario (fire on grass, a flood, a storm) or a small described setup. It returns a
summary in words (cells changed by material, peak counts, tick time against the budget, the
cell hash) plus the same run without the patch for comparison.

**Save and apply**
- `save(draft, title, idea)`: writes a draft, or a mod on by the id `room` gives, in the `rules/mods/` format (title, idea, patch).
  In the game, Editor included, it goes to the player's data folder beside `notes.md` (`claude-terminal/mods`), where an update can't
  reach it; a name a shipped mod has needs `overwrite`, and that copy stands in front of the shipped one (#63, "Where mods live" in
  `docs/MODDING.md`). The headless server (`core/Rules.Mcp`) still writes to the repo's `rules/mods`, as that's how a mod gets shipped.
  From the game's chat it's refused unless the player's own message says save
  (`Workbench.SaveRefused`): a model tried it unasked on real prompts. A terminal has a person to answer Claude's prompt.
- `apply(mod)` (phase 3): sends the mod into the game this MCP is attached to. In a room it goes
  as a rule-switch intent and follows the room's change mode (below). `revert(mod)`.

## Looks: procedural only

The view must draw any rule from its data:
- sizes, radii, heights and counts scale meshes, particles and decals from the numbers;
- a material's look comes from a **look family** plus parameters (color, glow, roughness,
  flow), never from a hard-coded id;
- an element's look is the colour of what it makes in the world, whichever spell or reaction
  made it: fire's flames, water, cold's frost;
- effects (fire, steam, frost, lightning...) are families too, scaled by power and area.

**Where it lives.** The family list is `rules/looks.json`; each material's and spell's own look
is a `"look"` group on its entry in `materials.json` or `spells.json`, and each element's on its
entry in `rules/elements.json` (`{ "fire": { "look": { "color": [0.95, 0.42, 0.12] } } }`, read
as `Rules.ElementLook`). An element's look is **bare**: no family, only `color`; a cover's or
gas's is bare too until a family of theirs is drawn, and each carries its colour as drawn today,
so the view can read data rather than ids. A mod gives an element's look in `"elements"` as
entries by id rather than a list of names. Not `world/looks.json`:
that's what a theme paints, a different idea. On the entry, because a mod's whole new thing then
travels as one patch entry, through the same patch format and rule switch. Looks stay out of
`Rules.Fingerprint`, so the fingerprint didn't move when they were added and a look-only mod
can't reach the netcode checksum; `Rules.ApplyPatch` keeps the old look when a replacing entry
doesn't give one, so a mod about numbers doesn't lose its drawing.

**Changing a look through the MCP.** Sizes follow the numbers by themselves; a colour is a look.
An element's plain colour goes on to every spell of that element with a colour of its own, in one call, as otherwise
each cast stays the old colour; `spells: false` leaves them, and the answer names them either way.
`set_look(id, param, value)` changes one parameter of a spell's, material's or element's look in
a draft: `color` (three numbers 0 to 1, or `"#rrggbb"`; the player says "blue", Claude picks the
numbers), and any other parameter its family lists (`height`, `glow`, `form`...). It answers with
what that family takes and refuses anything else with the list; `table` is needed where an id
names two things (`frost` is a spell and a cover, `steam` a gas and an element). A whole-element
change ("blue fire everywhere") goes on the element, and reaches the casts of its spells that chose
no colour; one spell's ("a blue fireball") goes on the spell, and colours only that spell's show and
the fire it lights (J-052). The server's instructions say so. `validate` checks every look's parameters against its family, and a
look-only draft leaves the fingerprint and the cell hash alone. What takes no colour (`list
looks`): the `fire` families (a fire's flames are the fire element's colour), the families not
drawn yet, and a `plant` tree or sapling (the valley's own model).

**Mixed and scoped looks** (`LookScopes.cs`; `docs/design/world-tools.md`). Wherever an element,
cover, gas or terrain takes a colour, its look may also give:
- `mix`: several colours with weights, `[{"color": [1, 0.25, 0.13], "weight": 2}, ...]`, in place
  of the one colour. Each cell picks one by a hash of where it is, with no draw from the world's
  RNG, so every peer sees the same patchwork. `set_look fire mix ["#ff4020", "#2050ff", "#30d040"]`
  takes plain colours too (weighing the same); setting `color` afterwards drops the mix. With
  `"blend": true` (`set_look fire blend true`) the mix is one colour instead, its colours mixed
  like paint by their weights (below): blue and yellow blend green.
- `scoped`: a list of `{"where": ..., "color" or "mix"}`, earliest first. `where` is
  `{"place": id, "within": cells}` (a place from `places()`; within 0 or left out is the place's
  own cells), `{"at": [x, y], "within": cells}` or `{"around": player, "within": cells}`.
  `set_look` with `where` adds one, or changes the one with that same where, and a null value
  takes it back; `"around": "me"` is written as the asking player's name
  (the core keys players by name), and that look lapses while they're not in the game. A place
  that doesn't exist is refused with the list of places, by `set_look` and by `validate`.

Where scoped looks overlap they mix like paint, each counting by how far into it the cell is (in
cells from its outer edge); a scope's edge mixes into what's under it over three cells
(`LookScopes.EdgeCells`), and elsewhere the plain look (or its mix) holds. A colour a spell
chose for its own fire still wins (J-052). Scopes are worked out when the view draws, not stored
on the fire, so a fire leaving the scope changes back: that is what makes "the fire near me" follow
the player. Across mods the scoped looks add up: a mod giving an entry's look keeps the scoped
looks of the mods before it, ahead of its own. `rules/budgets.json` `looks` caps a mix's colours
(8), a look's scoped looks (8) and a scope's reach (64 cells). All of it is inside `"look"`, so
it stays out of `Rules.Fingerprint` and the cell hash.

**Glow** (`docs/design/primitives.md` P4). A cover's look (bare, `slick` or `scorch`) and a `ground` terrain's may
give `glow`, a number from 0 (none) to 1 (brightest); `block` already took one. Anything else is refused: a glow past
0 to 1 by the checks ("look.glow must be a number from 0 (none) to 1 (brightest), not 1.5"), and a glow on a gas's
or element's look, or a family that doesn't list it, by validate (looks.json `bare.also` gives the covers' extra
parameter, and `list looks` says so). `set_look river-moss glow 0.3` sets it. Like the rest of the look it stays out
of `Rules.Fingerprint` and the cell hash (`LookTests.AGlowLeavesTheFingerprintAndTheCellHashAlone`), so a glow-only
mod never moves the netcode checksum. How it's drawn (`CellSlickMesh`): over every cell whose cover glows (a cover
hides its ground's), or whose ground terrain does with no cover, a second film in the films' tiles adds the look's
colour times `0.55 × (1 − e^(−glow / 0.3))` (0.1 gives 0.16, 0.3 gives 0.35, 1 gives 0.53), mottled by a small
tiling noise texture, through one shared additive material lit as the lamps are (`Nightfall.Lamps`, the windows'
light): 4% of it by day, all of it at night. No lights, no material per cell, nothing made while nothing glows; a
cell's own film and paint are drawn as before. 0.1 is a shimmer, 0.3 a clear glow at dusk; past about 0.6 a patch
reads as a lamp. Probe `glowmoss`.

How the view draws them (`ChemGrid.LookScopes.cs`). A cell's colour is worked out when it's drawn, and
with no such look on none of this runs. Flames, embers, fog, steam and smoke, oil, grease, frost,
thorns and vines take it cell by cell. The land and the lake are one surface each, so a `where` on
a terrain, on water, or on `ash` or `snow` shows as a film over the cell, and a `mix` on those shows
as its colours mixed, not a patchwork. Steam and smoke round their colour to a few steps, so a scope's
edge is a few copies of the effect and not one a cell. A moving `around` marks only the cells at
its edge that a look reaches, at most 160 a frame, and never the rule switch. Grass tufts and the sea
take no scope. A player's name in `around` is the room's ("Player 1"), their slot the same less one.

The view's side: `LookScopes.Compile(rules, places)` at each rule switch (a place's distances are
worked out then), `Of(table, id)` once a frame, and `CellLook.At(x, y, players, own)` per cell,
which allocates nothing; `players` is where each player stands (`PlayerSpot`, by name) and `own`
is a spell's own colour where the cell's fire has one. `Plain` says a look needs no per-cell call.
Asked of Claude, "make the fire near me green" is `set_look fire color [0.2, 0.9, 0.3]` with
`where {"around": "me", "within": 10}`, and "fire round the village in three colours" is `places`
(or `here`) for the village's id, then `set_look fire mix [three colours]` with
`where {"place": "<id>", "within": 20}`. The view doesn't call `At` yet (the PC's part).

**Looks by player.** A scoped look may give `by` in place of `where`: `{"by": 2, "color": ...}`
colours only what the player in slot 2 casts, and `{"by": "each", "mix": [...]}` gives each
player one colour of the mix, by slot (0 the host, then each in join order; the mix's colours go
round again past its end). On elements, covers and gases (what a cast makes: a fire keeps its
caster's colour as it spreads, the view's note J-052 carries) and on spells (the cast's own show);
not on terrains, which nobody casts. `set_look` takes `by`: `"me"` (written as the asking player's
slot, from `here()`), a slot, or `"each"` (with a mix). A look by a slot lapses once that player
leaves, and the others keep theirs: slots don't close up. Strongest first, a cell's colour is:
`by` (one naming the caster's slot, then `each`), a spell's own colour, `where`, the plain look;
looks of the same strength mix (two mods both colouring slot 1's fire give their mix); so
`CellLook.At` also takes `caster`, the slot whose cast made what's on the cell, and `players`
carries each player's slot (`PlayerSpot.Slot`). It's a look, not a fact: two peers can differ at
a fire's edge, and nothing scores who burnt what. Asked of Claude, "make the fire I create blue"
is `set_look fire color [0.25, 0.45, 1]` with `by "me"`, and "each player gets their own fire
colour" is `set_look fire mix [one colour per player]` with `by "each"` (on `fireball`, table
spell, for the spell's own show).

**Colour theory: mixing** (`Colors.cs`). Colours mix the way paint does, because that's what a
player expects: blue and yellow give green, red and blue purple, red and yellow orange, and a
colour mixed with itself is unchanged (averaging RGB would give grey, adding light white). The
model is the painter's wheel: each colour's whiteness set aside, its hue taken from RGB to red,
yellow and blue and averaged there by weight, converted back, at the weighted average of the
colours' brightness. One pure function does it, `Colors.Mix(a, b, t)`, with `ColourMixer` (a
struct: `Add(colour, weight)` then `Result(fallback)`) for more than two; neither allocates, and
the order colours are added in doesn't matter. Everything that mixes uses it: overlapping scopes,
a scope's edge, a `mix` with `blend`, looks by player of the same strength, and a new fire caught
from neighbours of different colours: the view adds the neighbours' colours to a `ColourMixer`
and asks `CellLook.Caught(mixer)`, so where a blue player's fire meets a yellow player's, a green
front burns between them. A stronger look still wins outright (`by`, then a spell's own colour,
then `where`); mixing is for looks of the same strength. Flames, embers, steam and smoke only turn
their art's hue (`looks.json` `turnsOnly`), so a mix that lands pale or grey there shows as its
nearest hue, or not at all; `set_look` says so when a mix (or its blend) is pale, and Claude tells
the player. Asked of Claude, "blend blue and yellow fire" is `set_look fire mix [blue, yellow]`
then `set_look fire blend true`.


**How it redraws.** The view reads looks from `World.Rules` when it draws, so what it draws next
is from the rules in force on every peer. A switch also redraws what already stands if its
terrain's look changed (`ChemGrid.RedrawLooks`, from `Announce`, which solo and online share).
Sizes come from the numbers: cells, flames and craters always did, and a cast's impact scales
through `EffectScale.Of` (family `scaleBy` and `base`, with the charge).

**A look's colour only uses its hue.** A spell's effects turn round the colour wheel to the hue
of its look's colour (`LookTint`), so how dark that colour is makes no difference, and a pale one
(saturation under 0.22) leaves the effects as authored. Details are in `rules/README.md`.

**Done so far (phase 2):** `CellDressing`'s terrain table and dressing switch, and
`ChemYardBuilder`'s look per spell. `validate` checks the family list (`ModCheck.Looks`). What's
still keyed to an id, with what each needs: `docs/handoffs/procedural-looks-scan.md`. Check it with
`tools/probe.sh looksmod`.

**Limits today.** A `ground`-family material isn't painted in its own colour (`CellPaint` picks
layers by id), so a mod's new flat ground looks like the land under it; its colour reaches only
chips. A new cover or gas has no family drawn yet, so it needn't name one, and draws as an unknown
one always did. Element, cover and gas colours are in the rules, but the view reads them only
where it has been switched over to them (the world's colours and a cast's show, below).


**The world's colours (`ChemGrid.WorldLook.cs`).** The view reads colours from the rules in force at
each rule switch, on every peer, and they stay outside `Rules.Fingerprint`. The fire element's
colour turns all flames, embers and fire lights; `lava`'s turns lava; a gas's colour its fog bank or
its steam and smoke; a cover's its film (oil, grease, frost), its thorns or vines, and `ash` and
`snow` the land's charred and snow layers; a ground terrain's the land's layer of it, and grass's
the grass tufts too; `water`'s the wet cells, the lake and the sea. What's drawn from art is turned
or tinted by how far the look has moved from the base rules' colour, so the numbers in
`elements.json` matter only as a starting point, and the rules as they ship change nothing.

A spell that chose a colour keeps it: its fire burns in its look among fire of the element's, a new
fire catching beside it takes that look, and a fire already burning keeps the one it has (J-052).
That edge is the view's own note, so two peers can differ there. A spell that chose nothing follows
its element in every cast effect too, from the orbs in the hands to the burst's lights
(`SpellVisuals.For`). Every spell ships with a colour, so "chose" goes by what's moved from the base
rules, as above: a spell chose if a mod moved its `look.color` to one that isn't pale, and white asks
for no colour. A spell follows the one element its `element` names, whichever orbs made it (Scalding
Steam follows `steam`, not fire or water); a spell with no `element` keeps its own. The two orbs each
start in their own element's colour and take the spell's look once the spell is known.

What doesn't take a colour yet is in one place, `rules/looks.json` "bare" (`shows`, `turnsOnly`) and
`LookFamilies.WontShow`, with the land's layers in `CellPaint.LayerLooks`: an element, cover or gas
nothing is drawn from; what can only turn its art, so a pale or grey colour or one of the same hue
does nothing (white fire, black smoke, a white Fireball); a flat ground with no layer of its own; the
land's own trees. `set_look` still takes the colour and says so in its reply, so Claude can tell the
player. Ice, falling snow and the houses aren't looks at all.

## Products: what a material leaves

What burning and water leave is data (`docs/design/primitives.md` P3; `rules/README.md` "Products"), so a
mod can say it with `set_material` and a sim patch, with no code naming it:

- A terrain's or cover's `burn.ash` (a cover) and `burn.smoke` (a gas): peat that burns to bog leaving
  `cinders` and giving off `reek`. Without them, `sim.fire.ash` (`ash`) and `sim.fire.smoke` (`smoke`).
- `sim.water.boils` (a gas, `steam`): what water boils off as on hot ground, under fire, by `boil`, when
  rain douses a fire; `condense` turns that gas back into water, and it scalds. `sim.water.freezes` (a
  terrain, `ice`): what frozen water is, for its `shatter.cracks`.
- `sim.weather.snow.cover` / `.bank` (covers) and `sim.weather.meteors.spell` (a spell).

`validate` refuses a name that isn't a material (or spell) of the right kind. They are names, so no budget
bounds them. The view reads the same names (`products-view`): snow paint, banks, footprints and the scoped snow
look follow `sim.weather.snow.cover` / `.bank` (`CellPaint.IsSnowy`, `IsBank`); what `sim.fire.ash` or any material's
`burn.ash` names (or a cover whose look family is `scorch`) paints charred ground (`CellPaint.IsAsh`), and a mod's own
ash wears its look's colour as a film over it (`CellSlickMesh`), so peat's `cinders` show as cinders. Probe
`productsview`.

## Temporary materials: what a mod adds lasts while it's on

`docs/design/primitives.md` §4, step 2; J-062. Every terrain, cover or gas a mod adds is **temporary**: it lives while
its mod is on (`TempMaterials.cs`, `World.Lapse.cs`). A row may give `lapse`:

```json
"black-ice": { "name": "Black ice", "tags": ["slippery"], "lasts": 360, "melt": { "water": 1 },
               "look": { "color": [0.08, 0.12, 0.35] }, "lapse": { "after": 3600 } }
```

- **`after`** (optional, ticks, 1 to `materials.maxLapse` 21 600, an hour): that many ticks after the mod switched on,
  the mod goes off by itself, as if reverted but past the change mode: its rows name the material, so they go with it,
  and the rules are back to what they were. The log says "Black ice lapsed (Open room)" and `RoomDecided` is raised with
  a revert. Without `after` it lasts while the mod is on. A cell's own `lasts` still withers a single cover sooner.
- **`into`** (optional): what a cell of it turns into, a material of the same kind the rules already have (not one the
  mod adds). None: a cover or gas just goes, a terrain becomes dirt.
- **The sweep.** When a mod goes, by revert or lapse, every loaded cell holding one of its materials is swept at that
  tick, in row-major order: the terrain turns into `into`, the cover or gas goes (or turns into `into`), and the cell's
  `Life` (and a cover's `Slick`) is cleared. Inactive chunks are swept too, as `ApplyRules`' `Sanitize` already writes
  them, so no chunk wakes holding one.
- **The retired list** (`RoomState.Retired`): each swept material with its `into`, mod and tick, the newest 32. A cell
  that comes back naming one later (the dragon's starting state, an older save) turns into the same `into`. It is in
  `RoomText` (so `CastHash`) and the room's save, both unchanged while it's empty; a mod switched on again takes its
  materials off it.
- **Lockstep.** A lapse is checked at the end of each tick (`World.EndTick`), mods in room order, at most one a tick
  (the next due waits a tick). The due tick is the mod's switch tick plus the shortest `after` of its materials, so a
  late joiner or a loaded save, which has both, lapses it at the same tick. No clock but the tick.
- **What may name it** (#86, #88). Any mod, while its owner is on: a wing names its house's `clay-tile`. Not only
  materials: anything a mod adds counts (`ModAdds`: a material, a style or other `buildings` table row, a building
  row under `world.buildings`, a species, a part, a trigger, any row of an id table the base rules lack, owned by
  the first mod on to add it). A mod naming one (a row by its id, or a string anywhere in its patch, `ModAdds.Named`;
  a variant's `variantOf` names its species) is that mod's
  **dependent**, and goes with it: when the owner is reverted or lapses, every dependent goes at that tick, and
  every dependent of a dependent (one pass in room order, `World.WithDependents`). They go in one switch, with one
  log line ("Ana switched off A stucco house, and A wing with it"), the owner first and then its dependents in
  reverse of the order they went on; the view hears a revert for each, in that order. A lapsing material is no
  exception: its dependents go at the lapse. A mod naming a material whose owner went is refused, naming it ("A wing
  names clay-tile, a material of the mod house, which isn't on; switch house on first", from the retired list; a
  style or another row the same way, "names adobe, a style of the mod house", from `RetiredRows`, which is in
  `RoomText` and the save only once it holds any, at most 64 kept); one
  never on in the room gets the checks' own reference sentence. No thing is made of another mod's material, and none
  lapses into one, since it would go at the same tick. Ids travel as strings (cells, saves, the patch): nothing on
  the wire holds an index.
- **Budgets** (`budgets.json` `materials`): `maxPerMod` 8 new materials a mod, `maxLive` 32 temporary rows live in a
  room (refused at the switch: "Big mod would make 35 temporary materials live; a room holds at most 32"), `maxLapse`
  21 600. A lapse on a material the rules already have is refused.
- **Tools.** `add_material` and `set_material` take `lapse` (`lapse.after`, `lapse.into`) on a terrain, cover or gas.

**In the view (`products-view`).** How a temporary cover is drawn:
1. `CellSlickMesh.IsFilm`: a slippery cover (oil, grease, frost, black ice), one whose look family is `slick`, and any
   cover the base rules don't have wears the glossy film, in its look's colour (`ChemGrid.CoverColorAt`); snow covers
   stay the land's paint. `IsSheen` (slippery and burns: oil, grease) is the rainbow on water. No cover is skipped.
2. On `RoomDecided` with a revert (a lapse is one), every inactive chunk is looked over again, six a frame
   (`ChemGrid.RedrawEveryChunk`), so a sweep clears everywhere; the active ones are walked anyway.
3. Before the next tick (`ChemGrid.FollowLapse`, solo and online), a revert the view didn't ask for puts
   `World.RoomRules` in force as `CurrentRules`, with the redraw, recolour and `RulesChanged` of any switch: the mods
   panel, lights and looks go back as on a revert.
4. The mods panel shows "lapses in 4:59" on a mod whose materials lapse (`RuleSwitcher.LapseNote`).

## Covers that spread

`docs/design/primitives.md` §3, P2. A cover may give `spread`, and it creeps onto the ground next to it with no cast
(`World.Spread.cs`):

```json
"river-moss": { "name": "Glowing moss", "tags": [], "look": { "color": [0.3, 0.85, 0.55] },
                "spread": { "every": 30, "chance": 0.5, "onto": ["stone", "rock"], "notOnto": ["sandy"] },
                "lapse": { "after": 1800 } }
```

- **`every`** (ticks, from 1; a mod's from `materials.minSpreadEvery` 30): each cell of it gets a turn every that many
  ticks, on the soak pass, each cell on its own turn of the count (as soaking is), so a patch doesn't pulse.
- **`chance`** (above 0, up to 1, default 1; a mod's at most `materials.maxSpreadChance` 0.5): on its turn, for each
  neighbour in N, E, S, W order that takes it, one roll of the world's seeded RNG.
- **`onto`**, **`notOnto`** (lists of terrain ids or terrain tags): a neighbour takes it if it's bare (no cover, no
  fire), not open water (a cover on water is `float`'s), its terrain's id or a tag is in `onto` (none: any terrain)
  and none is in `notOnto`. A name no terrain has is refused ("spread.notOnto pebbles is no terrain or terrain tag").
- **`reach`** (cells, from 1; a mod's at most `materials.maxSpreadReach` 64): how far it creeps from where it was laid,
  counting that cell, carried in `Cell.Slick` as a slick's reach is. None: no end.
- **When it lands.** What spreads in a tick is laid at its end, first come first laid, so it creeps a cell a turn
  whichever way the rows run; at most `materials.maxSpreadPerTick` (64) cells a tick in the whole world, and only into
  active chunks. A catch-up's spread lands on the next tick, under the same cap.
- **Cost.** None when no cover in the rules spreads (`Rules.AnySpreads`; no RNG is drawn, so every golden holds).
  A whole active chunk of spreading moss with nowhere left to go costs about 22 µs a tick more than the same chunk under
  a cover that doesn't spread (a VM, best of six runs of 2 400 ticks); a growing patch adds at most 64 landings a tick.
- **Temporary.** A mod's spreading cover is swept like any other at its lapse, every cell it reached and its reach.
- **Tools.** `add_material` and `set_material` take `spread` on a cover, whole (`spread`) or by field (`spread.every`,
  `spread.chance`, `spread.onto`, `spread.notOnto`, `spread.reach`), within the checks above.

**For the view.** Spreading moss is a mod's own cover, so it wears a film of its look's colour, redrawn as it creeps
in with no cast (a spreading cell counts as live, so its chunk's `LastLive` moves). With `look.glow` it glows after
dark (P4, "Glow" under Looks); probe `glowmoss` lays river stones on the bank and moss with glow 0.3 that creeps over them.

## Liquids: what a cell's water is

`docs/design/primitives.md` §3 P1, step 4 (core; branches `liquids`, and `liquids-2` for `hot`, `freezes`, `sets`). `Cell.Water` stays the amount; `Cell.Liquid` is the
**kind**, an index into the `liquids` table where 0 is water, built in (`Rules.Liquids.cs`, `World.Liquids.cs`). The
base rules have no table, so a world of water runs, hashes and saves exactly as before. A mod adds a row:

```json
"liquids": { "honey": { "name": "Honey", "thick": 4, "heap": 6, "soaks": false, "floats": false, "speed": 0.4,
                        "tags": ["sticky"], "look": { "color": [0.85, 0.6, 0.1] }, "lapse": { "after": 900 } } }
```

- **`thick`** (whole, 0 to 600; water 1): it flows on one tick in `thick` (on ticks a multiple of it); 0 never flows
  (quicksand). **`heap`** (whole, 2 to 16; water 2): the drop in units it needs to run on the flat, so honey stands in
  a mound (below 2 a puddle would slosh for ever). Downhill it runs on any drop, as water does.
- **`soaks`**, **`floats`**, **`drowns`** (flags, all true by default, as water): thin pools sink into the ground
  (`World.Soak`); a floating cover (oil) rides it (`World.Float`, `pushCover`); its depth counts for wading (raiders'
  rough ground), the dragon's shelter and breath (`World.DrownDepth`).
- **`boils`** (a gas): what it boils off as on hot metal, hot ground (`boil` terrains) and by a `boil` effect. None:
  it doesn't boil (water's is `sim.water.boils`).
- **`hot`** `{ "for": ticks }` (1 to 21 600): a pour of it is hot that long (`Cell.Heat`, set by `AddLiquid` and an
  `addWater` with its `liquid`; a unit that runs on takes its cell's heat). While hot, every 4 ticks
  (`World.HotTouchEvery`) it lays `fire` at power 1, quietly, on the cell under it and each dry cell beside it, and the
  fire rows decide: grass catches and burns to dirt, fire spreads on from there, a creature in it burns (its heat, as
  on hot metal), and the `hot` condition holds. Then it cools and stays the liquid it was (`World.HotLiquid`).
- **`freezes`** (a terrain or a cover): where water would freeze, it becomes that instead and the cell is dry
  (`World.FreezeLiquid`): a `freeze` effect (the base `cold-liquid` row, `if.freezes`, comes after `cold-water`, so a
  cold spell freezes any liquid that freezes, naming none), freezing ground (`freeze` terrains, on the soak pass) and
  snow on a thin pool (two units or less). Without `freezes` it doesn't freeze. A frozen syrup is a cover of candy.
- **`sets`** `{ "into": a terrain, "after": ticks }` (1 to 21 600): a pour of it sets that long after (`Cell.Sets`,
  counting down on the timers pass); a unit that runs or is pushed (a raised cell) onto a dry cell takes its count, onto its own kind the sooner of
  the two. At 0 the cell's liquid is gone and its terrain is `into` (`World.TickSets`). A mod's molten rock with
  `hot` and `sets` burns the grass it runs over, cools, and sets into rock where it lies. `Cell.Sets` is in the cell
  hash (`,p<n>`, only when set) and in a save (after the liquid's id), so old hashes and saves are unchanged.
- **`touch`** (an element). **Two kinds never share a cell**: a liquid runs only onto a dry cell or its own kind.
  Where the best cell it could run onto holds another kind, it lays its `touch` there at power 1 and the reactions
  decide; with no `touch` the other is a wall. A mod's molten rock with `touch: "lava"` running into a pond makes
  the base `lava-pool` row grow on the wet cell, and the water there quenches it into rock and steam (J-062: the base
  lava stays terrain; this liquid lava is a mod's, and comes first).
- **Water from the sky and the ground stays water**: rain, a rain cloud, springs, melting snow, a thaw, condensing
  steam and `addWater` without a liquid don't fall into another kind's cell. Rain doesn't dry or freeze another
  liquid. "Water" in a reaction (`if.water`, the `water` element's conductivity, dousing fire, rust) is kind 0.
- **In a reaction**: the condition `liquid` (an id, `water` for kind 0, or a tag: `"sticky"`) holds on a cell of that
  unfrozen liquid; `freezes: true` on a cell holding a liquid with `freezes`; the effect `addWater` with `liquid` pours that liquid (`{"addWater": "power", "liquid": "honey"}`).
- **Interning.** The table is rebuilt at each switch: water, then the base rows in file order, then each mod's in
  room order. At a switch every cell's kind is remapped by id (`World.RemapLiquids`), so nothing outside the core
  holds an index: the patch carries the row, the cell hash appends `,q<id>` and a save appends the id to a cell's row
  only when it isn't water; old saves load as water.
- **Temporary.** A mod's liquid is temporary like any material it adds (counted in `materials.maxPerMod`, swept at a
  revert or lapse). `lapse.into` may be `water` (the pool becomes water); with none, it dries.
- **Limits.** At most 254 liquids besides water. The still-water pass waits out the thickest liquid's `thick` before
  it skips a chunk as still, so a thick pool's turn is never missed.
- **Tools.** `add_material` and `set_material` take kind `liquid` (its fields are those above, each one value but
  `hot` {for}, `sets` {into, after} and `lapse`); `list liquids`, `describe liquids <id>`. A field the engine doesn't
  read is refused, and so is one `hot` or `sets` doesn't take.

**Cost.** On a full 384 × 384 lake of water with still-water skipping off, the flow pass costs about 0.3 ms a tick
more than before liquids (about 2.0 to 2.3 ms on `dev`, 2.25 to 2.6 ms here, a VM); a still lake with the
skip on costs the same as on `dev` (1.5 to 2 ms a tick in all). A honey lake costs less than water (about 3 ms
against 4.4 to 5 ms a tick, the skip off): it tries one tick in four.

**In the view (not built yet).** The water mesh takes a colour per kind (`LiquidDef.Look.color`, a vertex colour;
water keeps its own look); a thick liquid's surface may move slower (`look.flow`). Breath and wading should read
`World.DrownDepth` and `LiquidDef.Speed` instead of `Cell.Water` (the view's `Breath` caller, the mover's wading).

## Mix: rows of a group that meet make a third

`docs/design/primitives.md` §4, P5 (J-062; `Rules.Mix.cs`, `World.Mix.cs`, `MixTests`). A liquid or cover row may say
`"mix": "paint"` (any name; the core names none). Two rows of a table sharing a group that meet need no reaction row:
the room derives their mix. Red and blue paint make purple with only their own two rows.

- **Meeting.** A liquid touching another of its group (cells side by side, or one running onto the other), a cover laid
  by `setCover` on another. Touching mixes the two cells that touch; a mix beside its own parent changes nothing unless
  liquid runs onto it, so a band of purple stands between the pools rather than eating both. Water, and a liquid of no
  group or another, stays a wall (or lays its `touch`). With a group in common, a mix comes before a `touch`.
- **The row**, a pure function of the two rows, in id order: id every parent's sorted and joined by `+`
  (`blue-paint+red-paint`), so red meeting purple is purple and three paints come to one id whichever met first. Colour
  mixes as paint (`Colors.cs`), on the first look; numbers are the mean, rounded down when both are whole; flags hold if
  both do; a list keeps what both have (`tags`); a text stays only if both say the same, and a section holding one that
  differs goes whole. A field only one row gives counts its default if it has one (a liquid's `thick` 1, `heap` 2, `speed`
  1, flags true; a cover's `speed` 1), else it's left out. `lapse` isn't derived.
- **When.** Asked for where the pair first meets and made at the end of that tick, every pair of the tick in the order it
  met, in one switch: until then the liquid stops as at a wall and a cover waits to land. A meeting between ticks (a cast)
  makes it at once. Ticks run in a fixed order, so every peer makes it at the same tick.
- **The list.** `Room.Mixes`: table, id, parents, row and tick, in the order made. The derived rows compose over the mods,
  so they're in `Rules` and its fingerprint (bar the look, as ever); the list is in `RoomText` (`CastHash`) and the save,
  so a late joiner gets it with the snapshot. Each row is kept whole, because a parent may be gone by then.
- **Limits.** At most `materials.maxMixes` (16) a room: past it the arriving one stops as at a wall. A row that won't go in
  the rules is never made, and neither is a mix that stops fitting: it goes as if lapsed.
- **Lapse.** A mix lasts while any parent is in force. When the last goes, cells of it lapse as that parent's do (the
  retired list names `mix` as its owner). Without a room there's no list, so rows of a group are walls.

## Buildings: rows a mod can edit or add

`docs/design/builders.md` section 4, option (a), outside only. A building is a row: join points in half cells
from its `at`, wall segments between them with openings, and a `fill`, hollow unless it says solid.
`rules/buildings.json` holds the parts tables (`openings`, `floors`, `roofs`, `details`, `styles`), which a mod's
`buildings` section adds rows to. Its `world` section carries buildings, each row whole:

```json
{ "world": { "buildings": { "cabin#2": { "...": "the whole row, with its new walls" },
                            "beach-house#1": { "at": [501, 480], "style": "plain", "joins": {}, "walls": {} } } } }
```

An id the world has edits that building; a new id adds one. `BuildingRaster.Cells` (`Buildings.cs`) turns a row into
cells, a pure integer function; `Wing.cs` adds a wing; `World.Buildings.cs` lays them.

Through the room, as any mod (`World.Room.cs`): every peer validates it, then `World.SiteProblem` refuses cells a
building newly takes that are off the world, on another building or a thing, wet, or not open ground. At the switch
tick `ApplyRules` lays what changed in the rows, fresh, in the cell hash. A revert composes the rest and lays
the difference, putting back what stood under; a late joiner's `OpenRoom` and a save's room go the same way, and
the save keeps what stood under. The dragon's starting state follows, so it puts a house back and doesn't count it
as damage. Raiders on a new cell are pushed to the nearest free one, in id order, and logged. Players aren't core
state, so the view pushes them (below). `budgets.json` `buildings` has the limits: 24 × 24 cells, 64 joins,
walls and openings, 32 parts, 3 storeys, 4 buildings a mod.

Only what decides cells is in the fingerprint (`BuildingRow.Shared`): at, joins, walls, openings, fill, floor kind, wall
material. Heights, finishes, roofs, details and everything under `look` are the view's, so a repaint moves no hash.

The buildings the world was made with are rows too (`World.BaseBuildings`): `world/buildings.json` has the cabins,
their baked cells in `built`, and a village's come from its recipe laid again over the world's starting cells, since a
plot that stands is all wall and a refused one isn't (`WorldBuildings.LaidPlan`). Village rows are `fill: solid`
with `drawn-door`s, so their cells are what `VillagePlan.Cells` lays, and goldens hold.

**What the view reads.** `RowHouses` draws what the rules in force add or edit (`Rules.ModBuildings`), each through
`RowMesher`; what the world was made with stays as the valley drew it, so villages and cabins look the same until
a mod touches them. An edited building hides the scene's and shows it again on a revert. An edited cabin keeps its
own, since its row is cells with no walls: a wing stands beside it as a closed box. `World.BuildingsChanged` moves
this peer's player off a new wall, past what was laid, never into a hollow house. `CellBlockers` gives a row's walls,
doors and panes their boxes, since their looks have no height and no scene model covers them. `GameRoomDesk` is the `IWorldDesk`, so `find`, `find_site` and `validate` read
the real valley. `ValleyShape.Cabins` reads `site`; `RowMesherTests` holds the cabins to where they stood and the
villages' base rows to `ValleyShape.Villages`' digests. The game has no save of its own yet, so `World.Save` is
reached only from tests.

**Lit windows** (#71). An opening's `look.lamp` in `rules/buildings.json` is the colour its glass glows from dusk
to dawn (`"#rrggbb"`, brighter the lighter; `"off"` for none), and an opening in a row may give its own `lamp`;
`window` and `wide-window` ship with lamplight, `#ffb867`. The checks refuse anything else. The view draws it
in the pane's own mesh (`WindowPanes`, uv1, `Prototype/WindowGlass`), lit by the same `_LampGlow` the village
windows use (`Village`, from `Nightfall`): nothing by day, part lit at dusk, full at night. No light and no
material per window, so it costs nothing a house doesn't already draw. A pane goes dark when it shatters, when
fire reaches its stretch of wall or the one beside it, when its stretch falls, and all of them once half the
house is down (`RowRuin.Out`). Village houses and cabins keep their own lit windows.

**What won't show.** `buildings.json` `drawn` lists what the view reads of a row, a wall, a finish, an opening,
an opening's look and a style; a roof's fields are its shape's `look.takes`, a part's its detail's. It's kept
by hand in step with `RowMesher` and `RowHouses`. `validate` and `apply` end with a `Won't show:` line
(`BuildingsShown`) naming every field outside those lists, a roof shape or detail the shipped rules don't have, and
an opening that `becomes` a terrain of its own: on a building the house draws each opening from its row, so that
terrain's look never shows (Andrew's "Lit windows on the lake house", a `lit-pane` terrain with `glow`, was
this). The line tells Claude not to say it worked. It covers buildings only; colours that won't show have
`set_look`'s own line (above).

**A house in play.** `RowRuin` follows the cells, view only, so peers and late joiners get the same ruin. Each wall is
cut into **stretches**, one per cell. A stretch whose cell stops blocking falls to a broken stump, and the roof squares
nearest it go with it; a roof whose cover burns goes as soon as the fire reaches its wall; at half the stretches down
the whole roof goes. The head over a gap stands while a wall beside it, or meeting it at a join, does. A door's panel
goes with its cell, and fire leaves soot on the stretch and its neighbours. The floor and a chimney stand. Cells put
back bring it all back. `RowHouses` looks at one house a frame and writes one index list and one colour list, never a
mesh (worst 0.26 ms, in the hidden Editor). A wall burns only if its terrain says so.

**`stucco-wall`** (J-063: "Stucco burns, but slower than wood") is a base terrain, drafted with `add_material` and
checked with `validate`, then copied into `rules/materials.json`, since the server only makes patches. Its `burn`
is `catch` 0.05 and `fuel` 210, against the log wall's 0.15 and 140. In 40 runs of the same fire on 8 cells of wall
(`StuccoBurnsSlowerThanLogs`), the next cell catches after 53 ticks to the log wall's 22, a cell lasts half as
long again, and the wall takes 556 ticks to go to 277, about twice as long; and 6 of the 40 fires die in the stucco
where every log wall burns through. Its colour is the plain render; a house's finish stays in its row, so a
`set_material` on `burn.catch` and `burn.fuel` retunes every stucco house at once. A mod can still replace the row.

## Things: kinds and rows a mod adds

`docs/design/world-tools.md` G4 to G6. Named content never goes in the code or base rules (J-055): the golden squirrel
is a test fixture.

- **A look on a thing.** An `entities` or `items` row may give a `look` of family `creature` (`rules/looks.json` `things`):
  `species` (a row of `rules/fauna.json`, or one a mod adds; `list looks` names them), `color`, `glow`, `scale` (up to `budgets.json` `things.maxScale`). It's for a kind with no
  baked prefab; a kind the rules ship with keeps its model. `validate` refuses an added thing of a new kind with no look.
- **Adding one.** A mod's `world` section takes `"add": [{"kind", "cell": [x, y], "rot"?, "chance"?}]`, at most
  `things.maxPerMod` (16) rows. Each row is appended to its chunk's list, so the file's ids never move. A mod's thing has an
  index above any a file reaches, a hash of its kind, cell and turn (`WorldThings.With`), so reverting one mod doesn't
  renumber what another added. `World.ThingsNow` is the world's things under the rules in force, and the rows are in
  `Rules.Fingerprint`. Every peer refuses, at the switch, a row on a cell that's off the world, wet, on a building, or not
  open ground (`World.SiteProblem`). A revert is only composing again, so what the mod added goes.
- **A draft that's on.** Once a draft has gone on with `apply`, `validate`, `save` and the draft's own site notes read it in
  place of the mod it went on as, not over it (`World.SiteProblemOf`'s `replacing`), so its own things don't stand against
  it. That lasts while the mod is on as the draft sent it. Anything else on the cell still refuses: a base thing, another
  mod's, or a second draft's.
- **`chance`.** Above 0 and at most 1: `ThingChance.Holds` decides once from the world's seed and the thing's id by
  `WorldHash`, with no draw from the world's generator, so every peer agrees and the cell hash doesn't move. A rare find is
  several rows at a low chance. `things.json` may give `chance` too.
- **The first find.** `RoomCommand.Found(peer, id)` rides in the tick batch like any room command, but no change mode has a
  say, as it changes no rule. The first for a thing a mod added logs "Ana found the golden squirrel", from the row's `name`,
  on every peer, and goes in `Room.Found` (the checksum and the save). Anything else is ignored in silence.

**In the view.**
1. `ThingSpawner` and `NetChem`'s checksum read `grid.World.ThingsNow`. `grid.Things` stays the file alone, for what only the file holds: the links of sending stones, which a client needs before its room has opened and `ThingsNow` holds anything.
2. A kind with no prefab spawns as one of two generic ones, `dressed-prop` for an `entities` row and `dressed-item` for an `items` row, each a registered network prefab: a unit box that draws nothing. `ThingBodies` dresses it from `Look`: the body of `Species`, in `Color` in place of its coat (white, the default, keeps the coat), with `Glow`, at `Scale`. The box takes the body's size, so it rests, gets pushed and sits in a hand like any thing, and the body, posed once, goes where the box goes. Each peer dresses its own copy, the box's size included.
3. Online a client knows a generic prefab's kind by its id, from `ThingsNow`, so nothing about it is on the wire. A copy that arrives before this peer's rules have it waits, its `PhysicsProp` and `Carryable` disabled so they read no rules, and is taken up at the next `RulesChanged`; `ChemGrid.JoinRoom` raises that too, for a late joiner.
4. At each rule switch `ThingsNow.ChangesFrom` gives what to spawn and what to despawn, solo or by the host, in an inactive chunk too (the thing sleeps there). A despawned thing a player holds is theirs to lose: it leaves the hand on every peer (`Carryable.Lost`).
5. The first find: `NetItem`'s grant, host only, stamps `RoomCommand.Found(name, id)` into the next tick with `NetChem.StampFound`, past `RoomIntent` and its `RoomPolicy.Refusal`, so a Locked room can still find it. Solo, `ItemHolder` queues the same command.

Probes: `modthing` (solo) and `roomthing` (two peers and a late joiner).

**The test, by a real client.** From a folder of its own, against a copy of `rules/` and `world/` so `save` can't touch the repo,
with no stdin (`claude -p` waits 3 s for it and exits 1):

```sh
claude -p "Put a rare golden squirrel somewhere in the woods for us to find. Save it as a mod file called golden-squirrel." \
  --output-format stream-json --verbose --no-session-persistence --mcp-config .mcp.json --strict-mcp-config \
  --permission-mode dontAsk --tools Read --allowedTools mcp__spell-valley </dev/null
```

`.mcp.json` starts `Rules.Mcp.dll --rules <the copy>/rules`. It ended as one mod all three times it was run, with its spots from `find_site`.
What one run wrote is `core/Game.Core.Tests/RulesMcp/golden-squirrel.mod.json`, and `HiddenThingTests` holds it through two runs,
two peers and a save. **Limit:** the woods aren't a place and a stand-in valley has no trees, so a headless client can't tell
woods from a meadow, and `find_site` may give a cell a real tree stands on; the game's `find_site` reads the real valley,
and a room refuses a row on a blocked cell at the switch.

## Animals: fauna rows a mod adds or changes

`Workbench.Fauna.cs` (#61, #72). An animal is a row of `rules/fauna.json`, drawn and moved by the view (`ValleyFauna`); the
rows stay out of `Rules.Fingerprint` and off the cells, so no golden moves and the core stays as it was. A thing with a
`creature` look (above) is a still figure; an animal Claude adds is a row.

- **`add_species`** writes a variant (`variantOf`) of the species it's based on: that one's body, gait, wander, flee, idle and
  group. It copies the base row's `coat`, `coats`, `hair`, `vary` and the part of `behave` a variant reads, as a variant's reader
  would otherwise take its own defaults, then lays on the colours and habits given. A variant reads only `active`, `graze`,
  `thirst`, `play`, `laze`, `hunger`, `chase` and `hunts` from its own `behave`, so the tool refuses the rest and doesn't copy
  `wander`, `idle`, `flee` or `group` (#80): the view takes those from the base (`CreatureBody`), so a copy would be dead and go
  stale when the base changes. `describe` says them as the base's. `scale` is against the base species (a
  variant's `scale`, multiplied by the base's own). `spawn.density` is half the base's (at least 0.2) unless given; 0 leaves
  `spawn` out, so it never comes out in the wilds.
- **`set_species`** sizes a row against its normal size in the base rules, so 1 puts it back: a variant by its `scale`, a
  species of its own by its `size` pair (the view multiplies only a variant's `scale`). Its `behave` merges over the row's.
- **Checks.** `Rules.CheckFauna` refuses a bad row as for the base rules; `validate`'s budget refuses a `scale`, or a `size`
  against the base row's (or 1.1 for a new row), past `budgets.json` `things.maxScale`.
- **In the view.** A switch rebuilds only the rows that changed (`CreatureBodies.Refresh`); a new species spawns in the
  wilds by its density, anywhere its habitat fits, not at a place.
- **Reading a body** (`Workbench.Bodies.cs`, `BodyShape.cs`; `docs/design/bodies-and-surfaces.md` §3). Headless, Claude
  can't see an animal, so `describe fauna <id>` flattens its slots as the view does (`CreatureBody.Build`): one line per drawn
  copy (a mirrored slot twice, a slot on a mirrored one per copy), each with its part and role, what it hangs `on`, its `at`
  (body units on the body: x across the radius, y up it, z along the half-length, +1 the front; metres from the end of the slot
  it hangs on) and where it starts and ends at rest in metres. Only the first slot of a style group counts toward the shape.
  A variant's lines are its body's, with what it takes from that body said once. `whats_on` and `describe`'s mod lines see
  `fauna` and `parts` rows a mod adds or changes (#80). A graft's lines are its flattened slots, with its donors said once
  and `coat own (donor)` on each slot that wears its donor's colours.
- **Grafts** (`FaunaGraft.cs`; `bodies-and-surfaces.md` §2, #73): a made-up body from other species' slots. A row's `from`
  lists donors, and the core flattens it into plain slots when it's read (`FaunaTables.Species`), so the view, the checks
  and `describe` never see `from`. Each donor `{species, take?, drop?, at?, dir?, scale?, coat?}` gives all its slots, the
  slots it `take`s and every slot hung `on` them, or all but those it `drop`s and theirs (a variant donor gives its base's
  slots). The row's own `parts` come first; a donor's slot whose id is taken gets the donor's id before it (`human-tail`,
  `black-bear-fore`, then `-2`...). A donor's loose slots (no `on`, or `on` one left behind) hang on the new body: with `at`
  they move together so the first sits at `at` in body units, a mirrored pair keeping its spread across (`x`); `dir` adds
  to their rest direction; `scale` multiplies every slot's scale and a hung slot's `at`. `"coat": "own"` puts `coat: own`
  and `donor` on each of that donor's slots (`SlotDef.OwnCoatOf`), for the view's `graft-coats` to draw; until then all
  wear the row's coat. With no `coat` or `coats` of its own a graft wears its first donor's (with its `hair` and `vary`).
  `body`, `move` and `behave` may each say `"from": species` and override fields. `add_species` with `from` (no `basedOn`)
  writes such a row moving and behaving as its first donor, `size` its first donor's × `scale`; `set_species` with `from`
  replaces a row's donors, keeping its own slots (not on a variant). `Rules.CheckFauna` checks the donors as written, then
  the flattened slots (every part exists, `on` resolves), and refuses a loop of donors; `validate`'s budget
  (`budgets.json` `fauna`) refuses more than 4 new species a mod, 3 donors a graft, 40 slots once flattened, or a donor's
  `scale` outside 0.25 to 4. Gaits aren't by rule yet (Andrew, 2026-11-03), so a graft's legs move by their own poses.

## Spells that place

`docs/design/events-and-placing.md` 2a. A spell's `place` field puts something on cells where it lands, after its
element:

```json
"place": { "animal": "human-child", "count": 1, "within": 0 }
"place": { "thing": "scarecrow", "count": 3, "within": 2, "spacing": 1, "lasts": 3600 }
"place": { "terrain": "tree", "count": 5, "within": 4, "spacing": 2, "onto": ["soil"] }
```

- **What.** Exactly one of `thing` (an `entities` or `items` row), `animal` (a `fauna.json` species), `terrain` or
  `cover` (`Rules.Check`'s `PlaceRefs`). A spell with only a `place` and no element or effect is fine.
- **Where.** `count` cells (1 if left out) picked by `Placement.Pick` among the spell's cells, or among the cells within
  `within` of the aim as the crow flies when it's given (0 is the aim cell). `spacing` is the fewest cells between two
  picks. A cell is open when it's on the world, active, dry, not blocking, with no building or thing, and its terrain
  has one of the `onto` tags (`ground` with none): `World.PlaceOpen`. Each cast's salt is a hash of the tick and the
  place actions run so far, so a second cast picks afresh, and every peer the same. Too few open cells logs
  "Test spell: 2 of 3 had room".
- **A terrain or cover** is laid fresh (no fire, gas or water; height kept): cell state, in the cell hash.
- **A thing** goes in `World.Placed` and `ThingsNow` (after the file's and the mods' things), with the id
  `ThingId.Placed(chunk, n)`: its index has the top bit set, above every file's and mod's, and prints as
  `"1,0#placed 4"`. `lasts` (ticks) takes it away; none stays. At most `place.maxLive` (64) live: the oldest
  lapses first. `PlacedText()` is in `CastHash` and the save, "" until a spell places.
- **An animal** is the view's (J-056): `World.Summoned` lists this tick's (species, cell, `Individual`, running
  number `N`), cleared as each tick begins; `World.SummonedAfter(n)` gives those past the `n`th from the last 600 ticks,
  for a view that steps several ticks a frame. Nothing about it is in the hashes but the running count.
- **Budgets** (`budgets.json` `place`): `count` at most 8, `within` at most 8, `lasts` at most 21 600 (an hour),
  64 placed things live in a room, 24 summoned animals on a peer (the view's cap, `maxSummoned`).

**In the view (`place-view`).**
1. `ThingSpawner` diffs `ThingsNow` (`ChangesFrom`) after any tick whose `ThingsNow` isn't the table it last
   spawned from (a new table whenever a spell places or a placed thing lapses), not only at rule switches; the host
   spawns, as for a mod's thing. A placed thing has `ThingSpawn.Placed` and `Lapses`.
2. `ValleyFauna` brings out each `World.SummonedAfter(lastN)` on its cell with `Spawn(species, position, yaw, Individual)`,
   keeps at most `place.maxSummoned` (24), and lets it wander as ambient life.
3. A spell with a `place` and no element still needs its show (its `look`), as any cast.

## Laying cells: a mod's `lay` and `scatter`

`docs/design/events-and-placing.md` 2b. A mod's `world` section takes **`lay`**:
`[{ "terrain": "tree", "cells": [[x, y], ...], "seed": 7 }]` (or `"cover"`), at most `lay.maxCells` (256) cells a mod.

- **The switch.** `World.LayCells` (from `ApplyRules`, after the buildings) lays each cell the new rules lay and the old
  didn't, fresh (no fire, gas or water; height kept), and keeps what stood under (terrain and cover). A cell that isn't
  open now (`World.PlaceOpen` without the active check: wet, blocking, a building or thing on it) is skipped, the same on
  every peer, and logged: "3 of 50 tree had no room". A cell the old rules laid and the new don't goes back to what stood
  under. The starting state follows, as for a building, so the dragon keeps a laid tree.
- **State.** The cells are in the cell hash from that tick; what stood under is `LaidText()` in `CastHash` and `"laid"` in
  the save, both empty with nothing laid. The rows are in `Rules.Fingerprint` (`Rules.ModLays`, `Rules.Lay.cs`).
- **`scatter`** runs the pick against the world the MCP reads (the game's through `IWorldDesk`, or the stand-in), over
  the cells within `within` (12, at most `lay.maxWithin` 32) steps of `near`, off villages, courses and puzzles. The
  reply says how many, on what, how near: "50 tree on grass near the lake's shore, 2 cells apart at least, the nearest
  1 cells from it and the furthest 12 (seed 1)". The same what and seed replace their row; another seed adds one;
  `remove(draft, "lay", "tree#1")` takes one back. `list lay` shows the rows in force.

**In the view (`place-view`).** A laid `tree` is the real terrain (it blocks, burns, topples), but `CellDressing` dresses
only trees the land baked or that grew (`IsGrownTree`): it must dress any `tree` cell the land didn't bake as one of its
chunk's valley trees, and `brush` as a bush, redrawing on `ChunkFreshened`.

## Triggers: rules that react to a step

`docs/design/events-and-placing.md` 2c, J-067. A mod's **`triggers`** section holds rows by id (`rules/triggers.json`
is the table, empty in the base rules; a `null` takes a row out):

```json
"triggers": { "meteor-steps": { "on": "step", "who": "Andrew", "every": 3,
                                "do": [{ "cast": "meteor", "at": "ahead", "by": 2, "delay": 6 }] } }
```

- **`on`**: the event; only `step` so far (a `Stand` on another cell: not the first stand, not while downed).
- **`who`**: the player whose steps fire it, or `"each"`. `add_trigger` fills in the asker's name from `here()` when
  it's left out (J-067): their peer name, "Player 1". A stand's name is "solo" alone and "p0" online, so a trigger
  matches a stand by its own name or by its peer name (`TriggerDef.PeerOf`: "solo" and "p<n>" to "Player <n+1>").
- **Cost.** `simulate`'s slowdown against `simulate.maxSlowdown` is a guide: `apply` doesn't time any mod (a timing
  differs by machine, so it can't decide a room's rules). What `apply` checks, on every peer, are the budgets below.
- **`do`**: one or two actions. `{"cast": spell, "at", "by", "within", "delay"}` casts through `World.Release` as a
  player's cast does (a spell zone still smothers it); a `delay` puts it on the delayed path (`AimStar`, a target mark
  until it lands), and 0 keeps the spell's own. `{"place": {...}, "at", "by"}` places as a spell's `place` does,
  within the place's `within` of the action's cell. `at` is `here`, `ahead` or `behind` by `by` cells along the step,
  or `around`: a cell within `within` picked by `Placement.Pick`.
- **`every`** (ticks between firings, per player and trigger; at least 3, 3 if left out) and **`chance`** (0 to 1, a
  `WorldHash` of the seed, the trigger, the tick and the player: no draw from the world's generator).
- **Lockstep.** A trigger fires inside `ApplyStrike`, in batch order, so every peer fires the same; at most
  `triggers.maxPerTick` (4) fire a tick in a room, later ones that tick dropped. An action raises no event, so nothing
  chains. The cooldowns are `TriggerText()` in `CastHash` and `"triggers"` in the save, "" until one fires; an empty
  table stays out of `Rules.Fingerprint`, so every existing golden and fingerprint holds. Stands aren't saved: after a
  load each view sends its own again, which is no step.
- **Budgets** (`budgets.json` `triggers`): 4 a mod, 2 actions each, cast spells of level 7 at most (Meteor), a
  place within `place`'s caps.
- **Tools.** `add_trigger(id, do, who?, every?, chance?, on?)`; `list triggers`, `describe triggers <id>`,
  `remove(draft, "triggers", id)`. `simulate` walks when the draft has a step trigger (or `walk: true`; scenario `walk`
  is the walk alone): the trigger's player runs a loop round the meadow a cell every 2 ticks, and the reply adds
  "Andrew ran 59 steps round a loop; triggers fired 30 times, casting 30 spells".

**In the view (`triggers-view`).** `World.Triggered` lists this tick's triggered casts (`TriggeredCast`: `N`, trigger,
player, spell, cell, `Dir`, `Tick`, `Lands`), cleared as each tick begins; `World.TriggeredAfter(n)` gives those past
the `n`th from the last 600 ticks, for a view that steps several ticks a frame. `SpellVisuals` plays each one's show
at its cell (a delayed one's target mark is already in `Cell.Star`, and it lands through `LandStars` as Falling Star
does, so the landing show is whatever a delayed cast's landing plays now). Nothing else: the steps already go in as
`WizardStrike`'s `Stand` commands.

## Change modes (chosen when a room is made)

Andrew, 2026-10-31: the point is fun, not policing. A room picks one when it's created:
- **Open:** anyone's change just appears for everyone, at the next agreed tick. Surprise is the
  game; the log says who changed what, and anyone can revert.
- **Host only:** only the host's changes go in; other players' are refused with a sentence.
- **Locked:** no changes; the rules are what the room started with.

Whatever the mode, **every peer validates every incoming change itself**, with the same checks in
the core, before it switches on. A change that fails on one peer fails on all (the checks are
deterministic), so a broken or hostile peer can't push anything the checks wouldn't pass.

## Where it runs

**The MCP ships with the game.** Each player's Claude talks to the MCP of their own copy of the
game, on their own machine. There is no hosted MCP server: what crosses the network is the mod
patch, as the existing rule-switch intent (client to host, the host schedules a tick, every peer
switches on), so lockstep holds and we run no servers for it.


- **Phase 1, local:** a .NET MCP server (stdio) in `core/`, linking the core sources like the
  tests do, using the official C# MCP SDK (`ModelContextProtocol` on NuGet). Our own agents use
  it at once instead of editing rule JSON by hand.
- **Phase 3, in the game:** the same tools, served by the running game (or a small process it
  starts) on localhost, so a player adds it to their Claude once. `apply` goes into that game.

## Phases

1. **Local rules MCP** (Linux worker): read, draft, validate, simulate, save; tests in
   `core/`; every example in `rules/mods/` passes through it unchanged.
2. **Procedural looks** (Mac): id-keyed visuals found, look and effect families defined, the two
   biggest tables converted, a mod switched on mid-game rescales the view without a rebuild
   (`looksmod`). The rest is listed in `docs/handoffs/procedural-looks-scan.md`.
3. **In the game and live rooms**: the MCP served by the game on localhost, `apply` and
   `revert`, the three change modes chosen at room creation, every peer validating incoming
   changes, the log line per change, a mod gallery.
   - **Built, core half (2026-10-31, `live-rules`):** the room in the world state
     (`Room.cs`, `World.Room.cs`): a change mode set by `World.OpenRoom` when the world is made,
     saved with it (`World.Load` takes the budgets for a room's save) and in the checksum (through
     `CastHash`). A `RoomCommand` (switch a mod on, or revert one, naming the peer) rides in the
     tick batch (`TickBatch.Room`, `AddRoom` for a late joiner). At that tick every peer decides it
     itself: the change mode (`RoomPolicy.Refusal`), then `ModValidator` against its own rules in
     force, then the active mods composed again over the base in order. A refusal is the same
     sentence everywhere and changes nothing the checksum sees. The active mods keep id, title, who
     added it and the tick; a revert takes one off and recomposes the rest. One log line per change
     ("Ana switched on Big fireball (Open room)", "Revert refused: only the host can change this
     room"). The MCP has `apply`, `revert` and `room` against a world in memory. Tests:
     `RoomTests.cs` (two peers side by side, a hostile patch and a good one, hashes compared),
     `RoomToolsTests.cs`.
   - **Built, Unity half (2026-10-31, `live-rules-unity`):** every world is a room, solo too (an Open
     one); the host picks the mode in Esc > Play together before hosting (`NetSession.RoomMode`). A
     rebuilt world keeps the room and the mods on (`OpenRoom` takes them), and a host's solo mods
     become its room's starting mods, so they stay even in a Locked room. `RoomIntent`
     (`NetChem.Room.cs`): anyone asks with the patch, the host names the sender, may refuse at once
     (`RoomPolicy.Refusal`, a sentence every peer hears) and stamps the command for the next tick. A
     late joiner gets the room as its history starts, then the commands since. `RuleSwitcher`, and so
     the mods panel, goes through the room; in a room a batch's plain mod list changes nothing. Peers
     are "Player 1", "Player 2" until the game has names.
   - **The game serves the MCP:** the tools moved into `Chemistry/Mcp/` so Unity compiles them; both
     hosts read them from the `RulesTool` attributes. `RulesMcpServer` is HTTP on `127.0.0.1:47777/mcp`
     (the next port up for a second copy), one JSON answer per POST (`McpRpc`), on its own thread;
     `apply` and `revert` cross to the main thread (`GameRoomDesk`), go in as this player's commands,
     whoever the tool names, and wait for the room's sentence. It starts with the game and turns away
     a request with another Host or Origin, so a web page can't reach it. There's no sign-in: anything
     running as this user on this machine can use it. The Esc menu's Play with Claude page (`ClaudeMenu`) shows the line to add it to Claude and how it works, and a switch that closes the port (on by default,
     kept per player, J-047).
     **What answers is a seam:** `IChatHarness` (`Chem.Mcp`) is Claude Code behind `ClaudeChat`, or for evaluation the game's own loop behind `ApiChat`
     (`ApiLoop` in the core, `ApiWire` in the view for HTTP and the key). The loop is a client of this same server over the same HTTP, so a room's rules, `save`'s
     refusal and the tools' text are the server's for both. Without Claude Code there's no `dontAsk` or allow-list, so the loop decides what runs: only `ApiLoop.Allowed`, by name,
     plus `add_note`, and a new server tool stays out until it's named there. Only `chatprompts --harness api` starts it; `docs/telemetry/chat-api-2026-11-02.md` has the numbers.
     The chat starts claude with its own short system prompt (`ClaudeRun.ChatPrompt`): role, notes and staying in the tools,
     and no game knowledge. The game puts `notes.md` in the message, quoted line by line as a record and not instructions (`ClaudeRun.Message`), after the cached prefix; the record keeps the player's words alone. `Read`, `Edit` and `Write` of that file stay allowed, as claude's `Edit` needs a `Read`, and a terminal still opens it through the guide.
     It asks for Haiku (`ClaudeRun.ChatModel`, J-066); the Developer menu can leave the model to the account. If the account refuses the model, the message fails with no second run, and a red line names the model and says where to change it (#64, `docs/telemetry/chat-notes-2026-11-02.md`). Asked for something, Claude writes it: the server's instructions and `apply`'s description say to
     build a draft, `simulate` it if `validate` says to, and apply it with a title; a saved mod only if
     the player names one. **What a tool needs goes in its description, not the instructions:** Claude Code
     writes the instructions again with every prompt, and keeps the descriptions in the cached block. `apply` answers with what the draft changed, field by field, and a draft
     with no title is refused. Without `simulate`, "grass burns twice as fast" went in as half the
     fuel, and a fire then died before it spread (`roomasks`).
     Probes: `roomopen`, `roomhost`, `roomlocked`, `roommcp`, `roomclaude`, `roomasks`, `roomclip`.
4. **Assets**: not now. Procedural families come first, and may make most of it unnecessary.

## Open questions

- How much simulation a peer runs before switching on an incoming change: the checks only, or a
  short simulate too (it must finish inside the tick the host scheduled).
- The TS sandbox runs the same rules. If the MCP becomes the way rules change, do we freeze the
  sandbox? (already open in STATE)
- Budgets: start from what the perf work measures (storm at 20 fps, #24-#33).
