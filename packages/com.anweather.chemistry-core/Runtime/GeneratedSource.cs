// A generated map's generator as the World's source (D17's generate switch, docs/design/sparse-world.md, "The generate
// switch"): a map too big to bake whole ("bake": false, the 12 x 10) has its chunks made by ChunkGen when the World first
// reads them, the same on every peer (a chunk is a pure function of the map's inputs, GeneratedMap.InputHash), and held a
// while for the next reads. A packed chunk with no changes is "untouched": nothing but its coordinates.
//
// Making a chunk takes milliseconds (15 ms on a 2-core VM, against a whole tick's budget of 6), so the World never walks
// the untouched chunks of a world on this source: its hash covers only the cells that differ from the source (World.Chunks.cs,
// "the changes hash"), its land comes from here (World.LandFromSource), and the chunks a character is coming to are made
// ahead on a worker (Ahead, from ChunkPlanner and the view's drawn hint), so unpacking them at a chunk edge finds them made.
// Which chunks are made when, and on which thread, changes time only: never shared state.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>
    /// A source that makes a chunk's cells when they're first asked for (a generated map's, GeneratedSource), dear to read
    /// whole: the World never reads its untouched chunks, and hashes a world on it by its changes. It holds the land too.
    /// </summary>
    public interface IGeneratedSource : IChunkSource
    {
        /// <summary>The land under a cell as the map makes it, in water units (land.json's, had it been baked).</summary>
        int LandAt(int x, int y);

        /// <summary>Asks for these chunks to be made ahead, on a worker, nearest first: they're about to be read. Changes time only.</summary>
        void Ahead(IEnumerable<(int cx, int cy)> chunks);

        /// <summary>Makes these chunks now, on every core, those not made yet: they're about to be read. Changes time only.</summary>
        void MakeNow(IEnumerable<(int cx, int cy)> chunks);
    }

    /// <summary>A generated map's chunks made on demand by its ChunkGen, under the rules the map is made with, and held a while.</summary>
    public sealed class GeneratedSource : IGeneratedSource
    {
        /// <summary>The chunks held made at most (~24 KB each: their cells and land): the reach and drawn tier of several characters.</summary>
        public const int Holds = 768;

        readonly ChunkGen gen;
        readonly LegendCells legend;
        readonly int cw, ch;
        // The chunks made or being made, by key, each with the use it was last read at (for letting the oldest go).
        readonly Dictionary<long, Held> held = new Dictionary<long, Held>();
        // Chunks asked for ahead and not yet made, in the order asked.
        readonly Queue<long> asked = new Queue<long>();
        readonly HashSet<long> queued = new HashSet<long>();
        readonly object gate = new object();
        bool working;
        long uses;
        // The chunk the last read was in: one read a cell, from the World's thread.
        Held last;
        long made, madeAhead, madeOnRead, waits;
        long makeTicks, waitTicks;

        sealed class Held
        {
            public long Key;
            public GeneratedChunk Chunk;
            public Task<GeneratedChunk> Making;
            public long Used;
        }

        /// <summary>The map's size in cells.</summary>
        public int W { get; }
        public int H { get; }

        /// <summary>"generated:" and the map's inputs (GeneratedMap.InputHash): a save of one map is never loaded over another.</summary>
        public string Version { get; }

        /// <summary>Where the map's spawn is (map.json's).</summary>
        public (int x, int y) Start { get; }

        /// <summary>Chunks made since the source was, ahead on the worker of them, and how often a read waited for one being made there.</summary>
        public long Made => Interlocked.Read(ref made);
        public long MadeAhead => Interlocked.Read(ref madeAhead);
        public long Waits => Interlocked.Read(ref waits);
        /// <summary>Chunks made again after being let go (the hold's eviction): the same chunk as the first time, made twice.</summary>
        public long MadeAgain
        {
            get
            {
                lock (gate) return madeAgain;
            }
        }

        /// <summary>Chunks a read made one at a time on the World's thread, not made ahead or at once (MakeNow): each a stall of milliseconds.</summary>
        public long MadeOnRead => Interlocked.Read(ref madeOnRead);

        /// <summary>The time spent making chunks on any thread, and the World's thread spent making or waiting for them, in milliseconds.</summary>
        public double MakeMs => Interlocked.Read(ref makeTicks) * 1000.0 / Stopwatch.Frequency;
        public double WaitMs => Interlocked.Read(ref waitTicks) * 1000.0 / Stopwatch.Frequency;

        /// <summary>The chunks held made now.</summary>
        public int HeldCount
        {
            get
            {
                lock (gate) return held.Count;
            }
        }

        /// <summary>A generated map's source, under the rules it's played with.</summary>
        public GeneratedSource(GeneratedMap map, Rules rules) : this(map, rules, Holds)
        {
        }

        /// <summary>One holding at most <paramref name="holds"/> chunks made: a test's, to see chunks let go and made again.</summary>
        public GeneratedSource(GeneratedMap map, Rules rules, int holds)
        {
            if (holds < 1) throw new ArgumentOutOfRangeException(nameof(holds), "it holds at least one chunk");
            this.holds = holds;
            if (map == null) throw new ArgumentNullException(nameof(map));
            gen = map.Gen;
            legend = new LegendCells(rules ?? throw new ArgumentNullException(nameof(rules)));
            W = gen.CellsW;
            H = gen.CellsH;
            cw = gen.ChunksW;
            ch = gen.ChunksH;
            Version = "generated:" + GeneratedMap.InputHash(map.MapDir);
            Start = MapFile.Load(map.MapDir).Spawn;
        }

        public void Make(int x, int y, Cell cell)
        {
            var c = ChunkOf(x >> Chunks.Shift, y >> Chunks.Shift);
            legend.Make(c.Cells[(Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x)], cell);
        }

        public int LandAt(int x, int y) => ChunkOf(x >> Chunks.Shift, y >> Chunks.Shift).Land[(Chunks.LocalOf(y) << Chunks.Shift) | Chunks.LocalOf(x)];

        /// <summary>A chunk's things as the bake's things.json lists them (its "things"), made now if it isn't held: for the view.</summary>
        public JArray ThingsOf(int cx, int cy) => ChunkOf(cx, cy).Things;

        /// <summary>Whether a chunk is held made now (not being made).</summary>
        public bool IsMade(int cx, int cy)
        {
            lock (gate) return held.TryGetValue(Chunks.Key(cx, cy), out var h) && h.Chunk != null;
        }

        // The chunk made, from the last read's, the held ones, the worker's (waited for) or made here now.
        GeneratedChunk ChunkOf(int cx, int cy)
        {
            var l = last;
            long key = Chunks.Key(cx, cy);
            if (l != null && l.Key == key) return l.Chunk;
            if (cx < 0 || cy < 0 || cx >= cw || cy >= ch) throw new ArgumentOutOfRangeException(nameof(cx), $"chunk ({cx}, {cy}) is off the map");
            Held h;
            bool mine = false;
            lock (gate)
            {
                if (!held.TryGetValue(key, out h))
                {
                    h = new Held { Key = key };
                    held.Add(key, h);
                    mine = true;
                }
                h.Used = ++uses;
            }
            var c = h.Chunk;
            if (c == null)
            {
                long started = Stopwatch.GetTimestamp();
                if (mine)
                {
                    c = MakeTimed(cx, cy);
                    Interlocked.Increment(ref madeOnRead);
                    Finish(h, c);
                }
                else
                {
                    // Being made on the worker: waited for (a chunk the worker was asked for too late).
                    Interlocked.Increment(ref waits);
                    c = h.Making.Result;
                }
                Interlocked.Add(ref waitTicks, Stopwatch.GetTimestamp() - started);
            }
            last = new Held { Key = key, Chunk = c };
            return c;
        }

        GeneratedChunk MakeTimed(int cx, int cy)
        {
            long started = Stopwatch.GetTimestamp();
            var c = gen.Make(cx, cy);
            Interlocked.Add(ref makeTicks, Stopwatch.GetTimestamp() - started);
            Interlocked.Increment(ref made);
            lock (gate)
                if (!everMade.Add(Chunks.Key(cx, cy))) madeAgain++;
            return c;
        }

        // Every chunk made once, to count those made again: at most the map's chunks (9,720 on the 12 x 10).
        readonly HashSet<long> everMade = new HashSet<long>();
        long madeAgain;

        // How many chunks made it holds at most (Holds, unless a test says fewer).
        readonly int holds;

        // A chunk made: held, and the oldest let go past holds.
        void Finish(Held h, GeneratedChunk c)
        {
            lock (gate)
            {
                h.Chunk = c;
                Trim(e => e == h);
            }
        }

        // Past the hold, the chunks made longest unused let go, down to three quarters of it; never one being made, or one
        // the caller is about to read (kept). Under the gate.
        void Trim(Func<Held, bool> kept)
        {
            if (held.Count <= holds) return;
            var old = new List<Held>();
            foreach (var e in held.Values) if (e.Chunk != null && !kept(e)) old.Add(e);
            old.Sort((a, b) => a.Used.CompareTo(b.Used));
            for (int k = 0; k < old.Count && held.Count > holds * 3 / 4; k++) held.Remove(old[k].Key);
        }

        public void Ahead(IEnumerable<(int cx, int cy)> chunks)
        {
            if (chunks == null) return;
            lock (gate)
            {
                foreach (var (cx, cy) in chunks)
                {
                    if (cx < 0 || cy < 0 || cx >= cw || cy >= ch) continue;
                    long key = Chunks.Key(cx, cy);
                    if (held.TryGetValue(key, out var h))
                    {
                        h.Used = ++uses; // asked for again: kept
                        continue;
                    }
                    if (queued.Add(key)) asked.Enqueue(key);
                }
                if (working || asked.Count == 0) return;
                working = true;
            }
            Task.Run(Work);
        }

        // The worker: makes what's asked, in order, until nothing is, then stops.
        void Work()
        {
            while (true)
            {
                Held h;
                lock (gate)
                {
                    do
                    {
                        if (asked.Count == 0)
                        {
                            working = false;
                            return;
                        }
                        long key = asked.Dequeue();
                        queued.Remove(key);
                        if (held.ContainsKey(key)) h = null;
                        else
                        {
                            h = new Held { Key = key, Used = ++uses };
                            held.Add(key, h);
                        }
                    }
                    while (h == null);
                    h.Making = new Task<GeneratedChunk>(() => MakeTimed((int)(uint)h.Key, (int)(h.Key >> 32)));
                }
                h.Making.RunSynchronously();
                if (h.Making.IsFaulted)
                {
                    // Let a read make it again, and say why there.
                    lock (gate) held.Remove(h.Key);
                    continue;
                }
                Interlocked.Increment(ref madeAhead);
                Finish(h, h.Making.Result);
            }
        }

        /// <summary>
        /// Makes these chunks now on every core (MakeAll's way) and holds them, before a World reads them: a map's lays at
        /// build time, the spawn's reach. The same chunks as one at a time.
        /// </summary>
        public void MakeNow(IEnumerable<(int cx, int cy)> chunks)
        {
            var want = new List<(int cx, int cy)>();
            var seen = new HashSet<long>();
            lock (gate)
                foreach (var (cx, cy) in chunks)
                {
                    if (cx < 0 || cy < 0 || cx >= cw || cy >= ch || !seen.Add(Chunks.Key(cx, cy))) continue;
                    if (held.TryGetValue(Chunks.Key(cx, cy), out var h)) h.Used = ++uses; // asked for again: kept
                    else want.Add((cx, cy));
                }
            var got = new GeneratedChunk[want.Count];
            Parallel.For(0, want.Count, k => got[k] = MakeTimed(want[k].cx, want[k].cy));
            lock (gate)
            {
                for (int k = 0; k < want.Count; k++)
                {
                    long key = Chunks.Key(want[k].cx, want[k].cy);
                    if (held.ContainsKey(key)) continue;
                    held.Add(key, new Held { Key = key, Chunk = got[k], Used = ++uses });
                }
                // Kept to the hold as a chunk made on a read is (Finish), but none of these: they're about to be read. A
                // client's chunks come only this way and on reads (the worker's are the host's planner's), so without it
                // the held chunks grew with every chunk a character activated (core-guards).
                Trim(e => seen.Contains(e.Key));
            }
        }

        /// <summary>
        /// A world to play in on a generated map, as ChemGrid.MapWorld builds one from a baked map: made from the source
        /// (no chunk's cells are made until something reads it), its land the source's, then the map's puzzles, stamps,
        /// rows, golf and Hold from <paramref name="rowsDir"/> (GeneratedMap.EnsureRows), which make only the chunks they
        /// lay on, every core at once. Its starting state isn't kept: KeepStart, as ChemGrid.StartWorld does.
        /// </summary>
        public static World MapWorld(GeneratedSource source, string rowsDir, Rules rules, uint seed, out (int x, int y) start)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var map = MapFile.Load(rowsDir);
            var world = new World(rules, source.W, source.H, seed, wrapX: map.WrapX);
            world.SetLandRange(map.LandRange);
            world.MakeFrom(source);
            world.LandFromSource();
            start = source.Start;
            WorldPuzzles.Load(rowsDir).LayInto(world);
            // The map's stamps before its rows: a row's pad is cut level on the stamped land (ChemGrid.MapWorld's order).
            MapStamps.Load(rowsDir).LayInto(world);
            WorldBuildings.Load(rowsDir).LayInto(world);
            if (GolfCommand.Load(rowsDir) is { } hole) world.ApplyGolf(hole);
            if (WorldHold.Load(rowsDir) is { } hold && world.InBounds(hold.Heart.x, hold.Heart.y)) world.SetHold(hold);
            return world;
        }
    }
}
