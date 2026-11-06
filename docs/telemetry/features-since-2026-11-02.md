# Feature costs on origin/dev

59 features with cost telemetry, 2026-11-02 12:45 to 2026-11-05 21:22. Cost is in input-token equivalents (ite): new input + 1.25 × cache write + 0.1 × cache read + 5 × output.

**Total:** 133.76M ite, 4793 agent minutes, 4.33M output tokens, 937.45M cache read, 5577 tool calls (150 failed), 5 commits with trouble (compactions, API errors).

**Coordination overhead**, apart from the above and below: 136.01M ite, 50% of all spend. It's the Coordinator lines, which cover everything those sessions did between merges, so it follows the clock more than the feature. 37 commits carry only overhead (moment-guard, hold-waves, test-speed, hold-fight, maps-builder, acts-honest, named-acts, props-follow-land, stamp-guards, maps-stamps, sandbox-gauntlet-path, maps-export, maps-identity, maps-folder, maps-design, playtester-params, playtester-cheap, mix, pm-fixes, channels-3, channels-2, mod-stacking, room-tables, command-channel, chemgrid-current, fireline-out, folders-rest, mod-exception, dragon-tools, game-folders, roomruin-wait, room-agree-wait, glow-hint, player-mods-folder, play-log-switch, voice-log-words, esc-menu-cuts). --with-overhead counts it in.

**Where the cost is:** cache reads 70%, output 16%, cache writes 14%, new input 0%.

## Most costly

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-11-03 04:42 | water-perf-2 | 7.78M | 111 | 179k | 812 | 814 | 10k | host-ux |
| 2026-11-04 15:52 | land-shapes | 7.25M | 483 | 317k | 1551 | 1571 | 5k | host-ux |
| 2026-11-03 13:05 | glow | 6.08M | 83 | 173k | 585 | 587 | 10k | host-ux |
| 2026-11-04 00:35 | mcp-act | 5.05M | 137 | 114k | 750 | 756 | 7k | host-ux |
| 2026-11-05 02:58 | stamp-frame | 4.78M | 517 | 213k | 132 | 132 | 36k | host-ux |
| 2026-11-03 11:23 | products-view | 4.69M | 46 | 94k | 687 | 689 | 7k | host-ux |
| 2026-11-02 23:28 | water-look-perf | 4.64M | 369 | 117k | 136 | 138 | 34k | host-ux |
| 2026-11-03 01:04 | lit-windows | 4.49M | 68 | 107k | 462 | 464 | 10k | host-ux |

## Least costly

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-11-05 17:34 | hold-hands | 102k | 3 | 2k | 1476 | 1486 | 69 | host-ux |
| 2026-11-03 09:32 | split-2 | 263k | 15 | 12k | 112 | 112 | 2k | u2 |
| 2026-11-03 11:43 | cover-spread | 288k | 11 | 13k | 396 | 400 | 728 | u2 |
| 2026-11-04 07:36 | dragon-off | 357k | 18 | 15k | 21 | 21 | 17k | host-ux |
| 2026-11-03 09:27 | react-split | 432k | 19 | 21k | 343 | 343 | 1k | u2 |
| 2026-11-03 19:38 | liquids-2 | 466k | 22 | 19k | 536 | 538 | 869 | host-ux |

## Cost against lines changed

Over 58 features with code changes: Pearson r = 0.27 on log scales, Spearman ρ = 0.34. The fit is cost ≈ 517k × lines^0.20 ite: bigger changes cost less per line. Median 5k ite per code line.

## Outliers

**By cost** (robust z > 2 on log cost): hold-hands (102k).

**Against the fit** (cost far from what its lines predict):

- split-2: 263k for 112 code lines, 0.2× less than predicted.
- cover-spread: 288k for 396 code lines, 0.2× less than predicted.
- hold-hands: 102k for 1476 code lines, 0.0× less than predicted.

## Clusters (k-means on log code lines and log cost)

