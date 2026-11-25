Needs: unity (Punchy's Editor)
Area: `C:/Users/Andrew/Development/Punchy-chemistry` (Assets/Game/Scripts/ChemistryBridge/ only); in our repo a short note in docs/design/core-package/open-world.md ("Punchy as built")
Branch: Punchy `chemistry-fill-jobs` off `chemistry-open-world` (9fd5229c); our notes on `fill-hitch-notes` off `origin/dev`

# The host's freeze on a teleport into new ground (D20), Andrew 2026-11-13

Today the host samples a new chunk's ground (`IOpenGround.Sample`, over `TerrainSource`) on the main thread, at
6-16 ms a chunk and up to 16 chunks a tick, so a teleport into ground never filled stalls the host's frame by
100-250 ms for a few ticks. Read "Punchy as built" in `docs/design/core-package/open-world.md` first.

Only the host samples, and a fill reaches every peer as a command on a tick, so *when* a sample finishes never
affects sync. Andrew chose, in order:

1. **Sample off the main thread.** Find out first why a chunk costs 6-16 ms: per-cell managed calls into the sampler,
   allocations, or the work itself. Then run the sampling as Burst jobs over whole chunks (Punchy's terrain sampling
   is Burst code already; `TerrainSource`, the `TerrainEdits` SharedStatic). The planner asks for a chunk; the bridge
   schedules its job and stamps the fill on the first tick after the job is done. Keep the fills in a stable order
   (key order) when several finish together. A chunk still waiting is "not filled yet", as now (casts there are
   refused).
2. **Start early on a teleport.** When a teleport begins (or wherever Punchy knows the destination), ask for the
   chunks round the destination at once, so they're often filled on arrival. Find Punchy's teleport and `-place`
   paths. If the destination isn't known before the player moves, say so.

Not now: a per-frame time budget for anything still on the main thread (Andrew: "3 and 4 first"). If the jobs leave
the main thread with more than ~2 ms of fill work a frame, report the number.

## Must not

- Touch our game's `unity/` or the core package; merge or push our `dev`; change rules.
- Change how ground is classified: a chunk sampled by a job must equal the main-thread sample cell for cell (test
  it on a few hundred chunks, including the cave layer).

## Done when

- Punchy compiles with 0 errors and 0 new warnings; its play-mode tests as before.
- The host's worst frame on a teleport into new ground, before → after (aim under 16.7 ms), over 5 teleports. Also
  the time from arriving to the first cast taken (today 0.5 s).
- The far-apart two-peer run (`-chem-script`, as in the last job) still agrees: `desync=False`, 3 minutes at least,
  with a teleport each way.
- Push `chemistry-fill-jobs` to Skeleman/Punchy (J-092) once these pass.

## Report

Five lines: the Punchy commit (pushed?) and ours; why a chunk cost 6-16 ms; the worst frame and the time to the first
cast, before → after; the two-peer result; any blocker.

After: the lead lands `fill-hitch-notes`; then the Mac and PC over Relay, far apart.
