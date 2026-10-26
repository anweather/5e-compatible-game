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
and `SpellCaster.Cast` (the spell must be in the rules).

| Name | What it does | Checks |
|---|---|---|
| `smoke` | Checks every thing of `world/things.json` spawned on its own cell; walks toward the pond for up to 3 s, casts each Elements spell once (Fire Bolt on the oil, Create Water, Ray of Frost, Shocking Grasp, Grease, Gust of Wind, Thunderwave, Levitate), then Shape Earth | `things_spawned` (as many as the data lists, each on its cell with its kind), `walked` (over 1 m), `bar_has_<spell>` for each, `cast_<spell>` ("You cast …" in the World log; not Levitate, which logs nothing there), `fire_started`, `levitate_lifted` (a prop's "floats up" line), `reactions_logged` (World log lines other than casts), `mold_earth_shaped` (its cells' heights changed) |
| `fire` | Fire Bolt on the meadow west of the old yard, camera high above | `fire_spread_t10` (more than 1 burning cell at t+10 s); reports burning cells at t+0, t+10 and t+20 s and ash cells at t+0 and t+20 s |
| `earth` | Earthen Trench west from the pond's bank, wait 8 s, then Wall of Stone across it | `trench_dug`, `trench_has_water_t8` (a trench cell has water), `wall_raised` (every wall cell is a raised wall) |
| `perf` | Fixed camera over the meadow toward the pond for `--seconds` (default 10, 1 to 300) | Only `no_console_errors`; the point is `frames` |
| `online` | This Editor hosts; Multiplayer Play Mode's Player 2 joins on 127.0.0.1. On one schedule of sim ticks: Player 2's Thunderwave beside crate `4,5#2` and the host's wizard; each casts Fire Bolt (meadow; oil spill), Earthen Trench (from the pond's bank; beside it) and Wall of Stone; the host's wizard falls 10 m. Both sample at the same tick, 20 s after the fall | `virtual_player_joined`, `virtual_player_reported`, `checksums_equal` (every 30-tick checksum both recorded, at least 3), `sampled_same_world`, `client_saw_no_desync`, `client_terrain_matches` (cells' heights and the terrain drawn under the earth spells, within 1 cm, and dug), `client_water_matches` (wet cells, water, cells drawn wet), `client_walls_match`, `thing_ids_match` (the same stable ids and kinds on both, all of `world/things.json`), `prop_moved_matches` (crate `4,5#2`, found by its stable id, thrown over 1 m, client within 5 cm), `player_knockback_matches` (the host's wizard, same), `client_no_console_errors`, `reset_refused_online` (reset on the host is refused and both players stay), `tag_passes_on_touch` (the host starts tag as it and steps up to the virtual player, who is then it) |
| `menu` | The Esc menu with a controller: Start pressed on a virtual gamepad the run adds, then the menu's own `Navigate` / `Submit` / `Back` (what the d-pad, A and B call): down to Mods, A, A on the first mod twice, B; Credits and back; Join by code (letters stepped, A, B twice); B closes; Start opens and closes. `--shots` saves the main page with the highlight, Mods, Credits and Join by code | `start_opens_menu`, `wizard_paused`, `main_page_items`, `mods_page_opens`, `mods_page_lists_mods` (5 mods and Back), `a_toggles_mod`, `a_toggles_mod_back`, `b_backs_out_of_mods` (highlight back on Mods), `credits_page_opens`, `code_page_opens`, `code_page_b_goes_back_a_letter`, `code_page_b_leaves`, `b_closes_menu`, `wizard_resumed`, `start_closes_menu` |
| `radial` | The spell radial with a controller, on a virtual gamepad the run adds: hold LB with the right stick at Storm, then at Thunderwave in its arc, release; tap LB; hold LB at Earth and press B; d-pad right; RB. `--shots` saves the radial open on Storm's spells | `radial_ready`, `storm_has_thunderwave`, `lb_opens_radial`, `arc_opens_on_storm`, `release_picks_thunderwave`, `look_back_on` (camera look back as it was), `tap_swaps_back` (to Fire Bolt), `b_cancels`, `dpad_steps_element`, `rb_steps_spell` |
| `explore` | The wizard goes to grass 3 chunks or more from the yard (outside the old chemistry area) and casts Fire Bolt there; back home after. `--shots` saves the far fire | `whole_valley`, `far_meadow_found`, `chunks_wake_around_player` (25 at most awake), `yard_sleeps`, `far_fire_spreads`, `yard_unchanged_while_asleep`, `yard_wakes_on_return` |
| `collision` | The invisible blockers against what is drawn: the nearest valley tree north of the yard, then a Wall of Stone | `tree_found`, `tree_trunk_blocks`, `tree_beside_trunk_clear` (a ray 1 m beside the trunk passes), `wall_raised`, `wall_top_matches_drawing` (within 25 cm) |
| `wildfire` | A lot of fire, for frame times: the Wildfire mod on, Fire Bolts at five spots in the meadow north of the yard, 20 s of burning. `--shots` saves it 6 s in and at the end | `fire_spread` (over 150 squares), `frames_within_budget` (p95 within 33 ms). Metrics: `burning_peak`, `burn_mean_ms`, `burn_p95_ms`, and per frame the grid's step, cell drawing, surfaces, fire lights, burnt trees and ground repaint (`burn_*_ms`, `worst_*_ms`) |
| `charge` | Holding Cast, on a virtual gamepad through the wizard's own controls: Raise Earth held 1.1 s on the right trigger, then Lower Earth tapped. `--shots` saves both | `held_raise_earth_three_steps`, `tapped_lower_earth_one_step` |
| `hits` | Spells catching a wizard, as if cast by someone a few metres off: Fire Bolt on its square, Ray of Frost through it, Shocking Grasp on it | `fire_bolt_throws_wizard` (over 0.5 m), `ray_of_frost_slows_wizard`, `shocking_grasp_jolts_wizard` |
| `ball` | The walled court east of the yard and its ball: the wizard runs at the ball from 6 m away (Kicker) | `court_built`, `ball_spawned`, `run_kicks_ball` (over 2 m), `ball_stays_in_court` |
| `edge` | The wizard runs at each end of the land (north, east, south, west; where the valley's terrains stop) from 15 m inside it, for 10 s. `--shots` saves each edge | `edge_built`, `<side>_edge_holds` (still over the land), `<side>_on_ground` (standing on the terrain, not fallen) |
| `frost` | Ray of Frost from the lake's shore (the dry square outside the yard nearest its middle with lake water two squares south) out over the water, then Sleet Storm on open water further out. `--shots` saves both | `lake_shore_found`, `water_froze` (every wet square in the beam), `ice_stands_on_lake` (a ray down hits the Ice collider at the lake's surface), `sleet_storm_freezes_a_circle` (40 squares or more) |
| `sky` | The clock and the weather switched as a mod switches them (a probe mod over `sim.json` at the next tick), over the meadow toward the pond: noon, then 22:00, then a storm at 13:00 with lightning every half second, then fog. `--shots` saves noon, night, the first bolt, the storm and the fog | `sky_built`, `noon_is_bright`, `clock_line`, `night_is_dark` (sun out, moon up, ambient low), `storm_in_force` (with wind), `rain_falls`, `lightning_strikes` (logged by the core, a bolt drawn), `storm_is_dim`, `fog_closes_in` |

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
