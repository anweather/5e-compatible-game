Needs: any
Area: tools/world-paper/, docs/GLOSSARY.md (one new entry), docs/design/world-squares.md (a new section only)
Branch: world-sketch-paper

# Draw the world first, then fit the world squares to it (paper)

Andrew, 2026-11-09: today the plan picks a square template per world square and the land follows the grid. He
wants to turn that round in the paper prototype (`tools/world-paper/`):

1. **Draw biome shapes freely on the world**, independent of the grid: a lake as an organic blob, a mountain range as
   a long triangle, a forest, a desert, the ice cap. Shapes overlap and blend at their edges with the blending we
   already use (the BiomeField warp and blend; the paper's own edge blending), so edges are natural, not square.
2. **Map the drawing onto the world squares.** Each world square reads what the sketch puts under it (say half
   mountain, half forest) and gets the band and square template that fits best, keeping the side-matching rules
   the plan already has (touching world squares differ by at most one band, features that cross a side match on
   both sides, coasts agree). Where the drawing breaks a rule, show which world square and which rule, and what the
   plan did instead.
3. **Show both:** the drawn shapes, the world squares the plan picked from them, and the land that comes out, side by
   side or as layers you can switch. Works on the 3 x 3 nine and on the 12 x 10 world (J-083: 12 wide, 8 land rows
   plus ice north and desert south, wrapping east-west), with the sketch wrapping too.

Keep it paper: no Chemistry/ or Unity changes. A drawing should save and load as a small JSON (it may later become a
map.json field), and a few sample sketches ship with it (a lake district, a mountain spine, the default 12 x 10).

Name the idea before using it: add one GLOSSARY entry (a **world sketch** is the suggestion: the drawn biome shapes
a plan is fitted to; "biome" keeps its meaning from `world/biomes.json`). Add a short section to
`docs/design/world-squares.md` on how a sketch maps to world squares and what it would take in the core.

## Done

The page runs from a local file or `npx serve`, the existing panels still work, and the report has 2-3 small
screenshots (under 1280 px wide) of a sketch, its world squares and its land. Push the branch; the lead lands it.
