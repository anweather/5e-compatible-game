Needs: unity
Area: the PC's Editor preferences and window layout; unity/ProjectSettings/EditorSettings.asset; unity/Packages/manifest.json (and its lock); tools/probe.sh only if batch mode needs a flag; docs/FAST_LOOP.md is World's lane (put proposed text in the report)
Branch: editor-lean (only for ProjectSettings and package changes)

# The Editor set up for agents, measured (Andrew, 2026-11-09)

Nobody uses the Editor's own features on the PC: agents drive it through `unity command`, `tools/probe.sh` and
builder menus. Andrew: turn off what agents don't need, measure on the next tasks, keep what helps.

What's known (#145, `docs/FAST_LOOP.md`): Play mode is entered without a domain reload (J-023). A windowed Editor
grew to 14-18 GB in long sessions; a batch-mode Editor stayed flat at 5.6 GB through 7 builds, about 7 compiles and
a yard rebuild. On 2026-11-09 a modal "Scene(s) Have Been Modified" dialog on an Untitled scene stalled the Editor
for over an hour at the start of a full EditMode run.

## Measure first (the baseline, today's setup)

On the open Editor, with the yard built on the valley, record:
- a script compile after touching one file in `Scripts/`, and a domain reload (seconds, 3 runs);
- entering Play and `tools/probe.sh smoke` on the valley (seconds);
- a full EditMode run (minutes);
- the Editor's private memory and working set at the start and after each of the above, and the machine's free memory.

## Then try, one at a time, measuring the same after each

1. **Preferences:** Auto Refresh off (agents refresh explicitly); "Script Changes While Playing" = recompile after
   finished playing; Directory Monitoring off. Check that `unity command` refresh still picks up changes from git.
2. **Window layout:** close what agents don't use (Scene view, Inspector, Hierarchy, Project, Profiler, Package
   Manager, Animation, anything else open), gizmos off. Keep the Game view only if probes' stills, films or clips need
   it: find out by running `smoke` and one probe that takes a still and one that records a film. Save the layout as
   "Agents".
3. **Packages:** remove those nothing references, after a grep of `Assets/` and the asmdefs: candidates
   `com.unity.collab-proxy`, `com.unity.visualscripting`, `com.unity.ide.rider`, `com.unity.multiplayer.center`,
   `com.unity.project-auditor`, and built-in modules such as vr, xr, cloth, vehicles, video, tilemap, physics2d,
   unityanalytics, adaptiveperformance, umbra, wind. Keep anything a player build or a probe uses; build a development
   player once at the end to prove it.
4. **After each job:** `EditorUtility.UnloadUnusedAssetsImmediate`, `GC.Collect`, `Undo.ClearAll` (yard rebuilds
   pile up undo). Measure what a yard rebuild leaves behind with and without it. If it helps, add it to the end of
   the yard builder and of `tools/probe.sh` runs.
5. **Batch mode, a trial only:** start a second Editor in `-batchmode` (no `-nographics`) on a throwaway worktree
   of dev while the main Editor is closed or idle (RAM: one extra Editor at most, close it after), and see whether
   `unity command` reaches it, and whether `smoke`, a still and a film work there. Don't switch the main Editor to
   it; report what works and what doesn't.

## Don't

Kill the Unity Editor (close only your own second Editor, by pid); hand-edit `.unity`/`.prefab` YAML; touch
`unity/Assets/_Recovery/`; change the core or the game's behaviour. Preferences and layout are the PC's own: keep
a note of every value before you change it, so each can be put back.

## Done

A table in `docs/perf/2026-11-09-editor-lean.md`: each change, before and after (compile, reload, Play entry, smoke,
EditMode, memory), and whether it stays. Changes that help stay on (preferences and layout on the PC, ProjectSettings
and packages on the `editor-lean` branch, pushed; the lead lands it). EditMode passes and smoke passes on the
valley and the nine at the end. Proposed FAST_LOOP text in the report.
