# Text playtest, 2026-11-04: every note, judged

13 notes from two 20-minute sessions (seed 20261104). The raw notes are beside this file.

**Confirmed, new:** Haiku 4 is F-0008. Sonnet 9 is F-0009.

**Unconfirmed:**
- Haiku 3: a goblin at 1 hp left the creature list after a Thunderwave with no "defeated" line. Not reproduced by the model; I didn't try.

**False alarms, the tool's fault (fixed since):**
- Haiku 1, Sonnet 3, 4, 5, 6, 7: "a line starts at the clicked cell". `World.Cast`'s x, y is where a spell starts, and the tool passed the target. `Desk.CastAsAPlayer` now aims as `SpellCaster.Aim` does; a Rolling Boulder then hits the goblin on its line.
- Haiku 2, Sonnet 1: Misty Step doesn't move you. The core has no caster to move; the view does it. The prompt now says so.
- Sonnet 8: Move Earth at 0,0 did nothing. That chunk was inactive; from beside it the spell works. The prompt now says to go there first.

**False alarm, the rules intend it:**
- Sonnet 2: Molten Boulder's lava never cools. Base lava is terrain: cold crusts it, water boils off it. Both happened as the rules say.

**Duplicates of open issues:** none (`gh issue list`, `known-issues.json`). Nearest is #21, a different complaint about Rolling Boulder.
