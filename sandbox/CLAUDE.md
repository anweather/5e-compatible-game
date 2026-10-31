# The Spell Chemistry Sandbox

A top-down TypeScript paper prototype, **frozen** (Andrew, 2026-10-31;
J-042, D6). It runs `rules-frozen/`, a copy of `../rules/` taken that day,
not `../rules/` itself, so new rules never break it. The C# core is the only
engine that follows `rules/`: don't port changes here, and don't refresh
the snapshot. Keep its own tests, typecheck and build green.

```sh
npm ci
npm run typecheck                    # tsc --noEmit
npm test                             # node --test test/*.test.ts (Node 22.18+ runs .ts directly)
node --test --test-name-pattern "<name>" test/sim.test.ts   # one test
npm run build                        # -> dist/index.html, one self-contained file
npm run watch                        # rebuild on changes to src/ or rules-frozen/
```

- `sandbox.html` is a committed copy of a build (the published page);
  refresh it with `cp dist/index.html sandbox.html` only for a build you
  publish.
- Multiplayer rooms and live mods: `docs/LIVE_LAB.md`. `dist/dev.html` uses
  the fake platform in `dev/fake-claude.js` so two local tabs share a room.
- `validateRules` in `src/rules.ts` checked the same things as
  `Rules.Check.cs` at the freeze; the C# checks move on without it.
