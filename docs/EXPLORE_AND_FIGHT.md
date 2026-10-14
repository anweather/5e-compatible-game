# P6 concept: third-person adventure with turn-based fights

A **3D third-person action-adventure** in which you explore with real,
skilful movement (jump, climb, glide, blink, ride the updraft you just
cast) and **combat drops into physical turn-based tactics** (P5) right
where you stand. Divinity and BG3 do the two-mode split, but their
exploration is click-to-move from above, and movement there is
logistics, not play. Here, **exploration is the action half of the
game.**

It combines P2 (movement) with P5 (combat), and uses the traversal verbs
and critters from `TRAVERSAL.md` in both modes.

## Prior art: who already does parts of this [mem]

| Game | What it shows |
|---|---|
| **Divinity: Original Sin 2 / BG3** | Seamless explore→combat in the same space; surfaces and elements; positions carry into the fight. Exploration movement is shallow |
| **Mario + Rabbids: Sparks of Hope** | 3D field exploration, then turn-based fights where movement *within* a turn is free and physical (dashes, team-jumps off allies). Proof that turn-based movement can feel like action |
| **Clair Obscur: Expedition 33** (2025) | Turn-based, but **enemy turns ask for real-time dodge or parry inputs**. Skill expression on defence, exactly the "react when someone charges you" idea |
| **Metaphor: ReFantazio / Persona** | Real-time field action decides *how* the fight starts (first strike, ambush) |
| **Sea of Stars** | Climbing, swimming and ledge traversal in the field; fights happen in place; timed hits |
| **Zelda BotW / TotK** | The field-movement and chemistry bar to aim at |

The gap: **nobody pairs BotW-grade field movement and chemistry with
BG3-grade physical turn-based combat.** That's the pitch.

## The loop

```
 EXPLORE (real-time, 3rd person)                 FIGHT (turn-based, tactical cam)
 ─ jump / climb / glide / blink                  ─ 5E initiative, flat HP (P5)
 ─ class traversal verbs, critters               ─ intents resolved physically
 ─ chemistry runs in real time                   ─ chemistry ticks once per round
 ─ SCOUT + PREPARE the battlefield  ──engage──▶  ─ positions = where you stood
                                    ◀──resolve── ─ world changes persist
```

## Pillar 1: Exploration movement is skill, not logistics

- **One controlled hero at a time.** The rest of the party follows.
  **Swap leader** to use their verb: Monk runs the wall, Rogue climbs,
  Druid becomes a spider. This is the Trine pattern from
  `TRAVERSAL.md`.
- **Spells and critters are movement tools here:**
  - Gust as an updraft
  - Ray of Frost to freeze a river into a path
  - Levitate as a float
  - the mole raising a stepping pillar
- **The follower problem.** In BG3, followers getting stuck is a famous
  pain point [mem]. Rules:
  - followers path on the NavMesh where they can;
  - where they can't, they **use the leader's traversal output** (the
    Rogue's dropped rope, the Fighter's throw, the frozen bridge);
  - otherwise they **regroup at the next safe point**, out of sight.
  - Never block the player on companions.
- **Feel targets** (from `RESEARCH.md` §8): coyote time, jump buffer,
  variable jump height, ledge-grab assist, and a camera that frames
  vertical routes.

## Pillar 2: How a fight starts is decided in exploration

The seam between modes is the most interesting design space.

- **Positions carry over:** whoever stands where at "engage" is the
  opening formation (DOS/BG3). Scouting the high ground is real
  tactics.
- **Prepare the battlefield while still in real time:**
  - grease the bridge the patrol crosses;
  - park the ember newt next to the oil barrels;
  - raise a mole pillar for your archer;
  - flood the courtyard so the eel can electrify it on round 1.
