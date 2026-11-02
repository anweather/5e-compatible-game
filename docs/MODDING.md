# Writing a mod

A mod is a **JSON patch** over the rule tables in `rules/`. It can add
or replace things by id. It can't run code. The sandbox checks a patch
against the engine before it can switch on, and switches it on for
everyone in the room at the same tick.

**Three ways to write one.** All of them start from the Experiments
panel's **New experiment**:

1. **By hand**, starting from the template in the panel.
2. **Write it with Claude**: describe the idea; the page asks Claude and
   fills in the patch.
3. **Copy brief for another AI**: copies a complete brief (this format,
   the current tables, your idea). Paste it into any LLM, then paste
   its answer back into the patch box. Both a bare patch and
   `{"title", "patch"}` work.

## The patch format

Every field is optional.

```jsonc
{
  "elements": ["acid"],                       // new element names; or entries by id, an element's look:
                                              //   { "fire": { "look": { "color": [0.25, 0.45, 1] } } }
  "terrain":  { "mud": { "name": "Mud", "tags": ["ground"] } },   // add by id, or patch: only the fields named change
  "covers":   { "tar": { "name": "Tar", "tags": ["flammable"], "flammability": 0.5, "fuel": 20 } },
  "gases":    { "spores": { "name": "Spores" } },
  "components": { "furred": "Has fur, which burns long." },   // new component names (rules/components.json)
  "entities": { "ogre": { "name": "Ogre", "hp": 30, "mass": 3, "components": ["creature", "heavy", "furred"] } },
  "items":    { "spyglass": { "name": "Long spyglass", "use": "zoom", "zoom": 4 } },   // things to carry (rules/README.md)
  "machines": { "boiler": { "name": "Steam boiler", "needs": "steam", "build": 2, "how": "Builds pressure faster." } },
  "reactions": {
    "add":       [ /* cell rules: new ones go to the TOP of their element's list, so they win */ ],
    "addEntity": [ /* entity rules */ ],
    "remove":    [ "fire-water" ]             // rule ids to delete
  },
  "spells":   [ /* SpellDef, add by id, or patch: only the fields named change */ ],
  "buildings": { "styles": { "adobe": { "wall": { "material": "stone" } } } },   // rows of rules/buildings.json
  "world":    { "buildings": { "cabin#2": { /* a whole building row, by id: edits it; a new id adds one */ } },   // docs/RULES_MCP.md "Buildings"
                "add": [ { "kind": "ogre", "cell": [120, 80], "rot": 90, "chance": 0.2 } ] },   // things in the world: docs/RULES_MCP.md "Things"
  "groups":   { "shadow": { "name": "Shadow", "order": 6, "color": "#5a4a7a" } },   // spell radial elements (Unity)
  "critters": [ /* CritterDef, add by id, or patch: only the fields named change */ ],
  "sim":      { "push": { "slideCells": 6 } } // deep-merged over sim.json
}
```

## Rules

The full schema is in `rules/README.md`.

```jsonc
{ "id": "ice-cracks", "element": "earth",
  "if": { "frozen": true },
  "do": [{ "melt": true }],
  "log": "The ice cracks and gives way",
  "notable": true, "stop": true }
```

**Conditions:**

| Key | Meaning |
|---|---|
| `cover` | List of cover ids |
| `fire` | `true`, `false`, or a minimum intensity |
| `fireBelow` | Fire intensity below this number |
| `water` | Unfrozen water is present |
| `frozen` | The water here is ice |
| `gas` | A gas id, or `"any"` |
| `terrainTag`, `notTerrainTag` | The terrain has (or lacks) this tag |
| `conductive` | Water or metal is present |
| `power` | The element came with at least this power (a charged cast: power times charge) |
| `chance` | Applies only this often (0 to 1), rolled last on the world's generator |

**Effects** (exactly one per object):

| Group | Effects |
|---|---|
| Fire | `ignite` (a number, or `"cover"`), `extinguish` |
| Water and gas | `boil`, `melt`, `freeze`, `condense`, `addWater`, `gas` (with `n`), `pushGas`, `clearGas` |
| Covers | `setCover`, `spreadCover` |
| Terrain | `break`, `setTerrain`, `raise`, `grow` (with `ticks: [min, max]` and `wave`: it ripens into its `growsTo` later) |
| Energy | `conduct`, `fan` |
| Machines | `mark: "wind"` |
| Entity rules | `status: {name: ticks}`, `clear`, `damageMul` |

