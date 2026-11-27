// How the cells shape the chunk terrains (docs/WORLD_STREAMING.md, part 1): each
// heightmap sample's offset from the baked ground, from the cells around it, kept
// per chunk with an eased current value. Pure maths, no Unity: the view adds the
// baked base heights and hands the rectangles to TerrainData.SetHeightsDelayLOD.
//
// Not a port: the sandbox has no terrain.
//
// Rules, in metres, for a sample at world cell-space position (a sample is either
// inside one cell or on the border of 2 or 4):
//  - a cell's offset is (terrain elevation + Height) * StepHeight; cells off the
//    grid count as 0;
//  - a sample inside a cell takes that cell's offset;
//  - a sample on a border takes the mean of the cells touching it, so dug and
//    raised ground meets its neighbours in a slope one sample wide;
//  - except raised walls (World.IsRaisedWall): a border sample touching a wall takes
//    the highest wall's offset. The wall's whole cell stands at full height, where
//    its invisible blocker is, and its sides slope down outside it (a quarter cell
//    on a full-detail chunk). Walls look like walls, not like mounds;
//  - seams: on a full-detail chunk's edge facing a coarse chunk, the samples between
//    the coarse ones are interpolated between them, so both edges match exactly.
// Every other sample is a function of its world position alone, so chunks that
// share an edge agree on it.
//
// Arrays are Unity's heightmap order: [row = z (north), column = x (east)].
using System;
using System.Collections.Generic;

namespace Chem
{
    public sealed class CellHeightfield
    {
        readonly TerrainGrid grid;
        readonly Dictionary<(int i, int j), ChunkState> chunks = new Dictionary<(int i, int j), ChunkState>();

        sealed class ChunkState
        {
            public float[,] Current, Target;
            public ChunkRect? Moving;
            public int[,] FilledBy;
            public bool[,] Queued;
        }

        readonly Queue<(int i, int j, int x, int z)> waiting = new Queue<(int i, int j, int x, int z)>();

        readonly List<ChunkState> moving = new List<ChunkState>();

        public CellHeightfield(TerrainGrid grid, float stepHeight = 0.5f, float easeSpeed = 1f)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (easeSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(easeSpeed));
            StepHeight = stepHeight;
            EaseSpeed = easeSpeed;
            foreach (var (i, j) in grid.GridChunks()) chunks[(i, j)] = new ChunkState();
        }

        public float StepHeight { get; }
        public float EaseSpeed { get; }

        public int SamplesFilled { get; private set; }

        public bool FlatUnderWalls { get; set; }

        public IEnumerable<(int i, int j)> Chunks => chunks.Keys;

        public int Waiting => waiting.Count;

        public bool Settled
        {
            get
            {
                return waiting.Count == 0 && moving.Count == 0;
            }
        }

        public bool Shapes(int i, int j) => chunks.ContainsKey((i, j));

        public float[,] Current(int i, int j) => State(i, j).Current;

        public float[,] Target(int i, int j) => State(i, j).Target;

        public float OffsetNow(int i, int j, int sx, int sz)
        {
            if (!chunks.TryGetValue((i, j), out var s)) throw new ArgumentOutOfRangeException(nameof(i), $"The cells don't reach chunk ({i}, {j})");
            if (s.Target == null && lazyWorld != null) return SampleOffset(lazyWorld, i, j, sx, sz);
            return State(i, j).Current[sz, sx];
        }

        ChunkState State(int i, int j)
        {
            if (!chunks.TryGetValue((i, j), out var s)) throw new ArgumentOutOfRangeException(nameof(i), $"The cells don't reach chunk ({i}, {j})");
            if (s.Target == null && lazyWorld != null) Work(lazyWorld, (i, j), s);
            return s;
        }

        World lazyWorld;

        public int Worked
        {
            get
            {
                int n = 0;
                foreach (var s in chunks.Values)
                    if (s.Target != null) n++;
                return n;
            }
        }

        public void ResetLazily(World world)
        {
            lazyWorld = world ?? throw new ArgumentNullException(nameof(world));
            foreach (var s in chunks.Values) Drop(s);
            waiting.Clear();
            moving.Clear();
        }

