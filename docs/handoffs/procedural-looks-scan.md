# Procedural looks: scan for id-keyed visuals (phase 2 input)

Read-only scan for `docs/RULES_MCP.md` phase 2, 2026-10-31 (u1, Linux worker,
no Unity: grep only, nothing changed). It lists places in
`unity/Assets/Prototype/Scripts/` where a look or effect is keyed to a
specific rule id (a material, spell, element or gas named in a `switch`, an
`if`, or a dictionary of prefabs, colours or styles). A mod that adds a new
id gets the fallback at each of these places (often nothing drawn), so each
one is a candidate to become a **look family** read from the rule data.

Most ids go through the constants in `RuleIds.cs` (`SpellIds`, `TerrainIds`,
`CoverIds`, `GasIds`, `ElementIds`); a few are bare strings. Left out:
gameplay logic that only reads an id (cabin walls, panes shattering, lake
tests), the probes (`Probe*.cs`), perf scenes, the playtest bot and tests.
**Family** is a guess, for the phase 2 work to confirm.

Already data-driven, for comparison: the fire's look (`burn.look` in
`materials.json`, read by `FireLook.FlamesOf`: flare, wood, oil) is the
pattern to copy.

## Terrain

| Where | Id(s) | Family (guess) |
|---|---|---|
| `CellDressing.cs:17-34` | every terrain (bedrock, stone, pillar, rubble, dirt, grass, brush, tree, stump, wood, deadfall, sprouts, sapling, pit, metal, rock, brick, rusted-metal, mud, sand, glass, pane, gate, gate_open, coil, logwall, worn-logwall) | ground: a colour and height per terrain, belongs in the material's data |
| `CellDressing.cs:37` | tree, sapling, brush, stump, rubble, deadfall | dressing: which terrains get props on them |
| `CellDressing.cs:39` | bedrock, stone, pillar | block: drawn as a raised block |
| `CellDressing.cs:79-81` | brush, sapling, stump, rubble, deadfall, wood, metal, rusted-metal, coil, gate, gate_open, worn-logwall | dressing |
| `CellDressing.cs:88-121` | brush, stump, sapling, tree, rubble, deadfall, wood, metal, rusted-metal, coil, gate, gate_open, worn-logwall, pillar | dressing: plant (tree, sapling, brush, stump), debris (rubble, deadfall), plate (metal, rusted-metal), machine (coil, gate), wall (worn-logwall) |
| `CellDressing.Machines.cs:44` | rock (height 1) | block: a stepping stone |
| `CellDressing.Machines.cs:105` | gate, gate_open | machine |
| `CellDressing.Machines.cs:124` | logwall, worn-logwall | wall: log |
| `CellDressing.Growth.cs:16` | tree | plant: a grown tree |
| `ValleyChemistry.cs:219-225` | tree, logwall, deadfall | fire: how tall the flames stand over a terrain (could be `burn` data) |
| `ChemGrid.Visuals.cs:194` | tree | fire: taller flames on trees |
| `ChemGrid.Visuals.cs:231` | rusted-metal → metal | effect: chips when rust is scoured off |
| `ChemGrid.Visuals.cs:397` | lava | glow: lava lights its cell |
| `CellLavaMesh.cs:35` | lava, lava-pool | liquid: molten (lava mesh) |
| `CellSlickMesh.cs:67-68` | rock (cooled lava) | liquid: molten crust |
| `CellSlickMesh.cs:72` | mud, glass, brick | glaze: a wet or shiny surface |
| `CellSlickMesh.cs:205-207` | mud, glass, brick | glaze: colour per terrain |
| `CellCrackMesh.cs:53` | brick | crack: brick pattern vs plain |
| `CellWaterMesh.cs:445`, `:474` | river | liquid: water, flowing (foam) |
| `WaterCurrents.cs:48` | river | liquid: water, flowing |
| `CellBlockers.cs:76` | tree | collider: trunk |
| `ValleyChemistry.Roofs.cs:55` | shingle | roof: the default roof material |
| `Burnable.cs:10`, `Editor/PrototypeSceneBuilder.cs:98` | grass | label: a prop's display name |

## Covers

