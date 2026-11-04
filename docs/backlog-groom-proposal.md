# Backlog groom: proposal (2026-11-04)

Status: **waiting for Andrew's approval.** Nothing has been created or changed on GitHub yet.
Read: 56 open issues, `docs/BACKLOG.md` (30 sections), `docs/STATE.md`, `docs/design/maps-as-data.md`.
`gh` has the project scope. Existing boards: "Boardgame Platform" and "Hex Brain Prototype" (not this game); a new board is made.

## Epics (9)

| # | Epic | Done looks like |
|---|---|---|
| E1 | Material interactions | Materials, weather and time act on each other and on what stands on them: lava, ice, water, snow and fog behave, you can build on water, and the day has a night |
| E2 | Spell effects and casting | Every spell lands where aimed, charges, combines (incl. weather and three-element casts) and feels weighty; aim markers show what it will do |
| E3 | Attributes, bodies and items | Wizards, animals and things have rules-data attributes (size, parts, gaits, reactions to walking); items, inventory, weapons and who-did-what work through the rules |
| E4 | Mini games | Spell golf, ball in the cup, the Gauntlet, tower, tower defense and the dragon are polished and replayable, each a few clicks from the yard |
| E5 | Maps as data (#69) | A map is a folder; the host picks one; a second map loads and plays with two peers; Claude can shape land live. Children are the nine phases of `docs/design/maps-as-data.md`, plus game modes |
| E6 | Claude in the game | Chat and the MCP tools work for any account and harness, are cheap and honest, and let Claude change the world (acts, sizes, dragon, tools gaps) |
| E7 | Lockstep and online | Peers agree: replays, late joiners, rule switches, reset, interest by chunk, voice |
| E8 | Performance and test health | Frame time and GPU hot spots are flattened; flaky probes and tests are fixed or moved out of the gates |
| E9 | Look, feel and animation | Robe, HUD, fog, sounds, swim and climb animation, IK, controller on every platform |

## Labels

Kind (reuse): `bug`, `enhancement`, `design` (a design change, not a defect), `perf`. `ux` is folded into `design`; left on the old issues.
Source (reuse, unchanged): `playtest`, `human-playtest`, `rediscovered`.
New area labels: `area:chemistry` (cells, materials, weather), `area:spells` (casting, aiming, combos), `area:world` (bodies, things, items, land, maps), `area:minigames`, `area:claude` (chat, MCP, acts), `area:net` (lockstep, online), `area:view` (look, sound, UI, animation), `area:quality` (tests, probes, tooling).
New priority labels: `p1` (before the next release, or blocks work), `p2` (soon, in STATE's "Next"), `p3` (later).
New: `epic` (the epic issues).
Not used: `duplicate`, `invalid`, `wontfix` stay for closing, `good first issue`, `help wanted`, `question`, `accessibility`, `documentation` are left alone.

## New issues (one per backlog section with open work)

`BACKLOG n` means section n. Epic in brackets; kind, area and priority are `enhancement`/`design` unless said; p3 unless said.

| BACKLOG | Title | Epic |
|---|---|---|
| 1 | What night does: stars, lights at dusk, slowing or stopping time (Hasten Time is built) | E1 |
| 2 | Weather, the rest: heat, fronts per chunk, big-fire updraft, rain under roofs, splashes, Control Weather choosing wind or fog, recorded loops | E1 |
| 5 | Items and an inventory (hands only or a belt?), tools and flasks | E3 (p2) |
| 6 | Weapons: spell weapons or a gun module; needs a fit decision first | E3 |
| 7 | First-person mode | E9 |
| 8 | Sound by distance and proximity voice (Vivox, D8), sound-sight spells | E7 |
| 9 | Animation: foot and aim IK, casting poses | E9 |
| 10 | Components in the rules (ECS-ready step 1: `flammable`, `conducts`…; interned ids) | E3 |
| 11 | World streaming part 2: to disk, interest by chunk, floating origin | E7 |
| 13 | Swim animation | E9 |
| 14 | Climbing | E1 |
| 15 | Ball court scoreboard polish: HUD line, reset, clock or first to N | E4 |
| 16 | Platformer tower polish: wall jump, kept best times, shared collapse, camera on the far side | E4 |
| 17 | Tower defense: towers that shoot, upgrades, currency, flying and swimming raiders, shoves | E4 |
| 18 | Finer ground: a smoother height field, or finer height with mean-height water | E1 |
| 19 | Performance pass: player-build spikes, Play-mode start (merge with #70's perf set under E8) | E8 |
| 20 | World life: villages, villagers, fish, birds, generated wizard looks (`WORLD_LIFE.md`) | E3 |
| 21 | Player saves: progress travels with the player (J-038, `PLAYER_SAVE.md`) | E7 |
| 22 | Weather casts and three-element casts | E2 |
| 23 | The beard as a look option | E9 |
| 24 | Spell golf: play again after holing out (needs a `GolfCommand` intent) | E4 |
| 25 | The Gauntlet: a carnival course and spell-restricted zones, a small slice first (`docs/handoffs/obstacle-course-brief.md`) | E4 |
| 26 | Dragon leftovers: shelter in the rules, dawn, birds, a landing clip, raiders drawn down | E4 |
| 27 | Pick another LLM harness (Codex, Kiro): hard requirement and terms check first | E6 |
| 28 | Game modes: saved sets of mods | E5 |
| 30 | What Claude can do in the world: named acts, more triggers, general ball, animals apart, water level | E6 (p2) |
| old table 18/19 | Punchy animal port 2 and 3: variety as data; water life | E3 |
| old table 20 | Generated areas from biomes (baked from seed or runtime) | E5 |
| old table 21–23, 25, 27, 28 | World tools: reading and looks, mods in the world, spawners, scoped looks, colour mixing, paint as a test | E3 |
| old table 26 | Who did what: the core records who caused an effect (ADR needed, D9) | E3 |
| old table 24, 29, 30 | Terminal panel in the game, speech to text, a player page | E6 |
| 29 | = #69, no new issue | E5 |

Not issues: 3 (caves, deferred permanently), 4 (hardness, done), 12 (reset online, built: R held 3 s, `StampReset`), 29 (is #69).
Old priority tables: listed items are either above, built, or in the list; no separate issue.

Maps phases, children of #69 (nine issues, `area:world`, `enhancement`): 1 Folder (p1, brief exists), 2 Identity, 3 Land stamps live (after named acts), 4 Export, 5 Builder, 6 The scene lets go, 7 Sea and life, 8 The test (second map), 9 Keeping. p2 for 1–3, p3 for the rest.

Counts: 9 epics, 30 backlog issues (counting the grouped world tools and Punchy rows as one each), 9 map phases = 48 new issues.

## Existing issues (56)

All get their epic as a sub-issue plus labels. Kind kept if present; `[+kind]` where it was missing.

| Epic | Issues (area, priority) |
|---|---|
| E1 | #92 design chem p2; #91 bug chem p2; #90 design chem p3; #60 [+enhancement] chem p2; #28 chem p3; #34 chem p3; #54 [+bug] chem p3 |
| E2 | #22 spells p3; #21 spells (fixed, see defect review); #18 spells p3; #7 spells (see review); #2 spells p3; #44 [+bug] spells p3 |
| E3 | #73 world p2; #61 [+enhancement] world p2; #76 world p2 |
| E4 | #36 bug minigames p3 |
| E5 | #69 is the epic (+`epic`) |
| E6 | #77 claude p2; #64 [+bug] claude p1 (see review); #63 [+bug] claude p2; #59 [+enhancement] claude p2; #56 [+bug] claude p2; #53 [+bug] claude p1; #52 [+enhancement] claude p2 |
| E7 | #83 [+bug] net p2; #68 [+bug] net p2; #66 [+bug] net p3; #55 [+bug] net p2; #35 net p3 |
| E8 | #87 [+bug] quality p3; #82 quality p3; #45 quality p3; #40 [+bug] quality p3; #38 quality p3; #70 perf p2; #33 perf p3; #32 perf p3; #31 perf p3; #30 perf p3; #29 perf p3; #27 perf p3 |
| E9 | #8 view p3; #5 view p3; #4 view p2; #3 view p3; #37 view p2; #48 [+bug] view p3; #46 view p2; #23 (see review) |

Also placed: #14 and #6 in E2, #12 and #15 in E1, #23 in E1 (all in the review). Every one of the 56 appears once.

## Defect review

Evidence = merges I confirmed are on `dev` (`git branch --contains`) plus the issue's own comments. I did not read the code of each fix.

| Issue | Found | Would do |
|---|---|---|
| #21 Rolling Boulder four directions | Fixed in 646ef07 (merge boulder-dir), on dev; comment says "closes when the release ships" | Close |
| #12 ice loses speed | Fixed in bc50d9f, on dev; measured in comment | Close |
| #6 no way to end Shapeshift | Fixed in bc50d9f, on dev; tested on keys and pad | Close |
| #15 brick on water | Fixed in 00bfd3d, on dev (heat boils before baking) | Close |
| #7 held casts | Done in bd488d5 (charging costs time), on dev | Close |
| #14 health at 1 HP | Done in bd488d5 (downed, back at yard after 3 s), on dev | Close |
| #23 surf toward nearest land | Landed 1671b55, on dev; "closing then" | Close |
| #5 walking hitch | 2a020ea fixed the D3D12 night states; comment says left open for a live desktop look, exact 185 ms frame not reproduced | Keep open, ask for a recheck |
| #64 Haiku-less account | Comment: fail-loud message landed, retry removed | Close if Andrew agrees it no longer blocks 0.1.11 |
| #53 words in the log | Andrew said do it before 0.1.11; no landing seen | Keep, p1 |
| #4, #37 robe | #4's cause is in the issue (cloth stopped while hidden) and #37 is the NaN case, "possibly related" | Keep both; comment cross-link exists; not duplicates yet |
| #55, #83, #35 | All late-joiner / replay but different causes (CastHash after a Fireball; step-trigger history; announce wait) | Keep separate |
| #82, #45, #87 | Three flaky tests/probes | Keep; one theme under E8 |
| #3 | Pale band at the waterfall: no fix seen | Keep |

Nothing is closed until you approve a row.

## Questions

1. Close the seven fixed issues now (#21, #12, #6, #15, #7, #14, #23), or at the release as the comments say?
2. #64: close?
3. Eight area labels plus three priority labels: too many?
4. OK to put maps phases as 9 children, or only phase 1 to 3 now?
5. The board is named "Spell Chemistry Valley".
