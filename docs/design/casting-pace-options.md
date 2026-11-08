# Casting pace and big spells (playtest 2026-11-08, C1 and C2)

For Andrew. Controller first. Read against dev at `78e97241`. Nothing here is built.

## How casting works today

- **Ticks.** The world runs 6 ticks a second (`rules/sim.json` `ticksPerSecond`). The numbers below are ticks.
- **The hands** (D16, `docs/design/unified-casting.md`, core in `World.Hands.cs`; table in `rules/hold.json` `hands`):
  - Each hand holds an element. The trigger is the thrust, the bumper is the throw, both is the sweep.
  - Both hands on the same gesture merge into a pair's spell.
- **A tap is not instant.** The hand's meter runs `castTicks` before the spell goes off:
  - 6 for a single (1 s);
  - 9-12 for most pairs;
  - 18 for Meteor (3 s).
- **Holding** adds `chargeTicks` 3 a level, up to 3 levels (`rules/combos.json`). Charge only makes a spell stronger.
- **Cooldowns** start when the spell goes off (`hands.handCooldown` 12 = 2 s; `bothCooldown` 30 = 5 s).
  - So a tapped Fire Bolt comes out about once every 3 s per hand.
  - A full Meteor costs 4 s of charging, then 5 s of both hands locked.
  - The upgrades only trim this (`quick-hands` -15%, `calm-hands` -25%, `earth-focus` -30%).
- **The mana pool** is a view-only bar: `Vitals.Magic` (`Scripts/Vitals.cs`), a float out of 100.
  - Nothing spends it ("magic doesn't drain yet").
  - There's no mana in `rules/` or the core.
- **Merges** are in `rules/hold.json` `hands.spells`. Two pair slots are free since the cut:
  - air+air throw (Dust Storm went);
  - earth+air thrust (Sand Blast went).
  - Sweep pairs are unbuilt.

The waiting he feels is built in. Every cast has a wind-up, even a tap, and then a cooldown, and the pairs he reaches for
lock both hands.

## The ask, in his words

"The casting of the charges feel good", but a run is mostly charging or waiting. He wants to "just throw stuff around
everywhere" as well as charge big spells.

## Option A: tap is quick, hold is the charge (timing only)

- **On the pad:** a tap fires on the next tick or two, with no wind-up. Keeping the button down past about 0.3 s starts
  the charge, with rumble per level as now. Cooldowns drop to about 0.5 s for a tapped single. The long `castTicks` stays
  only on charged and merged casts.
- **Rules:** add `tapTicks` (2) and `tapCooldown` (3) to `hands`, and lower `handCooldown`.
  - A rules-only trial is possible tonight through the rules MCP: set every single's `castTicks` to 2 and `handCooldown`
    to 3. It's crude, but it shows the feel.
- **Code:** `World.Hands.cs` gives a tap its own meter. `CastFeel` and the icons need a short tap beat.
- **Lockstep:** none new. Integer ticks, in the hands state that's already saved and hashed. The hand goldens move.
- **Size:** S.
- **Risk:**
  - Nothing limits spam except the cooldown, so balance moves to damage numbers.
  - At 6 ticks a second, the fastest a hand can fire is about 3 casts a second.
  - Many casts at once cost chemistry time: watch the fire and water passes.

## Option B: cooldowns out, mana in

- **On the pad:** no cooldowns at all. Every cast costs mana, from one pool that refills over a few seconds.
  - Taps are cheap: about 20 casts on a full pool.
  - A charged cast costs more for each level.
  - A Meteor-tier cast costs about half the pool.
  - The pool is drawn as a ring round the hand icons, not as text (M2).
  - An empty pool buzzes and the icon greys.
- **Rules:** `hands.mana` (`max`, `regen` a tick, `regenDelay`). Each row gets a `cost` and `costPerLevel`. The
  upgrades become cost or regen percents.
- **Code:**
  - The core: an integer `Mana` per player in `PlayerHands`, spent on release and refused when short.
  - The view: `Vitals.Magic` reads it from the core and stops being its own float.
- **Lockstep:** the pool must live in the core. A view-side pool would let one peer refuse a cast the others ran.
  Integer, saved with the hands; the hand goldens move.
- **Size:** M.
- **Risk:**
  - Spam then a dry spell: a run turns into "wait for mana", which is the same wait in a new shape.
  - It needs a tuning pass per spell.

