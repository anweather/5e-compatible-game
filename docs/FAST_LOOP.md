# Fast loop: the probe

A quick, repeatable way for an agent (or you) to check the game in the open
Unity Editor: run a named, scripted scenario in Play mode and get back one
JSON result with pass/fail checks and the numbers that matter. One run takes
15 to 30 seconds and leaves the Editor back in Edit mode.

## Usage

The Editor must be open on `Assets/Prototype/Scenes/ChemistryYard.unity`,
with no compile errors.

```sh
tools/probe.sh list                         # the scenarios and what each checks
tools/probe.sh smoke                        # run one, print its JSON
tools/probe.sh fire --shots /some/folder    # also save screenshots there
tools/probe.sh perf --seconds 20            # hold the camera for 20 s
tools/probe.sh online                       # host + one Multiplayer Play Mode virtual player
tools/probe.sh online --keep-players true   # leave Player 2 running for faster reruns
```

`tools/probe.sh` exits 0 when every check passed, 1 when one failed or the
run was aborted, 2 when the run couldn't start (unknown name, already playing,
compiling, wrong scene).

It wraps three project CLI commands (`unity/Assets/Prototype/Scripts/Editor/ProbeCommands.cs`),
which you can also call directly:

```sh
unity command probe list --result-only
unity command probe run --name smoke [--shots <folder>] [--seconds N] --result-only
unity command probe result --wait true --timeout 300 --result-only
```

Why two calls, not one: entering Play mode reloads the scripting domain (the
project keeps domain reload on), which restarts the Editor's command server
and drops any open request. So `probe run` saves the request and enters Play
mode, and returns at once. After the reload, the scenario runs, its result is
written to `unity/Temp/Probe/result.json`, and the Editor leaves Play mode.
`probe result --wait true` waits for that file to say `done` or `aborted`. It
can fail to connect for a few seconds during the reload; the script retries.

## The result

```json
{
  "scenario": "fire", "state": "done", "pass": true,
  "checks": [ { "name": "fire_spread_t10", "pass": true, "detail": "48 burning cells at t+10 s" }, ... ],
  "metrics": { "burning_t10": 48, "burning_t20": 58, ... },
  "frames": { "source": "editor_play_mode", "count": 535, "seconds": 20.1, "mean_ms": 37.6, "p95_ms": 106.9, "worst_ms": 428.0 },
  "chem": { "tick_start": 6, "tick_end": 126, "ticks": 120, "burning_cells": 58, "log_lines": 4 },
  "console": { "error_count": 0, "warning_count": 0, "errors": [] },
  "screenshots": [ ".../fire-t10.png", ".../fire-t20.png" ],
  "id": "74ecff05", "wall_seconds": 26.8, "unity": "6000.3.27f1"
}
```

- `pass` is true only when every check passed. Every scenario adds
  `no_console_errors`: any `Debug.LogError`, exception or assert during the
  scenario fails the run (the first 20 are listed with their first stack line).
- `frames` are measured from the end of a 1 s settle to the end of the
  scenario. **They are Editor Play mode frame times**, with the Editor's own
  drawing in them: compare runs with each other, not with a player build (for
  that, use the `-fps-probe` player flag, `FpsProbe.cs`).
- `chem` is the chemistry world's tick at the start and end of the
  measured part, the burning cells at the end, and `World.LogAppended`.
- `state` is `aborted` (and `pass` false) when Play mode was stopped early or
  the scenario ran past its `max_seconds`, with a `reason`. An exception in a
  scenario stops its coroutine, so it shows up as a timeout plus the error.

## The scenarios

All run in the Chemistry Yard, in yard coordinates borrowed from
`DemoDirector` and `ValleyShowcase`, and cast through `SpellCaster.Select`
and `SpellCaster.Cast` (the spell must be on the bar).