- **Cluster 1** (15): median 53 code lines, 492k ite, 22 min. hands-feel, hold-hands, gate-step, castle-clearing, castle-stone, castle-crest, dragon-off, setup-page-width, liquids-2, cover-spread, building-gaps, settings-leak, split-2, react-split, creature-scale.
- **Cluster 2** (20): median 295.5 code lines, 1.39M ite, 36 min. waves-view, hand-looks, hold-fight, castle-road, stamp-frame, voice-libs-bundled, liquids-view, dependents-all, products-data, test-data-folder, rule-schema, graft, fauna-read, fauna-resize, fauna-rows, playtest-designs, water-look-perf, save-draft-id, chat-fail-loud, chat-notes-slow.
- **Cluster 3** (23): median 750 code lines, 3.23M ite, 54 min. folk-jobs, castle-camera, pso-terrainrock, land-shapes, ball-in-cup, mcp-act, whisper-spike, settings-api-key, liquids, glow, products-view, temp-materials, graft-coats, triggers-view, triggers-core, water-perf-2, water-perf, place-view, lit-windows, origin/dev, voice-hears, chat-notes-fallback, chat-api.

## By who did it

| Who | Features | Cost (ite) | Median per feature | Min |
|---|---|---|---|---|
| host-ux | 47 | 119.57M | 2.18M | 4511 |
| u2 | 16 | 22.12M | 999k | 557 |

(A feature worked by several agents counts under each.)

## Work with no commit

From docs/telemetry/sessions.jsonl, set beside the median feature (1.64M ite).

| When | Session | Cost (ite) | Min | Output |
|---|---|---|---|---|
| 2026-10-30 23:47 | playtest 0.1.8: 3 bot runs + triage | 0 | 42 | 0 |

