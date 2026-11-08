Needs: core
Area: the heightfield Reset and world rebuild path in unity/Assets/Prototype/Chemistry (hitch-hunt's d9a08cdb made it
cache each cell once); core tests
Branch: rebuild-threading

# Thread the world rebuild's height reset across chunks (J-079: Andrew said yes; given to u2)

hitch-hunt (d9a08cdb) cut a world rebuild from 1590 to 1050 ms in the Editor; the heightfield Reset is about 270 ms
of it (33 ms in core). Andrew approved the core's first threading (J-079), with a determinism test on the PC and the
Mac.

- Run the Reset across chunks in parallel (`Parallel.For` or a fixed set of tasks), each chunk writing only its own
  cells, so the result is identical whatever the order.
- C# 9 and netstandard2.1 (Unity's limit). No shared mutable statics. Keep the single-threaded path behind a switch for
  comparison.
- A golden: the same rebuild run single-threaded and threaded gives the same `World.CellHash` and the same heightfield
  bytes, over several seeds and the valley. The PC and the Mac each run it (say so in the report; the lead has them).
- Measure before and after in core (a bench, median of several runs) and say the numbers.

Before landing: `dotnet build core -c Release -warnaserror`, `dotnet test core` twice in full, `dotnet format`. Push the
branch only, remove this claim on the branch in the last commit. The PC compiles and measures in the Editor; the lead
lands it.
