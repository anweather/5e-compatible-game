Brief for the Mac: profile land stamps and memory, from dev (1c682dc9 or later). Measure and report; change no game code. Any throwaway probe stays out of git. Use a batch clone or a built player: the Mac's Editor may still be stuck on its dialog. Mac numbers are compared with each other, not with the PC's.

**1. Memory, #145.** The PC's Editor reached 14 to 18 GB in long sessions, yet held at 4.7 to 4.9 GB through a full run after a restart. So what grew? In a player, then in the Editor if you can reach one: log memory, mesh, texture and `Object` counts across 50 stamp and undo cycles, across canyon and cliff with rocks, and across repeated play-mode entries and yard rebuilds. **Report:** what climbs and doesn't come back, and by how much per cycle. If nothing does, say so, and name what you couldn't run.

**2. Carry query, #147.** The frame run had no crates on the stamp. Time the per-area query at radius 32 with 0, 20 and 100 props on it, easing. **Report:** mean, p95, worst.

**3. Core.** With `dotnet`, time a tick with a radius 32 stamp easing against none, and the cost of planning a canyon stamp at rough 40. **Report:** ms per tick, allocations, and the top three costs if a profiler gives them.

**Report:** one short message of numbers and the likeliest cause. Findings are for me: don't fix. Push nothing but a results note if it's long.