| Name | What it does | Checks |
|---|---|---|
| `smoke` | Checks every thing of `world/things.json` spawned on its own cell; walks toward the pond for up to 3 s, casts each Elements spell once (Fire Bolt on the oil, Create Water, Ray of Frost, Shocking Grasp, Grease, Gust of Wind, Thunderwave, Levitate), then Mold Earth | `things_spawned` (as many as the data lists, each on its cell with its kind), `walked` (over 1 m), `bar_has_<spell>` for each, `cast_<spell>` ("You cast …" in the World log; not Levitate, which logs nothing there), `fire_started`, `levitate_lifted` (a prop's "floats up" line), `reactions_logged` (World log lines other than casts), `mold_earth_shaped` (its cells' heights changed) |
| `fire` | Fire Bolt on the meadow west of the old yard, camera high above | `fire_spread_t10` (more than 1 burning cell at t+10 s); reports burning cells at t+0, t+10 and t+20 s and ash cells at t+0 and t+20 s |
| `earth` | Earthen Trench west from the pond's bank, wait 8 s, then Wall of Stone across it | `trench_dug`, `trench_has_water_t8` (a trench cell has water), `wall_raised` (every wall cell is a raised wall) |
| `perf` | Fixed camera over the meadow toward the pond for `--seconds` (default 10, 1 to 300) | Only `no_console_errors`; the point is `frames` |
| `online` | This Editor hosts; Multiplayer Play Mode's Player 2 joins on 127.0.0.1. On one schedule of sim ticks: Player 2's Thunderwave beside crate `1,1#2` and the host's wizard; each casts Fire Bolt (meadow; oil spill), Earthen Trench (from the pond's bank; beside it) and Wall of Stone; the host's wizard falls 10 m. Both sample at the same tick, 20 s after the fall | `virtual_player_joined`, `virtual_player_reported`, `checksums_equal` (every 30-tick checksum both recorded, at least 3), `sampled_same_world`, `client_saw_no_desync`, `client_terrain_matches` (cells' heights and the terrain drawn under the earth spells, within 1 cm, and dug), `client_water_matches` (wet cells, water, cells drawn wet), `client_walls_match`, `thing_ids_match` (the same stable ids and kinds on both, all of `world/things.json`), `prop_moved_matches` (crate `1,1#2`, found by its stable id, thrown over 1 m, client within 5 cm), `player_knockback_matches` (the host's wizard, same), `client_no_console_errors` |

## The online scenario

`online` needs a second Editor, so `probe run` does more for it:

- If Player 2 (Window > Multiplayer > Multiplayer Play Mode) isn't
  running, `probe run` starts it (`VirtualPlayers`, by reflection: MPPM
  2.0 keeps that API internal) and enters Play mode once it is up, about
  15 to 20 s later; MPPM takes Player 2 into Play mode too. After the run
  it stops Player 2 again through MPPM, unless `--keep-players true`.
  Never kill a virtual player's process. A run with Player 2 already up
  takes about 45 s; from cold about 55 s.
- The two Editors talk through files in the main project's
  `unity/Temp/Probe/online/`: `host.json` (hosting, then the start tick)
  and `client.json` (joined, then Player 2's report). Player 2's half,
  `OnlineProbeClient` (`ProbeOnline.cs`), starts itself in any virtual
  player's Play mode and waits for a fresh `host.json`.
- Both sides record from `NetChem.TickApplied`, so they write down the
  very same ticks; the host compares.
- Screenshots (`--shots`) are the host's only.

## Adding a scenario

Subclass `ProbeScenario` (`unity/Assets/Prototype/Scripts/ProbeScenario.cs`), which is a
`ShowcaseScript`, so `Place`, `Cut`, `Fly`, `Wait` and `Yard` work as in the
showcases. Put the steps in `Run()`, and record with `Check(name, pass,
detail)`, `Metric(name, value)` and `Shot(label)`. Then add an entry to
`ProbeScenarios.All` (`ProbeScenarios.cs`) with its name, a description of
what it checks, and its `max_seconds`. Recompile; `probe list` shows it.

## Not covered yet

- These run in the Editor only, not in a player build, and not as Unity
  PlayMode tests: those would need `Scripts/` in its own assembly (an
  `.asmdef`, see `docs/QUALITY.md` "Recommendations") so a test assembly can
  reference it.
- Screenshots are of the Game view at its current size.
- `online` runs two Editors on one machine, not a Mac host with a Windows
  client, and over 127.0.0.1, not Relay. It has one virtual player; the
  4-player and late-join checks in `docs/MULTIPLAYER.md` were by hand.
