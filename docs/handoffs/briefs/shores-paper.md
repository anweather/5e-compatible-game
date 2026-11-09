Needs: any
Area: tools/world-paper/, docs/design/world-squares.md (a new section only), docs/GLOSSARY.md (new entries only)
Branch: shores-paper

# Water that doesn't look square: shores, marsh, deltas, lakes (paper)

Andrew, 2026-11-09, on the 12 x 10 heights picture (`docs/design/world-12x10/heights.png`): the land reads well but
the water is still square. Part of it is #176 (the sea floor drops ~175 units in one cell where a coast's sea ramp
meets an open-sea world square). He wants water edges that blend, and not one way everywhere:

- **Marsh:** where land is low and flat by water, a fringe of small land bits standing out of shallow water, so the
  shore breaks up instead of being a line.
- **Delta:** where a river meets the sea or a lake, the river splits into channels with land between, fanning out.
- **Lake:** some water is a lake with its own level (above the sea), a shore and a beach or reeds, not sea.
- Open coast: a natural, ragged coastline and a sea floor that shelves gradually (fixing #176's step).

Do it in the paper first (`tools/world-paper/`): on the 12 x 10 and the nine, and with world sketches (a drawn lake
should become a lake at a lake level; the sketch paper left drawn lakes at sea level). Pick the shore kind from what
the land says (how flat, how low, whether a river arrives, the biome: foggy-marsh likes marsh, say), with a seed, so
the same world always comes out the same. Show the shore kinds as a layer, and before/after pictures of the same
world. Note what each kind would need in the core (ChunkGen, the sea mask, water levels, rivers) and rough days.

Name new ideas in the GLOSSARY before using them (check the existing words for sea, lake, river, shore and water
level first; reuse them).

## Done

The page runs; existing panels still work; 2-4 screenshots under 1280 px (before/after of the 12 x 10, a delta, a
marsh, a lake) committed under `docs/design/shores/`; a "Shores" section in world-squares.md. Push the branch; the
lead lands it.
