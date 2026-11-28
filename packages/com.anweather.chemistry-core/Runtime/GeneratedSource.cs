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
    public interface IGeneratedSource : IChunkSource
    {
        int W { get; }
        int H { get; }

        int LandAt(int x, int y);

        void Ahead(IEnumerable<(int cx, int cy)> chunks);

        void MakeNow(IEnumerable<(int cx, int cy)> chunks);

        void LandInto(int cx, int cy, int[] into);

        void Keep(int cx, int cy, bool kept);
    }

    public sealed class GeneratedSource : IGeneratedSource
    {
        public const int Holds = 768;

        readonly ChunkGen gen;
        readonly LegendCells legend;
        readonly int cw, ch;
        readonly Dictionary<long, Held> held = new Dictionary<long, Held>();
        readonly Queue<long> asked = new Queue<long>();
        readonly HashSet<long> queued = new HashSet<long>();
        readonly object gate = new object();
        bool working;
        long uses;
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

        public int W { get; }
        public int H { get; }

        public string Version { get; }

        public (int x, int y) Start { get; }

        public long Made => Interlocked.Read(ref made);
        public long MadeAhead => Interlocked.Read(ref madeAhead);
        public long Waits => Interlocked.Read(ref waits);
        public long MadeAgain
        {
            get
            {
                lock (gate) return madeAgain;
            }
        }

        public long MadeOnRead => Interlocked.Read(ref madeOnRead);

        public Action<int, int> MadeOnReadAt { get; set; }

        public double MakeMs => Interlocked.Read(ref makeTicks) * 1000.0 / Stopwatch.Frequency;
        public double WaitMs => Interlocked.Read(ref waitTicks) * 1000.0 / Stopwatch.Frequency;

        public int HeldCount
        {
            get
            {
                lock (gate) return held.Count;
            }
        }

        public GeneratedSource(GeneratedMap map, Rules rules) : this(map, rules, Holds)
        {
        }

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

        public void LandInto(int cx, int cy, int[] into)
        {
            if (into == null || into.Length < Chunks.Area) throw new ArgumentException($"a chunk's land is {Chunks.Area} ints", nameof(into));
            Array.Copy(MadeChunk(cx, cy).Land, into, Chunks.Area);
        }

        public JArray ThingsOf(int cx, int cy) => ChunkOf(cx, cy).Things;

        public bool IsMade(int cx, int cy)
        {
            lock (gate) return held.TryGetValue(Chunks.Key(cx, cy), out var h) && h.Chunk != null;
        }

        GeneratedChunk ChunkOf(int cx, int cy)
        {
            var l = last;
            if (l != null && l.Key == Chunks.Key(cx, cy)) return l.Chunk;
            var c = MadeChunk(cx, cy);
            last = new Held { Key = Chunks.Key(cx, cy), Chunk = c };
            return c;
        }

        GeneratedChunk MadeChunk(int cx, int cy)
        {
            long key = Chunks.Key(cx, cy);
            if (cx < 0 || cy < 0 || cx >= cw || cy >= ch) throw new ArgumentOutOfRangeException(nameof(cx), $"chunk ({cx}, {cy}) is off the map");
            Held h;
            GeneratedChunk c;
            Task<GeneratedChunk> making;
            bool mine = false;
            lock (gate)
            {
                if (!held.TryGetValue(key, out h))
                {
                    h = new Held { Key = key, Making = new Task<GeneratedChunk>(() => MakeTimed(cx, cy)) };
                    held.Add(key, h);
                    mine = true;
                }
                h.Used = ++uses;
                c = h.Chunk;
                making = h.Making;
            }
            if (c != null) return c;
            long started = Stopwatch.GetTimestamp();
            if (mine)
            {
                making.RunSynchronously();
                Interlocked.Increment(ref madeOnRead);
                MadeOnReadAt?.Invoke(cx, cy);
                if (making.IsFaulted)
                {
                    lock (gate) held.Remove(key);
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(making.Exception.InnerException ?? making.Exception).Throw();
                }
                c = making.Result;
                Finish(h, c);
            }
            else
            {
                Interlocked.Increment(ref waits);
                c = making.Result;
            }
            Interlocked.Add(ref waitTicks, Stopwatch.GetTimestamp() - started);
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

        readonly HashSet<long> everMade = new HashSet<long>();
        long madeAgain;

        readonly int holds;

        readonly HashSet<long> pinned = new HashSet<long>();

        public void Keep(int cx, int cy, bool kept)
        {
            lock (gate)
            {
                if (kept) pinned.Add(Chunks.Key(cx, cy));
                else pinned.Remove(Chunks.Key(cx, cy));
            }
        }

        public int KeptCount
        {
            get
            {
                lock (gate) return pinned.Count;
            }
        }

        void Finish(Held h, GeneratedChunk c)
        {
            lock (gate)
            {
                h.Chunk = c;
                Trim(e => e == h);
            }
        }

        void Trim(Func<Held, bool> kept)
        {
            int besides = 0;
            foreach (long key in pinned)
                if (held.ContainsKey(key)) besides++;
            if (held.Count - besides <= holds) return;
            var old = new List<Held>();
            foreach (var e in held.Values) if (e.Chunk != null && !kept(e) && !pinned.Contains(e.Key)) old.Add(e);
            old.Sort((a, b) => a.Used.CompareTo(b.Used));
            for (int k = 0; k < old.Count && held.Count - besides > holds * 3 / 4; k++) held.Remove(old[k].Key);
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
                        h.Used = ++uses;
                        continue;
                    }
                    if (queued.Add(key)) asked.Enqueue(key);
                }
                if (working || asked.Count == 0) return;
                working = true;
            }
            Task.Run(Work);
        }

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
                    lock (gate) held.Remove(h.Key);
                    continue;
                }
                Interlocked.Increment(ref madeAhead);
                Finish(h, h.Making.Result);
            }
        }

        public void MakeNow(IEnumerable<(int cx, int cy)> chunks)
        {
            var want = new List<(int cx, int cy)>();
            var seen = new HashSet<long>();
            lock (gate)
                foreach (var (cx, cy) in chunks)
                {
                    if (cx < 0 || cy < 0 || cx >= cw || cy >= ch || !seen.Add(Chunks.Key(cx, cy))) continue;
                    if (held.TryGetValue(Chunks.Key(cx, cy), out var h)) h.Used = ++uses;
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
                Trim(e => seen.Contains(e.Key));
            }
        }

        public static World MapWorld(GeneratedSource source, string rowsDir, Rules rules, uint seed, out (int x, int y) start, Func<World, World> modes = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var map = MapFile.Load(rowsDir);
            var world = new World(rules, source.W, source.H, seed, wrapX: map.WrapX, wrapY: map.WrapY);
            if (modes != null) world = modes(world);
            world.SetLandRange(map.LandRange);
            world.MakeFrom(source);
            world.LandFromSource();
            start = source.Start;
            WorldPuzzles.Load(rowsDir).LayInto(world);
            MapStamps.Load(rowsDir).LayInto(world);
            WorldBuildings.Load(rowsDir).LayInto(world);
            world.MapLaid(rowsDir);
            return world;
        }
    }
}
