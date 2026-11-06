Brief for the Mac: the Hold, phase 5, progress. Read `docs/design/the-hold.md` (phases 3 to 5) and J-038. Branch `hold-progress` off dev, after `moment-guard`. Core, rules and tests first; a plain view is enough, and the PC does the look.

**Goal:** a reason to play another run: choices inside a run, and things you keep between runs.

**Inside a run:**
- Between waves, each wizard is offered three upgrades, from rows in the rules, chosen by the seeded RNG. Each peer picks its own, by command. No pick before the next wave starts gives a seeded default, so nobody holds up a run.
- An upgrade is a row changing numbers a wizard already has (cooldown, damage of one gesture or element, range, health). About 12 to start, with a spread over elements and gestures, so a hand set can lean into itself. Upgrades end with the run.

**Between runs:**
- A wizard starts with one hand, one element and the thrust gesture. In order, runs unlock: throw, sweep, the second hand, other elements, then kit spells. What's unlocked and what it costs are rows.
- What earns an unlock is a dial: start with waves survived adding up across runs, and say what you chose.
- Discoveries stay, as they do today.

**Progress belongs to the player, not the world (J-038).** A player's unlocks live in their own save, and they bring them to any host's world: on joining, a command tells every peer what that wizard has, so the worlds stay the same. Upgrades in a run are core state. A world reset or a lost run never touches a player's unlocks. Say how you guard against a save that won't load.

**Playtesting mustn't get harder:** a moment gives its hand set whatever is unlocked, and dev builds and the Editor have a way to start with everything. Andrew's pad test needs both hands and all gestures on day one. The headless curve gets a run with upgrades and a run with a fresh player, so we can read both.

**Keep:** core state, commands only, seeded RNG, no golden moved, words in the glossary first ("upgrade", "unlock"). Sandbox unchanged. Don't tune Cinders or Roots: Andrew plays them first. The PC is on `waves-view`, so stay out of its view files and say if you need one.

**Tests:** offers are the same on all peers, a pick reaches only that wizard, the seeded default, unlock order, a save round trip, a joiner's unlocks matching on all peers, a reset keeping unlocks, two worlds matching.

**Report:** one short message: the commit, gates with exit codes, the curve with and without upgrades, how long a new player takes to unlock everything, and a numbered list for the PC: what the offer and the unlocks must show. Don't merge it. Ask before anything much larger than this brief.