**Shaping the ground** is a spell effect, not a reaction verb. A spell with
`"effect": "terraform"` raises or digs every square it touches, by
`raise` or `dig` steps or up to an absolute `height` (exactly one); see
`rules/README.md` for the keys.

**New verbs need code.** If an idea can't be said with these, write it
up as an experiment anyway, with the idea filled in and a best-effort
patch, and ask for the verb as a code change.

## Worked examples

**Steam is scalding: fire can't take hold where steam hangs.**

```json
{ "reactions": { "add": [
  { "id": "steam-damps-fire", "element": "fire", "if": { "gas": "steam" },
    "do": [{ "extinguish": true }], "log": "The steam keeps everything too damp to burn", "stop": true }
] } }
```

**Earth on ice cracks it.** The burrow mole now breaks frozen ponds.

```json
{ "reactions": { "add": [
  { "id": "earth-cracks-ice", "element": "earth", "if": { "frozen": true },
    "do": [{ "melt": true }], "log": "The ice cracks and gives way", "notable": true, "stop": true }
] } }
```

**A new critter, the bog toad, that spits grease.**

```json
{ "critters": [
  { "id": "bog-toad", "name": "Bog toad", "glyph": "🐸", "color": "#7a8f3a", "mode": "emit",
    "element": "grease", "every": 10, "blurb": "Leaves a slick trail. Mind the fire." }
] }
```

**A deep moat.** Dig three steps instead of two (the most the ground goes
down is `sim.height.min`).

```json
{ "spells": [
  { "id": "earthen-trench", "group": "earth", "name": "Earthen Trench", "level": 2, "shape": "line", "origin": "self", "length": 8,
    "effect": "terraform", "dig": 3, "blurb": "A 40 ft moat three steps deep from your feet." }
] }
```

A mod changes only the fields it names on an entry that's already there
(`Rules.MergedEntry`): groups such as `crater` or a material's verbs merge key
by key, `null` takes a field out, and a list or number replaces. So mods stack,
and a revert undoes only its own. A new spell needs its `group` (which element
of the Unity game's spell radial it sits under, from `rules/groups.json`); with
none it goes under the last element. `"radial": false` keeps one off the radial
(Earthen Trench and Move Earth: its Earth is Raise Earth, Lower Earth and Wall
of Stone).

**Charged casts.** Holding the cast charges a spell (1, a tap, to 3): its
power times the charge, a terraform's raise or dig times the charge (a
`height` 2 more per level), a line half again as long per level, and a
spot, square or burst a square bigger per level (a point spreads to the
squares within a step or two; earth shaping goes deeper instead). In the
Unity game a push is half again as hard per level, and the spells that
move things reach further (Misty Step, Mage Hand) or last longer
(Levitate). A mod that changes `power`, `raise`, `dig`, `height`,
`length`, `size`, `radius`, `push`, `range` or `duration` changes what
each charge does.

**A new element on the spell radial.** Frost gets its own wedge; Ray of
Frost moves to it.

```json
{ "groups": { "frost": { "name": "Frost", "order": 2.5, "color": "#bfe6ff" } },
  "spells": [
  { "id": "ray-of-frost", "group": "frost", "name": "Ray of Frost", "level": 0, "shape": "line", "origin": "self", "length": 5,
    "element": "cold", "damage": [1, 8], "blurb": "Cantrip. A 25 ft beam of frost." }
] }
```

**Longer slides on ice.**

```json
{ "sim": { "push": { "slideCells": 7 } } }
```

