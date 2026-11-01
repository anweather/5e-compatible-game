# Brief: the play log (frame rate while Andrew plays)

For DesktopUnity, 2026-10-31. Andrew wants to play normally and get frame-rate logs afterwards, with no
cost to the game. Branch `play-log` off dev. Reuse what exists: `FrameStats` (frame times), `FpsProbe.ReadTiming`
(CPU main, render and GPU from FrameTimingManager), `PerfMarkers` (marker breakdown, development builds only).

## Build

1. **Always on, in every player build.** Each frame only adds the frame time to running totals (no allocation,
   no string). Once a second, write one line to `play-log-<date>-<time>.csv` beside the player log: time, fps,
   worst frame ms, frames over 33 ms and 50 ms, CPU main / render / GPU ms when available, weather, active
   chunks, players, the mods on (by title), and what happened that second (cast, dragon, summons).
   Write on a background thread or buffered, never a synchronous disk write in a frame.
2. **Hitches.** A frame over 100 ms adds its own line: what was happening; in a development build, its top
   markers from PerfMarkers.
3. **"That felt bad" key (F8).** Adds a marked line, and a small screenshot from the game's own camera
   (as FpsProbe does), so Andrew's feeling lines up with the numbers. A toast says "Noted".
4. **Off switch** in the pause menu (on by default); keep the last 10 files, delete older.
5. **A summary at quit:** one line with the session's average, 1% low and worst seconds, plus the file's
   path, so a worker can read it fast.
6. Say in `docs/FAST_LOOP.md` where the files are on Windows and macOS and how to read one.

## Prove it

- Cost: run the perf scenario (storm) with the log on and off; the difference must be in the noise
  (report both numbers). No garbage per frame (a probe line, as `burn_makes_no_garbage` does).
- A short play session on the PC, then a chart of its log on Andrew's board (fps over time with the
  marked moments), as the example of what he'll get.

## Rules of the road

- The 0.1.10 RC and the online run on the lost-cast fix come first when the PM asks.
- If it's done and green before the RC is built, it can ride in 0.1.10; otherwise 0.1.11.
