Needs: core
Area: unity/Assets/Prototype/Chemistry/ (Cell, the cell store, World's cell accessors, GroundBuilder's arrays from the
land) and their tests; docs/adr/ (a D9 note if the cell layout changes)
Branch: cell-memory

# Cells small enough for the nine (world squares; before the 3 x 3 playtest)

The PC measured `test-fixtures/maps/flat-1728` (the nine's size, 729 chunks) in the Editor on 2026-11-08, against the
valley in the same run:

| | valley (81 chunks) | flat-1728 (729) |
|---|---|---|
| Start-up to playable | 4.6-5.6 s | 14.0 s (GroundBuilder "arrays from the land" 5.75 s, bind 0.7 s) |
| Managed heap | 1.16-1.18 GB | 2.15 GB (about 1.5 MB per chunk, twice the 0.7 MB estimate) |
| Editor private memory | 4.4-4.6 GB | 6.05 GB, 8.26 GB after three rebuilds (the heap never shrinks) |
| Rebuild (`ResetWorld`) | 1.0 s | 5.5-6.3 s |

A two-peer run didn't fit on the PC (free RAM fell to 2.9 GB). Every start, host and online reset pays the 6 s.

1. **Measure in core first** (no Unity): load flat-1728 under `dotnet`, split the time and bytes between allocating
   `Cell` objects, the land arrays and the rest; say where the 1.5 MB per chunk goes, field by field.
2. **Then make it small,** D9's seams: cells as arrays (struct of arrays or a packed struct per cell) behind the
   existing accessors, so callers don't change; or hold an inactive chunk as its packed rows and make `Cell`s only for
   active chunks. Pick by the numbers from 1 and say why. World hashes and every golden must stay byte-identical.
3. **Find why the heap never shrinks after a rebuild** (#145 may be the same leak): something holds the old world.
Targets: flat-1728 under 1 GB of managed heap, a rebuild under 2 s, the valley no slower than now. Layer 1 of
`docs/TESTING_LAYERS.md`; it changes `Chemistry/`, so the PC runs full EditMode under Mono before it lands.
