Needs: unity (Punchy's Editor)
Area: `C:/Users/Andrew/Development/Punchy-chemistry` (Assets/Game/Scripts/ChemistryBridge/, and Punchy's own effect assets used from there; no edits to Punchy's systems outside the bridge); in our repo a "Spells in Punchy" note in docs/design/core-package/open-world.md
Branch: Punchy `chemistry-spells` off `chemistry-fill-jobs` (5e00ecce); our notes on `punchy-spells-notes` off `origin/dev`

# The cells-only spells in Punchy, with Punchy's effects (Andrew 2026-11-13)

Punchy casts six spells today: Fireball, Fire Bolt, Frost, Sleet Storm, Create Water and Wave (keys and the pad's
hands). The core runs every spell's chemistry in an open world already (`EverySpellCastsInAnOpenWorld`). This job
brings over **every spell whose effect is only on cells**, and draws what they do **with Punchy's own effects**:
its particle systems, shaders, materials and weather visuals. Andrew: "make sure we use Punchy effects". Write new
effects only where Punchy has nothing close, in Punchy's style.

Read first:
- Punchy's bridge: `ChemistryBridge*.cs`, `ChemistryEffects.cs`, its README (how casts, hands and the view work);
- Punchy's effects: find what it has (fire, smoke, dust, rain, splash, magic, lightning in `Weather/` and
  `Net/World/WorldSync.cs`, particles in Prefabs and Materials) and list them in the notes;
- our rules for each spell (`rules/spells.json`, `reactions.json`, `elements.json`) and how our game draws it
  (`unity/Assets/Prototype/Scripts/`, read-only), only to know what each should look like;
- `docs/design/core-package/open-world.md` ("Punchy as built"); `GLOSSARY.md` (groups, elements).

## The spells

| Group | Spells |
|---|---|
| fire | Burning Hands, Meteor, Flame Wave, Falling Star, Scalding Steam, Lava Pool |
| water | Ray of Frost, Flood, Geyser, Iceberg |
| storm | Lightning Bolt, Thunder Strike, Shocking Grasp, Rain Cloud, Dust Storm, Sand Blast |
| earth | Mud |
| nature | Acid Splash, Plant Growth, Spike Growth |
| arcane | Grease |
| time | Fog Cloud |

If one of these turns out to need bodies, buildings or other things the open world doesn't run (it runs chemistry,
casts, hands and strikes), leave it out and say why. Not in this job: ground shaping, pushes, Misty Step, Shapeshift,
Hasten Time, Control Weather, Light, and Punchy's weather lightning (backlog #222).

## The job

1. **Casting:** every spell above on the pad's element radial (groups as the top ring, as our game's D16 hands) and
   on keys for testing; casts stay session requests.
2. **What each does to the cells, drawn with Punchy's effects:** lava, mud, ice, fog, acid, grease, growth, thorns,
   lightning arcs through water, rain, dust and sand, steam. Include what the reactions make: lava on water turning to
   stone and steam, lightning through a wet or flooded area, grease catching fire, rain putting out a fire.
3. **The look stays the same on every peer** from the cells: effects are drawn from what the cells are, not from local
   random choices that would make two screens differ in what matters.
4. **Cost:** the effects stay within Punchy's frame. Measure a big scene (a Meteor into a flooded, greased meadow in
   a fog) before and after.

Work group by group (fire, water, storm, then the rest) and commit after each, so a cut-off job keeps what's done.

## Must not

- Touch our game's `unity/` or the core package; merge or push our `dev`; change rules.
- Change Punchy's systems outside the bridge (use its effects; don't rewrite them).
- Leave Punchy's uncommitted ProjectSettings/URP/.vsconfig changes as they are.

## Done when

- Punchy compiles with 0 errors and 0 new warnings; its play-mode tests as before.
- Each spell above casts in solo and shows its effect; a `-chem-script` run casts them all, with no exceptions.
- The reactions in item 2 are seen.
- Two peers on the PC cast a mix: `desync=False` for 3 minutes.
- The big scene's frame time, before and after.
- **A demo clip** (≤30 s, 720p MP4 plus a poster still) of the best reactions, in
  `C:/Users/Andrew/Development/5e-compatible-game/unity/Builds/demo-videos/` (the `demo-clip-per-feature` rule).
- A "Spells in Punchy" note in `open-world.md` (spell, effect used, Punchy asset or new), on `punchy-spells-notes`.
- Push `chemistry-spells` to Skeleman/Punchy (J-092) once these pass.

## Report

Five lines: the Punchy commit (pushed?) and ours; spells done / left out (and why); the effects reused vs new; the
two-peer result and frame time; the clip's path and any blocker.

After: the lead lands `punchy-spells-notes`; then Andrew picks the next tier (ground shaping) or lightning storms (#222).