## Every feature

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-11-03 04:42 | water-perf-2 | 7.78M | 111 | 179k | 812 | 814 | 10k | host-ux |
| 2026-11-04 15:52 | land-shapes | 7.25M | 483 | 317k | 1551 | 1571 | 5k | host-ux |
| 2026-11-03 13:05 | glow | 6.08M | 83 | 173k | 585 | 587 | 10k | host-ux |
| 2026-11-04 00:35 | mcp-act | 5.05M | 137 | 114k | 750 | 756 | 7k | host-ux |
| 2026-11-05 02:58 | stamp-frame | 4.78M | 517 | 213k | 132 | 132 | 36k | host-ux |
| 2026-11-03 11:23 | products-view | 4.69M | 46 | 94k | 687 | 689 | 7k | host-ux |
| 2026-11-02 23:28 | water-look-perf | 4.64M | 369 | 117k | 136 | 138 | 34k | host-ux |
| 2026-11-03 01:04 | lit-windows | 4.49M | 68 | 107k | 462 | 464 | 10k | host-ux |
| 2026-11-03 00:26 | voice-hears | 4.00M | 35 | 147k | 374 | 380 | 11k | host-ux |
| 2026-11-02 15:32 | save-draft-id | 3.86M | 72 | 179k | 217 | 217 | 18k | host-ux |
| 2026-11-03 02:42 | water-perf | 3.84M | 53 | 99k | 415 | 419 | 9k | host-ux |
| 2026-11-03 09:17 | graft-coats | 3.50M | 41 | 95k | 609 | 611 | 6k | host-ux |
| 2026-11-03 18:45 | liquids | 3.45M | 48 | 107k | 1101 | 1107 | 3k | u2 |
| 2026-11-03 05:56 | fauna-resize | 3.44M | 87 | 77k | 265 | 267 | 13k | host-ux |
| 2026-11-03 07:44 | triggers-core | 3.43M | 166 | 99k | 1173 | 1181 | 3k | host-ux, u2 |
| 2026-11-02 13:37 | chat-notes-fallback | 3.23M | 54 | 149k | 830 | 830 | 4k | host-ux |
| 2026-11-05 07:00 | castle-camera | 3.18M | 317 | 99k | 1106 | 1112 | 3k | host-ux |
| 2026-11-05 21:22 | waves-view | 3.06M | 152 | 71k | 345 | 349 | 9k | host-ux |
| 2026-11-05 12:50 | folk-jobs | 3.05M | 95 | 75k | 674 | 682 | 5k | host-ux |
| 2026-11-03 08:32 | triggers-view | 2.91M | 32 | 88k | 421 | 427 | 7k | host-ux |
| 2026-11-03 01:45 | place-view | 2.79M | 39 | 79k | 551 | 555 | 5k | host-ux |
| 2026-11-03 00:41 | origin/dev | 2.62M | 37 | 81k | 1196 | 1210 | 2k | u2 |
| 2026-11-05 17:22 | hold-fight | 2.52M | 200 | 88k | 38 | 38 | 66k | host-ux |
| 2026-11-05 10:04 | castle-road | 2.26M | 92 | 78k | 389 | 393 | 6k | host-ux |
| 2026-11-02 12:45 | chat-api | 2.18M | 57 | 103k | 1613 | 1625 | 1k | host-ux |
| 2026-11-04 07:18 | ball-in-cup | 2.18M | 402 | 89k | 602 | 604 | 4k | host-ux |
| 2026-11-03 13:23 | dependents-all | 2.08M | 54 | 65k | 482 | 486 | 4k | host-ux, u2 |
| 2026-11-03 10:38 | temp-materials | 1.86M | 30 | 68k | 703 | 711 | 3k | u2 |
| 2026-11-05 14:02 | gate-step | 1.74M | 67 | 41k | 22 | 22 | 79k | host-ux |
| 2026-11-05 11:35 | castle-clearing | 1.64M | 72 | 33k | 39 | 39 | 42k | host-ux |
| 2026-11-04 17:30 | pso-terrainrock | 1.64M | 82 | 48k | 2262 | 2262 | 724 | host-ux |
| 2026-11-03 20:22 | settings-api-key | 1.58M | 49 | 97k | 1592 | 1610 | 990 | host-ux |
| 2026-11-03 08:37 | graft | 1.58M | 41 | 62k | 622 | 624 | 3k | host-ux, u2 |
| 2026-11-03 21:28 | setup-page-width | 1.46M | 50 | 29k | 47 | 47 | 31k | host-ux |
| 2026-11-02 13:56 | chat-notes-slow | 1.45M | 30 | 47k | 84 | 84 | 17k | host-ux |
| 2026-11-03 09:57 | settings-leak | 1.38M | 20 | 51k | 30 | 30 | 46k | host-ux |
| 2026-11-03 10:13 | test-data-folder | 1.33M | 14 | 43k | 170 | 174 | 8k | host-ux |
| 2026-11-05 18:46 | hand-looks | 1.31M | 41 | 44k | 467 | 488 | 3k | host-ux |
| 2026-11-03 22:06 | voice-libs-bundled | 1.24M | 23 | 39k | 174 | 176 | 7k | host-ux |
| 2026-11-03 09:45 | rule-schema | 1.22M | 29 | 49k | 447 | 451 | 3k | u2 |
| 2026-11-02 14:21 | chat-fail-loud | 1.08M | 24 | 54k | 289 | 289 | 4k | host-ux |
| 2026-11-03 00:09 | fauna-rows | 1.01M | 16 | 26k | 277 | 279 | 4k | u2 |
| 2026-11-02 23:47 | playtest-designs | 991k | 10 | 43k | 351 | 351 | 3k | u2 |
| 2026-11-03 10:23 | products-data | 918k | 31 | 34k | 307 | 311 | 3k | u2 |
| 2026-11-03 19:11 | liquids-view | 900k | 28 | 54k | 302 | 304 | 3k | host-ux |
| 2026-11-03 08:04 | fauna-read | 857k | 14 | 35k | 464 | 468 | 2k | host-ux, u2 |
| 2026-11-05 18:05 | hands-feel | 830k | 40 | 34k | 37 | 37 | 22k | host-ux |
| 2026-11-05 07:44 | castle-crest | 761k | 43 | 40k | 53 | 53 | 14k | host-ux |
| 2026-11-03 00:04 | creature-scale | 667k | 8 | 27k | 30 | 30 | 22k | u2 |
| 2026-11-03 21:08 | whisper-spike | 644k | 14 | 16k | 1842 | 1872 | 350 | host-ux |
| 2026-11-05 08:10 | castle-stone | 492k | 27 | 22k | 64 | 64 | 8k | host-ux |
| 2026-11-03 22:52 | coordination | 479k | 27 | 11k | 0 | 2 | - | host-ux |
| 2026-11-03 11:34 | building-gaps | 469k | 28 | 20k | 222 | 222 | 2k | u2 |
| 2026-11-03 19:38 | liquids-2 | 466k | 22 | 19k | 536 | 538 | 869 | host-ux |
| 2026-11-03 09:27 | react-split | 432k | 19 | 21k | 343 | 343 | 1k | u2 |
| 2026-11-04 07:36 | dragon-off | 357k | 18 | 15k | 21 | 21 | 17k | host-ux |
| 2026-11-03 11:43 | cover-spread | 288k | 11 | 13k | 396 | 400 | 728 | u2 |
| 2026-11-03 09:32 | split-2 | 263k | 15 | 12k | 112 | 112 | 2k | u2 |
| 2026-11-05 17:34 | hold-hands | 102k | 3 | 2k | 1476 | 1486 | 69 | host-ux |