- **Engagement types:**

  | Engagement | Trigger | Effect |
  |---|---|---|
  | Ambush | You hit an unaware enemy in real time (a thrown rock, Fire Bolt, a drop attack from a ledge) | Free surprise round, as in 5E's Surprise |
  | Even | Mutual sight | Normal initiative |
  | Ambushed | They saw you first | They get the surprise round |

- **Optional fast-resolve:** trivial fights (overwhelming odds) resolve
  in real time with a single strike, as in Metaphor. This keeps
  traversal flowing, so not every rat becomes a turn-based fight.

## Pillar 3: Combat is P5, with movement kept

Everything in `PHYSICAL_TACTICS.md` (intent→physical resolution, flat
HP, preview equals outcome), plus:

- **Traversal verbs work inside turns.** Climb, jump a gap or blink to
  a ledge, all paid from the movement budget. A jump is still a real,
  physical jump (Mario + Rabbids proves this reads well).
- **Reactions, in two flavours to prototype side by side:**
  - **(a) Menu reactions:** pause, then choose Shield, readied Gust,
    interpose. Strategic.
  - **(b) Timed reactions (Expedition 33):** during an enemy attack, a
    timed input dodges or parries. Shield or Misty Step trigger on a
    well-timed press and still spend your 5E reaction.

  (b) carries the action-game skill into turn-based play, and that
  suits this paradigm. (a) fits P5 alone.
- **Chemistry on round time.** Fire spreads, water flows and ice melts
  **once per round** in combat, and continuously in exploration (the
  DOS surfaces model). Same rules, two clocks: the README's two-clock
  core, used *within* one game.

## Unity build notes (learning payoff)

| Need | Unity pieces |
|---|---|
| Explore controller | `CharacterController` or a kinematic controller, Input System, Cinemachine third-person camera |
| Fight controller | NavMesh plus a movement-budget path preview; tactical Cinemachine camera; **a camera blend** between the two (Cinemachine handles this) |
| Mode switch | A `GameMode` state machine. Pause the real-time systems (AI, chemistry ticker) rather than using `Time.timeScale = 0`, so animations and VFX keep running |
| Followers | `NavMeshAgent` plus off-mesh links for jumps and ropes; regroup teleport out of camera view |
| Preview | The sandbox `PhysicsScene` from P5 |
| Timed reactions | Animation events open a parry window; a hit-stop on success |

## Prototype scope

It's cheap once P2 and P5 exist: the new work is the seam.

- **Space:** one valley about 150 m across. A river, a cliff with two
  routes up (climb or pillar plus jump), a ruined bridge, a goblin camp
  with oil barrels, and a watchtower.
- **Party:** Monk (wall-run), Rogue (climb and rope), Wizard (blink,
  levitate, Gust). Critters: ember newt and burrow mole.
- **Content:**
  - one approach to the camp that is physically different per party
    leader;
  - battlefield prep in real time;
  - three engagement types;
  - one fight with each reaction flavour (a, b).
- **Done when:** you take the camp twice, by two different
  routes-and-preps, and the second fight plays out completely
  differently because of what you set up in exploration.

## Questions it answers

1. Does good movement in exploration make the world worth exploring in
   an RPG, or does it feel like a different game bolted on?
2. Is "prepare the battlefield" the hook: the moment the two modes
   multiply each other instead of just alternating?
3. Menu reactions or timed reactions: which feels better in 3D?
4. Does the explore→fight transition feel seamless (camera, pacing),
   or jarring?
5. Followers: is the regroup rule invisible enough?

## Risks

| Risk | Mitigation |
|---|---|
| Two games' worth of work | It reuses P2 and P5 almost entirely; the slice is one valley, one camp |
| Movement too strong breaks combat (blink everywhere) | Movement verbs cost the same budget in combat; a few verbs are explore-only |
| Followers getting stuck | Regroup rule; traversal outputs (rope, bridge) that serve the whole party |
| Constant fights interrupt the flow | Fast-resolve trivial fights; fewer, more meaningful encounters |
