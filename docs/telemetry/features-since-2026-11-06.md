# Feature costs on origin/dev

31 features with cost telemetry, 2026-11-06 23:58 to 2026-11-08 20:45. Cost is in input-token equivalents (ite): new input + 1.25 × cache write + 0.1 × cache read + 5 × output.

**Total:** 119.80M ite, 5337 agent minutes, 3.20M output tokens, 868.72M cache read, 5180 tool calls (102 failed), 2 commits with trouble (compactions, API errors).

**Coordination overhead**, apart from the above and below: 145.38M ite, 55% of all spend. It's the Coordinator and PM lines, which cover everything those sessions did between merges, so it follows the clock more than the feature. 22 commits carry only overhead (roomopen-sync, coordinator, chunk-hash, map-size, queue-glob, watch-pause, landmark-look-pass, landmark-intent-fixes, landmark-intent-fixes, landmark-intent, ground-cell-mismatch, rebuild-threading, biome-field, hold-east-path, pad-hands-menu, hold-bodies, hold-batch, casting-leftovers, hold-lockstep-fast, mac-checks, worker-log, mcp-controls-text). --with-overhead counts it in.

**Where the cost is:** cache reads 73%, output 13%, cache writes 14%, new input 0%.

## Most costly

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-11-07 21:07 | castle-cliff-canyon | 15.94M | 437 | 398k | 1229 | 1237 | 13k | host-ux |
| 2026-11-07 08:52 | unified-casting-view | 13.38M | 2096 | 370k | 2043 | 2045 | 7k | host-ux, u2 |
| 2026-11-08 17:00 | maps-phase-6 | 12.95M | 99 | 238k | 1328 | 1338 | 10k | host-ux |
| 2026-11-08 13:20 | wow-looks | 10.45M | 104 | 339k | 892 | 904 | 12k | host-ux |
| 2026-11-08 11:40 | wow-spells | 9.87M | 322 | 252k | 833 | 839 | 12k | host-ux, u2 |
| 2026-11-08 01:47 | peer-vanish | 7.77M | 284 | 216k | 530 | 534 | 15k | host-ux |
| 2026-11-08 15:26 | casting-taps | 6.88M | 137 | 216k | 506 | 510 | 14k | host-ux, u2 |
| 2026-11-08 18:31 | maps-phase-7 | 5.95M | 84 | 111k | 2004 | 2024 | 3k | host-ux |

## Least costly

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-11-08 09:21 | u2 takes punchy-sync | 63k | 2 | 3k | 0 | 0 | - | u2 |
| 2026-11-08 07:46 | u2 takes player-mods | 86k | 167 | 3k | 0 | 0 | - | u2 |
| 2026-11-07 15:35 | air-tuning-rules | 110k | 11 | 3k | 160 | 160 | 688 | u2 |
| 2026-11-06 23:58 | bridge-outbox-repeats | 141k | 98 | 9k | 84 | 84 | 2k | u2 |
| 2026-11-07 19:03 | mcp-blue-fire | 206k | 148 | 10k | 61 | 61 | 3k | u2 |
| 2026-11-07 06:13 | hold-offer-suits | 265k | 15 | 13k | 115 | 115 | 2k | u2 |

## Cost against lines changed

Over 28 features with code changes: Pearson r = 0.60 on log scales, Spearman ρ = 0.60. The fit is cost ≈ 15k × lines^0.80 ite: bigger changes cost less per line. Median 7k ite per code line.

## Outliers

**By cost** (robust z > 2 on log cost): u2 takes punchy-sync (63k).

**Against the fit** (cost far from what its lines predict):

- view-rebuild: 407k for 402 code lines, 0.2× less than predicted.
- world-plan: 1.46M for 3475 code lines, 0.1× less than predicted.
- punchy-sync: 810k for 1934 code lines, 0.1× less than predicted.
- air-tuning-rules: 110k for 160 code lines, 0.1× less than predicted.
- chunk-gen: 607k for 1467 code lines, 0.1× less than predicted.

## Clusters (k-means on log code lines and log cost)

- **Cluster 1** (10): median 153 code lines, 418k ite, 48 min. view-rebuild, test-speed-core, ship-update-delta, chat-new-request, spell-balance, mcp-blue-fire, air-tuning-rules, hold-offer-suits, stamp-steepness, bridge-outbox-repeats.
- **Cluster 2** (9): median 353 code lines, 2.77M ite, 57 min. chunk-gen, cell-memory, world-plan, punchy-sync, pants-and-colours, hitch-hunt, probe-cleanup, player-mods, stamp-looks.
- **Cluster 3** (9): median 892 code lines, 9.87M ite, 207 min. maps-phase-7, maps-phase-6, casting-taps, wow-looks, wow-spells, peer-vanish, castle-cliff-canyon, aimed-spells, unified-casting-view.

