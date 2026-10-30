# Brief: gameplay sessions on alpha-0.1.8 (host, after the release candidate)

Andrew (2026-10-30): a few gameplay sessions on this release, wandering with
goals and recording issues and observations; measure what the sessions cost,
though they make no commits; and see whether we find fewer issues than before.

**Account: Pro** (Andrew). The Pro coordinator runs it on the host, as a sub-agent with a fresh context (cheaper than its own long one; feature-costs reading, 2026-10-30), and records the cost under ~/.claude-pro with `CLAUDE_CONFIG_DIR=$HOME/.claude-pro`.

Run this on the PC once the 0.1.8 Windows release candidate is built, with no
Editor open (the player renders on its own). Read playtest/DESIGN.md (the
bot, `playtest run`), playtest/BACKLOG.md and playtest/known-issues.json
first. Lean budget.

## 0. Measure the session

Note the start: `date +%s%3N` → START (milliseconds). Everything you do from
here to step 4 is the session's cost.

## 1. Three bot runs, 8 minutes each, on the release candidate

`playtest run --seed <N> --seconds 480`, seeds 801, 802 and 803, against the
0.1.8 player (`playtest check` names the player it would use; it must be the
release candidate's build, not an older one). Before the first run, check the
bot's cast goals can reach this release's spells (`PlaytestGoals`, the spell
list it draws from): if the new combo spells (Forge, Falling Star, Geyser,
Wave, Frost, Rolling Boulder, Plant Growth, Wall of Stone, Lightning, Rain
Cloud, Sand Blast, Whirlwind) are missing and adding them is a list change,
add them; if it needs more than that, say so and run with what it has.

## 2. Goals to watch for (what this release is about)

The bot picks its own goals; read its runs against these, and look at its
screenshots for each that came up:
- Casting by elements: each combination lands where aimed; the meter and
  cancel feel right.
- Chains: Forge on mud (brick), Fireball near dust (flash), Lightning into a
  puddle, Frost on water then walking across, Geyser under a prop, Wave over
  a fire, a boulder through a window, vines up a wall, stepping stones.
- Glass: village windows crack in a web, then shatter; ice too.
- The wizard: hat and cowl, the face, no clipping under the robe; the staff
  (no flare).
- Frame time and errors: spikes, console errors, anything stuck.

## 3. Write it up

- **Issues:** each new one as a finding in playtest/findings/ (the bot's
  format), checked against dev first (it may be fixed) and against
  known-issues.json.
- **Observations:** what isn't a bug but matters (a cast that's hard to aim,
  a look that doesn't read, something that feels off): a short list in
  playtest/sessions/0.1.8.md, with screenshot paths.
- **Fewer than before?** Count this release's issues per hour of play against
  the earlier runs (playtest/findings/F-0001 to F-0005, the 0.1.5 runs) and
  Andrew's playtest of 2026-10-27 (docs/PLAYTESTS.md): a small table, with
  the caveat that the bot's goals changed.

## 4. Record the cost (no commit needed for this part)

```
CLAUDE_CONFIG_DIR=$HOME/.claude-pro COMMIT_COST_ROLE=Host COMMIT_COST_NAME=playtest-0.1.8 COMMIT_COST_SINCE=<START> \
  COMMIT_COST_PROJECT=<this session's transcript folder name> \
  node tools/workers/commit-cost.mjs --record "playtest 0.1.8: 3 bot runs + triage" docs/telemetry/sessions.jsonl
```

`tools/telemetry/feature-costs.mjs` shows it under "Work with no commit",
beside the median feature.

## Deliver

Commit the findings, playtest/sessions/0.1.8.md and the sessions.jsonl line
on a branch `playtest-0.1.8` and push it (not merged). Report to the PM in 8
lines or fewer: issues (new, known, fixed already), the top observations,
issues per hour against before, and the recorded cost.
