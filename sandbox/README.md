# Spell Chemistry Sandbox

A top-down, browser-based **paper prototype** of the spell interactions.
It's the cheapest way to answer the bet underneath every prototype in
`docs/PROTOTYPES.md`: *is the chemistry rich, surprising and readable?*
It does that before any Unity work.

Every reaction it shows comes from `../rules/*.json` (see that folder's
README). The TypeScript here is throwaway; the rule tables are not.

## Run it

```bash
cd sandbox
npm ci
npm run build      # -> dist/index.html, one self-contained file; open it in a browser
npm run watch      # rebuilds on every change to src/ or ../rules/
npm test           # node:test suite (Node 22.18+ runs the .ts directly)
npm run typecheck
```

## What's in it

- **Three rooms:**
  - *The Steam Gate*: a puzzle with nine tested ways through.
  - *Goblin Bridge*: a fight using the environment.
  - *Open Meadow*: free play with Build tools.
- **Kit:**
  - The 8 shared spells, plus Shocking Grasp for lightning.
  - All six critters from `docs/TRAVERSAL.md`.
  - Five machines that open gates: a boiler, a pressure plate, a water
    wheel, a lightning coil and a windmill.
- **Physical layer:** push force against mass, sliding on ice and
  grease, falling into pits, slamming into walls, levitation.
- **Readability tools:**
  - a running log of what happened and why;
  - a hover inspector showing each square's state;
  - the reaction table, rendered from the JSON.

## Layout

| File | Role |
|---|---|
| `src/rules.ts` | Types for the JSON, plus `validateRules` (cross-checks the tables) |
| `src/world.ts` | State, the seeded RNG, small helpers |
| `src/react.ts` | **The rule interpreter**: element reaches square, rules run |
| `src/sim.ts` | One tick: critters, fire, water, gas, entities, machines |
| `src/entities.ts` | Damage, explosions, push and slide, per-tick statuses |
| `src/actions.ts` | Casting, placing critters, painting |
| `src/scenarios.ts` | ASCII maps and goals |
| `src/render.ts`, `src/main.ts`, `index.html` | The page |
| `test/sim.test.ts` | Interaction tests, including every Steam Gate route and determinism |

The simulation is deterministic for a seed: all randomness comes from a
mulberry32 cursor in the world state. That is the same discipline the
C# core needs.

## Deliberate simplifications

- Top-down 2D grid, one entity per square, no characters: you are a
  disembodied caster. Misty Step isn't in, because it needs a body.
- No saving throws or attack rolls. Damage is rolled flat.
- Water is integer units on a two-level height map (ground and pit), so
  a channel fills as a slope rather than levelling out.
- Every powered machine opens every gate (one channel).
- Critters are invulnerable, and you carry one of each.

## What to look for while playing

The scorecard in `docs/PROTOTYPES.md` applies here too:
- unscripted chains (write them down);
- whether a friend can see *why* something happened;
- which rules feel missing or wrong. Fix those in `rules/`, not here.
