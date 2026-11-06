Brief for the PC: the Hold, phase 3, how hands look. Read `docs/design/the-hold.md` ("Showing it"). Branch `hand-looks` off dev. View only: no core change, no rules change unless a look needs a row.

**Goal:** you can see what each wizard holds and what each arm is doing, without the HUD.

**Build:**
- **Each hand shows its element at all times:** fire in the hand, water round it, stones orbiting it, air curling off it; nothing for an empty hand. On your own wizard and on friends', on all peers, from the hands core state. Cheap: GPU first, and check it holds frame time with a full party.
- **Three gesture poses:** thrust, throw and sweep, per arm. `CastPose` has a start on these.
- **Both at once reads as both:** different gestures play on their own arms, so one arm can thrust while the other throws. Same gesture on both plays the two-hand version. This replaces the one symmetrical pose the hands run found.
- **Say what you find on timing:** press to act is about 0.67 s for a punch and 0.95 s for a lone spell. Don't change it; Andrew hasn't decided it's too slow. If the poses make it look slower or faster, say so.

**Keep:** the sandbox's look doesn't change. No hand effect outside the Hold unless it's already a Hold loadout. Words as in the glossary. `hold` and `hands` probes stay green; add a probe that each hand shows the right element on another peer's wizard.

**Report:** one short message: the commit, gates with exit codes, frame time before and after, and shots at 800 px: each element in a hand, the three poses, and a mixed pair (thrust plus throw). A short clip of a mixed pair if you can. Don't merge it.
