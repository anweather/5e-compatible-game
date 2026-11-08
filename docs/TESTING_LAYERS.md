# Testing in layers (the Swiss cheese model)

Andrew, 2026-11-08: "We need a Swiss cheese model. Not every layer needs to test everything." Each layer catches
some faults and lets others through; the next layer's holes are in different places. A test runs at the layer
where it's cheapest to catch what it catches, and once, not at every layer.

Why this exists: on 2026-11-08 a PC Unity job spent about half its time testing. A full EditMode run (about 14
minutes, past the CLI's 600 s timeout) reran the whole Chemistry suite under Mono, which `dotnet test core` had
just run in 3 minutes, and the landing gates then ran it a third time. Long probe lists ran for changes that couldn't
reach them.

| Layer | Who | Runs | Doesn't run |
|---|---|---|---|
| 1. **Author** | the worker writing the change (u2, the Mac, a PC job) | New or changed tests for what it changed; `dotnet build core -c Release -warnaserror` and the tests nearest the change while working | The whole suite on every edit |
| 2. **Unity check** | a `unity-task` on the PC, or the Mac | Compile with 0 errors; the Unity-side tests (`Scripts/Editor/Tests`, `Scripts/Playtest/Editor`) and the touched fixtures; `smoke` and the probes the change can reach; a clip only when asked. **The full EditMode suite (Chemistry under Mono) only when `Chemistry/` code changed** | Core tests under Mono for view-only changes; unrelated probes |
| 3. **Landing gates** | `land.sh` | Core build, format, duplicate code, **the whole core suite once** (.NET), the workers and chat tests | Unity, probes |
| 4. **Sweep** | the idle Unity machine, at least once a day and after a big batch of landings | Full EditMode under Mono, every probe, a two-peer run, the hitch budget, on dev as it stands: what the layers above skip and what only appears when branches meet | Nothing skipped; it's the net |
| 5. **Release** | the release steps (`docs/RELEASING.md`) | `smoke`, `online`, the release's own probes, install from the draft, the PC joining a Mac host | Anything a sweep already covered on that commit |

The holes, on purpose:

- **Mono versus .NET rounding** (seen 2026-11-08 on the seeded heights): layer 2 runs Mono only for core changes;
  the sweep runs it for everything.
- **A probe a change "couldn't reach" but did:** the sweep runs every probe on dev.
- **Two branches that pass alone and break together:** the sweep, and the next Unity check that merges dev.
- **u2's core-only landings have no Unity check:** the next Unity check or sweep compiles them.

A failure in the sweep goes to whoever landed the change that broke it, found by bisecting the day's landings.