        public bool Forget(int i, int j)
        {
            if (lazyWorld == null || !chunks.TryGetValue((i, j), out var s) || s.Target == null || s.Moving.HasValue) return false;
            if (s.Queued != null)
                foreach (bool q in s.Queued)
                    if (q) return false;
            Drop(s);
            return true;
        }

        static void Drop(ChunkState s)
        {
            s.Target = s.Current = null;
            s.Moving = null;
            s.Queued = null;
            s.FilledBy = null;
        }

        void Work(World world, (int i, int j) key, ChunkState s)
        {
            var was = cache;
            cache = CacheRound(world, key.i, key.j, key.i, key.j);
            try
            {
                WorkCached(world, key, s);
            }
            finally
            {
                cache = was;
            }
        }

        void WorkCached(World world, (int i, int j) key, ChunkState s)
        {
            s.Target = Compute(world, key.i, key.j);
            s.Current = (float[,])s.Target.Clone();
            s.Moving = null;
            s.Queued = null;
        }

        CellCache CacheRound(World world, int i0, int j0, int i1, int j1)
        {
            int u0 = i0 * TerrainGrid.ChunkCells - CacheMargin, u1 = (i1 + 1) * TerrainGrid.ChunkCells + CacheMargin;
            int v0 = j0 * TerrainGrid.ChunkCells - CacheMargin, v1 = (j1 + 1) * TerrainGrid.ChunkCells + CacheMargin;
            var (xa, ya) = grid.GridCell(u0, v0);
            var (xb, yb) = grid.GridCell(u1, v1);
            return Cache(world, Math.Min(xa, xb), Math.Min(ya, yb), Math.Max(xa, xb), Math.Max(ya, yb));
        }

        const int CacheMargin = 4;

        public const int MostCachedChunks = 400;

        public void Prepare(IEnumerable<(int i, int j)> wanted)
        {
            if (lazyWorld == null) return;
            var todo = new List<(int i, int j)>();
            var seen = new HashSet<(int i, int j)>();
            int i0 = int.MaxValue, j0 = int.MaxValue, i1 = int.MinValue, j1 = int.MinValue;
            foreach (var at in wanted)
            {
                if (!chunks.TryGetValue(at, out var st) || st.Target != null || !seen.Add(at)) continue;
                todo.Add(at);
                i0 = Math.Min(i0, at.i);
                j0 = Math.Min(j0, at.j);
                i1 = Math.Max(i1, at.i);
                j1 = Math.Max(j1, at.j);
            }
            if (todo.Count == 0) return;
            var world = lazyWorld;
            bool overSeam = (world.WrapX && i1 - i0 > grid.ChunksX / 2) || (world.WrapY && j1 - j0 > grid.ChunksZ / 2);
            if (todo.Count == 1 || overSeam || (long)(i1 - i0 + 1) * (j1 - j0 + 1) > MostCachedChunks)
            {
                foreach (var at in todo) Work(world, at, chunks[at]);
                return;
            }
            var states = new ChunkState[todo.Count];
            for (int n = 0; n < todo.Count; n++) states[n] = chunks[todo[n]];
            cache = CacheRound(world, i0, j0, i1, j1);
            try
            {
                if (Threaded) System.Threading.Tasks.Parallel.For(0, todo.Count, n => WorkCached(world, todo[n], states[n]));
                else for (int n = 0; n < todo.Count; n++) WorkCached(world, todo[n], states[n]);
            }
            finally
            {
                cache = null;
            }
        }

        public const float BankDepth = 0.45f;

        public float CellOffset(World world, Cell c)
        {
            if (c == null) return 0f;
            float bank = CellPaint.IsBank(c.Cover, world.Rules) ? BankDepth : 0f;
            return (world.TerrainOf(c).Elevation + world.HeightOf(c)) * StepHeight + bank;
        }