| Where | Id(s) | Family (guess) |
|---|---|---|
| `CellSlickMesh.cs:61` | oil, grease | slick: a liquid film on the ground |
| `CellSlickMesh.cs:78` | oil, grease, frost | slick |
| `CellSlickMesh.cs:204`, `:209` | grease, frost | slick: colour per cover |
| `CellLavaMesh.cs:41` | embers | glow: hot debris |
| `ChemGrid.Visuals.cs:368` | ash | scorch: ash on the ground |
| `ChemGrid.Visuals.cs:382` | thorns | plant: spiky growth |
| `ChemGrid.Visuals.cs:404` | vines | plant: climbing growth |
| `SurfaceEffects.cs:437` | vines | plant: climbable |
| `ChemGrid.cs:103` | oil | test map legend (the yard's ASCII map), not a look |

## Gases

| Where | Id(s) | Family (guess) |
|---|---|---|
| `ChemGrid.Effects.cs:233-238` | steam, smoke | vapour: a particle prefab per gas |
| `ChemGrid.Effects.cs:53`, `:249`, `:263` | smoke, steam | vapour: smoke takes the fire's look; emission and material per gas |
| `FogBank.cs:51` | fog | bank: a thin veil at amount 1 |
| `FogBank.cs:54` | fog, dust, cloud, storm, snowcloud | bank: drawn as a fog bank rather than particles |
| `FogBank.cs:56-62` | dust, cloud, storm, snowcloud (fog the fallback) | bank: a style per gas |
| `SpellForces.cs:76` | steam | force: steam lifts things like water (physics, not a look) |

## Spells

| Where | Id(s) | Family (guess) |
|---|---|---|
| `Editor/ChemYardBuilder.cs:420-461` | fire-bolt, burning-hands, fireball, ray-of-frost, frost, geyser, wave, rolling-boulder, stone-ramp, create-water, sleet-storm, grease, shocking-grasp, gust-of-wind, thunderwave, levitate, misty-step, mage-hand, mold-earth, mold-earth-heap, earthen-trench, wall-of-stone, move-earth, plant-growth, spike-growth, hasten-time, control-weather, fog-cloud | cast: one `SpellVisuals.Entry` (projectile and impact prefabs, glow) per spell id; a new spell gets none. Families by element and shape: bolt, blast, beam, cone, storm, splash, sparkle |
| `Editor/ChemYardBuilder.cs:471`, `:488` | meteor, shapeshift | cast: as above (from the sky; tinted mist) |
| `SpellVisuals.cs:56` | (lookup by spell id) | cast: the dictionary itself |
| `WeatherFx.cs:39-40`, `ChemGrid.Whirls.cs:126` | meteor | cast: falling stars borrow Meteor's look |
| `Shapeshift.cs:234` | shapeshift | cast |
| `SpellRadialHud.cs:293` | acid-splash | icon: a special radial icon |

## Elements

| Where | Id(s) | Family (guess) |
|---|---|---|
| `ChemGrid.Whirls.cs:30-37` | fire, water, sand, smoke, snow | whirl: column tint per thing carried |
| `HudDraw.cs:94-97` | fire, water, earth, air | icon: colour per element |
| `HudDraw.cs:111-114` | fire, water, earth, air | icon: glyph per element |
| `SpellForces.cs:146-159` | fire, cold, lightning, water | force: how a cast pushes props (physics, not a look) |
| `SwimStyle.cs:36`, `SwimPose.cs:53` | water | liquid: swimming style |

## Sounds (effects keyed by id, for completeness)

| Where | Id(s) | Family (guess) |
|---|---|---|
| `GameSounds.cs:40-55` | air, fire, cold, water, grease, lightning, thunder, acid | sound: per element (fire bursts by shape) |
| `CastSoundTable.cs:128-131` | fire, water, earth, air | sound: the gather sound per element |
| `CastSoundTable.cs:150-152` | earth, mud, air, water, growth, thorns | sound: cast kind by element |
| `SpellCaster.cs:43` | acid, heat, sand, lightning | cast: elements with their own handling |

## What it suggests

- **Terrain** is the biggest group: `CellDressing`'s colour and height table
  plus its dressing switch. A `look` group on each material (`ground`,
  `dressing: plant | debris | plate | block | wall | machine`, colour,
  height) would cover nearly all of it.
- **Gases** split into two families already (`vapour` particles and `bank`
  fog styles); naming the family and its colour in `materials.json` would
  let a new gas draw.
- **Spells** are the one real prefab-per-id table (`ChemYardBuilder`'s
  `SpellVisuals`). A family per element and shape (bolt, blast, beam, cone,
  storm, splash) scaled by radius and power is the procedural version.
- `validate`'s sixth check ("every new thing names a look family the view
  already draws") waits on this list becoming the family list.
