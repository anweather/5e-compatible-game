# Core review 0: lockstep determinism, 2026-11-09 to 11-11

Status: done, no lockstep bug found. Four regression tests added, all passing.

The scope is the core code that landed on dev between 2026-11-09 and 2026-11-11
(`git log --since=2026-11-09 -- unity/Assets/Prototype/Chemistry`):
- the sparse World (D17: packed chunks, `World.Pack.cs`, `ChunkCells`, the per-chunk arrays);
- the east-west wrap (sim-across-the-seam);
- generate-switch (`GeneratedSource`, chunks made on demand);
- the far ring's `KnownRing`;
- rule-switch-by-chunk (`World.Fit.cs`);
- the late-join core.

## What was looked for

Anything that can make two peers given the same commands disagree (`docs/ARCHITECTURE.md`,
`Chemistry/CLAUDE.md`):
- iteration over an unordered collection that changes state;
- floating point in state;
- the clock, the environment or threads reaching state;
- a seeded draw whose order depends on chunk-load order or on which peer made a chunk first;
- state that depends on how a chunk is held (packed or unpacked, made on demand or baked);
- anything off the World's thread.

The method: grep the new lines for those shapes, read each hit in place, and for each place where peers
legitimately differ, run the same inputs on two peers that differ only there and compare `CellHash` every tick.

## What was checked, and why it holds

| Area | Verdict | Why |
|---|---|---|
| `World.chunks`, a `Dictionary<long, Chunk>`, iterated in about ten places (`World.Pack`, `World.Fit`, `World.Dragon`, `World.StillWater`, `SetLand`, `LandFromSource`) | Holds | Chunks are added once, in the World's constructor, in a fixed order, and never removed, so the order is the same on every peer. Each of these loops also writes only its own chunk, or sums, so order wouldn't matter anyway. The tick's passes use `SortedChunks`. |
| `World.Fit` (`HashSet<int> held`, `fitLiquids` keys) | Holds | Membership and removal only; nothing reads an order. |
| `ChunkStreaming` / `ChunkPlanner` (`lingering`, `cutOff`) | Holds | Every plan is sorted before it's sent, and the plan becomes a shared command (`ChunkSwitch`), so only the host's counts. |
| `GeneratedSource`: a cache of 768 chunks, a worker (`Task.Run`), `Parallel.For` in `Ahead`, eviction by use | Holds, if `ChunkGen.Make` is pure | The cache decides *when* a chunk is made, never what it is. `Make` writes no shared state once the generator is built. The rivers' memo tables, the spawns, the place ids and the pinned block's rim are all filled in its constructor. **New test:** every chunk made on 8 threads in a scrambled order equals the same chunk made alone. |
| `ChunkGen.Land`'s `blockKnown` | Holds | Filled once, after a `Parallel.For` has finished, in the constructor; read-only after. |
| `KnownRing`, `CellHeightfield` (threads, floats) | Out of scope | View-only: the World never reads them. |
| `Stopwatch`, `Interlocked` in `World.Pack.cs` and `GeneratedSource` | Holds | Timing counters only (`UnpackMs`, `MakeMs`); nothing in state reads them. |
| Floats | Holds | None in World state. The generator reads map inputs as doubles once and rounds them to fixed point (`BiomeMath`, Q16/Q24). |
| Off the World's thread | Holds | `OwnThread` refuses a pack or unpack from another thread (`PackTests.AReadOfAPackedChunkOffTheWorldsThreadThrowsAndLeavesItPacked`). |
| Packing and the still-water pass | Holds | A pack drops a chunk's water snapshot (`SnapValid = false`), which resets its `UnchangedFor`. Only inactive chunks pack, and the pass skips a chunk only when the full pass would do nothing there, so skipping differently can't change cells. **New test** with thick liquids, below. |

## Tests added (`Chemistry/Tests/CoreReviewTests.cs`, one in `GeneratedSourceTests.cs`)

The setup is the same in each: one peer never packs, and the other packs and unpacks its inactive chunks at beats of
its own, and packs on its own (`packTicks` 0). Both hash the same every tick.

- `AThickLiquidAndARenumberingHashTheSameWhateverEachPeerPacked`: honey and magma (thick), chunks let go and taken
  back, and a rule switch at tick 90 that swaps the liquids' order. That renumbers them, so inactive chunks are
  remapped by id only when they activate. 240 ticks.
- `TheSeamRunsTheSameWhateverEachPeerPacked`: a world that wraps. Fire lit on both sides of the seam, water poured
  on it, and the chunks beside it let go and taken back. 260 ticks.
- `TheDragonsResetIsTheSameWhateverEachPeerPacked`: the dragon summoned at tick 40 over the valley-sized meadow,
  with an inactive chunk packed on one peer when the start was kept. 700 ticks.
- `GeneratedSourceTests.ChunksMadeAtOnceOnManyThreadsAreTheSameAsOneAtATime`: `ChunkGen.Make`'s purity, on threads.

These complete what was already covered: `PackTests.APeerPackingDifferentlyHashesTheSame` (a height switch),
`ALateJoinerReplaysToTheHostsStateWhateverEachPeerPacked`, and `GeneratedSourceTests.TwoPeersAndLateJoinersAgree`
(prefetching against not, a replay and a load).

## Fragile, not broken (worth a guard when next touched)

1. **`World.chunks` iteration is safe only because chunks are made once, in order.** If chunks are ever created
   lazily (say, the 12 x 10 making `Chunk` objects as they're first wanted), a `foreach` over `chunks.Values` that
   writes order-dependent state would follow creation order, which differs by peer. New loops should use
   `SortedChunks`, or a comment at the field should say why it's safe.
2. **`ChunkGen.Make` must stay pure.** Anything it caches while making a chunk (a memo filled lazily, as `Drained`
   and `HubLevel` are during construction) would race on `GeneratedSource`'s threads and could depend on which chunk
   came first. The new test catches a race only if it shows on the two-squares map.
3. **`GeneratedSource`'s eviction is untested.** The test maps have fewer chunks (162) than it holds (768), so no
   chunk is ever evicted and remade in a test. Purity makes a remade chunk equal to the first, but nothing exercises
   it. A test would need the hold size to be settable.

## Related, not a lockstep bug

From `save-land` (docs/design/player-saves.md, "The land"): a baked map sets its land after the lays, while a generated
map makes its land as the source does. The two disagree on the land under water a lay put down. Every peer plays the
same source (a save or a join refuses another), so peers never disagree. But the same map played baked and played
generated runs differently there, which is a question for Andrew.
