# Feature costs: what the telemetry says (PM, 2026-10-30)

Made with `node tools/telemetry/feature-costs.mjs --out <report> --csv <csv>` on dev at
17f43a1; the generated report follows the reading. Costs are input-token equivalents
(ite); add `--price <$ per million input tokens>` for dollars.

## The reading

1. **Half of all spend is coordination, not features.** The coordinator's merge lines
   come to 41.1M ite against 41.8M for the work itself. They cover everything the
   coordinating account did between merges, so they follow the clock: idle hours,
   compactions and restarts land on whichever merge comes next. The worst cases are
   whirlwind-pull (9.5M overhead on a 0.4M feature, across the overnight outage),
   view-ground and combo-water (the coordinator's restart today). This is the cheapest
   place to save: fewer long-lived coordinator turns, and compacting less often.
2. **Cache reads are two thirds of the build cost** (68%); output is 16% and cache
   writes 17%. Long sessions re-reading big contexts cost more than the code they write.
   One task per boot (fresh context) is already the right shape; long host passes are
   the opposite.
3. **Most costly features:** the host passes (the wizard and spell looks 3.1M, the
   effects pass 2.7M, casting by elements 2.2M), then view-ground 2.4M and the combo
   rows (about 2M each). The host passes write the most code but run in one long
   session.
4. **Least costly:** small single-verb rule changes on w1: player-save (a design note),
   glass, creature-materials, hot-metal, light-spell, about 0.3-0.6M each.
5. **Cost does follow lines changed, but weakly:** r = 0.60 on log scales. The fit is
   cost ≈ 133k × lines^0.37, so doubling a change's size adds only about 30% to its
   cost. Even the smallest tasks cost about 0.5M (reading in, testing), and
   batching related work into one task is cheaper per line.
6. **Three clusters:** small rule tweaks (about 130 lines, 0.6M, 7 min), mid-size rule
   features (about 180 lines, 1.2M, 10 min) and big features or passes (about 630
   lines, 1.9M, 22 min). The middle cluster costs twice the first for barely more code;
   the telemetry doesn't say why (a guess worth checking: more golden churn or retries).
7. **Outliers:** none against the fit once overhead is apart; player-save is low only
   because it's a document.

## Caveats

- WIP commits that stop.cmd saves carry no cost lines, so a rescued task loses the
  first worker's share (whirlwind-pull's w2 part, hardness's w1 part).
- Minutes are the model's own time, not wall clock (combo-water: 19 min measured, about
  75 min on the clock, much of it copying the Library).
- Host passes' minutes include idle time between replies in a long session.
- The cost hook went in on 2026-10-29, so earlier work isn't measured.

---

# Feature costs on origin/dev

33 features with cost telemetry, 2026-10-29 18:28 to 2026-10-30 13:36. Cost is in input-token equivalents (ite): new input + 1.25 × cache write + 0.1 × cache read + 5 × output.

**Total:** 41.81M ite, 1153 agent minutes, 1.32M output tokens, 282.46M cache read, 1355 tool calls (31 failed), 1 commits with trouble (compactions, API errors).

**Coordination overhead**, apart from the above and below: 41.14M ite, 50% of all spend. It's the coordinator's merge lines, which cover everything that account did between merges, so it follows the clock more than the feature. 1 commits carry only overhead (workers). --with-overhead counts it in.

**Where the cost is:** cache reads 68%, output 16%, cache writes 17%, new input 0%.

## Most costly

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-10-30 11:12 | the wizard's new look and the new spells' looks | 3.14M | 647 | 151k | 1739 | 1766 | 2k | host |
| 2026-10-29 23:45 | host pass over tonight's effects | 2.69M | 70 | 97k | 471 | 473 | 6k | host |
| 2026-10-29 21:45 | view-ground | 2.42M | 22 | 59k | 372 | 374 | 7k | w3 |
| 2026-10-30 13:36 | combo-fire-cleanup | 2.42M | 33 | 54k | 478 | 478 | 5k | w1 |
| 2026-10-30 00:14 | casting by elements on the HUD and pad | 2.17M | 30 | 83k | 1239 | 1253 | 2k | host |
| 2026-10-30 13:01 | combo-air | 2.03M | 19 | 51k | 632 | 634 | 3k | w1 |
| 2026-10-30 12:24 | combo-water | 1.93M | 19 | 62k | 491 | 493 | 4k | w1 |
| 2026-10-29 20:26 | mud | 1.63M | 17 | 35k | 205 | 205 | 8k | w2 |

## Least costly

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-10-30 04:08 | player-save | 321k | 1 | 5k | 144 | 144 | 2k | w1 |
| 2026-10-30 06:56 | whirlwind-pull | 434k | 10 | 21k | 341 | 341 | 1k | w4 |
| 2026-10-29 20:29 | glass | 507k | 6 | 10k | 53 | 53 | 10k | w1 |
| 2026-10-29 18:28 | creature-materials | 526k | 4 | 6k | 44 | 44 | 12k | w1 |
| 2026-10-29 20:45 | hot-metal | 557k | 8 | 17k | 173 | 173 | 3k | w1 |
| 2026-10-30 03:45 | light-spell | 589k | 7 | 13k | 120 | 120 | 5k | w1 |

## Cost against lines changed

Over 33 features with code changes: Pearson r = 0.60 on log scales, Spearman ρ = 0.54. The fit is cost ≈ 133k × lines^0.37 ite: bigger changes cost less per line. Median 4k ite per code line.

## Outliers

**By cost** (robust z > 2 on log cost): player-save (321k).

**Against the fit:** none.


## Clusters (k-means on log code lines and log cost)

