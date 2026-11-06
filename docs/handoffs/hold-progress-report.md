# The Hold, phase 5: report from the Mac

For the next PM: `hold-progress`, not merged. Sent to the last PM's session too, which had handed off. Code as verified at e1bee1e0; this file is the only commit after it.

**Gates:** `gates.sh` exit 0, 1677 of 1677. EditMode exit 2: 1591 of 1601, 2 skipped, the 8 failures the known `ApiKeyTests`. Mac batch player: `moments` 87 of 87, `hands` 87 of 87, `hold` 43 of 43, 0 console errors. No golden moved. 29 mutations: 28 caught, one equivalent.

**Curve** (scripted wizard, 3 seeds, mean minutes, target 20; `core/Hold.Runner`):

| Hands | No upgrades, 1 peer | Upgrades, 1 peer | Upgrades, 2 peers |
|---|---|---|---|
| fire+fire | 17.1 | 26.7 | 39.7 |
| fire+water | 16.5 | 19.3 | 24.3 |
| fire+air | 11.9 | 12.7 | 16.7 |
| water+air | 9.4 | 11.5 | 11.4 |
| earth+air | 8.5 | 8.7 | 8.3 |

- `health` went from 360 to 300 because of upgrades. Fire+water is on target, fire+fire over it.
- **Two peers coast with fire:** two fire+fire wizards last 40 minutes or more (two longer runs ended at 42 and 55). No health bump built, as asked; try a real two-peer run first.
- **A new player:** 57 points unlock everything: 6 runs, 1.8 hours for the bot. A fresh player reaches wave 8 or 9 in about 14 minutes, which is the bot barely needing the second hand; a human's first run is likely shorter.

**Choices:** points are `pointsPerWave` 1 for waves survived, added across runs, and are all the save holds. Unlock order: throw, sweep, second hand, water, earth, air, Light, Hasten Time, Shapeshift, Control Weather. A wizard with no points brought has everything, as does anyone in a moment. Dev builds bring everything; F7 plays as the save says. A bad save is kept as `.broken`, the last good copy is used, the player is told, and the file is never written lower. Hosts believe what a player brings. `World.Raiders.cs` changed by one line: a damage percent, 100 unless an upgrade applies. Boss health by peers: `boss.peerHealth`.

**Not checked:** two real peers or anything online; a real release player; the offer on screen; a player build using a real save (core tests only). Pad picking doesn't exist: 1, 2 and 3, or a click.

**For the PC:**
1. The offer: three upgrades between waves, picked on a pad, and how long before one's picked for you.
2. What each upgrade does in words, and the wizard's picks during a run.
3. Unlocks: what you have, what's next, how many points away.
4. A run's end: what was earned, and anything newly unlocked.
5. A locked gesture or hand should say so when pressed: today nothing happens.
6. The attuning room shows only what's unlocked; health visibly changes with Tough.
7. F7 and a fresh run with a real save: quit, reload and a reset keep the points.
8. Two peers with different points: hands match on host and joiner, no desync.
9. Andrew: does a first run feel right, and is 1.8 hours too short a road?
