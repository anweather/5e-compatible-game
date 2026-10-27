# Finer ground: smooth channels without smaller squares everywhere

Backlog #15 (§18, "Finer ground"), with Andrew's idea of a different
granularity per effect. A design spike, 2026-10-27, World lane. Nothing here
is built. The spike's prototype is on branch `finer-spike`
(`FinerSpikeTests`), never merged.

**The problem:** a cell is 1.5 m (the SRD's 5 ft square). A trench dug at
a slant, or the river, steps a whole cell at a time, so it reads as a
staircase, not a channel. The terrain already slopes between cell centres
(`CellHeightfield`), and a wider blur was tried and rejected: it flattens a
one-cell pit to a quarter of its depth.

## What we have

- **One `Height` per cell:** whole 0.5 m steps of earth raised or dug,
  `sim.height` −6 to +8, a wall from +3. Water reads `ElevationUnits` =
  (terrain elevation + `Height`) × `unitsPerStep` (4) every tick.
- **Shapes** (lines, slanted lines `SlantCells`, squares, bursts, cones,
  craters) pick whole cells. Every number in `rules/` that measures space
  (radius, length, size, spread, reach) counts cells: 5 ft squares.
- **Goldens:** `World.CellHash` appends `Height` and `Life` only where they
  aren't 0, which is how new cell state has joined without moving old
  goldens.
- **Saves** (branch `stream-disk`, unmerged): changed cells whole, by local
  index, `SaveFormat` 1.
- **Caves** (`docs/CAVES.md`, option C): levels below, each its own 2.5D
  grid like the surface.

## Three ways

### 1. 0.75 m cells everywhere

Halve the cell: four times as many, each a quarter the area.

- **Cost:** four times the sim, every effect. From the caves spike's numbers
  (an active chunk with water moving is about 0.055 ms a tick), a wizard's
  5 × 5 reach goes from about 1.4 ms to 5.5 ms a tick before fire and gas,
  and Hasten Time's 20 ticks a frame goes from heavy to impossible. Memory:
  the valley's 330 000 cells become 1.3 million (about 130 MB as today's
  `Cell` objects).
- **The rules:** every space number in `rules/` doubles. A cell is no longer
  the SRD's 5 ft square, so spell areas, speeds and the defense field's
  paths all change meaning, and the 5e reading ("a 20 ft radius is 4
  squares") gets a factor of two everywhere.
- **Lockstep and goldens:** still integer and deterministic, but **every
  golden moves** in both engines, and so does the network checksum.
- **The sandbox:** its board, its numbers and every golden, rebuilt.
- **Saves:** chunks of 64 × 64 cells cover a quarter of the ground, so the
  chunk size, the streaming reach and the save's chunk keys all change.
- **Caves (C):** levels inherit it at the same ×4 cost.

### 2. Finer height only: 2 × 2 sub-squares per cell, water reads the mean

Andrew's per-effect granularity. Each cell keeps four 0.75 m sub-square
heights instead of one `Height`. Fire, gas, charge, water, statuses and
every rule number stay per 1.5 m cell. Only shaping (a cast, rare) works on
the finer grid.

- **Cost:** memory: four heights fit one int (`sbyte` × 4, the spike's
  `SubHeights`), 4 bytes a cell, 1.3 MB for the valley. Time: shaping does
  up to four writes instead of one, once per cast. Water reads the sum of the
  four, which is exact in water units: four sub-squares in steps, × 4 units
  per step, ÷ 4 sub-squares is the sum itself (the spike pins it). Kept as a
  per-cell field updated when the ground moves, it costs the tick nothing.
- **What gets smoother:** slanted lines dig sub-squares along the true aim.
  The spike's 15 m trench at a shallow slant strays at most 0.47 m from its
  aim, against 0.95 m with whole cells. Round craters cut round. The terrain
  is drawn from the sub-squares (`CellHeightfield` samples at 0.75 m), so the
  channel is drawn smooth. Water still moves a cell at a time, but along a
  channel whose floor slopes by quarter-steps.
- **Lockstep and goldens:** integer, ordered, deterministic. A cell whose
  four sub-squares are alike is today's cell (the spike checks every height
  and elevation), and `CellHash` appends the four only where they differ, as
  `Height` and `Life` joined. So **the goldens stand** for every shape that
  stays whole-cell (straight lines, squares, bursts, cones); only shapes we
  move to the finer grid (slanted lines, craters) get new goldens, shared
  with the sandbox.