- **Cluster 1** (10): median 130 code lines, 573k ite, 7 min. whirlwind-pull, player-save, s3776, oil-tuning, light-spell, steam-cools, wind, hot-metal, glass, creature-materials.
- **Cluster 2** (10): median 182 code lines, 1.22M ite, 10 min. forge-star, hardness, combo-air, combo-water-earth, combo-fire, steam, workers-wip, creature-verbs, mud, oil-on-water.
- **Cluster 3** (13): median 632 code lines, 1.93M ite, 22 min. combo-fire-cleanup, combo-air, combo-earth, combo-water, the wizard's new look and the new spells' looks, whirl-dust, cracked, casting by elements on the HUD and pad, host pass over tonight's effects, glass-windows, combo-core, view-effects, view-ground.

## By who did it

| Who | Features | Cost (ite) | Median per feature | Min |
|---|---|---|---|---|
| w1 | 17 | 18.21M | 740k | 187 |
| host | 3 | 8.00M | 2.69M | 747 |
| w3 | 5 | 6.59M | 1.10M | 98 |
| w4 | 5 | 5.12M | 1.03M | 87 |
| w2 | 3 | 3.89M | 1.40M | 34 |

(A feature worked by several agents counts under each.)

## Every feature

| When | Feature | Cost (ite) | Min | Output | Code lines | All lines | ite / code line | Who |
|---|---|---|---|---|---|---|---|---|
| 2026-10-30 11:12 | the wizard's new look and the new spells' looks | 3.14M | 647 | 151k | 1739 | 1766 | 2k | host |
| 2026-10-29 23:45 | host pass over tonight's effects | 2.69M | 70 | 97k | 471 | 473 | 6k | host |
| 2026-10-29 21:45 | view-ground | 2.42M | 22 | 59k | 372 | 374 | 7k | w3 |
| 2026-10-30 13:36 | combo-fire-cleanup | 2.42M | 33 | 54k | 478 | 478 | 5k | w1 |
| 2026-10-30 00:14 | casting by elements on the HUD and pad | 2.17M | 30 | 83k | 1239 | 1253 | 2k | host |
| 2026-10-30 13:01 | combo-air | 2.03M | 19 | 51k | 632 | 634 | 3k | w1 |
| 2026-10-30 12:24 | combo-water | 1.93M | 19 | 62k | 491 | 493 | 4k | w1 |
| 2026-10-29 20:26 | mud | 1.63M | 17 | 35k | 205 | 205 | 8k | w2 |
| 2026-10-29 22:27 | glass-windows | 1.63M | 21 | 64k | 737 | 750 | 2k | w4 |
| 2026-10-30 12:41 | combo-earth | 1.61M | 24 | 53k | 539 | 541 | 3k | w1 |
| 2026-10-30 00:25 | combo-fire | 1.42M | 8 | 15k | 98 | 98 | 14k | w1 |
| 2026-10-30 03:24 | combo-water-earth | 1.40M | 10 | 20k | 115 | 115 | 12k | w2 |
| 2026-10-30 03:35 | combo-air | 1.35M | 9 | 19k | 159 | 159 | 8k | w1 |
| 2026-10-29 22:42 | steam | 1.33M | 12 | 20k | 133 | 133 | 10k | w1 |
| 2026-10-29 21:54 | view-effects | 1.14M | 13 | 29k | 622 | 628 | 2k | w4 |
| 2026-10-29 22:27 | combo-core | 1.14M | 15 | 86k | 1371 | 1377 | 829 | w3 |
| 2026-10-30 07:19 | hardness | 1.10M | 33 | 52k | 410 | 412 | 3k | w3 |
| 2026-10-30 08:11 | cracked | 1.03M | 25 | 54k | 654 | 663 | 2k | w4 |
| 2026-10-29 20:16 | oil-on-water | 1.03M | 9 | 26k | 148 | 148 | 7k | w1 |
| 2026-10-30 09:41 | whirl-dust | 1.02M | 22 | 65k | 903 | 907 | 1k | w3 |
| 2026-10-29 21:50 | workers-wip | 910k | 6 | 26k | 208 | 208 | 4k | w3 |
| 2026-10-30 07:46 | forge-star | 878k | 18 | 49k | 539 | 539 | 2k | w4 |
| 2026-10-29 20:42 | creature-verbs | 863k | 7 | 25k | 235 | 235 | 4k | w2 |
| 2026-10-30 04:03 | s3776 | 740k | 6 | 12k | 176 | 176 | 4k | w1 |
| 2026-10-29 23:04 | steam-cools | 662k | 7 | 12k | 140 | 140 | 5k | w1 |
| 2026-10-30 03:54 | oil-tuning | 607k | 7 | 12k | 105 | 105 | 6k | w1 |
| 2026-10-29 22:53 | wind | 599k | 8 | 13k | 117 | 117 | 5k | w1 |
| 2026-10-30 03:45 | light-spell | 589k | 7 | 13k | 120 | 120 | 5k | w1 |
| 2026-10-29 20:45 | hot-metal | 557k | 8 | 17k | 173 | 173 | 3k | w1 |
| 2026-10-29 18:28 | creature-materials | 526k | 4 | 6k | 44 | 44 | 12k | w1 |
| 2026-10-29 20:29 | glass | 507k | 6 | 10k | 53 | 53 | 10k | w1 |
| 2026-10-30 06:56 | whirlwind-pull | 434k | 10 | 21k | 341 | 341 | 1k | w4 |
| 2026-10-30 04:08 | player-save | 321k | 1 | 5k | 144 | 144 | 2k | w1 |
