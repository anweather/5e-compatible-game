# P5 concept: physical turn-based tactics

A traditional party RPG with turn-based combat, in 3D, where **the world
resolves your intentions physically** instead of rolling them against
a spreadsheet.

You say "attack that goblin". If a stone wall is in the way, you hit
the wall. If you charge, you end up where the charge ended. If you're
strong enough, you pick up the goblin and throw it into its friend.

**What it must *not* become:** Pocket Tactics in 3D (a grid, FFT-style
jobs, HP numbers that grow), or BG3 with more physics (still a
health-sponge fight). Four pillars keep it distinct.

## Pillar 1: Intent, then physical resolution

Every action is an **intent** (target, verb) that the world resolves.

| Intent | Physical resolution |
|---|---|
| Melee attack | The character paths to the target. Anything that blocks the swing arc (wall, ally, a crate shoved in the way) takes the hit instead |
| Ranged attack / bolt | A projectile is simulated along its path. Cover is real geometry, not a "half cover" flag. A miss lands *somewhere* and can ignite or break things there |
| Charge | Move plus attack. You end your turn where the charge ended, not back in formation |
| Shove / Gust / Thunderwave | An impulse. Where the target lands (ledge, pool, spike growth, a friend) is the payoff |
| Throw (creature or object) | Allowed if Strength and size outmatch the target's weight (a 5E lift rule in spirit, BG3 in practice). Damage to both target and impact point comes from impact speed |
| Misty Step / teleport | Blink to a point you can see; must fit the capsule there |

**Positioning persists.** No "return to the party line" like classic
Final Fantasy. Where you end a turn is where you stand when enemies
act. That is the whole tactical layer.

## Pillar 2: Reactions are the skill expression

5E already gives everyone **one reaction per round**. Make it the star.

- **Telegraphed enemy intent** (as in Into the Breach): before acting,
  enemies show what they're about to do. "The ogre will charge Mira",
  drawn as a path.
- **Interrupt windows.** When an enemy's movement or attack triggers a
  reaction, time pauses and you choose. All of these are SRD or close:
  - **Shield** (spell): AC up; the hit glances.
  - **Misty Step**: blink away. In 5E it's a bonus action, so treat it
    as a "readied" reaction variant.
  - **Gust of Wind / Thunderwave**: readied, so the charge stops short
    or veers off a ledge.
  - **Opportunity attack**: a swing when someone leaves your reach.
  - **Brace / interpose**: a fighter steps into the path.
- **Readying** (5E's Ready action): spend your turn to set a trigger
  ("if anything crosses this doorway, Thunderwave"). That's an ambush
  you set up.
- **Budget:** one reaction per character per round. Party-wide combos
  come from *sequencing* who reacts.

## Pillar 3: No health sponges

Fights are short and lethal on both sides. Skill, not numbers, decides
them.

- **Flat HP.** Health barely grows with level. A fighter might go from
  12 to 20 over a whole campaign. Levels add *options* (spells,
  reactions, throws), not padding. This extends 5E's own "bounded
  accuracy" idea from to-hit to HP.
- **The environment does the big numbers.** Falls, being thrown into a
  wall, being crushed, drowning and fire do the same damage to a
  level-1 goblin and a level-10 knight. So positioning kills, and
  enemies can use it against you.
- **Two-layer damage** (optional, worth trying): this is a well-known
  d20 variant [mem], also used in other d20 games.
  - *Guard/Stamina* is spent dodging and blocking, and refills between
    fights.
  - *Wounds* are real and few. Crits, falls and backstabs go straight
    to wounds.
- **Conditions do the work HP does elsewhere:** prone, restrained,
  disarmed, off-balance, wet, burning, frozen in place.
- **Armour reduces damage** instead of only making you harder to hit.
  It's heavier, so it is easier to shove you *less*, but harder to throw
  *others* without strength. Weight is part of the build.
- **Enemies obey the same rules.** A bandit captain dies to a 30 ft fall
  just like you do. Big monsters are dangerous because of *size and
  weight* (they can't be thrown; they throw you), not HP.

## Pillar 4: Preview equals outcome

Physics is chaotic, and a turn-based player needs to plan. The Unity
answer is a **sandbox physics scene**:

1. When you aim an action, clone the relevant bodies into a separate
   `PhysicsScene`. Unity supports this:
   `CreateSceneParameters(LocalPhysicsMode.Physics3D)`, then
   `PhysicsScene.Simulate(dt)`, the standard trajectory-prediction
   technique.
2. Simulate the action ahead, record every body's trajectory, and draw
   the preview: arc, landing spot, who gets hit.
3. **On commit, replay the recording** instead of re-simulating. What
   you saw is exactly what happens, even though PhysX isn't
   bit-for-bit repeatable.
4. Randomness (the d20) is rolled *before* the preview and shown
   ("78% to hit"), or the action is deterministic and the uncertainty
   lives in enemy reactions. Pick one per action type. Don't hide dice
   inside the physics.

This also covers D4 if the game ever goes multiplayer or async on the
platform. The acting client's recording *is* the move; the server
checks plausibility; nobody re-simulates.

## Turn structure

Start with **5E initiative** (individual turns, party interleaved with
enemies), plus reactions and telegraphed enemy intents. Variants to try
only if that feels slow:
- **Side turns** (XCOM): the whole party acts, then all enemies.
- **Plan-then-resolve** (Frozen Synapse): both sides commit, then
  physics plays out. Chaotic and funny, and probably a different game.

## Prototype scope (1–2 weeks of evenings, after P2)

It reuses P2's 3D greybox arena and props. P2's third-person controller
isn't needed; this uses a tactics camera (orbit, zoom, click-to-move on
a NavMesh).

- **Party:** Fighter (shove, throw, interpose), Wizard (Fire Bolt,
  Thunderwave, Shield, Misty Step), Rogue (move-then-strike,
  opportunity attacks).
- **Enemies:** 3 goblins and 1 ogre (the ogre can't be thrown and
  throws you).
- **Arena:** a low wall, a ledge with a 20 ft drop, a pool, an oil
  barrel, a wooden bridge, loose crates.
- **Build:**
  - NavMesh movement with a movement budget;
  - a melee swing-arc check;
  - projectile simulation;
  - sandbox-scene preview, then replay;
  - one reaction per round, with an interrupt prompt;
  - telegraphed enemy intents;
  - flat HP plus fall/collision damage;
  - the shared chemistry table (D3).
- **Done when:** you win a fight you would have lost to raw numbers by
  using position, a reaction and a throw. And the preview never lied.

## Questions this prototype answers

1. Does physical resolution feel like *strategy*, or like randomness?
   (Watch for "I didn't expect that" moments that feel unfair, as
   opposed to delightful.)
2. Do reactions and telegraphs make enemy turns interesting to *watch*?
3. Is flat HP plus environmental lethality fun, or too punishing?
   (Tune with stamina/wounds.)
4. Does it feel different enough from Pocket Tactics and BG3 to be
   worth making?