        public float SampleOffset(World world, int i, int j, int sx, int sz)
        {
            int p = grid.SamplesPerCell(i, j);
            if (sz % p != 0 && OnCoarseSeam(i, j, sx, -1, 0))
                return Between(RawOffset(world, i, j, sx, sz - sz % p), RawOffset(world, i, j, sx, sz - sz % p + p), sz % p, p);
            if (sx % p != 0 && OnCoarseSeam(i, j, sz, 0, -1))
                return Between(RawOffset(world, i, j, sx - sx % p, sz), RawOffset(world, i, j, sx - sx % p + p, sz), sx % p, p);
            return RawOffset(world, i, j, sx, sz);
        }

        static float Between(float a, float b, int k, int p) => a + (b - a) * k / p;

        bool OnCoarseSeam(int i, int j, int s, int dx, int dz)
        {
            if (!grid.IsHighRes(i, j)) return false;
            if (s == 0) return IsCoarse(i + dx, j + dz);
            return s == grid.Resolution(i, j) - 1 && IsCoarse(i - dx, j - dz);
        }

        bool IsCoarse(int i, int j) => grid.HasChunk(i, j) && !grid.IsHighRes(i, j);

        float RawOffset(World world, int i, int j, int sx, int sz) =>
            CellsOffset(world, i, j, sx, sz) + (world.LandHasMoved ? LandOffset(world, i, j, sx, sz) : 0f);

        float LandOffset(World world, int i, int j, int sx, int sz)
        {
            int p = grid.SamplesPerCell(i, j);
            float unit = StepHeight / world.Rules.Sim.WaterUnitsPerStep;
            if (p == 1)
            {
                var (u0, u1) = Touching(sx, p, i * TerrainGrid.ChunkCells);
                var (v0, v1) = Touching(sz, p, j * TerrainGrid.ChunkCells);
                float sum = 0f;
                for (int v = v0; v <= v1; v++)
                    for (int u = u0; u <= u1; u++) sum += LandAt(world, u, v);
                return sum * unit / ((u1 - u0 + 1) * (v1 - v0 + 1));
            }
            float fu = i * TerrainGrid.ChunkCells + (float)sx / p - 0.5f, fv = j * TerrainGrid.ChunkCells + (float)sz / p - 0.5f;
            int cu = (int)Math.Floor(fu), cv = (int)Math.Floor(fv);
            float tu = fu - cu, tv = fv - cv;
            float near = LandAt(world, cu, cv) + (LandAt(world, cu + 1, cv) - LandAt(world, cu, cv)) * tu;
            float far = LandAt(world, cu, cv + 1) + (LandAt(world, cu + 1, cv + 1) - LandAt(world, cu, cv + 1)) * tu;
            return (near + (far - near) * tv) * unit;
        }

        float LandAt(World world, int u, int v)
        {
            var (x, y) = grid.GridCell(u, v);
            x = world.FoldX(x);
            y = world.FoldY(y);
            if (cache != null && cache.Holds(x, y)) return cache.Land[cache.Index(x, y)];
            if (cache != null && !grid.InGrid(x, y)) return 0f;
            return LandOf(world, x, y);
        }

        int LandOf(World world, int x, int y) => LaidLandDrawn ? world.LandMovedAt(x, y) : world.LandStampedAt(x, y);

        float CellsOffset(World world, int i, int j, int sx, int sz)
        {
            int p = grid.SamplesPerCell(i, j);
            var (u0, u1) = Touching(sx, p, i * TerrainGrid.ChunkCells);
            var (v0, v1) = Touching(sz, p, j * TerrainGrid.ChunkCells);
            float sum = 0f, wall = float.NegativeInfinity;
            for (int v = v0; v <= v1; v++)
            {
                for (int u = u0; u <= u1; u++)
                {
                    Read(world, u, v, out float o, out bool raised, out _);
                    sum += o;
                    if (raised && !FlatUnderWalls) wall = Math.Max(wall, o);
                }
            }
            if (!float.IsNegativeInfinity(wall)) return wall;
            float fu = i * TerrainGrid.ChunkCells + (float)sx / p - 0.5f, fv = j * TerrainGrid.ChunkCells + (float)sz / p - 0.5f;
            float mound = BankAt(world, fu, fv);
            if (p == 1) return sum / ((u1 - u0 + 1) * (v1 - v0 + 1)) + mound;
            Read(world, u1, v1, out float own, out _, out _);
            return Blended(world, i, j, sx, sz, p, own) + mound;
        }

