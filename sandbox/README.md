# Spell Chemistry Sandbox

A top-down, browser-based **paper prototype** of the spell interactions.
It's the cheapest way to answer the bet underneath every prototype in
`docs/PROTOTYPES.md`: *is the chemistry rich, surprising and readable?*
It does that before any Unity work.

Every reaction it shows comes from `../rules/*.json` (see that folder's
README). The TypeScript here is throwaway; the rule tables are not.

## Run it

`sandbox.html` is a committed copy of the build, the same page as the
published artifact. Open it in a browser with no setup. Refresh it with
`cp dist/index.html sandbox.html` after a build you publish.

```bash
cd sandbox
npm ci
npm run build      # -> dist/index.html, one self-contained file; open it in a browser
npm run watch      # rebuilds on every change to src/ or ../rules/
npm test           # node:test suite (Node 22.18+ runs the .ts directly)
npm run typecheck
```

## How to play

- **You're the wizard.** Walk with the arrow keys or WASD, or the Move
  pad under the board.
- **Pick a tool** on the right: a spell, a critter or a Build brush.
- **Click a square to use it:**
  - Ray of Frost and Gust of Wind shoot out of the wizard toward the
    click.
  - Thunderwave goes off around her.
  - Misty Step blinks her to a square she can see, up to 30 ft away.
  - Everything else lands on the square you click.
  - The highlighted squares preview what a spell will hit.
- **Watch the log.** It says what reacted and why.

## Play together and change the rules live

See `docs/LIVE_LAB.md` for the full picture.

- **Play together.** Start a room and share the code. Friends need the
  published page shared with them at Contributor or above.
- **Experiments.** Write a mod (by hand, with Claude, or by pasting the
  brief into any AI; see `docs/MODDING.md`), switch it on for the whole
  room, and vote keep, meh or drop.
- **Locally:** `dist/dev.html` includes a fake platform (`dev/fake-claude.js`),
  so two tabs on one machine can share a room. Serve `dist/` over HTTP;
  browsers don't share rooms between `file://` tabs.

## What's in it

- **Three rooms:**
  - *The Steam Gate*: a puzzle with ten tested ways through, including
    a Misty Step between the gate bars.
  - *Goblin Bridge*: a fight using the environment.
  - *Open Meadow*: free play with Build tools.
- **Kit:**
  - The 8 shared spells, plus Shocking Grasp for lightning.
  - A wizard you walk around. Beams and Gust come out of her, and she
    slides on ice and grease like everything else.
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
| `src/terraform.ts` | Earth shaping: terraform spells raise and dig cells; raised ground sheds its water |
| `src/scenarios.ts` | ASCII maps and goals |
| `src/intents.ts` | Every player action as a small JSON message, applied identically on every page |
| `src/net.ts` | Lockstep sessions: host election, batches, snapshots, checksums |
| `src/room.ts` | Transports: solo, and the artifact `room` + `db` |
| `src/lab.ts` | The Experiments panel and the brief for any LLM |
| `dev/fake-claude.js` | A local stand-in for the platform, for two-tab testing |
| `src/render.ts`, `src/main.ts`, `index.html` | The page |
| `test/sim.test.ts` | Interaction tests, including every Steam Gate route and determinism |
| `test/net.test.ts` | Multiplayer: late join, lost batches, host leaving, mods mid-game; the doc examples validate |
| `test/terraform.test.ts` | Earth shaping, with the `CellHash` golden shared with the C# `TerraformTests.cs` |
| `test/items.test.ts` | `items.json` checks, worded like the C# `Rules.CheckItems.cs` |

The simulation is deterministic for a seed: all randomness comes from a
mulberry32 cursor in the world state. That is the same discipline the
C# core needs.

## Deliberate simplifications

- Top-down 2D grid, one entity per square. The wizard moves a square
  at a time and can't climb into a dry pit on purpose. Goblins stand
  still; they're targets, not opponents.
- Spell range isn't limited except for Misty Step and Mage Hand.
- No saving throws or attack rolls. Damage is rolled flat.
- Water is integer units over each cell's elevation plus its raised or
  dug height, so a channel fills as a slope rather than levelling out.
- Every powered machine opens every gate (one channel).
- Critters are invulnerable, and you carry one of each.

## What to look for while playing

The scorecard in `docs/PROTOTYPES.md` applies here too:
- unscripted chains (write them down);
- whether a friend can see *why* something happened;
- which rules feel missing or wrong. Fix those in `rules/`, not here.
