# World squares, on paper

A paper prototype of `docs/design/world-squares.md` (J-080), one self-contained page: a seeded world plan solved
like a puzzle (bands, templates, edges and coasts matched, fillers), landmarks by biome, and the active, generated and
known rings as you walk. Open `index.html` in a browser. Not the game: invented templates, coarse chunk hashes.

Sizes: the nine (3 × 3), **12 × 10** (J-083: 8 land rows between an ice row and a desert row, wrapping east-west) and
12 × 12.

## The world sketch panel (draw first, then fit the world squares)

**World sketch** turns the plan round: pick a biome (lake or sea, marsh, grassland, forest, desert, ashlands, ice cap,
mountains) and drag closed outlines on the world; they wrap east-west, overlap (the last on top) and interleave at their
edges over the blend width, after the climate's warp. Each world square with a quarter or more drawn asks the band its
biomes allow and weighs templates by what's drawn on it; the solve and its side rules are unchanged. Three views, or all
three side by side: the sketch, the world squares the plan picked (a solid red box: a rule changed the band or the sea;
a dashed one: another anchor biome; faded: under a quarter drawn) and the land that comes out. The list names each
break's world square, rule and what the plan did instead; tap a row or a world square for its reading. *Land follows
the sketch* paints the drawn biome where the plan has it in that world square or one beside it, and drawn lakes in low
land become water. The map's **Sketch** layer shows the outlines. Save, copy, download or load a sketch as JSON
(`docs/design/world-squares.md`, "Draw first"); samples: a lake district (the nine), a mountain spine and a default
12 × 10. From the address: `#sketch=lake-district|mountain-spine|default-12x10&sview=all|sketch|squares|land&sksel=x,y`,
and `#size=12x10`.

## The horizon panel (section 4, the far ring in the view)

Below the map, **Horizon** shows what the character sees from its chunk toward the far ring, before phase 6 builds it.
A column raymarch over a heightfield, deterministic from the seed:

- **Generated chunks** (within `sight` 4) are sampled every 4th cell, coloured by the biome at each sample.
- **Known chunks** (within `know`, and stopping one chunk out to sea, as section 3 says) are drawn only from their
  9 × 9 heights (every 8th cell), painted one colour: the dominant biome of 9 samples.
- **Haze** is exponential-squared, `1 - exp(-(d × k / know_m)²)`, with `k` = 1.98 (Punchy's) giving 98% at `know`.
- **Landmarks** from the plan (villages' great tree, volcano, spire, castle, caves, outcrops, waystations) are stand-in
  far meshes, drawn inside the known ring as part of the land and past it out to their `seenFrom`, with their own
  haze (a set percentage at their `seenFrom`) and a fade into the sky over their last chunk and a half.
- Past the known ring there is no land: only the **sea plane** to the horizon (a toggle) and the sky. *Tint the rings*
  shows generated land green, known land purple and the void below the horizon magenta.

Sliders: facing, `know`, haze `k`, eye height, landmark haze at `seenFrom`, and a `seenFrom` offset for every
landmark. *Face* turns to a landmark; *Face the sea* to the nearest sea chunk. The table lists each landmark near you:
how far, its `seenFrom`, whether it is known land, a far mesh or not yet drawn, and its haze under the land's haze and
its own. The map draws the view's wedge from the character.

Stills from the address: `index.html#size=3&seed=valley-1&at=dx,dy&face=spire|sea|<degrees>&know=7&haze=1.98&eye=10`
`&lm=own|same&lmAt=0.9&seen=0&tint=1&thin=1&sea=0&shot=1` (`at` in chunks from the spawn; `shot` writes the canvas's
PNG data URL into `#hzShot`, for a headless browser's `--dump-dom`).

### What it suggests (3 × 3, seed `valley-1`)

- **`know` 7 with `k` 1.98 (98%, Unity `fogDensity` 0.00295 /m for ExponentialSquared) hides the ring's edge**, the
  square's corners too (they reach ~1 km, past 99.9%). Half the haze is at 283 m, so the generated ring at `sight` 4
  (384 m) is already 72% fog and known chunks (480-672 m) are 88-98%: the 9 × 9 heights are more than enough; known
  land reads only as a faint skyline. `know` 9 (question 3 (b)) moves half-haze to 364 m; worth it only if the land
  itself should read farther, not for landmarks.
- **Landmarks need their own haze.** Under the land's haze anything past `know` is ≥ 99% fog (the volcano at 10 chunks
  is 99.97%), so "shows through the fog" can't happen with one density. With their own haze at ~90% at `seenFrom`,
  the spire (`seenFrom` 12) reads from the valley's spawn (0.72 km, 59%) and the volcano, given `seenFrom` 14, from
  10 chunks off (1.14 km, 81%).
- **Fade far meshes into the sky, not the fog.** A tall thing hazed to the fog colour still stands out against a sky
  that is bluer above the horizon, so a landmark would pop in at its `seenFrom` even at 98% haze.
- **`seenFrom` only matters past `know`.** The great tree's 5, the castle's 6 and the lake's 3 are inside the known
  ring, so they never act as far meshes. To show through the fog a landmark wants `seenFrom` ≥ `know` + 3 or so:
  the volcano and the spire about 14 (1.3 km), a great tree or the castle 9-10, outcrops 9-10.
- **Read `seenFrom` as a radius** (chunks × 96 m). The map's Chebyshev square would draw a landmark 1.4 times farther on
  a diagonal than on a cardinal, against a haze that is round.
- **Looking out to sea there is nothing past the known ring**, so the view needs a sea plane to the horizon (maps phase
  7) or a void below the horizon in the fog colour. In Unity the fog doesn't touch the skybox, so its lower half would
  show there.
