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

## Life layers

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

## The coast, seats and currents (built)

- **The coast** (`Editor/ValleyCoast.cs`, `Editor/CoastBuilder.cs`, `Ocean.cs`, `Shaders/Ocean.shader`):
  - Over a wooded hill south-east of the lake: dunes with palms, a sand beach, and the sea out to the horizon.
  - The sea is one level with the lake (`LakeLevel`), so its squares are the lake's kind of water.
  - Its mesh carries the water's depth, so the swell shoals, breaks into foam and washes up the sand.
- **The island:** Sunward Key is out in the bay. Its resort is a village in `world/villages.json`, and it has palms, glowbulbs and a dock.
- **The path:** a dirt path (`ValleyCoast.PlanPath`) runs from the yard over the river's bridge and the hill down to the beach.
- **Generated props** (`GeneratedProp`, `PropMesher`): palms, glowbulbs, loungers, umbrellas, towels, benches, driftwood, starfish and the dock. Palms stand on tree squares and burn.
- **Seats:**
  - `Seat`, `Sitter` and `SitPose`: E sits the wizard on a bench, or lays it back on a lounger.
  - The pose is worked out on every machine from where the wizard is.
- **Currents:**
  - `WaterCurrents` carries swimmers (`CurrentRider`) and floating props (`PhysicsProp`): downstream in the river, and ashore with each breaker in the surf.
  - Physics only, as D4 says.
- **The falls:** a 36 m cliff whose rocks are solid, and a walkable scree ramp up its east flank.
- **Probes:** `sit`, `current`, and `falls` (with its ramp).

## Backlog: a world that's lived in

What would make the valley feel like a place people and animals live in,
roughly in order of how much life each adds for its cost. Each one follows
the pattern above: a recipe, a seed and a builder, view-only unless it
changes what players can do.

### Next: cheap and very visible

1. **Gulls over the beach.** Built (AmbientLife).
   - A flock circling the surf and crying.
   - They land on the dock's bollard, the palms and the umbrellas.
   - They scatter from fire, thunder or a wizard running at them.
   - The first sky layer: light flocking on the CPU, one instanced batch.
2. **Fish.** Built (AmbientLife).
   - Shoals in the lake, the river and the shallows, moved in the shader like the fireflies.
   - Now and then one jumps.
   - They scatter from lightning, and freeze in place under ice.
   - Later: catchable (then they're core).
3. **Crabs on the beach.** Built (AmbientLife).
   - They scuttle sideways and dig into the sand when you come close.
   - At night they come out by the water.
4. **Villagers.** Started: they wander their village, leashed to it (ValleyFauna, Village.residents). Still to do:
   - Hollowmere's and Sunward Key's folk walk between their doors, the well and the benches, and sit on the benches (`Seat` already poses any humanoid).
   - They run from a burning house, and go indoors at night and in rain.
   - The barkeep stands behind the beach bar.
   - View-only at first: Punchy's leashed residents, using its humanoid fauna.
5. **Sound.**
   - A soundscape per place: surf and gulls on the beach, birdsong in the woods, the river's rush, chatter and a fiddle from the inn at night.
   - Today only the falls roars.

### Things to do in the places that exist

6. **A rowing boat at the dock.**
   - It floats on the currents and you can ride it out to sea and back.
   - The floating-props physics is already there.
7. **Rafts and logs down the river.**
   - Felled trees (deadfall) float off down the river, and you can ride one.
8. **The inn's inside.**
   - Walk into the inn: tables, chairs (seats), a hearth that's real fire, a cellar of barrels.
9. **Market stalls on the square.** Goods that are items (`items.json`), and a shopkeeper.
10. **Tide pools and shells.**
    - Shells and starfish you can pick up (items).
    - Pools in the rocks with little critters.
    - Sandcastles from Mold Earth.

### Dressing the land

11. **A path network.**
    - Paths between every place (yard, village, lake, beach, falls), with signposts at the forks.
    - Lanterns along them that light at dusk.
    - A road out of the valley.
12. **Fields and orchards round Hollowmere.**
    - Crops and fruit trees as squares that burn.
    - Fences, a hay cart, a scarecrow, a sheep pasture.
13. **Lights at night.**
    - Lanterns on the dock, torches by the inn door.
    - The window glow already works (checked at 22:00: windows, the inn's door lamp, the island's glowbulbs).
14. **Wind made visible.**
    - Palm fronds, grass and laundry lines sway with the weather's wind (`World.Weather`), and the inn's sign swings.
15. **Day and night.**
    - Bats and owls at dusk, songbirds at dawn, fireflies (built) over the lake.
16. **The horizon.**
    - Distant headlands to close the gap left in the skybox mountains over the sea.
    - A ship that passes now and then, and more islands far off.

### Bigger moves

17. **Grow the world grid.**
    - The island can't go farther out: the cells stop at the map's south-east corner.
    - Another row and column of chunks would allow a farther, bigger island and more coast. That moves cell counts pinned by the netcode checksums and tests, so it needs its own plan.
18. **Villages from the themes.** Port settlements into `ThemeBlueprint` (D11), so the generated land's `town` features each get a village.
19. **A second strange biome.** The island's glowbulbs, grown into a whole weird shore: tide-pool forests, giant shells, coral arches.
20. **`world/life.json`.**
    - One file of density knobs for all of the above, with a master "life" level for slow machines.
    - A `life` probe that counts what's out and measures what it costs.

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