More complete examples, saved as `{"title", "idea", "patch"}`, are in
`rules/mods/`. The Unity game reads the same mods (`ModPatch.Parse` in the
C# core) and also understands `sim.fire.fadeCells`: fire dies out after
about that many squares (the sandbox ignores it). It is stricter about
types, too: a sim number that isn't a number, or is negative, is refused
(except `sim.height.min`, which must be 0 or below).

## Using the rules MCP

Instead of writing the patch by hand, you can ask your own Claude to build
it through the **rules MCP server** (`core/Rules.Mcp`, design in
`docs/RULES_MCP.md`). Its tools are typed, every change is checked, and it
can run the change headless before anyone plays it.

**Add it to Claude Code**, from the repo root (it needs the .NET 8 SDK):

```sh
dotnet build core/Rules.Mcp -c Release
claude mcp add rules -- dotnet core/Rules.Mcp/bin/Release/net8.0/Rules.Mcp.dll --rules "$PWD/rules"
```

**Or add the game itself.** A running copy serves the same tools on your own
machine, against the world you're playing in, so `apply` changes the game in
front of you, for everyone in the room. Nothing needs installing. Run this once
(Or press F9: a terminal opens with all of this set up, and you type `claude`. Esc > Play with Claude shows it with the address to use, and the switch that closes the port):

```sh
claude mcp add --scope user --transport http spell-valley http://127.0.0.1:47777/mcp
```

Then ask Claude for a bigger fireball. It writes the change itself as a new draft, checks it and
switches it on with a title, and tells you what it changed; it takes a saved mod only if you name one. What goes in is up to the room's change
mode, picked in Esc > Play together before hosting: Open, Host only or Locked.

**The tools:**

| Kind | Tools |
|---|---|
| Read | `list(table)`, `describe(table, id)`, `explain(element, material)` |
| Draft | `add_material`, `set_material`, `add_reaction`, `set_reaction`, `add_spell`, `set_spell`, `tune`, `remove` |
| Check | `validate(draft)`: shape, references, the design rules, determinism, budgets |
| Run | `simulate(draft, scenario, ticks)`: `fire-on-grass`, `fireball`, `flood` or `storm`, with and without the draft |
| Save | `save(draft, title, idea)`: writes `rules/mods/<name>.json` once it validates |

A draft is a patch held in memory under an id (`d1`, `d2`...): leave the
draft out of the first call to start one, then pass its id to the rest.
`set_` tools write only the field they set into the draft, so the mod patches
just that. `validate` and `simulate` also
take a mod's name from `rules/mods` (`wildfire`) or the patch JSON itself.
What one mod may ask for (new entries, spell sizes, spread rates, the
ticks a run may take) is in `rules/budgets.json`. A new terrain or spell needs a `"look"`
naming a family from `rules/looks.json` so the game can draw it (`rules/README.md`, "Looks";
`rules/mods/moonstone.json` is a whole example); changing a look alone needs no rebuild and moves
no fingerprint.

**An example conversation:**

> **You:** Make steam put fires out, and check it doesn't wreck the meadow fire.
>
> **Claude** calls `explain(fire, steam)`: no rule for fire on a cell with
> steam stops the list, so what burns depends only on the terrain under it.
> It calls `add_reaction(id: "steam-damps-fire", element: "fire",
> conditions: {"gas": "steam"}, effects: [{"extinguish": true}], log: "The
> steam keeps everything too damp to burn", stop: true)` and gets draft
> `d1`, then `validate(d1)`: no problems. Then `simulate(d1,
> "fire-on-grass")`: 3,566 cells changed with the mod and without it, but
> the fire peaks later (tick 145 against 108) and there's a third of the
> smoke left at the end (12 cells against 34); ticks per second well within
> the budget.
>
> **Claude:** Steam slows the fire where the pond boils but doesn't save the
> meadow: it all burns either way. Save it as it is, or make it stronger?
>
> **You:** Save it as it is; call it "Damp steam".
>
> **Claude** calls `save(d1, "Damp steam", "Steam keeps everything too
> damp to burn, so fire stops where steam hangs.")`: written to
> `rules/mods/damp-steam.json`.

If `validate` finds a problem, each one is a sentence saying what to
change ("Spell fireball has a radius of 20; a mod's spell may have at most
6, so one cast can't cover a chunk or stall the tick."), so Claude can fix
the draft and try again.

## Good experiments

- **One idea per mod.** Stacking three changes makes the vote mean
  nothing.
- **Write the idea in words, too.** That is what people vote on, and
  what goes into the repo when it's kept.
- **Play it for a few minutes with the mod on, then off.** The
  difference is the result.
- **Leave a note when you vote.** "Fun once, then annoying" is the most
  useful note there is.
