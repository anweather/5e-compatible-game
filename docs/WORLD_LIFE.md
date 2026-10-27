# World life: villages, creatures and wizards from seeds

How the world gets more to look at without an art pipeline: buildings,
villages, ambient creatures and the wizards' looks are all **generated
from a seed and a small recipe**, so variety costs data, not assets. The
decision behind it is D12 (`adr/0012-generated-content-from-seeded-recipes.md`).

## The pattern

Every generator has the same three parts:

1. **A recipe** in data (`world/*.json`): ranges and choices, not models.
   A house recipe says "2–3 storeys, timber or stone, gable or hipped
   roof"; a life layer says "fish, 3 shoals per hectare of lake".
2. **A seed**: the recipe plus a seed always gives the same result, on
   every machine. Variant `n` of a recipe is just another seed.
3. **A builder** that turns the result into meshes or behaviour from
   primitive shapes (boxes, prisms, tubes, cones) with colour in the
   vertices. No textures and no imported models.

Then "more life" means one of three things: turn a density up, add an
entry to a recipe file, or (for something new) write a builder plus its
entry. The recipes are the knobs.

## What's shared and what isn't (lockstep)

The rule from D3/D4/D7 still holds: anything that changes what players can
do to the world lives in the core and is the same everywhere.

| Part | Where | Why |
|---|---|---|
| Village layout: the square, lanes, building footprints | Core (`Chemistry/Village.cs`), integer cells, `WorldHash` | Footprints are wall cells that burn, block and break |
| Building meshes, colours, signs, smoke | View (`Scripts/Village/`) | Only looks; rebuilt from the same spec on each machine |
| Fish, birds, critters, pollen | View, per machine | Nothing in the game depends on where a fish is (D9 allows view-only crowds) |
| Wizard look | View, synced as cosmetic state | Each player sees the same wizard, but it isn't tick state |
| Villagers | View for now | They become core entities when they matter to play, the same open question as the tower-defense critters (BACKLOG §17) |

View-only life still **reads** the cells: fish keep to water cells and
scatter from lightning, birds flee fire, villagers walk on lanes and run
from a burning house. That's where spell chemistry shows up in it.

## Villages

### Layout (core)

`VillagePlan.Lay(spec, seed, buildable)` places a village on cells:

- **The square:** a dirt square in the middle with a well (one water cell,
  so spells have water to work with).
- **Landmarks on the square:** the inn and the shop face it. Its fourth
  side opens to the road.
- **Lanes:** two cells wide, leading out from the square's other sides.
- **Houses:** on plots along both sides of each lane, facing it, with gaps
  between them. A plot where `buildable` says no (water, a steep slope, the
  yard) is skipped. `buildable` is the caller's, so the core stays
  engine-free.

Everything is axis-aligned to the cell grid, so a footprint is exactly
the cells it covers. Timber buildings are log-wall cells (`L`): they burn
like the cabins. Stone ones are stone-wall cells (`#`): they don't burn,
but earth shaping can still break them.

The plan returns plain data: the square and lanes as rectangles, and each
building's kind, footprint, door side, storeys, roof, walls and its own
seed. `VillageTests` pins a plan's digest.

### Looks (view)

`MeshKit` is the shape kit (boxes, gable prisms, hip roofs, tubes, cones,
faces with one colour each, darkened near the ground for cheap AO).
`BuildingMesher` turns one building into two meshes, its body and its
roof, so the cabins' burn rules work on it unchanged:

- **Walls:** timber frame (plaster panels between dark beams, an upper
  storey jutting out), stone (blocks of jittered tone), or log.
- **Roof:** thatch (thick, rounded eaves), slate or shingle; gabled or
  hipped; with or without a chimney.
- **Details:** a door with a step, shuttered windows that glow at night,
  flower boxes, the inn's hanging sign, the shop's striped awning.
- **Colours:** from small palettes (plaster, timber, stone and roof
  tints), picked by the building's seed.

A `Village` component in the scene holds the spec. It builds the meshes
when it loads, so the scene stores only the spec and nothing generated
reaches git.

### Trying it

Change `seed` in `world/villages.json` (or `houses`, `square`, `lanes`,
`stoneInTen`) and run **Prototype > Build Chemistry Yard**. The first
village, Hollowmere, stands west of the yard. Set fire to it: timber walls
burn and spread from house to house, a burnt building chars and loses its
roof ("The inn burns down"), and stone houses stand.

### Where villages go

To start with, one village west of the yard, from `world/villages.json`,
placed by the valley builder: `ValleyShape` levels a pad for it, and
`WireChemistry` writes its cells. Once settlements are ported into
`ThemeBlueprint` (D11), the themes' `town` features will place villages
in the generated land the same way.

## Life layers (next)

`world/life.json` will hold one entry per layer, each with a density and
the cells it lives on:

| Layer | Examples | How it's drawn and moved |
|---|---|---|
| Ground | deer, rabbits, foxes | `ValleyFauna` (exists) |
| Sky | flocks of birds, crows, a hawk circling | light flocking on the CPU, one instanced batch |
| Water | shoals of fish, a fish jumping now and then | moved in the shader, like the fireflies |
| Air | pollen, mist over water, embers by a fire | Punchy's ambient particles (credited) |
| Critters | fireflies, butterflies | `AmbientCritters` (exists) |
| Village | chimney smoke, lit windows, villagers | the village's own life, leashed to it |

There's one master "life" level as well, so a slow machine can turn it
all down together.

## Wizards (later)

A `WizardLook` recipe and a seed give each wizard their own look: the
height and stoop of the body; the robe's length, flare, hem and colours;
the hat's height, brim, droop, bend and band; the beard; a staff (gnarled,
crystal or orb). The parts hang off the Starter Assets rig's bones in
`ChemYardBuilder.AddWizardParts`, so poses, IK and both wizards keep
working. Online, the seed goes out as cosmetic state.

## Credit

Punchy (Zachary Franklin, credited in `ThirdParty/Punchy/README.md`) has
the ideas this follows: buildings as seeded variants of a species-like
recipe, settlements as props around a centre, residents leashed to home,
critters moved in shaders. Our mesh kit is our own, because Punchy's is
built around its flora shader. Code taken from Punchy later goes under
`ThirdParty/Punchy/` with a header and a README row, as before.
