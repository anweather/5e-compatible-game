# Brief: host UX pass 3 (Gameplay, on the PC with the Unity Editor and GPU)

For alpha-0.1.8 (Andrew, 2026-10-30: Option C, a looks pass, then the release
tonight). Pull `dev` (b33c6bd or later). Read COORDINATION.md (GPU-first
effects, lean budget) and docs/SPELL_COMBOS.md. Same way of working as UX
pass 2: GPU-first (shaders, tints, decals, the pooled effects library), a
probe frames each item, before/after shots.

**Timebox: about 1.5 hours.** Part 1 must ship; part 2 is polish to squeeze
in, in order, as time allows. Stop at the timebox and say what's left.

## 1. The new casts' looks (rules landed today in VMs; nobody has seen them)
- **Geyser:** the jet out of a pond or river, and what it throws.
- **Wave:** the rolling front, what it carries, the mud it leaves.
- **Frost:** the ice bridge, frozen raiders, the slippery frost cover.
- **Rolling Boulder:** the boulder rolling (built from simple pieces), its
  dust trail, where it comes to rest.
- **Plant Growth:** vines over walls and stone (climbable), and the rubble
  when roots break a wall.
- **Wall of Stone:** stepping stones over water, and the ramp.
- **Lightning:** the jump to water or metal and the charge running through
  it to a gate; the storm cloud striking; the snow cloud.
- **Sand Blast's scour:** rust back to metal, worn log walls.
- **Fireball's debris, Lava Pool running and crusting, Steam rusting.**
- The `spelllooks` probe grows to cover these.

## 2. Polish (Andrew: squeeze in what fits)
1. **Village windows:** the cracked stage on the village and cabin panes
   (the spiderweb) reads well before they shatter; check in the village.
2. **Ice:** the crack web on bulging ice (it breaks up on a mound); check
   flat ice too.
3. **Frozen raiders:** cracks as a web, not split colours.
4. The `looks` probe's crate at 8 m/s that sometimes misses the pane
   threshold: make the throw land consistently.

## Deliver
Merge to dev when the probes pass, with no new warnings. One before/after
shot pair per item into unity/Builds/screens/<item>/. One short clip of a
chain for the reel (for example lightning through a puddle to a gate, or a
boulder through a window). Report to the PM in 10 lines or fewer, with
what's left of part 2. Then close the Editor: the release builds next.
