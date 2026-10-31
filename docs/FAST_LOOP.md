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
| `online` | This Editor hosts; Multiplayer Play Mode's Player 2 joins on 127.0.0.1. On one schedule of sim ticks: Player 2's Thunderwave beside crate `4,5#2` and the host's wizard; each casts Fire Bolt (meadow; oil spill), Earthen Trench (from the pond's bank; beside it) and Wall of Stone; the host's wizard falls 10 m. Both sample at the same tick, 20 s after the fall | `virtual_player_joined`, `virtual_player_reported`, `checksums_equal` (every 30-tick checksum both recorded, at least 3), `sampled_same_world`, `client_saw_no_desync`, `client_terrain_matches` (cells' heights and the terrain drawn under the earth spells, within 1 cm, and dug), `client_water_matches` (wet cells, water, cells drawn wet), `client_walls_match`, `thing_ids_match` (the same stable ids and kinds on both, all of `world/things.json`), `prop_moved_matches` (crate `4,5#2`, found by its stable id, thrown over 1 m, client within 5 cm), `player_knockback_matches` (the host's wizard, same), `client_no_console_errors`, `reset_online` (the host's R resets the world for everyone: the countdown heard for the reset's tick, not reloaded, both players stay), `reset_things_back` (the same stable ids after, a crate broken just before back too), `reset_wizard_at_start`, `reset_clock_restarts`, `reset_checksums_equal` (every 30-tick checksum after the reset equal on both, at least 3), `tag_passes_on_touch` (the host starts tag as it and steps up to the virtual player, who is then it); the virtual player sees the host's Shapeshift form and its end (`shape_*` checks) |
| `menu` | The Esc menu with a controller: Start pressed on a virtual gamepad the run adds, then the menu's own `Navigate` / `Submit` / `Back` (what the d-pad, A and B call): down to Mods, A, A on the first mod twice, B; Credits and back; Join by code (letters stepped, A, B twice); B closes; Start opens and closes. `--shots` saves the main page with the highlight, Mods, Credits and Join by code | `start_opens_menu`, `wizard_paused`, `main_page_items`, `mods_page_opens`, `mods_page_lists_mods` (5 mods and Back), `a_toggles_mod`, `a_toggles_mod_back`, `b_backs_out_of_mods` (highlight back on Mods), `credits_page_opens`, `code_page_opens`, `code_page_b_goes_back_a_letter`, `code_page_b_leaves`, `b_closes_menu`, `wizard_resumed`, `start_closes_menu` |
| `radial` | The spell radial with a controller, on a virtual gamepad the run adds: hold LB with the right stick at Storm, then at Thunderwave in its arc, release; tap LB; hold LB at Earth and press B; d-pad right; RB. `--shots` saves the radial open on Storm's spells | `radial_ready`, `storm_has_thunderwave`, `lb_opens_radial`, `arc_opens_on_storm`, `release_picks_thunderwave`, `look_back_on` (camera look back as it was), `tap_swaps_back` (to Fire Bolt), `b_cancels`, `dpad_steps_element`, `rb_steps_spell` |
| `explore` | The wizard goes to grass more than the keep distance (3 chunks) from the yard (outside the old chemistry area) and casts Fire Bolt there; the yard, once settled, deactivates; back home after, the fire it left lingers and burns on. `--shots` saves the far fire | `whole_valley`, `far_meadow_found`, `chunks_activate_around_player` (besides lingering chunks, at most the 7×7 the keep distance spans), `yard_inactive`, `far_fire_spreads`, `yard_unchanged_while_inactive`, `yard_activates_on_return`, `far_fire_lingers` (its chunk stays active, out of reach, while it burns), `far_fire_burns_on` |
| `collision` | The invisible blockers against what is drawn: the nearest valley tree north of the yard, then a Wall of Stone | `tree_found`, `tree_trunk_blocks`, `tree_beside_trunk_clear` (a ray 1 m beside the trunk passes), `wall_raised`, `wall_top_matches_drawing` (within 25 cm) |
| `wildfire` | A lot of fire, for frame times: the Wildfire mod on, Fire Bolts at five spots in the meadow north of the yard, 20 s of burning. `--shots` saves it 6 s in and at the end | `fire_spread` (over 150 squares), `frames_within_budget` (p95 within 33 ms). Metrics: `burning_peak`, `burn_mean_ms`, `burn_p95_ms`, and per frame the grid's step, cell drawing, surfaces, fire lights, burnt trees and ground repaint (`burn_*_ms`, `worst_*_ms`) |
| `charge` | Holding Cast, on a virtual gamepad through the wizard's own controls: Raise Earth held 1.1 s on the right trigger, then Lower Earth tapped. `--shots` saves both | `held_raise_earth_three_steps`, `tapped_lower_earth_one_step` |
| `hits` | Spells catching a wizard, as if cast by someone a few metres off: Fire Bolt on its square, Ray of Frost through it, Shocking Grasp on it | `fire_bolt_throws_wizard` (over 0.5 m), `ray_of_frost_slows_wizard`, `shocking_grasp_jolts_wizard` |
| `ball` | The walled court east of the yard and its ball: the wizard runs at the ball from 6 m away (Kicker) | `court_built`, `ball_spawned`, `run_kicks_ball` (over 2 m), `ball_stays_in_court` |
| `court` | The court's dirt floor, gate and score (BallCourt, CourtGate): the wizard walks from 9 m west of the gate onto the court, then the ball is sent into the east goal and later left outside the south rail. `--shots` saves the gate shut, open and shut again, and the goal | `court_built`, `floor_cell_dirt`, `floor_painted_dirt` (Earth layer over 0.5), `floor_no_grass`, `gate_shut_at_start`, `gate_opens` (over 0.9), `walks_through_gate` (3 m in), `gate_shuts`, `ball_found`, `goal_scored` (one at the east end), `goal_announced`, `board_shows_score`, `ball_back_after_goal`, `ball_out`, `ball_back_after_out` (within 1.5 m of the spot) |
| `river` | The river from the falls into the lake and the bridge over it: the river measured across at world z 170, 95 and 30; the wizard dropped into its deepest middle; then walked over the bridge from its west landing to its east; then digging from the river's edge east of the yard with Lower Earth, charged. `--shots` saves the swim, the wizard on the bridge and the dug square | `river_wide` (5 squares across or more at each place), `river_deep` (over 1.8 m of water over the bed in the middle), `swims_in_river`, `bridge_built`, `bridge_over_river` (the deck's middle over river water, over 1.2 m above it), `bridge_dry` (`SurfaceEffects.Swimming` stays false on the way), `bridge_walked_over` (up on the deck, not under it), `bridge_crossed` (within 1.5 m of the east landing), `river_found` (near world (120, 30)), `river_full`, `dug_square_fills_from_river`, `river_stays_full` |
| `blast` | Fireball cast from the bar on the meadow east of the yard | `crater_middle` (two steps down), `crater_ring` (one), `crater_drawn` (the terrain 0.6 m lower or more), `edge_burns` (burning, or burnt to dirt) |
| `slant` | Earthen Trench aimed about 20 degrees south of east, through the wizard's own controls on a virtual gamepad | `trench_dug`, `trench_slants` (more columns than rows, two rows or more), `squares_share_sides` |
| `topple` | Move Earth on the valley tree nearest the yard, then Fire Bolt on it; then Move Earth on the cabin west of the yard | `tree_toppled`, `fallen_tree_drawn`, `no_blocker_left`, `fallen_tree_burns`, `cabin_walls_cave_in`, `cabin_falls` |
| `meteor` | Meteor cast from the bar on the meadow east of the yard; shots as it falls, lands and after | `meteor_falls` (over 50 m up), `meteor_glows`, `not_landed_yet`, `crater` (three steps down), `ring_of_fire` (8+ squares past the crater burning or burnt) |
| `falls` | The waterfall at the head of the river: the wizard goes to the pond at its foot; shots from the pond and the cliff top | `falls_built`, `cliff_clad` (20 rocks or more), `pond_is_river` (full), `river_runs_on`, `splashes`, `roars` |
| `swim` | The wizard drops into the deepest part of the lake | `deep_water_found`, `afloat`, `at_the_surface`, `stays_afloat`, `swims_along`, `jump_kicks_up`, `back_afloat` |
| `critters` | Fireflies on a clear night, butterflies at a clear noon (AmbientCritters, from Punchy) | `critters_built`, `fireflies_at_night`, `butterflies_by_day` |
| `look` | The wizard's outfit (WizardOutfit): dressed from the saved seed, then three new looks; portraits from the front and the side (`--shots`) | `dressed`, `parts_hung` (six or more, the hat on the head), `same_seed_same_look`, `seeds_differ`, `new_look`; the head stays inside the headwear (`head_inside_headwear`: about 2000 points over 100 wizards in each, 2026-10-30) |
| `cloth` | The wizard's robe as cloth (RobeCloth): walking and running filmed alongside, the run again from ahead, a dodge roll, sitting on the nearest bench and lying on the nearest lounger (each from the side and the front), swimming in the lake. Set `WizardOutfit.RigidRobe` first for the old shell | `robe_is_cloth`, `cloth_steps` (over 300), `cloth_stays_whole` (no particle over 1.5 m from the hips). Metrics: `rigid`, `cloth_steps`, `farthest_from_hips_m` |
| `swell` | The wizard dropped into the sea 30 m out from the shore, floating for ten seconds | `sea_built`, `swims_at_sea`, `rides_the_swell` (rises and falls over half the swell's height, in step with it) |
| `wet` | The meadow north of the yard at noon, clear, then heavy rain, then clear: a dry and a wet shot from the same place | `valley_built`, `dry_in_the_sun`, `soaks_in_the_rain` (wetness over 0.8 after 14 s), `dries_after` (still drying 20 s after the rain stops) |
| `night` | Hollowmere's square from above at 16:00, dusk (18:30) and night (21:30), clear, the wizard out on open ground | `village_found`, `bright_by_day`, `lamps_off_by_day` (the inn's and shop's door lights and the well lamp off, windows dark), `dim_at_dusk`, `lamps_on_at_dusk` (all lit, windows glowing), `dark_at_night`, `stars_out_at_night`, `fireflies_by_the_hour`, `dark_hint` (the log says how to see in the dark) |
| `villagers` | Hollowmere at 15:00, clear, the camera over the square: the folk come out and go about their day, a Fire Bolt sets the nearest timber or log house alight, then dusk (18:06) | `village_found`, `folk_come_out` (half or more), `folk_walk_about`, `folk_sit` (on a bench within 40 s), `folk_flee_fire` (nobody within 6 m of the burning wall after 7 s), `folk_go_in_at_night` (all in within 45 s, still in 4 s later) |
| `sleep` | The wizard goes beyond the keep distance from the yard and comes back | `yard_has_props`, `awake_while_active`, `yard_inactive`, `props_sleep_with_chunk` (asleep and kinematic), `none_asleep_in_active_chunks`, `held_while_asleep`, `yard_activates_on_return`, `props_wake_with_chunk`, `awake_prop_moves` |
| `fauna` | The valley's animals (ValleyFauna, from Punchy): 10 s for them to come out, a deer put 8 m north of the wizard, then a little parade in the meadow. `--shots` saves the parade and the valley | `fauna_built`, `animals_out` (10 or more), `activities` (two kinds or more after 20 s), `deer_bolts` (over 15 m away after 3 s) |
| `snow` | Heavy snow at noon, the wizard walking through the yard, a Fire Bolt into the snow. `--shots` saves a bank, the yard and the footprints | `snow_lies`, `banks_drift`, `bank_raises_ground`, `footprints`, `fire_melts_snow`; the paint wait is capped at 60 s |
| `blink` | Misty Step aimed through the crosshair all round the wizard, in the yard and the meadow, out to twice its range, then six real blinks, then six casts through the caster two frames apart (out and back onto the mist it left; the fourth straight after a Thunderwave), then one held to full charge past a tap's range | `aims_land` (80% or more land; the detail tallies the rest and how many hit the exact spot), `blinks_arrive`, `spam_casts_arrive` (the detail names what stood in the way if not), `held_blink_reaches_further`. Metrics: `spam_casts`, `spam_arrived`, `blink_tap_m`, `blink_held_m` |
| `charges` | Every spell stronger held: a few spells cast through the caster at a tap and at full charge, compared | `every_spell_chargeable`, and `<name>_stronger_held` (held half as much again or more) for `create_water` (water units), `grease` (greased squares), `ray_of_frost_beam_m`, `thunderwave_shove_m` (the crate by yard (15, 6)), `levitate_rise_m` (props by yard (17, 6)), `misty_step_reach`, `mage_hand_reach`, `mage_hand_lift`. Metrics: each of those tapped and held, and `cells_<spell>_tap` / `_held` for every spell |
| `shapeshift` | Shapeshift on a deer put 8 m in front of the wizard in the meadow (aimed by a ray from its eyes), a walk, Fire Bolt tried, a second deer 6 m away, Cast to change back, then shifted again and a heavy blow. `--shots` saves the wizard as a deer | `aims_at_deer`, `aim_at_nothing_fizzles`, `shifted`, `model_hidden`, `deer_drawn`, `deer_speeds`, `walks_with_gait`, `casting_refused`, `deer_stays` (under 5 m, not fleeing), `changed_back`, `blow_changes_back`; the form ends on Z and on RB (any cast button), on keys and pad |
| `downed` | On a virtual controller: Circle punches, Square doesn't, Circle cancels a cast without punching. Then the wizard, carrying a thing, takes more than its health: it falls, drops the thing and loses its controls, and after `sim.json` `downed.seconds` it's back at the yard, full. `--shots` saves down and back | `seconds_down` |
| `sprint` | A virtual controller walks the wizard north, clicks the left stick, lets go and pushes again, then holds the left trigger | `click_runs` (over 1.3 times the walk), `letting_go_stops`, `trigger_free` (the left trigger no longer sprints) |
| `roll` | A virtual controller walks the wizard north and pulls the left trigger (the dodge roll, `DodgeRoll` + `RollPose`), then pulls it again 0.55 s in, inside the wait before the next. `--shots` saves the wizard mid-roll and after | `roll_parts`, `rolls`, `quick` (3 m in under 0.6 s), `tumbles_once` (hips to head turn 300 to 420 degrees), `ends_upright` (within 25 degrees), `cooldown_holds` (the second pull doesn't roll). Metrics: `reached_3m_s`, `turned_deg`, `end_tilt_deg` |
| `dither` | The wizard put 2.5 m behind the terrain tree nearest it, the camera cut 7 m before the tree and 6 m up looking at it, then cut round to the open side (`OccluderFade`). `--shots` saves both views | `dither_parts`, `tree_occludes`, `dither_on` (fade over 0.9, the global `_OccluderDither` cut over 0.5), `silhouette_on`, `clear_view`, `dither_off`, `silhouette_off`, `materials_restored` (no renderer left on DitherLit). Metrics: `hidden_fade`, `hidden_cut`, `hidden_swapped`, `clear_fade` |
| `edge` | The wizard runs at each end of the land (north, east, south, west; where the valley's terrains stop) from 15 m inside it, for 10 s. `--shots` saves each edge | `edge_built`, `<side>_edge_holds` (still over the land), `<side>_on_ground` (standing on the terrain, not fallen) |
| `frost` | Ray of Frost from the lake's shore (the dry square outside the yard nearest its middle with lake water two squares south) out over the water, then Sleet Storm on open water further out. `--shots` saves the beam, the ice and the sleet | `lake_shore_found`, `frost_beam_along_line` (one FrostBeam from the wizard to the line's end, drawn, no mesh or collider), `no_ice_sphere_or_projectile` (no IceLance, nothing flying), `water_froze` (every wet square in the beam), `ice_stands_on_lake` (a ray down hits the Ice collider at the lake's surface), `sleet_storm_freezes_a_circle` (40 squares or more) |
| `sleet` | Sleet Storm cast from the bar on the meadow east of the yard (SleetStorm), after a Fire Bolt lit its middle and puddles were laid on every other dry square round the fire. `--shots` saves the storm raging and the ice after | `something_to_freeze`, `storm_shown` (cloud, sleet and pellets), `storm_over_target` (the burst's width each way), `cloud_overhead`, `no_projectile` (nor the old ice impact), `not_landed_yet`, `sleet_falls` (over 500 streaks, moving down), `sleet_lands_on_squares` (90% of those near the ground over them), `puddles_froze` (every wet square), `fire_out`, `storm_fades`, `storm_ends` |
| `gestures` | The casting gestures (CastPose, CastGestures), each cast from the bar with the camera beside the wizard: Gust of Wind, Raise Earth, Fireball, Meteor; the hands followed frame by frame in the wizard's frame (height over its feet, reach toward the target). `--shots` saves two moments of each (`<spell>-a` the wind-up, `<spell>-b` the action) | `<spell>_gesture` (Shove, Raise, Throw, CallDown), `gust_both_hands_push` (over 0.4 m forward), `raise_hands_low_then_high` (below 0.95 m, then over the head), `fireball_hand_back_then_forward` (behind the shoulder, then over 0.3 m forward), `meteor_hands_overhead_then_down` (over the head, then below the shoulders), `meteor_at_the_pull` (the meteor appears at the gesture's release) |
| `timeweather` | The Time & Weather spells cast from the bar on a clear 9:00: Hasten Time (then again to settle it), Control Weather four times, then Fog Cloud in the yard with a Misty Step aimed through it, a Fire Bolt into its middle and a Gust of Wind east through it. `--shots` saves the hastened sky, the rain, the snow and the fog | `weather_fx`, `clock_races` (90 game minutes or more in 6 s), `second_cast_settles`, `weather_rain`, `rain_drawn` (WeatherFx), `weather_snow`, `snow_drawn`, `weather_storm`, `weather_clear`, `fog_lies` (two thirds of its squares thick), `fog_drawn` (FogBank), `fog_hides` (no line of sight through it), `blink_refused`, `fire_burns_fog`, `gust_blows_fog` |
| `nature` | A Fireball burns the meadow round the yard's tree at (5, 8), Plant Growth (charge 2) regrows it and the stump's sapling grows into a tree; then Spike Growth on the south meadow, the wizard walking through it, and a Fire Bolt into it. `--shots` saves the burnt patch, the regrown one, the grown tree, the thorns, and the thorns burning and burnt | `fire_burns`, `tree_burns_to_stump`, `ash_cleared` (green shoots), `grass_regrows` (grass counted before and after), `stump_sprouts`, `sapling_grows_into_tree` (and drawn), `thorns_grow` (60 squares or more, clumps drawn), `thorns_slow` (under 0.75 of the open-grass speed, metres a second compared), `thorns_hurt` (health lost), `thorns_burn` (alight, then ash) |
| `tower` | The climbing tower south-west of the yard (ValleyTower, TowerCourse, TowerPlatform) and the double jump (DoubleJump): a moving platform watched for 1 s, a phasing one for a whole cycle, the wizard put on the lowest collapsing one, a single jump then a jump and a second in the air on open ground by the start pad, then onto the start pad, off it, and onto the summit by the flag. `--shots` saves the tower from the yard, the collapse, the view from the top and the fireworks | `tower_built`, `has_static_platforms`, `has_moving_platforms`, `has_collapsing_platforms`, `has_phasing_platforms` (3 or more each), `tower_tall` (summit 30 to 45 m up), `moving_platform_moves` (over 0.1 m in 1 s), `phasing_platform_toggles` (solid and ghost both seen), `collapsing_platform_falls` (within 1.5 s), `collapsing_platform_returns` (within 6 s), `double_jump_part`, `second_jump_taken`, `double_jump_higher` (over 0.6 m above a single), `clock_starts_off_pad`, `finish_fireworks`, `finish_logged` ("You reached the top in m:ss!"). Metrics: platforms of each kind, `summit_height_m`, `collapse_fell_s`, `collapse_back_s`, `single_jump_m`, `double_jump_m`, `best_time_s` |
| `defense` | The defense field north of the yard (DefenseFieldBuilder, DefenseField, World.Defense.cs): the wizard stands on the start plate, watches the first raiders come through the gate, casts a Fireball a square ahead of the leader, raises a Wall of Stone across the path 8 squares short of the flag and waits for a raider to get through. Checks: the field is built with a one-row dirt path 50 to 70 m long, the plate starts the waves, a raider spawns within 10 s and walks over 2 squares nearer the flag in 3 s, raiders are drawn, the Fireball beats at least one, the wall stands and the next raider passes its column off the wall's rows (the way costs more), a raider at the flag costs a life (banner and HUD line show it), no console errors. Shots: field, mid-wave, wall, end |
| `sky` | The clock and the weather switched as a mod switches them (a probe mod over `sim.json` at the next tick), over the meadow toward the pond: noon, sunset at 18:18, then 22:00, then a storm at 13:00 with lightning every half second, then a meteor shower at 23:00, then snow at noon over puddles laid in the yard, then fog. `--shots` saves noon, night, the first bolt, the storm, the meteors and the fog, the snow, and the sky (CloudSky) at noon, sunset, night, in the storm and in the snow | `sky_built`, `noon_is_bright`, `clock_line`, `night_is_dark` (sun out, moon up, ambient low), `storm_in_force` (with wind), `rain_falls`, `lightning_strikes` (logged by the core, a bolt drawn), `storm_is_dim`, `storm_clouds` (over 70% cover), `meteor_shower`, `meteors_fall`, `snow_falls`, `puddles_freeze`, `fog_closes_in` |
| `looks` | The material effects staged in the yard at noon, one picture each (`--shots`): rust, sand and glass, a glow ball breaking, ice shards, mud, hot metal, Acid Splash and its stain, oil on the pond and fire across it, the shock flash, a window pane before, shattering and after. Checks: lightning fuses sand, a ball survives a drop, breaks on a hard throw and leaves nothing behind, mud slows the wizard, hot metal burns it, acid eats a cover, fire runs over oil on water, the charge runs through the pond, a crate nudged at a pane leaves it whole and one thrown hard breaks it, no console errors. Metrics: `crate_hit_ns_11ms`, the 11 m/s crate's hit in N s against WindowPanes.HeavyImpulse (120) |
| `spelllooks` | The combo table's new spells staged in the yard at noon, one picture or more each (`--shots`; ProbeSpellLooks.cs): Forge on sand, mud and metal; Falling Star's mark, fall, impact and embers; Whirlwind carrying sand, water, fire, smoke and snow; a dust cloud flashing in fire; Lava Pool; Rain Cloud; Sand Blast; Light at night; the pick's swing; cracked glass, ice and a frozen raider. Checks: what each does to the cells, no console errors |
| `castlooks` | The new casts' looks alone, spelllooks' second half, for working on them (`--shots`): Geyser, Wave, Frost, Rolling Boulder, vines, stepping stones and the ramp, Lightning to a gate, storm and snow clouds, the scour, Fireball debris, Lava running, Steam rust. Checks: what each does to the cells, no console errors |
| `aimlooks` | The aim marker of each kind of cast, one picture each (`--shots`; ProbeAimLooks): lanes at an angle, a ring growing as Cast is held, the outlines of what rises, Spike Growth's marching dashes, on flat ground, water and a slope, and the lock-on on a deer. Checks: each spell's kind, one mesh draws it, the ring grows with the charge, no console errors |
| `flare` | Raw pixels of 960 views round the wizard, 30-60 degrees down, in both headwear, standing and walking (UX pass 4, #16). Checks: `no_bad_pixels` (no NaN), `nothing_far_too_bright`, `culprit_bad` names the source when one shows |
| `housecam` | Walks into four village houses from every side (#20). Checks: `arm_holds_at_a_house` (the camera arm keeps its 4 m against a wall in front; walls behind still pull it in); also walks the snake form (body radius 0.12 m) into the walls: the camera check mustn't start inside one |
| `flames` | The fire's look (#19): cells burning on flat ground, close up. Checks: `flames_drawn`, `flame_bank`, `every_cell_drawn`, `tongues_off_centre`, `sizes_vary`, `timing_varies`, `no_two_cells_alike`, `no_fire_effects` (no per-cell particle systems left); the three fire types each keep their look (`three_types`), drawn from the original flipbooks by fire level (`flipbooks`) (2026-10-31) |
| `movers` | The wave and the boulder aimed off the grid's four ways on flat dirt (#11, #21; `--shots`). Checks: each goes along the aim (within 8 degrees), a crate rides the wave along it, the wave leaves water, no console errors |
| `fogcost` | What fog weather costs: frame times over the same view in clear weather and under fog, with the gas cells and FogBank drawn (`--shots`: clear, foggy, low in it). Checks: the fog lies, its thin veil isn't drawn cell by cell, no console errors. Metrics: frame ms clear and foggy (2026-10-31: 14.6 and 22.9 in the Editor) |
| `golf` | Spell golf's hole played through the caster (GolfCourse, World.Golf): tee, flag, hole and ball drawn, the HUD's stroke line, Gust of Wind to the hole; then the meadow coil beside it: its cells laid, no tree on its pond, a bolt into the pond opens the gate (`--shots`) |
| `feel` | What a hand on the controls should feel, on a virtual pad and keyboard: ice and Frost's glaze keep the speed and slide, snow and a bank slow the wizard to the rules' share, F cracks then breaks a pane, F and Circle knock a crate and the ball, Plant Growth charged on burnt ground, Wall of Stone held to a ramp, with timings |
| `combo` | Casting by elements on a virtual gamepad, with the HUD on: water then earth loaded in the window, the cast meter filling, the cast landing at the crosshair and joining the grimoire; walking at the rules' factor under the meter; B cancels with an item in hand and doesn't throw it; d-pad right cycles, up casts the selected spell, down opens the grimoire and Back closes it to play; the stamina line under Health and Magic drains (`stamina_drains`). Shots of each, the orbs in the hands, the slam and the whole HUD (`--shots`), no console errors |

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


**Player look (2026-10-31):** a player build takes `-player-look` to stage the look checks (markers, fire) in the built game.