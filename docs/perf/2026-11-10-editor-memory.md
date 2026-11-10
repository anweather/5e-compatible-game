# Why the PC's Editor grows (#145), measured (2026-11-10)

Brief: `docs/handoffs/briefs/editor-memory-root-cause.md`. Branch `editor-memory` off dev 2fc32bf9, with
`com.unity.memoryprofiler` 1.1.12 (only for this hunt; the lead decides whether it stays).

## How it was measured

The open Editor (pid 56196, up 10 h since its restart at 22:06, on host-ux) was not restarted. After each run the
helper forced a `GC.Collect` and logged the process's private bytes and working set, Unity's native totals
(`Profiler.GetTotalAllocatedMemoryLong` / `Reserved`), the Mono heap and the graphics driver's share. After each set it
took a census of every loaded object by type and a Memory Profiler snapshot (native objects and allocations, no
managed heap: about 40 MB each). The snapshots were diffed with `PackedMemorySnapshot` by native type, root area
and memory region; the census by object type and, for repeats, by name and hide flags.

The starting point: **private 12.8 GB, working set 6.1 GB**, native allocated 2.8 GB (4.3 GB reserved), **Mono heap
5.4 GB** (1.1 GB of it in use), graphics 0.76 GB. About 2.5 GB of the private bytes is memory Unity doesn't track
(thread stacks, DLLs, the driver). Free RAM was 1-4 GB for the whole hunt.

## Growth per kind of work

Private bytes, MB a run (after the first run of a kind, which warms caches):

| Work | Runs | Private | Native allocated | What the diff shows |
|---|---|---|---|---|
| Full EditMode run | 2 (a 3rd was stopped by the low-memory guard) | +0, +53 | -51, +2 | Texture2D -93 MB (60 unloaded), back on the next play. Mono heap +32 MB once |
| `probe.sh smoke` | 3 | +1 each | 0 | nothing |
| Yard rebuild | 4 | -70 then 0 | 0 | +23 meshes (4 MB) over four, scene parts |
| Domain reload (forced recompile) | 4 | +5 to +45 | +2 to +25 | +4 Texture2D (16 MB) over four: the **shore map**, one a reload |
| Development player build | 1 (free RAM fell to 0.86 GB, so the set stopped) | -107 | -51 | nothing |
| Stills (`mapshots`) | 3 | +260 (first play after the reloads), +18, +11 | 0 | textures the play loads |
| Film (`punchclip`) | 3 | +30, +7, 0 | +6, +2, +1 | nothing |
| Play on the nine, in and out | 5 | +4, 0, +2, -1 | +15, +3, +5 | one **Cell cracks** mesh a play; PhysX +49 MB once |
| A rule switch on the nine | 1 | +5 | 0 | nothing |
| A reload, then Play on the nine | 1 | +105 | -20 | nothing |

None of today's agent work grows the Editor by more than tens of MB a run. At these rates a day of jobs adds about
1 GB, not the 37 GB seen on 2026-11-09.

## What leaked, and the fixes

| Leak | Owner | Size | Fix | After |
|---|---|---|---|---|
| The **shore map** (`ShoreMap.FromLand`, a DontSave texture and ScriptableObject) | ours: `MapLand.Current` dropped it on a map switch, and a domain reload forgets the static that held it | 4 MB a reload or map switch; 112 held, **458 MB**, after 10 h | e1c2b4d5: `MapLand` frees it when the map changes and before a reload | 0 over two reloads |
| The **Cell cracks** mesh (DontSave) | ours: `ChemGrid.OnDestroy` freed every cell mesh but `cracks` | about 1.5 KB a play (97 held) | babbc312: freed in `OnDestroy` | 0 over a play |
| URP's hidden FallbackError, FallbackLoading and BRGPicking materials | Unity (URP 17.3) | +2 a reload, +1 a play, 1.5 KB a set (272 held) | none, too small to matter | — |
| `TestRunnerApi` ScriptableObjects | Unity (Test Framework) | 2 a reload, tiny | none | — |

The orphans already in this Editor stay until it restarts (DontSave objects nothing points to can only be found
by name, and the new code frees only what it made).

## Where the 49 GB of 2026-11-09 came from

Today's work doesn't reproduce it, but the previous session's log does show it. Unity logs `Memory consumption
went from X to Y` (its tracked total) at every asset unload. In `Editor-prev.log` (the 20 h session) it went
0.6 → 25.75 GB, almost all of it in three steps:

| Step | What ran |
|---|---|
| 0.63 → 5.67 GB | the first Play on the nine, with three `DemoRecorder` recordings |
| 6.24 → 18.00 GB | the first Play on the nine **after a domain reload** (worldlooks / the showcase sky) |
| 19.21 → 25.00 GB | the same again, after another reload |

Between the steps, each Play on the nine added about 34 MB. The steps are memory allocated once and never touched
again, which is what pages out. They aren't Unity objects (those were 1.3 GB). That session's code built all 729 of
the nine's terrains and ground arrays at every Play. Since then, #205 (terrains for the drawn tier only, pooled),
the per-chunk startup (#206, #207) and the sparse world (#204) changed that path. On today's dev a reload followed by
Play on the nine adds 105 MB, and this session's own tracked total went only 0.36 → 2.87 GB in 10 h with no step.
So **the big steps came from the old nine startup and are gone on dev**. That's inferred from the log and not proven
by rerunning the old code: a 12 GB step with 1 GB free would only page.

## What's left, and whose it is

- **The Mono heap stays at its peak: 5.4 GB, with 1.1-1.7 GB in use.** Unity's Mono (Boehm GC) never gives heap back
  to the OS, so whatever peak one job reaches (the EditMode suite; #211 fixed its whole-map statics) is held until a
  restart and pages out. This is the biggest single piece of today's 13 GB. The runtime is Unity's, the peak is ours.
  Finding the job that sets the peak needs a fresh Editor and the heap logged after each job.
- **About 2.5 GB that Unity doesn't track** (thread stacks, native DLLs, the graphics driver). No growth was seen in it.
- **Asset import workers**: two idle `AssetImportWorker` processes hold about 2 GB private each beside the Editor. They're
  Unity's. Preferences > Asset Pipeline sets how many stay on standby.
- **Physics 866 MB and GfxDevice 978 MB** at rest in edit mode: the yard's colliders and render targets. Steady, not growth.

## What to do next

1. Land the two fixes. They're small, but the shore map was the one steady leak: half a gigabyte a day.
2. To confirm the big steps are gone: on a fresh Editor, log `Memory consumption` (or private bytes) across a day of
   nine work with reloads. A step over 1 GB is a regression. `grep "Memory consumption went" Editor.log` is enough.
3. To lower the Mono peak: on a fresh Editor, log `Profiler.GetMonoHeapSizeLong()` after each EditMode fixture and
   probe, to find the one that sets it.

Measured with the helper scripts in the session's scratchpad (`mem.sh`, `snapsum.sh`, `diff.sh`). The snapshots were
deleted after the diff; the numbers above are from them.
