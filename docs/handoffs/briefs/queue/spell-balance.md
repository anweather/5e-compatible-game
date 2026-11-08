Needs: core
Area: rules/ (spells.json, hold.json, combos.json) through the rules MCP (D13), core tests; LookScopeTests.cs for the flake
Branch: spell-balance

# Spell balance from the 2026-11-06 playtest, and one flaky test (Andrew, 2026-11-07; given to u2)

From `docs/playtests/2026-11-06-human-results.md`, part two:

1. **Fireball is too strong.** It hits for about 56, and Andrew sees no reason to cast anything else. Bring it into
   line with the other level-3 spells, so other spells are worth casting.
2. **Meteor is "completely broken"**, and he loved it. Keep how it feels: big and rare. Check it against fireball, so it
   is clearly the bigger spell but costs more (a longer cast, a cooldown or a price in the Hold's points). Don't make
   it weak.
3. **Dust Storm, and "flaw" (as dictated; find the closest spell name in `rules/spells.json`) look like they do
   nothing.** Make them worth casting. `air-tuning-rules` already tuned Gust and Whirlwind; read its numbers first and
   follow the same approach.

Do it through the rules MCP. Put a short table in the report: each spell's damage, cast time and area before and
after, with one line of reasoning per change. Use the SRD 5.2.1 numbers as the anchor where they apply. Add or update
core tests so the numbers are pinned. The Hold's bot runs (`hold` lockstep test) must still pass.

4. **The flake:** `Chem.Tests.LookScopeTests.AtAllocatesNothing` (LookScopeTests.cs:284) failed once in a full parallel
   `dotnet test core` on the PC (6560 bytes instead of 0), and passed 3 of 3 runs alone. Most likely it measures the
   whole process's allocations while other tests run in parallel, or it isn't warmed up. Make it measure only its own
   thread (e.g. `GC.GetAllocatedBytesForCurrentThread`) after a warm-up call, or put it in a non-parallel collection.
   Don't loosen it to a tolerance.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in full, `dotnet format`. Push
the branch only. Remove this claim file on the branch in the last commit. The PC compiles it and the lead lands it.