        const float MoundCore = 0.3f, MoundReach = 1.45f;

        float BankAt(World world, float fu, float fv)
        {
            float best = 0f;
            if (cache != null && !cache.AnyBank) return best;
            for (int v = (int)Math.Floor(fv - MoundReach); v <= (int)Math.Ceiling(fv + MoundReach); v++)
                for (int u = (int)Math.Floor(fu - MoundReach); u <= (int)Math.Ceiling(fu + MoundReach); u++)
                {
                    if (!Read(world, u, v, out _, out _, out bool bank) || !bank) continue;
                    float du = fu - u, dv = fv - v;
                    float d = (float)Math.Sqrt(du * du + dv * dv);
                    if (d >= MoundReach) continue;
                    float t = Math.Max(0f, (d - MoundCore) / (MoundReach - MoundCore));
                    best = Math.Max(best, BankDepth * MoundJitter(u, v) * (1f - t * t * (3f - 2f * t)));
                }
            return best;
        }

        static float MoundJitter(int u, int v)
        {
            unchecked
            {
                uint h = (uint)u * 0x8da6b343u ^ (uint)v * 0xd8163841u;
                h ^= h >> 15;
                h *= 0x2c1b3c6du;
                h ^= h >> 12;
                return 0.8f + 0.4f * (h & 0xffff) / 65535f;
            }
        }

        float Offset(World world, Cell c) =>
            c != null && FlatUnderWalls && world.IsRaisedWall(c) ? (world.TerrainOf(c).Elevation + Math.Min(world.HeightOf(c), 0)) * StepHeight : GroundOffset(world, c);

        float GroundOffset(World world, Cell c) => c == null ? 0f : (world.TerrainOf(c).Elevation + world.HeightOf(c)) * StepHeight;

        float Blended(World world, int i, int j, int sx, int sz, int p, float own)
        {
            float fu = i * TerrainGrid.ChunkCells + (float)sx / p - 0.5f, fv = j * TerrainGrid.ChunkCells + (float)sz / p - 0.5f;
            int u = (int)Math.Floor(fu), v = (int)Math.Floor(fv);
            float tu = fu - u, tv = fv - v;
            float Corner(int du, int dv)
            {
                if (!Read(world, u + du, v + dv, out float o, out bool raised, out _)) return 0f;
                return !FlatUnderWalls && raised ? own : o;
            }
            float near = Corner(0, 0) + (Corner(1, 0) - Corner(0, 0)) * tu;
            float far = Corner(0, 1) + (Corner(1, 1) - Corner(0, 1)) * tu;
            return near + (far - near) * tv;
        }

        static (int lo, int hi) Touching(int s, int p, int first)
        {
            int c = first + s / p;
            return s % p != 0 ? (c, c) : (c - 1, c);
        }

        bool Read(World world, int u, int v, out float offset, out bool raised, out bool bank)
        {
            var (x, y) = grid.GridCell(u, v);
            x = world.FoldX(x);
            y = world.FoldY(y);
            if (!grid.InGrid(x, y))
            {
                offset = 0f;
                raised = bank = false;
                return false;
            }
            if (cache != null && cache.Holds(x, y))
            {
                int k = cache.Index(x, y);
                offset = cache.Offset[k];
                raised = cache.Raised[k];
                bank = cache.Bank[k];
                return true;
            }
            var c = world.At(x, y);
            offset = Offset(world, c);
            raised = c != null && world.IsRaisedWall(c);
            bank = c != null && CellPaint.IsBank(c.Cover, world.Rules);
            return true;
        }

        sealed class CellCache
        {
            public float[] Offset, Land;
            public bool[] Raised, Bank;
            public bool AnyBank;
            public int X0, Y0, W, H;

            public bool Holds(int x, int y) => x >= X0 && y >= Y0 && x < X0 + W && y < Y0 + H;

            public int Index(int x, int y) => (y - Y0) * W + (x - X0);
        }

        CellCache cache;

        CellCache Cache(World world) => Cache(world, 0, 0, grid.GridW - 1, grid.GridH - 1);

