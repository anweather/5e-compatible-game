# Brief: host UX pass 2 (Gameplay, on the PC with the Unity Editor and GPU)

You are the Gameplay session on the host, the only place with a GPU. Pull
`dev`. Read COORDINATION.md (GPU-first effects; the host Editor only when the
GPU is needed; lean budget), docs/SPELL_COMBOS.md (the one-verb table, J-039)
and docs/MATERIALS.md.

Start once `cracked` is on dev (the PM tells you). Everything here was built
in VMs with no GPU, so nobody has seen it. GPU-first throughout: shaders,
tints, decals and the pooled effects library, not new entities.

## 1. The wizard's look (Andrew)
- **No more clipping under the robe:** keep the body rig as it is, but hide
  the parts of the model the robe covers (torso, back, shoulders, upper
  legs). Keep the feet visible.
- **The face:** a black fuzzy sphere with two red vertical lines for eyes.
  Mysterious, simple; a shader, not a detailed model.
- **Hats as options:** the wizard hat, plus a cowl (hood). A simple swap,
  ready for more.

## 2. The new spells' looks (rules landed tonight; frame each with a probe)
- **Forge:** the heat on the target; sand to glass, mud to brick (brick's
  colour is in), metal glowing.
- **Falling Star:** the target mark (its tint is in), the fall, the impact,
  the burning embers and the lava core.
- **Whirlwind:** its pull and its carrying: fire tornado, waterspout,
  sandstorm, smoke column, snow flurry (once `whirl-dust` is on dev).
- **The dust explosion.**
- **Tonight's earlier casts:** Lava Pool, Rain Cloud, Sand Blast, Dust
  Storm's dust, Light's glow (Cell.Lit).
- **The pick:** its swing in Play mode (only tested in the core so far).

## 3. Cracked materials
The spiderweb crack on cracked glass (ground and windows), ice and frozen
creatures, from the `cracked` branch: check it reads clearly, then tune it.

## Deliver
One before/after shot pair per item into unity/Builds/screens/<item>/ (lean
budget). Merge to dev when the probes pass, with no new warnings. One short
clip of a chain (for example dust storm + fire, or Forge on mud) for the
reel. Report in 10 lines or fewer, with the shot paths. Then stop and close
the Editor.
