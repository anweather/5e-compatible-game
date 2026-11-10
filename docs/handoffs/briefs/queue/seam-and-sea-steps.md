Needs: core
Area: unity/Assets/Prototype/Chemistry/ (ChunkGen.Land.cs, ChunkGen.cs, SeaMask.cs, GeneratedMap.cs; the generator's seam with a pinned map), its tests and goldens
Branch: seam-and-sea-steps

# Steps where generated land meets the pinned valley (#193) and the sea (#176)

Two steps in the generated land, both in the generator (core only; no view change).

1. **#193:** on the nine, the drawn ground steps up to 3.1 m across the valley's east seam (rows 1418-1431, the
   valley's own steep hillside) and 1.6 m on the north seam at x 99-104. The generated side should meet the pinned
   valley's edge heights: blend the generated land toward the pinned edge over a band (say 8-16 cells) so the
   first generated cell is within a height step (0.5 m) of its pinned neighbour, everywhere along both seams. The
   pinned valley itself must not change (its cells are byte-identical).
2. **#176:** the sea floor drops ~175 units in one cell where a coast's sea ramp meets an open-sea world square
   (seen on the 12 x 10; `docs/design/world-12x10/`). The shores paper (`tools/world-paper/`, Shores panel,
   `docs/design/world-squares.md` "Shores") shelved it gradually (30 m -> 0.16 m): do the same in the core.

## Check

A test per step that walks the seam (or the coast-to-open-sea edge) and asserts no cell-to-cell step over the
limit; the nine's chunk golden and the 12 x 10 plan golden update in the same commit (say from what to what);
the valley map stays byte-identical. Generator version bump if the land changes (old bakes are remade). Every
fixture needs its `[Category]`; no `Does.Contain(object)`.

## Done

`dotnet build core -c Release -warnaserror`, `dotnet test core`, `dotnet format core --verify-no-changes` and the
duplicate gate pass. Push the branch; the PC compiles it and the lead lands it. Report the largest step before and
after on each seam and on the 12 x 10's coasts.
