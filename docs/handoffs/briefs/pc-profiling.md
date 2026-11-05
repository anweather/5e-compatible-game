Brief for the PC: find the slowest frames in the game and say how to fix them. From `dev` (b65be232 or later). Measure and recommend; change no game code, push nothing but a results note. Throwaway timers stay out of git. Use a Windows player at the 1080p target for numbers, with `tools/probe.sh perf` as the baseline, and the Unity Profiler for where the time goes. Mac numbers (#145, #151) are rough: don't lean on them.

**1. Where the worst frames are.** Run these and rank them by worst frame and p95, with how often each spike happens:
- Walking the valley, chunk edges and a village included, and a long run standing still.
- A heavy cast, several casts at once, fire and water reacting.
- A stamp landing, easing and undone, at radius 32 and radius 8 (#151: the Mac saw 112 to 121 ms on the frame a stamp lands; you saw 115 ms once on an undo). Is it real here, and what is it?
- The dragon's flight and chunk reset.
- A late join on the host and on the joiner.
- Start-up's first minute, once the menu is gone.

**2. Where the time goes.** For the top five, the Profiler's split: main thread or render thread, GPU if it reads (it read 0 before: say if you can fix that), and the top three markers, with GC allocations. Say whether each is steady cost or a one-off hitch.

**3. How to fix it.** For each of the top five: the likely fix, what it would save in ms, how big the job is, and whether it risks lockstep (D4) or a golden. Rank by saving for the work. Say plainly where you're guessing, and which spike isn't worth fixing.

**4. Memory, #145.** Log the Editor's memory through this session, a reading per part, with what you did in between: the Mac couldn't run a windowed Editor, Game view or test runs, so the growth may live there. Restart if it passes 12 GB and say so.

**Report:** one message: the ranked table, then the fixes. Findings are for me.
