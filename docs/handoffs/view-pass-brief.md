# Brief: the view pass for the new materials (from the PM, Max account)

You are a worker VM on the Max account. Pull `dev` first. Read COORDINATION.md
(the rules, especially the GPU-first entry of 2026-10-29) and
docs/MATERIALS.md. The features below exist in the rules and core already;
your job is to make them VISIBLE and FELT in Unity.

**GPU-first (Andrew's rule):** use shaders, vertex colours, cell/terrain
tints and the existing pooled particle effects. Don't spawn many entities or
GameObjects. If an object is truly needed, build it procedurally from small
simple pieces, like the world builders do.

**Lend:** you may edit Gameplay's view files these need (CellDressing.cs, the
effects pool, shaders, the wizard's movement and damage, the radial's view)
and sandbox/render.ts. List every file you touched in your report.

**Checks before you push:** Unity batch compile clean with no new warnings,
EditMode tests pass, core and sandbox tests pass. No screenshots (no GPU
here); the host does one shot pass later.

**Deliver:** one branch, pushed, NOT merged (the PM merges). Report to the PM
with SendMessage in 5 lines or fewer: the branch, the commit, what each item
does, the files touched, time and RAM. Then stop.

## w3: branch `view-ground`
1. Rust: rusted-metal colour (CellDressing.cs; sandbox render.ts too).
2. Sand and glass colours. Glow balls break when thrown or hit hard (glass
   is brittle: add "breaks" in the rules and a pooled shard effect).
3. Mud: a mud look (darker, wet, a little shiny), and the wizard really
   slows in it in Unity (use the core's move cost).
4. Hot metal: a heat glow by heat (emissive or tint), and the wizard takes
   burn damage in Unity when touching hot metal.

## w4: branch `view-effects`
1. First, the rule: shock-water. Lightning into water spreads through the
   connected water (conduct.chainsWet) and shocks swimmers and wet creatures.
   Both engines, a shared golden. (It was started on another worker and not
   finished; start fresh.)
2. The shock's look: a brief flash over the connected water (a shader term
   or pooled sparks).
3. Oil on water: a visible slick (a tint or shader term on the water
   surface); fire across it uses the existing fire effect.
4. Shatter: frozen things that shatter burst into the pooled shard effect
   with a sound, instead of the normal defeat.
5. Acid Splash: castable now as a temporary 4th slot in the nature group
   (the radial will be replaced by element combos later; see
   docs/SPELL_COMBOS.md), with an icon, a pooled green splash, a hiss and an
   "eaten" tint on dissolved cells.
6. Check that damp burning and creature materials need nothing new; say so.
