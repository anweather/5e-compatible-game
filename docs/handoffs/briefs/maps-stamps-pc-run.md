Brief for the PC: land stamps on real hardware (`maps-stamps`, `docs/design/maps-as-data.md` section 3). The Mac's Editor is stuck, so nothing here has been seen drawn. The core tests prove the integers, the undo, the save and the replay; these steps prove the view.

**Build:** a Windows player from `maps-stamps`, `PrototypeBuild.BuildWindowsFromCommandLine`, or the Editor on the branch. Stamps go through the chat's `act`. Ask for them in words: "raise the land 30 m, radius 32, near me". Pick open country: a stamp touching water or a village is refused.

**1. Cost per stamp, on the GPU.** `tools/probe.sh perf --seconds 30` for a baseline in open country, then the same with a stamp asked for as it runs. Stamps to time: radius 32 rise 10 m, radius 32 rise 100 m (dome), radius 32 rise 100 m rough 50, radius 8 rise 10 m. **Report:** mean, p95 and worst frame, and how long the ground takes to finish moving. It eases at `stepHeight / shapeSeconds`, so 100 m may take over a minute, with four chunks rewritten every frame. The worst reshaping frame on record is 31 ms. If radius 32 at 100 m is over 33 ms, say so: `land.maxRise` in `rules/budgets.json` is the lever, or snapping big stamps.

**2. Drawn and stood on.** Stamp a dome beside the wizard, walk up it. **Must show:** the wizard stands on the drawn ground all the way, no seam or crack at a chunk edge, the middle nine's edge above all, and a cast lands on the surface, not in or over it. **Look for, unknown:** do trees stay on the ground as it moves? Do props resting there fall through or float? A prop or tree left hanging is a finding for me, not a failure of the run.

**3. Water.** Pour Create Water on a slope of the dome. **Must show:** it runs down. A stamp across the lake is refused, in words.

**4. Two peers, a stamp, an undo, a late join.** A hosts on the same network, B joins `127.0.0.1`. A stamps a dome. **Must show:** the same ground on both, no desync. A: `undo_act`. **Must show:** it goes back on both, no desync. A stamps again, then C joins late. **Must show:** C has the hill, no desync, and an undo from A takes it down on all three.

**5. The dragon.** Stamp, then summon the dragon. **Must show:** after the flight the land is the map's own on both peers.

**Report:** the numbers, what each step showed, any desync, and anything hanging in the air. Not this run's job: named acts, `keep_land`, a stamp across water.
