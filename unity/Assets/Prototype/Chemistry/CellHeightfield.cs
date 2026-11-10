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
            /// <summary>Where Current may still differ from Target; null when it has caught up.</summary>
            public ChunkRect? Moving;
            /// <summary>The Update that last filled each sample, so cells close together fill it once.</summary>
            public int[,] FilledBy;
            /// <summary>The samples in the queue for FillSome, so a sample waits only once.</summary>
            public bool[,] Queued;
        }

        // Samples Queue has asked for and FillSome hasn't reached, oldest first.
        readonly Queue<(int i, int j, int x, int z)> waiting = new Queue<(int i, int j, int x, int z)>();

        // The chunks with a Moving rectangle, each once: Advance and Settled look at these, never every chunk of the map
        // (#199: 729 a frame on the nine, 9,720 on the 12 x 10).
        readonly List<ChunkState> moving = new List<ChunkState>();

        /// <param name="grid">The chunk layout.</param>
        /// <param name="stepHeight">Metres per height step (ChemGrid.stepHeight).</param>
        /// <param name="easeSpeed">How fast an eased sample moves toward its new height, in metres per second.</param>
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

        /// <summary>How many samples the last Update worked out: only those round the changed cells.</summary>
        public int SamplesFilled { get; private set; }

        /// <summary>
        /// When set, the ground under a raised wall stays at the wall's foot (its height
        /// counted as 0, or less for a wall's square dug below it first) instead of rising
        /// to its top: the view builds the wall itself out of rock models, which the
        /// terrain's own wall would bury.
        /// </summary>
        public bool FlatUnderWalls { get; set; }

        /// <summary>The chunks the cells shape (TerrainGrid.GridChunks); the others keep their baked heights.</summary>
        public IEnumerable<(int i, int j)> Chunks => chunks.Keys;

        /// <summary>How many samples are waiting for FillSome.</summary>
        public int Waiting => waiting.Count;

        /// <summary>Whether every eased sample has reached its target, and none waits to be worked out.</summary>
        public bool Settled
        {
            get
            {
                return waiting.Count == 0 && moving.Count == 0;
            }
        }

        /// <summary>Whether the cells reach this chunk (it is one of Chunks).</summary>
        public bool Shapes(int i, int j) => chunks.ContainsKey((i, j));

        /// <summary>The offsets drawn now, [z, x], for one of Chunks. Call Reset first.</summary>
        public float[,] Current(int i, int j) => State(i, j).Current;

        /// <summary>The offsets the cells ask for, [z, x], for one of Chunks. Call Reset first.</summary>
        public float[,] Target(int i, int j) => State(i, j).Target;

        ChunkState State(int i, int j)
        {
            if (!chunks.TryGetValue((i, j), out var s)) throw new ArgumentOutOfRangeException(nameof(i), $"The cells don't reach chunk ({i}, {j})");
            if (s.Target == null && lazyWorld != null) Work(lazyWorld, (i, j), s);
            return s;
        }

        // ---------- a chunk at a time ----------

        // The world a lazy Reset left the chunks to be worked out from, as each is first asked for; null after a whole Reset.
        World lazyWorld;

        /// <summary>How many chunks have their offsets worked out now (a lazy Reset works out none until asked).</summary>
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

        /// <summary>
        /// Reset for a big map (#206, J-086): no chunk is worked out now, each is the first time it's asked for (Current,
        /// Target, an Update or a Queue reaching it), from the cells as they are then; until a chunk is, nothing the cells
        /// did can have moved it. The same offsets as Reset for a chunk whose cells haven't changed since.
        /// </summary>
        public void ResetLazily(World world)
        {
            lazyWorld = world ?? throw new ArgumentNullException(nameof(world));
            foreach (var s in chunks.Values) Drop(s);
            waiting.Clear();
            moving.Clear();
        }

        /// <summary>
        /// After a lazy Reset: lets a chunk's offsets go (its terrain went) when nothing is easing or waiting in it; it's
        /// worked out again from the cells when next asked for. False when it's kept.
        /// </summary>
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

        // One chunk's offsets from the cells now, through a cache of the cells it reads (the chunk and a margin for the
        // seams and the snow mounds).
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

        // A chunk's offsets worked out through the cache set now.
        void WorkCached(World world, (int i, int j) key, ChunkState s)
        {
            s.Target = Compute(world, key.i, key.j);
            s.Current = (float[,])s.Target.Clone();
            s.Moving = null;
            s.Queued = null;
        }

        // The cells chunks i0..i1, j0..j1 read, in a cache.
        CellCache CacheRound(World world, int i0, int j0, int i1, int j1)
        {
            int u0 = i0 * TerrainGrid.ChunkCells - CacheMargin, u1 = (i1 + 1) * TerrainGrid.ChunkCells + CacheMargin;
            int v0 = j0 * TerrainGrid.ChunkCells - CacheMargin, v1 = (j1 + 1) * TerrainGrid.ChunkCells + CacheMargin;
            var (xa, ya) = grid.GridCell(u0, v0);
            var (xb, yb) = grid.GridCell(u1, v1);
            return Cache(world, Math.Min(xa, xb), Math.Min(ya, yb), Math.Max(xa, xb), Math.Max(ya, yb));
        }

        // How far past a chunk's own cells its samples read: a seam's cell and a mound's reach (BankAt), with room.
        const int CacheMargin = 4;

        // The most chunks one Prepare caches the cells of at once (a box round them all); more apart, they're worked one by one.
        const int MostCachedChunks = 400;

        /// <summary>
        /// After a lazy Reset: works out those of these chunks not worked out yet, side by side (as Reset does, through one
        /// cache of the cells round them all), so a batch of terrains made at once doesn't wait for them one at a time.
        /// </summary>
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
            if (todo.Count == 1 || (long)(i1 - i0 + 1) * (j1 - j0 + 1) > MostCachedChunks)
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

        // ---------- the rule ----------

        /// <summary>How high a snow bank stands over the ground, in metres.</summary>
        public const float BankDepth = 0.45f;

        /// <summary>A cell's offset from the baked ground in metres (a snow bank's on top); 0 for no cell.</summary>
        public float CellOffset(World world, Cell c)
        {
            if (c == null) return 0f;
            float bank = CellPaint.IsBank(c.Cover, world.Rules) ? BankDepth : 0f;
            return (world.TerrainOf(c).Elevation + world.HeightOf(c)) * StepHeight + bank;
        }

        /// <summary>One sample's offset, seams included.</summary>
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

        /// <summary>
        /// Whether sample index s (along x when dx is -1, along z when dz is -1) is on an edge of
        /// a full-detail chunk that faces a coarse one.
        /// </summary>
        bool OnCoarseSeam(int i, int j, int s, int dx, int dz)
        {
            if (!grid.IsHighRes(i, j)) return false;
            if (s == 0) return IsCoarse(i + dx, j + dz);
            return s == grid.Resolution(i, j) - 1 && IsCoarse(i - dx, j - dz);
        }

        bool IsCoarse(int i, int j) => grid.HasChunk(i, j) && !grid.IsHighRes(i, j);

        /// <summary>
        /// A sample's offset from the cells round it, before the seam rule. A sample in or on the
        /// border of a raised wall stands at the wall's top, so walls keep their edges. Otherwise, at
        /// full detail the ground is blended between cell centres (bilinear: each centre exactly at
        /// its cell's height, sloping between), so a trench or a river at a slant reads as a channel
        /// rather than a staircase of blocks; a coarse chunk's samples are its cells' corners, the
        /// average of the four cells there.
        /// </summary>
        float RawOffset(World world, int i, int j, int sx, int sz) =>
            CellsOffset(world, i, j, sx, sz) + (world.LandHasMoved ? LandOffset(world, i, j, sx, sz) : 0f);

        // What land stamps have added here, in metres: between cell centres at full detail, a corner's four cells on a
        // coarse chunk, as the cells' own offsets are, so a stamp's ground takes the same path as a dug cell's.
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

        // Land moved at a cell by TerrainGrid's u, v, which the cells are offset from.
        float LandAt(World world, int u, int v)
        {
            var (x, y) = grid.GridCell(u, v);
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

        // ---------- snow banks as mounds ----------

        // A snow bank is a rounded mound (Andrew's playtest #30: banks showed as squares, a flat
        // 0.45 m block per cell): each bank cell raises a bump centred on it, full height within
        // MoundCore cells of its centre and easing to nothing at MoundReach; each a little higher
        // or lower (its own hash), and where bumps overlap the higher wins, so a row of banks is one
        // drift with a rounded top. The terrain is also the ground walked on, so the walking follows.
        const float MoundCore = 0.3f, MoundReach = 1.45f;

        /// <summary>The snow banks' height at a point in cell units (cell centres on whole numbers).</summary>
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

        // 0.8 to 1.2, the same for a cell every time (a view-only look, not world state).
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

        // A cell's offset as the ground under it is drawn (a raised wall over FlatUnderWalls drawn flat).
        float Offset(World world, Cell c) =>
            c != null && FlatUnderWalls && world.IsRaisedWall(c) ? (world.TerrainOf(c).Elevation + Math.Min(world.HeightOf(c), 0)) * StepHeight : GroundOffset(world, c);

        // A cell's ground without its snow bank (BankAt draws the banks as mounds).
        float GroundOffset(World world, Cell c) => c == null ? 0f : (world.TerrainOf(c).Elevation + world.HeightOf(c)) * StepHeight;

        // Bilinear between the centres of the four cells round a full-detail sample. A neighbour that
        // is a raised wall (drawn by its own rule) counts as the sample's own cell; one off the grid as 0.
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

        /// <summary>The world cells (inclusive) a sample index touches along one axis: one inside a cell, two on a border.</summary>
        static (int lo, int hi) Touching(int s, int p, int first)
        {
            int c = first + s / p;
            return s % p != 0 ? (c, c) : (c - 1, c);
        }

        // What the samples read of a cell: its drawn offset (Offset), whether it's a raised wall and whether a snow bank
        // lies on it. False, with nothing, off the grid. From the cache during a Reset, else from the cell.
        bool Read(World world, int u, int v, out float offset, out bool raised, out bool bank)
        {
            var (x, y) = grid.GridCell(u, v);
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

        // A Reset reads every cell twenty-odd times over (each high-detail sample reads its cell, its four corners and
        // the sixteen a snow mound could reach from): the cells' numbers once each instead, for that Reset only.
        // It was 750 ms of a world rebuilt in the player (the 2 s stalls of Andrew's 2026-11-08 log).
        sealed class CellCache
        {
            public float[] Offset, Land;
            public bool[] Raised, Bank;
            public bool AnyBank;
            // The grid cells it holds: X0..X0 + W - 1, Y0..Y0 + H - 1 (the whole grid for a Reset, a chunk and its margin for one).
            public int X0, Y0, W, H;

            public bool Holds(int x, int y) => x >= X0 && y >= Y0 && x < X0 + W && y < Y0 + H;

            public int Index(int x, int y) => (y - Y0) * W + (x - X0);
        }

        CellCache cache;

        CellCache Cache(World world) => Cache(world, 0, 0, grid.GridW - 1, grid.GridH - 1);

        // The cells x0..x1, y0..y1 (inclusive, clipped to the grid).
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

        /// <summary>A whole chunk's offsets, [z, x].</summary>
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

        // ---------- changes ----------

        /// <summary>
        /// The samples whose offsets may change when these cells change, one rectangle per chunk
        /// (only chunks this heightfield keeps). Neighbouring chunks are included when a cell
        /// touches their edge, and a one-cell margin covers the seam samples interpolated from
        /// a changed corner.
        /// </summary>
        public List<ChunkRect> DirtyRects(IEnumerable<(int x, int y)> cells)
        {
            var rects = new ChunkRects();
            foreach (var (x, y) in cells) ForEachRect(x, y, rects.Add);
            return rects.ToList();
        }

        // The samples one cell's change can reach, one rectangle per chunk.
        void ForEachRect(int x, int y, Action<ChunkRect> visit)
        {
            var (u, v) = grid.WorldCell(x, y);
            // Two cells round it: a snow bank's mound reaches past its own cell (BankAt).
            var (iLo, iHi) = TerrainGrid.ChunkSpan(u - 2, u + 3);
            var (jLo, jHi) = TerrainGrid.ChunkSpan(v - 2, v + 3);
            for (int j = jLo; j <= jHi; j++)
                for (int i = iLo; i <= iHi; i++)
                    if (Shapes(i, j)) visit(Clip(i, j, u - 2, v - 2, u + 3, v + 3));
        }

        /// <summary>World cell corners c0..c1 as the samples they span in chunk (i, j).</summary>
        ChunkRect Clip(int i, int j, int u0, int v0, int u1, int v1)
        {
            int p = grid.SamplesPerCell(i, j), x0 = i * TerrainGrid.ChunkCells, z0 = j * TerrainGrid.ChunkCells;
            int last = TerrainGrid.ChunkCells;
            return new ChunkRect(i, j,
                Math.Max(0, u0 - x0) * p, Math.Max(0, v0 - z0) * p,
                Math.Min(last, u1 - x0) * p, Math.Min(last, v1 - z0) * p);
        }

        /// <summary>False while the baked ground is read: it knows the stamps a save brings, and not the map's laid land.</summary>
        public bool LaidLandDrawn { get; set; } = true;

        /// <summary>
        /// Whether Reset works the chunks out side by side (J-079, the core's first threading; on by default). Off, one
        /// after another as before, for comparison: both give the same bits (HeightfieldThreadingTests).
        /// </summary>
        public bool Threaded { get; set; } = true;

        /// <summary>Computes every chunk's offsets and draws them at once (no easing).</summary>
        /// <remarks>
        /// Threaded, each chunk is one work item that reads the cell cache (made before, then only read) and the grid
        /// (fixed), and writes only its own ChunkState's arrays. A sample's value is a function of its position alone,
        /// so the order the chunks run in changes nothing.
        /// </remarks>
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


        /// <summary>
        /// Recomputes the targets around changed cells. With snap the drawn heights jump there
        /// too; otherwise Advance eases them. Returns one rectangle per chunk holding everything
        /// recomputed (with snap, where Current changed: apply them now). Only the samples round
        /// each cell are worked out, not the whole rectangle: a snowfall's few hundred cells are
        /// scattered, and their rectangles would cover most of 50 chunks.
        /// </summary>
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

        /// <summary>
        /// Update spread over frames: notes the samples round changed cells for FillSome, each once however
        /// many cells reach it, and works none out yet. A big land stamp's samples took 80 ms in one frame
        /// (#151). Only the view draws from this, so a sample waiting a few frames moves nothing shared.
        /// </summary>
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

        /// <summary>Works out up to <paramref name="most"/> waiting samples from the cells as they are now, for Advance to ease to.</summary>
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

        // A chunk's samples in r ease from now on.
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

        // Fill, skipping samples this Update has already worked out.
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

        /// <summary>
        /// Moves the drawn heights toward their targets by EaseSpeed * dt metres. Returns the
        /// rectangles where Current changed: hand those to the terrains.
        /// </summary>
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

        /// <summary>Eases one rectangle; whether any sample moved, and whether any is still short of its target.</summary>
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
