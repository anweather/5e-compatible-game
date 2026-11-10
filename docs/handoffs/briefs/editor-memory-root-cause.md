Needs: unity
Area: unity/Packages/manifest.json (com.unity.memoryprofiler, on the branch only), unity/Assets/Prototype/Scripts/ and Editor/ (fixes to what leaks), docs/perf/ (a new file)
Branch: editor-memory

# Why the Editor's memory grows (#145), found, not restarted away

Andrew, 2026-11-10: restarting Unity to free memory "feels like a hack and a workaround for some root cause". What's
known: on 2026-11-09 the Editor (up 20 h) held 49 GB private with only 5.6 GB in RAM (the rest paged out: allocated
and never touched again, a leak's signature), 26 GB of it native, Unity objects 1.3 GB; restarted, it was back to
12.1 GB within about 8 h of jobs. The Mono heap reaches 4.6 GB after a full EditMode run and never shrinks (#211
fixed the whole-map test statics). A batch-mode Editor stayed flat at 5.6 GB through builds, compiles and a yard
rebuild (#145); the Mac found only small leaks (~19 MB a domain reload, 27-186 MB a yard rebuild, a mesh a Play entry).

## What to do

1. Add `com.unity.memoryprofiler` on this branch. Restart nothing: start from the Editor as it is, but note its
   private, working set and native totals, then ask Andrew (through the lead) for a fresh Editor if a clean baseline
   is needed.
2. Take a snapshot, then repeat each kind of agent work 3-5 times with a snapshot after each set: a full EditMode run;
   `tools/probe.sh smoke`; a yard rebuild; a domain reload (a refresh after a script change); a development player
   build (`PrototypeBuild.Build(..., BuildOptions.Development)`); a probe that takes stills and one that records a film;
   entering and leaving Play on the nine. Also note the process's private and native totals after each (the totals
   are what pages out).
3. Diff the snapshots by native type and by object type: what grows each time and never comes back (meshes,
   textures, render textures, native arrays, graphics buffers, terrain data, the profiler's own, the Recorder's,
   asset import workers'). Name the owner of each in our code (builders, probes, tests, captures) or in Unity.
4. Fix what's ours and contained (`Destroy`/`DestroyImmediate` in teardown, `Dispose` of native buffers, release of
   render textures after captures), each its own commit, and measure the growth again. For what's Unity's own, say
   what it is and whether a setting avoids it.

## Done

`docs/perf/2026-11-10-editor-memory.md`: the growth per kind of work, before and after each fix, and what's left;
an issue per leak that's left (`Discovery-Cost:` line). EditMode passes, smoke passes on the valley and the nine. The
branch pushed; the lead decides whether the package stays (it's only needed for this hunt).

Don't kill the Editor, start a second one, hand-edit `.unity`/`.prefab` YAML, or touch `unity/Assets/_Recovery/`.
