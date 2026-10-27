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
        }

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

        /// <summary>
        /// When set, the ground under a raised wall stays at the wall's foot (its height
        /// counted as 0, or less for a wall's square dug below it first) instead of rising
        /// to its top: the view builds the wall itself out of rock models, which the
        /// terrain's own wall would bury.
        /// </summary>
        public bool FlatUnderWalls { get; set; }

        /// <summary>The chunks the cells shape (TerrainGrid.GridChunks); the others keep their baked heights.</summary>
        public IEnumerable<(int i, int j)> Chunks => chunks.Keys;

        /// <summary>Whether every eased sample has reached its target.</summary>
        public bool Settled
        {
            get
            {
                foreach (var s in chunks.Values) if (s.Moving.HasValue) return false;
                return true;
            }
        }

        /// <summary>Whether the cells reach this chunk (it is one of Chunks).</summary>
        public bool Shapes(int i, int j) => chunks.ContainsKey((i, j));

        /// <summary>The offsets drawn now, [z, x], for one of Chunks. Call Reset first.</summary>
        public float[,] Current(int i, int j) => State(i, j).Current;

        /// <summary>The offsets the cells ask for, [z, x], for one of Chunks. Call Reset first.</summary>
        public float[,] Target(int i, int j) => State(i, j).Target;

        ChunkState State(int i, int j) =>
            chunks.TryGetValue((i, j), out var s) ? s : throw new ArgumentOutOfRangeException(nameof(i), $"The cells don't reach chunk ({i}, {j})");

        // ---------- the rule ----------

        /// <summary>How high a snow bank stands over the ground, in metres.</summary>
        public const float BankDepth = 0.45f;

        /// <summary>A cell's offset from the baked ground in metres (a snow bank's on top); 0 for no cell.</summary>
        public float CellOffset(World world, Cell c)
        {
            if (c == null) return 0f;
            float bank = c.Cover == World.BankCover ? BankDepth : 0f;
            return (world.TerrainOf(c).Elevation + c.Height) * StepHeight + bank;
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
        float RawOffset(World world, int i, int j, int sx, int sz)
        {
            int p = grid.SamplesPerCell(i, j);
            var (u0, u1) = Touching(sx, p, i * TerrainGrid.ChunkCells);
            var (v0, v1) = Touching(sz, p, j * TerrainGrid.ChunkCells);
            float sum = 0f, wall = float.NegativeInfinity;
            for (int v = v0; v <= v1; v++)
            {
                for (int u = u0; u <= u1; u++)
                {
                    var c = CellAt(world, u, v);
                    bool raised = c != null && world.IsRaisedWall(c);
                    float o = Offset(world, c);
                    sum += o;
                    if (raised && !FlatUnderWalls) wall = Math.Max(wall, o);
                }
            }
            if (!float.IsNegativeInfinity(wall)) return wall;
            if (p == 1) return sum / ((u1 - u0 + 1) * (v1 - v0 + 1));
            return Blended(world, i, j, sx, sz, p, Offset(world, CellAt(world, u1, v1)));
        }

        // A cell's offset as the ground under it is drawn (a raised wall over FlatUnderWalls drawn flat).
        float Offset(World world, Cell c) =>
            c != null && FlatUnderWalls && world.IsRaisedWall(c) ? (world.TerrainOf(c).Elevation + Math.Min(c.Height, 0)) * StepHeight : CellOffset(world, c);

        // Bilinear between the centres of the four cells round a full-detail sample. A neighbour that
        // is a raised wall (drawn by its own rule) counts as the sample's own cell; one off the grid as 0.
        float Blended(World world, int i, int j, int sx, int sz, int p, float own)
        {
            float fu = i * TerrainGrid.ChunkCells + (float)sx / p - 0.5f, fv = j * TerrainGrid.ChunkCells + (float)sz / p - 0.5f;
            int u = (int)Math.Floor(fu), v = (int)Math.Floor(fv);
            float tu = fu - u, tv = fv - v;
            float Corner(int du, int dv)
            {
                var c = CellAt(world, u + du, v + dv);
                if (c == null) return 0f;
                return !FlatUnderWalls && world.IsRaisedWall(c) ? own : Offset(world, c);
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

        Cell CellAt(World world, int u, int v)
        {
            var (x, y) = grid.GridCell(u, v);
            return grid.InGrid(x, y) ? world.At(x, y) : null;
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
            foreach (var (x, y) in cells)
            {
                var (u, v) = grid.WorldCell(x, y);
                var (iLo, iHi) = TerrainGrid.ChunkSpan(u - 1, u + 2);
                var (jLo, jHi) = TerrainGrid.ChunkSpan(v - 1, v + 2);
                for (int j = jLo; j <= jHi; j++)
                    for (int i = iLo; i <= iHi; i++)
                        if (Shapes(i, j)) rects.Add(Clip(i, j, u - 1, v - 1, u + 2, v + 2));
            }
            return rects.ToList();
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

        /// <summary>Computes every chunk's offsets and draws them at once (no easing).</summary>
        public void Reset(World world)
        {
            foreach (var kv in chunks)
            {
                var s = kv.Value;
                s.Target = Compute(world, kv.Key.i, kv.Key.j);
                s.Current = (float[,])s.Target.Clone();
                s.Moving = null;
            }
        }

        /// <summary>
        /// Recomputes the targets around changed cells. With snap the drawn heights jump there
        /// too; otherwise Advance eases them. Returns the rectangles whose targets were
        /// recomputed (with snap, where Current changed: apply them now).
        /// </summary>
        public List<ChunkRect> Update(World world, IEnumerable<(int x, int y)> cells, bool snap = false)
        {
            var rects = DirtyRects(cells);
            foreach (var r in rects)
            {
                var s = chunks[(r.I, r.J)];
                if (s.Target == null) throw new InvalidOperationException("Call Reset before Update");
                Fill(world, s.Target, r);
                if (snap) Copy(s.Target, s.Current, r);
                else s.Moving = s.Moving.HasValue ? s.Moving.Value.Union(r) : r;
            }
            return rects;
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
            foreach (var s in chunks.Values)
            {
                if (!s.Moving.HasValue) continue;
                var r = s.Moving.Value;
                var (moved, still) = Ease(s, r, step);
                if (moved) changed.Add(r);
                if (!still) s.Moving = null;
            }
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