## Option C: free taps, mana pays for the big ones (A plus B)

- **On the pad:** taps are free and spammable, as in A, with a tiny cooldown that only stops one button machine-gunning.
  - Charging and merging spend mana as you hold: the ring drains while it grows, so you see the cost.
  - A big spell that can't be paid stops charging at the level you can afford, and still fires.
  - Meteor-tier spells keep one long cooldown of their own.
- **Rules:** A's `tapTicks` and `tapCooldown`, plus B's `mana` with a `costPerLevel` and a `cost` for pairs only.
- **Code:** A's and B's core changes (about one file each), plus one HUD ring.
- **Lockstep:** as B.
- **Size:** M (A first, then the pool, each landing alone).
- **Risk:**
  - Free taps make the singles' damage the balance lever, as in A.
  - The pool needs a clear look on screen, or it's invisible.

**Recommendation: C, built as A then the pool.** A tests the spam half in a rules-only evening. If spamming feels right
but big spells get cast too often, the pool goes in. This gives him both modes he asked for: the triggers for throwing
things around, holding for the big ones.

**Decision for Andrew:** A only, B, or C (recommended)? And should a tapped single have any cooldown at all?

## A Meteor for every element (C2)

The pattern: **each element's same-element throw is its wow spell.** Fire+fire throw is Meteor already. Each one would
cost what Meteor does, in mana or cooldown, so Meteor becomes one of four rather than weaker.

| Hands (group) | Wow spell | What it does, and the chemistry it uses |
|---|---|---|
| fire+fire throw (fire) | **Meteor** (kept) | As now |
| air+air throw (storm), the free slot | **Tornado** | Whirlwind grown huge and lasting. Its `carries` rows already exist: over the lake it becomes a waterspout, over burning ground a fire tornado. It lifts raiders and flings them (`LiftInTheEye`) |
| fire+air thrust (storm), replacing Falling Star (a second Meteor) | **Thunder Strike** | A column from the sky on the target: `lightning` plus `thunder`. It turns a rain cloud into a storm (`lightning-cloud`), arcs through water and metal (`lightning-conduct`), fuses sand to glass (`lightning-fuse`), shatters ice (`lightning-ice`), and the clap clears gas (`thunder-gas`) |
| earth+earth throw (earth), replacing Stone Ramp | **Earth Spires** | A jagged line of pillars bursting toward the target (`earth-raise` makes `pillar`). It throws raiders up, and leaves cover raiders must batter (`pillar` strength 6). An earth spell breaks them to rubble (`earth-break`). A spire's slope can serve as the ramp |

Water and ice are open (group "Water & Ice"; elements `water` and `cold`). Three ideas besides hail, rain and a
snowball:

1. **Frozen Tide** (water+water throw, beside Wave). A wall of water rises, rushes forward and freezes mid-crash into
   ridges of walkable ice, freezing the raiders it catches.
   - Chemistry: `cold-water` freeze, `e-fire-frozen`.
   - Fire melts it back into a flood (`fire-thaw`: "its meltwater rushes out").
2. **Flash Freeze.** A shock of cold spreading in rings from a point through everything wet. The lake goes walkable, steam
   condenses and freezes, clouds turn to snow clouds, and Soaked raiders freeze solid.
   - Chemistry: `cold-water`, `cold-steam`, `cold-cloud`.
   - It pairs with a friend's Flood or Rain Cloud, and with Lightning, which shatters what's frozen.
3. **Maelstrom.** A whirlpool in the target's water, or in a pool it makes first (`create-water`), that drags raiders in
   and under. It reuses Whirlwind's `inward` pull on water cells. Lightning into it arcs through everything caught.

**Recommendation: Frozen Tide.** It's the most visible, it reuses Wave's travel, and fire undoes it in a flood. Flash
Freeze is the best two-player combo, so keep it for a sweep pair later.

**Decision for Andrew:**

- Do the wow spells take the same-element throws, so Stone Ramp and Falling Star go? (Recommended.) Or should they wait
  for the unbuilt sweep-pair tier, all four buttons, which is hard to hold on a pad?
- Which ice idea?

Each spell is a `spells.json` row plus a `hold.json` hands row, through the rules MCP. Tornado and Spires need new
behaviour in the core, with C# goldens. Tornado is mostly Whirlwind's existing fields turned up. Sizes:

- Tornado: S.
- Thunder Strike: M.
- Spires: M.
- Frozen Tide: M.