- **The sandbox:** `Cell` gains the packed four (absent when uniform), and the
  shaping of slanted lines and craters is mirrored, with one shared golden.
  Water and every other system are unchanged.
- **Saves:** a changed cell's record gains its four heights. If finer ground
  lands before saves ship, it goes into `SaveFormat` 1; after, it is
  `SaveFormat` 2.
- **Caves (C):** levels are 2.5D grids like the surface, so they get it for
  free. A shaft opens when a cell's sub-squares reach the bottom (all four,
  or the mean: a question below).

### 3. Finer height and finer water

As 2, and water also lives on the 2 × 2 sub-squares and flows between them.

- **Cost:** water is the biggest tick cost when it moves, and it becomes four
  times the work: the settled 5 × 5 lake's 5.0 ms tick (0.7 ms with
  still-water skipping) becomes about 20 ms while water moves. Memory: three
  more water counts a cell.
- **The rules:** everything that reads a cell's water (dousing, freezing,
  boiling, conducting, rain filling, deep water for raiders, swimming) needs
  a cell total or a rule per sub-square, and `water.maxLevel` changes
  meaning. Mixed resolutions everywhere water meets anything else.
- **Lockstep and goldens:** deterministic, but **every water golden moves**,
  and the network checksum.
- **The sandbox:** its water sim, mirrored line by line, rewritten twice.
- **Saves:** four water counts a changed cell.
- **Caves (C):** links per sub-square. Water falls down a shaft as finely as
  it was dug.
- **What it buys over 2:** water running smoothly down a slanted channel,
  rather than a cell at a time along a smoothly drawn channel. Visible only
  in water moving fast along a slant.

## Side by side

| | 1. 0.75 m cells | 2. Finer height | 3. Finer height + water |
|---|---|---|---|
| Tick cost | ×4 | unchanged | water ×4 |
| Memory (valley) | ×4 (≈130 MB) | +1.3 MB | +1.3 MB + 3 ints a cell |
| Rule numbers | all double | unchanged | water's change |
| SRD 5 ft squares | lost | kept | kept |
| Goldens | all move | stand (new ones for slanted and round shapes) | water's move |
| Sandbox | rebuilt | shaping mirrored | water rewritten |
| Saves | chunk size changes | four heights per cell | and four water counts |
| Caves (C) | ×4 per level | free | links per sub-square |
| Slanted trench | smooth | drawn smooth, dug along the aim | smooth, flows smooth |

## Recommendation: 2, finer height, water reading the mean

It's the backlog's own guess, and the spike supports it. It costs almost
nothing a tick, keeps the SRD's square and every golden that isn't about a
slanted or round shape, and touches only shaping and drawing in both
engines. Water through a smoothly dug channel still moves a cell at a time,
but the eye sees the channel. Go to 3 only if a playtest shows water moving
visibly wrong along a slant, and then only for water.

**Slices:**

1. **The core:** `Cell` keeps four sub-square heights (one packed int, a cached
   water-units field), `Height` reads as their mean for everything that
   asks, and slanted lines and craters shape sub-squares. `CellHash` appends
   them when they differ. Tests: uniform cells are today's (goldens stand),
   and a new golden for a slanted trench and a crater, shared with the
   sandbox.
2. **The sandbox:** the same, mirrored.
3. **The view:** `CellHeightfield` samples the sub-squares, keeping a one-cell
   pit at full depth.
4. **Saves and caves:** the four in a save's cell record, and a shaft opening
   by its sub-squares.

## Andrew's questions

1. **Which shapes go finer?** Proposed: slanted lines and round craters only,
   while straight lines, squares, bursts and cones stay whole cells. That
   keeps the goldens and the SRD's square areas.
2. **When is a half-raised cell a wall?** When its highest sub-square reaches
   `blockAt` (a slanted wall has no gaps to walk through), or when its mean
   does?
3. **Is water moving a cell at a time acceptable** along a smoothly drawn
   slanted channel, with finer water only if a playtest shows it looks wrong?
4. **Before or after saves ship?** If finer ground lands first, the four
   heights go into `SaveFormat` 1 and old saves never need converting.
5. **Caves:** does a shaft open when all four sub-squares reach the bottom,
   or when their mean does?
