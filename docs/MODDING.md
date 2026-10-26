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
  "elements": ["acid"],                       // new element names
  "terrain":  { "mud": { "name": "Mud", "tags": ["ground"] } },   // add or replace by id
  "covers":   { "tar": { "name": "Tar", "tags": ["flammable"], "flammability": 0.5, "fuel": 20 } },
  "gases":    { "spores": { "name": "Spores" } },
  "entities": { "ogre": { "name": "Ogre", "hp": 30, "mass": 3, "flammable": false, "creature": true } },
  "items":    { "spyglass": { "name": "Long spyglass", "use": "zoom", "zoom": 4 } },   // things to carry (rules/README.md)
  "machines": { "boiler": { "name": "Steam boiler", "needs": "steam", "build": 2, "how": "Builds pressure faster." } },
  "reactions": {
    "add":       [ /* cell rules: new ones go to the TOP of their element's list, so they win */ ],
    "addEntity": [ /* entity rules */ ],
    "remove":    [ "fire-water" ]             // rule ids to delete
  },
  "spells":   [ /* SpellDef, add or replace by id */ ],
  "groups":   { "shadow": { "name": "Shadow", "order": 6, "color": "#5a4a7a" } },   // spell radial elements (Unity)
  "critters": [ /* CritterDef, add or replace by id */ ],
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

**Effects** (exactly one per object):

| Group | Effects |
|---|---|
| Fire | `ignite` (a number, or `"cover"`), `extinguish` |
| Water and gas | `boil`, `melt`, `freeze`, `condense`, `addWater`, `gas` (with `n`), `pushGas`, `clearGas` |
| Covers | `setCover`, `spreadCover` |
| Terrain | `break`, `setTerrain`, `raise` |
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

A replaced spell is replaced whole, so keep its `group` (which element of
the Unity game's spell radial it sits under, from `rules/groups.json`) and
its `"radial": false` if it had one (Earthen Trench and Move Earth do: they
stay in the rules but off the radial, whose Earth is Raise Earth, Lower
Earth and Wall of Stone). A spell with no group goes under the last element.

**Charged casts.** Holding the cast charges a spell (1, a tap, to 3): its
power times the charge, a terraform's raise or dig times the charge (a
`height` 2 more per level), a line half again as long per level. A mod
that changes `power`, `raise`, `dig`, `height` or `length` changes what
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

## Good experiments

- **One idea per mod.** Stacking three changes makes the vote mean
  nothing.
- **Write the idea in words, too.** That is what people vote on, and
  what goes into the repo when it's kept.
- **Play it for a few minutes with the mod on, then off.** The
  difference is the result.
- **Leave a note when you vote.** "Fun once, then annoying" is the most
  useful note there is.
