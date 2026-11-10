# The PC's Editor set up for agents, measured (2026-11-09)

Brief: `docs/handoffs/briefs/editor-lean-trial.md`. The open Editor (pid 20380, `host-ux` on `perf-fix-3`
6eb40570, the yard on the valley) was measured as it was, then after each change, one at a time. The batch-mode
trial ran a second Editor on the `pc-compile` worktree, on `editor-lean` (dev 346aabf3 with the package change).

How each number was taken:

- **Compile:** a new one-line script in `Scripts/` written, `AssetDatabase.Refresh()` through `script eval`, timed
  until `console status` says `"compiling": false` (Tundra's and the domain reload's own times from the log). 3 runs.
- **Reload:** `EditorUtility.RequestScriptReload()`, timed the same way. 3 runs.
- **Play:** `editor.playmode start` until `ChemGrid.World` is built and 3 frames have run (polled through `script eval`,
  so about 0.3 s of the time is the poll).
- **Smoke:** `tools/probe.sh smoke`, wall time; frame times from its JSON.
- **Memory:** the Editor's private bytes and working set, and the machine's free memory (`Win32_OperatingSystem`).

The machine was short of memory throughout: 10.5 GB free at the start, 2-4 GB for most of the trial, under 1 GB
for a moment with both Editors up. Differences under about 10% are noise here.

## The table

| | Compile (s) | Reload (s) | Play (s) | Smoke (s, frame mean) | EditMode | Editor memory | Stays |
|---|---|---|---|---|---|---|---|
| **Baseline** (20 h old Editor) | 19.3, 17.4, 17.4 (Tundra 7.2-8.3, reload 5.1-5.5) | 9.1, 8.1, 9.2 (reload 4.7-4.8) | 7.3 | 23.4, 12.4 ms, pass | 14.8 min, peak working set 5.9 GB, free down to 3.3 GB (result lost: the CLI client was killed at low memory) | private 50.2 GB, working set 2.0 GB rising to 5.9 GB | — |
| **1. Preferences** (Auto Refresh off, recompile after Play, Directory Monitoring off) | 39.9*, 19.2, 19.5 | 9.8, 8.8, 10.4 | 7.8 | 23.8, 14.0 ms, pass | not run | unchanged | yes |
| **2. Layout** (Game view only, saved as "Agents") | 16.1, 18.8, 18.5 | 9.4, 10.0, 10.1 | 7.5 | 23.9, 12.6 ms, pass | not run | unchanged | yes |
| **3. Packages** (16 removed, below) | 16.3, 17.5, 17.5 (Tundra 7.1-7.4) | 10.4, 9.3, 9.4 (reload 4.6-5.2) | 6.7 | 23.1, pass | 11.5 min, 1913/1914 (1 skipped), in the batch Editor | unchanged | yes (`editor-lean`) |
| **4. Clean-up** (`Undo.ClearAll`, `UnloadUnusedAssetsImmediate`, `GC.Collect`) | — | — | — | — | — | managed 1371 → 1084 MB once; native 26.3 GB untouched | no |
| **5. Batch mode** (fresh Editor, `editor-lean`) | 14.0, 12.2, 12.4 (Tundra 7.5-8.0, reload 2.3-2.5) | 3.7, 3.8, 5.0 (reload 2.0-2.2) | 5.6 | 25.1, 15.9 ms, pass; the nine pass | 11.5 min, 1913/1914, peak working set 4.1 GB | private 2.7 GB at start, 5.3 GB peak | trial only |

\* The first compile after the change ran with 2.9 GB free; the next two are the comparable ones.

**What moved:**

1. **Preferences:** no speed change (none expected). They stay because they stop work nobody asked for: an import
   when someone focuses the Editor, and a recompile in the middle of a probe's Play session (the "recompile in Play
   floods errors" lesson). Agents already refresh explicitly; a refresh through `unity command` still picks up files
   changed by git (every compile above was one).
2. **Layout:** within noise. The Scene view, Hierarchy, Project, Inspector and Console windows are closed; the Game
   view stays because stills are taken from it (films are not: `ClipFilm` renders its own 1280 × 720). `smoke`,
   `fire --shots` (stills 1280 × 720) and `stampclip --shots` (763 frames) all passed with it. It stays: nothing lost.
3. **Packages:** removed `com.unity.collab-proxy`, `ide.rider`, `multiplayer.center`, `project-auditor`,
   `visualscripting`, and the modules `adaptiveperformance`, `cloth`, `physics2d`, `tilemap`, `umbra`,
   `unityanalytics`, `vehicles`, `video`, `vr`, `wind`, `xr`. Nothing in `Assets/` (our code, the asmdefs, the
   Asset Store packs' prefabs and scenes) references them; RobeCloth is our own cloth. The script build evaluates
   1414 items instead of 1654; compile about 0.5 s faster, Play entry about 0.6 s, within noise otherwise. A
   development player built from it (155 s, 0 errors) starts and builds the valley. It stays, on `editor-lean`.
4. **Clean-up after a job:** a yard rebuild leaves about 6 MB of native memory and no undo groups behind, so there
   is nothing to clean. Not added to the yard builder or `probe.sh`.
5. **Batch mode** (`Unity.exe -batchmode -projectPath <unity> -logFile <file>`, no `-nographics`): `unity command`
   reaches it, the yard builds (by hand: `YardAutoBuild` skips batch mode), `smoke` passes on the valley and the
   nine, films come out at 1280 × 720, EditMode passes, and a player builds. **Stills don't come out right:** they
   take the hidden Game view's size, 481 × 530 instead of 1280 × 720 (the picture itself is fine). Closing it with
   `unity close <path>` was graceful.

## The finding that matters most

The open Editor had been up 20 hours: **50 GB of private memory committed** (its peak working set 20 GB), with
Unity reporting 26 GB of native allocations and 39 GB reserved. Its objects (textures, meshes and the rest) account
for only about 1.3 GB, so this is native memory piling up somewhere, not assets, and the clean-up calls don't touch
it. The machine's commit was at 92 of 102 GB. The fresh batch Editor did the same work in 3-5 GB, and its domain
reload took 2 s against the old Editor's 5 s: most of batch mode's speed here is a fresh Editor's.

So the biggest lever on this PC is restarting the windowed Editor once its private memory passes about 20 GB (or
once a day), not any setting. Finding what holds the native 26 GB would need a Memory Profiler snapshot of a long
session.

## Putting it back

Before the trial: `kAutoRefreshMode` = 1 (Enabled), `ScriptCompilationDuringPlay` unset (0, recompile and continue),
`DirectoryMonitoring` unset (true). The layout before had Game, Scene, Hierarchy, Project, Console and Inspector;
Window > Layouts > Default brings that back, and the trial's copy of the old layout files is in the PC's scratchpad.
