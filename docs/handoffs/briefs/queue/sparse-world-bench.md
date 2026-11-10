Needs: core
Area: unity/Assets/Prototype/Chemistry/ (a prototype of packing one chunk, behind no switch), its tests and benches, docs/design/sparse-world.md (a new "Measured" section only)
Branch: sparse-world-bench
After: sparse-world-1 (build on it)

# The sparse World's main risk, measured before step 2 (D17 is still proposed)

Your design (`docs/design/sparse-world.md`, D17 proposed) packs an inactive chunk as its source plus its changes and
unpacks it on activation or a draw read. A reader flagged what it doesn't measure yet:
- the hash (`CellHash`, `CloseHash`, `HashSlice`) and saves reading packed cells: the decode cost a chunk, against
  today's walk over `Cell` objects;
- unpack and pack time a chunk (activation must fit a tick's budget when a character crosses a chunk edge: 5 new
  chunks at once);
- how big "changes" gets on a burnt, flooded or terraformed chunk against its source;
- the packing hold time and the drawn hint ("a few hundred ticks").

Prototype pack / unpack for one chunk (no switch, not wired in), and bench those four under dotnet on the nine's
chunks (a plain one, a village one, one after a Fireball and a flood); say whether option A (packed arrays) is needed
as a fallback, and propose the hold-time numbers. A test that a peer packing differently hashes the same (the doc
plans it for step 2: write it now against the prototype).

The core gates pass. Push the branch; the lead reads the numbers before Andrew decides on D17.