        CellCache Cache(World world, int x0, int y0, int x1, int y1)
        {
            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            x1 = Math.Min(grid.GridW - 1, x1);
            y1 = Math.Min(grid.GridH - 1, y1);
            int w = Math.Max(0, x1 - x0 + 1), h = Math.Max(0, y1 - y0 + 1), n = w * h;
            var c = new CellCache { Offset = new float[n], Land = new float[n], Raised = new bool[n], Bank = new bool[n], X0 = x0, Y0 = y0, W = w, H = h };
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var cell = world.At(x, y);
                    int k = c.Index(x, y);
                    c.Offset[k] = Offset(world, cell);
                    c.Raised[k] = cell != null && world.IsRaisedWall(cell);
                    c.Bank[k] = cell != null && CellPaint.IsBank(cell.Cover, world.Rules);
                    c.AnyBank |= c.Bank[k];
                    c.Land[k] = LandOf(world, x, y);
                }
            }
            return c;
        }

        public float[,] ComputeUnlaid(World world, int i, int j)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            bool was = LaidLandDrawn;
            var wasCache = cache;
            LaidLandDrawn = false;
            try
            {
                cache = CacheRound(world, i, j, i, j);
                return Compute(world, i, j);
            }
            finally
            {
                LaidLandDrawn = was;
                cache = wasCache;
            }
        }

        public float[,] Compute(World world, int i, int j)
        {
            int res = grid.Resolution(i, j);
            var a = new float[res, res];
            Fill(world, a, grid.SampleRect(i, j));
            return a;
        }

        void Fill(World world, float[,] a, ChunkRect r)
        {
            for (int sz = r.Z; sz <= r.ZMax; sz++)
                for (int sx = r.X; sx <= r.XMax; sx++) a[sz, sx] = SampleOffset(world, r.I, r.J, sx, sz);
        }

        public List<ChunkRect> DirtyRects(IEnumerable<(int x, int y)> cells)
        {
            var rects = new ChunkRects();
            foreach (var (x, y) in cells) ForEachRect(x, y, rects.Add);
            return rects.ToList();
        }

        void ForEachRect(int x, int y, Action<ChunkRect> visit)
        {
            var (u, v) = grid.WorldCell(x, y);
            var (iLo, iHi) = TerrainGrid.ChunkSpan(u - 2, u + 3);
            var (jLo, jHi) = TerrainGrid.ChunkSpan(v - 2, v + 3);
            for (int j = jLo; j <= jHi; j++)
                for (int i = iLo; i <= iHi; i++)
                    if (Shapes(i, j)) visit(Clip(i, j, u - 2, v - 2, u + 3, v + 3));
        }

        ChunkRect Clip(int i, int j, int u0, int v0, int u1, int v1)
        {
            int p = grid.SamplesPerCell(i, j), x0 = i * TerrainGrid.ChunkCells, z0 = j * TerrainGrid.ChunkCells;
            int last = TerrainGrid.ChunkCells;
            return new ChunkRect(i, j,
                Math.Max(0, u0 - x0) * p, Math.Max(0, v0 - z0) * p,
                Math.Min(last, u1 - x0) * p, Math.Min(last, v1 - z0) * p);
        }

        public bool LaidLandDrawn { get; set; } = true;

        public bool Threaded { get; set; } = true;

        public void Reset(World world)
        {
            lazyWorld = null;
            cache = Cache(world);
            try
            {
                var keys = new List<(int i, int j)>(chunks.Keys);
                var states = new List<ChunkState>(chunks.Values);
                if (Threaded) System.Threading.Tasks.Parallel.For(0, keys.Count, k => WorkCached(world, keys[k], states[k]));
                else for (int k = 0; k < keys.Count; k++) WorkCached(world, keys[k], states[k]);
            }
            finally
            {
                cache = null;
            }
            waiting.Clear();
            moving.Clear();
        }

        public List<ChunkRect> Update(World world, IEnumerable<(int x, int y)> cells, bool snap = false)
        {
            var changed = new List<(int x, int y)>(cells);
            var rects = DirtyRects(changed);
            foreach (var r in rects)
                if (State(r.I, r.J).Target == null) throw new InvalidOperationException("Call Reset before Update");
            updates++;
            SamplesFilled = 0;
            foreach (var (x, y) in changed) ForEachRect(x, y, r => FillOnce(world, chunks[(r.I, r.J)], r));
            foreach (var r in rects)
            {
                var s = chunks[(r.I, r.J)];
                if (snap) Copy(s.Target, s.Current, r);
                else Move(s, r);
            }
            return rects;
        }

        public void Queue(IEnumerable<(int x, int y)> cells)
        {
            foreach (var (x, y) in cells) ForEachRect(x, y, Enqueue);
        }

        void Enqueue(ChunkRect r)
        {
            var s = State(r.I, r.J);
            if (s.Target == null) throw new InvalidOperationException("Call Reset before Queue");
            s.Queued ??= new bool[s.Target.GetLength(0), s.Target.GetLength(1)];
            for (int sz = r.Z; sz <= r.ZMax; sz++)
            {
                for (int sx = r.X; sx <= r.XMax; sx++)
                {
                    if (s.Queued[sz, sx]) continue;
                    s.Queued[sz, sx] = true;
                    waiting.Enqueue((r.I, r.J, sx, sz));
                }
            }
        }

        public void FillSome(World world, int most)
        {
            SamplesFilled = 0;
            while (SamplesFilled < most && waiting.Count > 0)
            {
                var (i, j, x, z) = waiting.Dequeue();
                var s = State(i, j);
                if (s.Queued == null) continue;
                s.Queued[z, x] = false;
                s.Target[z, x] = SampleOffset(world, i, j, x, z);
                SamplesFilled++;
                if (Math.Abs(s.Target[z, x] - s.Current[z, x]) <= 0f) continue;
                Move(s, new ChunkRect(i, j, x, z, x, z));
            }
        }

        int updates;

        void Move(ChunkState s, ChunkRect r)
        {
            if (s.Moving.HasValue)
            {
                s.Moving = s.Moving.Value.Union(r);
                return;
            }
            s.Moving = r;
            moving.Add(s);
        }

        void FillOnce(World world, ChunkState s, ChunkRect r)
        {
            s.FilledBy ??= new int[s.Target.GetLength(0), s.Target.GetLength(1)];
            for (int sz = r.Z; sz <= r.ZMax; sz++)
            {
                for (int sx = r.X; sx <= r.XMax; sx++)
                {
                    if (s.FilledBy[sz, sx] == updates) continue;
                    s.FilledBy[sz, sx] = updates;
                    s.Target[sz, sx] = SampleOffset(world, r.I, r.J, sx, sz);
                    SamplesFilled++;
                }
            }
        }

        static void Copy(float[,] from, float[,] to, ChunkRect r)
        {
            for (int z = r.Z; z <= r.ZMax; z++)
                for (int x = r.X; x <= r.XMax; x++) to[z, x] = from[z, x];
        }

        public List<ChunkRect> Advance(float dt)
        {
            var changed = new List<ChunkRect>();
            float step = EaseSpeed * dt;
            int kept = 0;
            for (int k = 0; k < moving.Count; k++)
            {
                var s = moving[k];
                var r = s.Moving.Value;
                var (moved, still) = Ease(s, r, step);
                if (moved) changed.Add(r);
                if (still) moving[kept++] = s;
                else s.Moving = null;
            }
            moving.RemoveRange(kept, moving.Count - kept);
            changed.Sort((a, b) => a.J != b.J ? a.J.CompareTo(b.J) : a.I.CompareTo(b.I));
            return changed;
        }

        static (bool moved, bool still) Ease(ChunkState s, ChunkRect r, float step)
        {
            bool moved = false, still = false;
            for (int z = r.Z; z <= r.ZMax; z++)
            {
                for (int x = r.X; x <= r.XMax; x++)
                {
                    float diff = s.Target[z, x] - s.Current[z, x], size = Math.Abs(diff);
                    if (size <= 0f) continue;
                    s.Current[z, x] = size <= step ? s.Target[z, x] : s.Current[z, x] + Math.Sign(diff) * step;
                    moved = true;
                    still |= size > step;
                }
            }
            return (moved, still);
        }
    }
}