## By who did it

| Who | Features | Cost (ite) | Median per feature | Min |
|---|---|---|---|---|
| host-ux | 19 | 112.48M | 4.10M | 4584 |
| u2 | 19 | 44.30M | 864k | 3797 |

(A feature worked by several agents counts under each.)

## Work with no commit

From docs/telemetry/sessions.jsonl, set beside the median feature (2.12M ite).

| When | Session | Cost (ite) | Min | Output |
|---|---|---|---|---|
| 2026-10-30 23:47 | playtest 0.1.8: 3 bot runs + triage | 0 | 42 | 0 |

## Every feature

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-11-07 21:07 | castle-cliff-canyon | 15.94M | 437 | 398k | 1229 | 1237 | 13k | host-ux |
| 2026-11-07 08:52 | unified-casting-view | 13.38M | 2096 | 370k | 2043 | 2045 | 7k | host-ux, u2 |
| 2026-11-08 17:00 | maps-phase-6 | 12.95M | 99 | 238k | 1328 | 1338 | 10k | host-ux |
| 2026-11-08 13:20 | wow-looks | 10.45M | 104 | 339k | 892 | 904 | 12k | host-ux |
| 2026-11-08 11:40 | wow-spells | 9.87M | 322 | 252k | 833 | 839 | 12k | host-ux, u2 |
| 2026-11-08 01:47 | peer-vanish | 7.77M | 284 | 216k | 530 | 534 | 15k | host-ux |
| 2026-11-08 15:26 | casting-taps | 6.88M | 137 | 216k | 506 | 510 | 14k | host-ux, u2 |
| 2026-11-08 18:31 | maps-phase-7 | 5.95M | 84 | 111k | 2004 | 2024 | 3k | host-ux |
| 2026-11-07 12:21 | aimed-spells | 5.42M | 207 | 135k | 813 | 821 | 7k | host-ux |
| 2026-11-08 05:32 | hitch-hunt | 4.10M | 54 | 88k | 353 | 357 | 12k | host-ux |
| 2026-11-08 04:38 | probe-cleanup | 3.46M | 75 | 89k | 283 | 283 | 12k | host-ux |
| 2026-11-08 06:47 | pants-and-colours | 3.25M | 73 | 70k | 200 | 202 | 16k | host-ux |
| 2026-11-08 19:56 | cell-memory | 3.02M | 33 | 66k | 346 | 346 | 9k | host-ux |
| 2026-11-07 13:44 | stamp-looks | 2.77M | 85 | 76k | 250 | 252 | 11k | host-ux |
| 2026-11-08 03:22 | player-mods | 2.76M | 115 | 70k | 1202 | 1227 | 2k | host-ux, u2 |
| 2026-11-08 02:45 | spell-balance | 2.12M | 275 | 63k | 193 | 195 | 11k | host-ux, u2 |
| 2026-11-08 19:05 | coordinator | 2.04M | 63 | 104k | 0 | 0 | - | u2 |
| 2026-11-08 18:42 | world-plan | 1.46M | 34 | 71k | 3475 | 3491 | 419 | u2 |
| 2026-11-08 07:20 | chat-new-request | 1.17M | 42 | 42k | 132 | 132 | 9k | host-ux, u2 |
| 2026-11-08 18:47 | test-speed-core | 1.05M | 54 | 38k | 170 | 170 | 6k | u2 |
| 2026-11-07 06:10 | stamp-steepness | 864k | 24 | 41k | 207 | 207 | 4k | u2 |
| 2026-11-08 07:56 | punchy-sync | 810k | 57 | 17k | 1934 | 1934 | 419 | host-ux, u2 |
| 2026-11-08 20:45 | chunk-gen | 607k | 6 | 14k | 1467 | 1475 | 413 | u2 |
| 2026-11-08 15:55 | ship-update-delta | 428k | 131 | 28k | 146 | 146 | 3k | u2 |
| 2026-11-08 20:37 | view-rebuild | 407k | 5 | 8k | 402 | 404 | 1k | host-ux |
| 2026-11-07 06:13 | hold-offer-suits | 265k | 15 | 13k | 115 | 115 | 2k | u2 |
| 2026-11-07 19:03 | mcp-blue-fire | 206k | 148 | 10k | 61 | 61 | 3k | u2 |
| 2026-11-06 23:58 | bridge-outbox-repeats | 141k | 98 | 9k | 84 | 84 | 2k | u2 |
| 2026-11-07 15:35 | air-tuning-rules | 110k | 11 | 3k | 160 | 160 | 688 | u2 |
| 2026-11-08 07:46 | u2 takes player-mods | 86k | 167 | 3k | 0 | 0 | - | u2 |
| 2026-11-08 09:21 | u2 takes punchy-sync | 63k | 2 | 3k | 0 | 0 | - | u2 |
