// The known ring (docs/design/world-squares.md section 3): every chunk within sim.json's streaming.know of a character
// and past its streaming.sight, as a KnownChunk (ChunkGen.Know), for the view to draw far off. It stops one chunk past
// the coast: a chunk that's all sea is known only beside one that isn't. The chunks are worked out off the tick, on a
// worker, and handed over at a tick boundary in chunk order. Round a world that wraps east-west it reaches across the
// seam: distances go the short way round (ChunkGen.ChunkDistance) and chunk ids take x modulo the width. A pure function of the seed, the plan and where the
// characters stand: no switch, nothing shared depends on it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Chem
{
    /// <summary>The known chunks round the characters, for the view's far ring.</summary>
    public sealed class KnownRing
    {
        readonly ChunkGen gen;
        public readonly int Sight, Know;
        // Every chunk worked out so far, shown or not (a sea chunk's neighbours are read to see whether it touches land).
        readonly Dictionary<int, KnownChunk> made = new Dictionary<int, KnownChunk>();
        readonly Dictionary<int, KnownChunk> shown = new Dictionary<int, KnownChunk>();
        List<KnownChunk> ordered = new List<KnownChunk>();
        // The chunks within know and past sight of every character, in chunk order.
        List<int> wanted = new List<int>();
        (int x, int y)[] characters = Array.Empty<(int, int)>();
        Task<List<KnownChunk>> job;
        bool dirty;

        public KnownRing(ChunkGen gen, StreamingParams streaming) : this(gen, streaming.Sight, streaming.Know)
        {
        }

        public KnownRing(ChunkGen gen, int sight, int know)
        {
            if (sight < 0 || know < sight) throw new ArgumentException($"the known ring needs 0 <= sight <= know, not {sight} and {know}");
            this.gen = gen;
            Sight = sight;
            Know = know;
        }

        /// <summary>Goes up each time the known chunks change, so the view can poll it.</summary>
        public int Version { get; private set; }

        /// <summary>Whether chunks are being worked out on the worker.</summary>
        public bool Busy => job != null;

        /// <summary>The known chunks, in chunk order (north to south, west to east).</summary>
        public IReadOnlyList<KnownChunk> Chunks => ordered;

        /// <summary>Known chunk (<paramref name="cx"/>, <paramref name="cy"/>), if it's known now (x modulo the width where the map wraps).</summary>
        public bool TryGet(int cx, int cy, out KnownChunk chunk) => shown.TryGetValue(Id(cx, cy), out chunk);

        /// <summary>The chunk id the ring keys by: <c>cy × ChunksW + cx</c>, cx taken modulo the width where the map wraps.</summary>
        public int Id(int cx, int cy) => cy * gen.ChunksW + gen.WrapChunk(cx);

        /// <summary>
        /// Where the characters stand now (map chunks), at a tick boundary: the chunks they know follow, worked out on the
        /// worker and shown from a later <see cref="Handover"/>.
        /// </summary>
        public void Want(IEnumerable<(int x, int y)> characterChunks)
        {
            var now = characterChunks.Select(c => (x: gen.WrapChunk(c.x), c.y)).Distinct().OrderBy(c => c.y).ThenBy(c => c.x).ToArray();
            if (now.SequenceEqual(characters)) return;
            characters = now;
            var near = new SortedSet<int>();
            foreach (var (x, y) in characters)
                for (int cy = Math.Max(0, y - Know); cy <= Math.Min(gen.ChunksH - 1, y + Know); cy++)
                    for (int cx = gen.WrapX ? x - Know : Math.Max(0, x - Know); cx <= (gen.WrapX ? x + Know : Math.Min(gen.ChunksW - 1, x + Know)); cx++)
                        if (Nearest(gen.WrapChunk(cx), cy) > Sight) near.Add(Id(cx, cy));
            wanted = near.ToList();
            // Forget what's past every character's ring and the chunk beyond it.
            foreach (int id in made.Keys.Where(id => Nearest(id % gen.ChunksW, id / gen.ChunksW) > Know + 1).ToList()) made.Remove(id);
            dirty = true;
            Start();
        }

        /// <summary>
        /// At a tick boundary: takes what the worker has finished and shows the known chunks it decides, in chunk order;
        /// false if nothing changed.
        /// </summary>
        public bool Handover()
        {
            if (job != null && job.IsCompleted)
            {
                foreach (var k in job.Result) made[Id(k.X, k.Y)] = k;
                job = null;
                dirty = true;
            }
            if (!dirty) return false;
            dirty = false;
            Show();
            Start();
            return true;
        }

        /// <summary>Works out everything wanted now, on this thread, and shows it (at load, and in tests).</summary>
        public void Fill()
        {
            job?.Wait();
            if (job != null) foreach (var k in job.Result) made[Id(k.X, k.Y)] = k;
            job = null;
            foreach (var k in Work(Missing(), made, wanted)) made[Id(k.X, k.Y)] = k;
            dirty = false;
            Show();
        }

        void Show()
        {
            bool changed = false;
            var next = new Dictionary<int, KnownChunk>();
            foreach (int id in wanted)
            {
                if (!made.TryGetValue(id, out var k)) continue;
                if (k.IsSea && !TouchesLand(k, out bool decided))
                {
                    if (!decided && shown.TryGetValue(id, out var was)) next[id] = was;
                    continue;
                }
                next[id] = k;
            }
            if (next.Count != shown.Count || next.Keys.Any(id => !shown.ContainsKey(id))) changed = true;
            shown.Clear();
            foreach (var p in next) shown[p.Key] = p.Value;
            if (!changed) return;
            ordered = wanted.Where(shown.ContainsKey).Select(id => shown[id]).ToList();
            Version++;
        }

        // Whether a sea chunk has a chunk beside it that isn't all sea; decided is false while a neighbour isn't worked out.
        bool TouchesLand(KnownChunk k, out bool decided)
        {
            decided = true;
            foreach (var (nx, ny) in Neighbours(k.X, k.Y))
            {
                if (!made.TryGetValue(Id(nx, ny), out var n))
                {
                    decided = false;
                    continue;
                }
                if (!n.IsSea) return true;
            }
            return false;
        }

        IEnumerable<(int x, int y)> Neighbours(int cx, int cy)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = gen.WrapChunk(cx + dx), ny = cy + dy;
                    if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < gen.ChunksW && ny < gen.ChunksH) yield return (nx, ny);
                }
        }

        // The wanted chunks not worked out yet, in chunk order.
        List<int> Missing() => wanted.Where(id => !made.ContainsKey(id)).ToList();

        void Start()
        {
            if (job != null) return;
            var missing = Missing();
            bool seaPending = wanted.Any(id => made.TryGetValue(id, out var k) && k.IsSea && !TouchesLand(k, out bool decided) && !decided);
            if (missing.Count == 0 && !seaPending) return;
            var have = new Dictionary<int, KnownChunk>(made);
            var want = wanted;
            job = Task.Run(() => Work(missing, have, want));
        }

        // Works out the chunks (and, for each that's all sea, the neighbours that say whether it touches land); reads only
        // the generator and its own copies of what's made and wanted, so it runs on any thread.
        List<KnownChunk> Work(List<int> ids, Dictionary<int, KnownChunk> have, List<int> want)
        {
            var mine = new Dictionary<int, KnownChunk>(have);
            var done = new List<KnownChunk>();
            KnownChunk Get(int cx, int cy)
            {
                int id = Id(cx, cy);
                if (mine.TryGetValue(id, out var k)) return k;
                k = gen.Know(cx, cy);
                mine[id] = k;
                done.Add(k);
                return k;
            }
            // The chunks themselves on every core (each is a pure function of where it is), kept in chunk order.
            var first = new KnownChunk[ids.Count];
            Parallel.For(0, ids.Count, n => first[n] = gen.Know(ids[n] % gen.ChunksW, ids[n] / gen.ChunksW));
            foreach (var k in first)
            {
                mine[Id(k.X, k.Y)] = k;
                done.Add(k);
            }
            foreach (int id in want)
            {
                if (!mine.TryGetValue(id, out var k) || !k.IsSea) continue;
                foreach (var (nx, ny) in Neighbours(k.X, k.Y))
                    if (!Get(nx, ny).IsSea) break;
            }
            return done;
        }

        int Nearest(int cx, int cy)
        {
            int near = int.MaxValue;
            foreach (var (x, y) in characters) near = Math.Min(near, gen.ChunkDistance(x, y, cx, cy));
            return near;
        }
    }
}
