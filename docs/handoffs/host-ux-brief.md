# Brief: host UX pass (Gameplay, on the PC with the Unity Editor and GPU)

You are the Gameplay session on the host, the only place with a GPU. Pull
`dev`. Read COORDINATION.md (GPU-first effects; the host Editor only when the
GPU is needed; lean budget) and docs/SPELL_COMBOS.md.

## Part 1: look at tonight's effects, and fix what's wrong
They were written in VMs with no GPU, so nobody has seen them. For each, run
or write a probe that frames it, take one before/after shot pair into
unity/Builds/screens/<feature>/, and fix what looks wrong (GPU-first: shader,
tint and pool tweaks, not new entities):
rust; sand; glass and glow balls shattering; mud (and the wizard slowing);
hot metal's glow (and the wizard's burn); the oil slick and fire across it;
the shock flash over water; shatter shards; Acid Splash (cast, splash, hiss,
stain); glass windows once that branch lands.
Keep screenshots to one pair per feature (lean budget). Merge to dev when
the probes pass, with no new warnings. Report in 10 lines or fewer, with the
shot paths.

## Part 2: the combo spell UI (once `combo-core` is on dev)
From docs/SPELL_COMBOS.md: the four element inputs in sequence on the
bumpers and triggers; an element orb in each hand and the slam on cast; the
centre bar showing the loaded orbs (coloured circle with the element's icon)
and the draining cast meter; walking at the cast's movement factor; B
cancels; the d-pad cross (bottom left: left/right arrows, the selected spell
on top, a book icon below; up casts, down opens the grimoire); and a simple
grimoire screen listing discovered combos. Keep the radial as a debug tool.
One short clip of a cast for the reel. Report, then stop.
