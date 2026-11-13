# #172 probe sweep on the Mac (2026-11-11, dev 05d67fac; earlier runs on dev 0ad93be2)

Parked, nothing fixed. Probes run one at a time on the Mac Editor, valley.

- Pass: earth, defense, feel, roll, roofs, glowmoss.
- hitch: `rebuild_within_budget` passes on the idle Editor (median 1263 ms of 1300; 2874 ms when the Editor was busy).
  `switch_compose_within_budget` still fails: median 454 ms against 150 (454, 450, 715). A Mac timing; not seen on the PC.
- hands: every gesture check gets an empty result (", wanted burning-hands" etc.), keyboard and pad alike, and
  `the_merge_fires_once` fails. Looks like input not reaching the probe in the Editor (focus), not the gesture code; unconfirmed.
- rowhouses: all checks passed but the probe hit its 180 s limit (budget problem, like swell). Not rerun.
- productsview, triggerview: peer mismatches (rules hashes, 5 projectiles for 1 bolt); same area as the `online` probe
  failing on plain dev (#218). Not rerun on current dev.
- swell (#215): branch `swell-215` (5cabbcd8); `rides_the_swell` still fails on the nine (Editor frames ~180 ms).
Raw JSON was in the Mac's scratchpad and is not kept.
