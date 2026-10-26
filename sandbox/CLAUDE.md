# The Spell Chemistry Sandbox

A top-down TypeScript paper prototype that runs `../rules/*.json`. Its sim
(`src/world.ts`, `sim.ts`, `react.ts`, `terraform.ts`, `rules.ts`) is the
reference the C# core in `unity/Assets/Prototype/Chemistry/` mirrors, so a
change to chemistry behaviour here needs the same change there, plus a
shared golden (`World.CellHash`, see `test/terraform.test.ts`).

```sh
npm ci
npm run typecheck                    # tsc --noEmit
npm test                             # node --test test/*.test.ts (Node 22.18+ runs .ts directly)
node --test --test-name-pattern "<name>" test/sim.test.ts   # one test
npm run build                        # -> dist/index.html, one self-contained file
npm run watch                        # rebuild on changes to src/ or ../rules/
```

- `sandbox.html` is a committed copy of a build (the published page);
  refresh it with `cp dist/index.html sandbox.html` only for a build you
  publish.
- Multiplayer rooms and live mods: `docs/LIVE_LAB.md`. `dist/dev.html` uses
  the fake platform in `dev/fake-claude.js` so two local tabs share a room.
- `validateRules` in `src/rules.ts` and `Rules.Check.cs` in the C# core
  check the same things with the same messages; change both.
