# Brief: the cast-feel pass (host, on the PC, right after UX pass 4)

Andrew, 2026-10-30: casting is the core of the game, and it "feels super floaty, no weight to it".
The staff gets in the way of two-handed element casting, so it goes. Then casting gets weight
through anticipation, impact and recoil. Approved as laid out, with punchy shake and rumble behind
a slider.

Pull `dev`. Read COORDINATION.md (lean), docs/SPELL_COMBOS.md, and the casting code (SpellCaster
and its Combos part, CastPose, the cast meter, charged casts from #7 and bd488d5). Run it as short
fresh sub-agents per part, and merge after each part. The sound pass (docs/handoffs/sound-pass-brief.md)
comes after this; this pass builds the casting sounds itself.

## 1. The staff goes
- Remove the staff from the default wizard: the model, its glow and the hand that held it. Both
  hands are free for elements.
- Keep the code where it's cheap to keep, so a staff can come back later as an item (a focus).
  Mark it unused rather than deleting it.

## 2. Hands by IK, through six beats
Two-bone IK on both arms (Unity Animation Rigging), driven by the cast state: poses with targets
and timing curves, no new animation clips. All poses and timings go in one tuning asset, so they
can be adjusted by feel.
1. **First element:** that hand swings down and back to the side, palm open, and the element
   gathers there (dust or sparks drawn in).
2. **Second element:** the other hand does the same, so the wizard stands braced, arms wide.
3. **Slam:** both hands snap together in front of the chest, fast (about 0.1 s), with no ease-in.
4. **Hold to charge (#7):** hands pressed together, knees bent, leaning in, feet planted, movement
   slower. Energy grows between the hands, with a pulse at each charge level.
5. **Release:** hands thrust toward the aim, and the body recoils back about half a step.
6. **Recovery:** back to neutral quickly, so the next cast can begin.

A single-element cast skips beat 2. Cancel (B) drops the hands with a fizzle. Other players see the
same beats online: drive them from the synced cast state, not from local input.

## 3. Impact
- **Hit-stop** on the slam and the release: a view-only freeze of 2-3 frames (animation and camera
  only), never game time, which has to stay identical for every player.
- **Camera:** a Cinemachine impulse on the slam (small) and the release (in the cast's direction,
  scaled by spell size), a slight tightening while charging, and a shake that builds with charge.
- **Rumble on the pad:** gathering ticks, a slam thump, charge-level ticks, a release kick, and
  impacts scaled by distance and size.
- **Impact feedback at the target:** shake and rumble scaled by distance, so a big spell you feel
  lands harder than one you watch.
- **An Esc-menu slider for shake and rumble** (punchy by default, down to off).

## 4. The casting sounds
- Each element gathering.
- The slam's thump.
- The charge building, rising with each level.
- The release, per kind of cast.

Take them from the CC0 packs (credit them), and use the same sound plumbing the sound pass will
build on, so nothing is done twice.

## Checks
- A `castfeel` probe: each beat reached in order, hands at their targets (within a tolerance), the
  slam's hit-stop doesn't touch the game's tick, shake and rumble fire and scale, the slider at off
  stops them, no console errors.
- EditMode passes, no new warnings.
- Look at online: another wizard's cast shows the same beats.

## Deliver
A before and after clip of a few casts (single, pair, charged, cancelled), with sound, for Andrew's
verdict. Merged per part. Report to the PM in 8 lines or fewer after each part, with the clip path.
