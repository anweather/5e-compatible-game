// The world plan (docs/design/world-squares.md sections 2 and 5, J-080 phase 2): the coarse world, solved at load on
// every peer from a seed. Each world square gets a band (an integer field, then an envelope so touching squares differ
// by at most one), the coasts that go with the bands (matched across sides and at corners), a template from
// world/squares/ (wave-function style, with backtracking and a filler per band so it never fails, repeats weighed down
// and never within 3 squares; a must-have, "a volcano", takes a square first), then landmarks, the crossings nature
// allows, the roads between them and sights along them, and a spawn point per square (WorldPlan.Spawns.cs). Ported from the paper prototype Andrew tried
// (tools/world-paper/index.html: buildWorld, buildCoasts, placeInlandSeas, Solver, finishPlan, solveRoads,
// placeOutcrops), its rules but not its floats: every number here is an integer and every roll comes from WorldHash, so
// the plan, and its hash, depend only on the seed and the inputs (D4).
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed class PlanInputs
    {
        public string Seed = "valley-1";
        public int Width = 3, Height = 3;
        public PlanWrap Wrap = PlanWrap.None;
        public bool HalfProfile = true;
        public int Latitude = 60;
        public int Sea;
        public int Mountains = 30;
        public int Towns = 35;
        public int MainRoads = -1;
        public int InlandSeas, InlandSize = 1;
        public ValleySides Valley;
        public (int x, int y)? ValleySpawn;
        public List<string> Musts = new List<string>();
        public WorldSketch Sketch;
        public Dictionary<(int x, int y), ForcedSquare> Forced = new Dictionary<(int x, int y), ForcedSquare>();
        public WorldPlan Held;
        public (int x, int y) HeldAt;
        public int Budget = 3000, LocalBacktracks = 40;

        public int Size => Math.Max(Width, Height);

        public static PlanInputs For(string seed, int size, PlanWrap wrap, ValleySides valley = null) => For(seed, size, size, wrap, valley);

        public static PlanInputs For(string seed, int width, int height, PlanWrap wrap, ValleySides valley = null)
        {
            int size = Math.Max(width, height);
            return new PlanInputs
            {
                Seed = seed,
                Width = width,
                Height = height,
                Wrap = size <= 3 ? PlanWrap.None : wrap,
                Sea = size <= 3 ? 0 : 12,
                InlandSize = size <= 3 ? 1 : 4,
                Valley = valley,
            };
        }

        public int MainRoadCap => MainRoads >= 0 ? MainRoads : Math.Max(1, Math.Min(6, (Size + 1) / 2));
    }

    public sealed class ForcedSquare
    {
        public const string Filler = "filler";
        public string Template;
        public int Band;
    }

    public sealed class ValleySides
    {
        public bool[] Sea = new bool[4];
        public int[] Band = { -1, -1, -1, -1 };
        public bool[][] SeaSlots = { new bool[9], new bool[9], new bool[9], new bool[9] };

        public static ValleySides Read(int[] land, int size, int seaLevel, double unitMetres, SquareLibrary lib)
        {
            var v = new ValleySides();
            int per = size / lib.Slots;
            v.SeaSlots = new bool[4][];
            for (int s = 0; s < 4; s++)
            {
                v.SeaSlots[s] = new bool[lib.Slots];
                var heights = new List<int>();
                int seaSlots = 0;
                for (int k = 0; k < lib.Slots; k++)
                {
                    int sea = 0;
                    for (int t = k * per; t < (k + 1) * per; t++)
                    {
                        var (x, y) = s switch { 0 => (t, 0), 1 => (size - 1, t), 2 => (t, size - 1), _ => (0, t) };
                        int h = land[y * size + x];
                        if (h < seaLevel) sea++;
                        else heights.Add(h);
                    }
                    v.SeaSlots[s][k] = sea * 2 > per;
                    if (v.SeaSlots[s][k]) seaSlots++;
                }
                v.Sea[s] = seaSlots * 2 > lib.Slots;
                if (v.Sea[s] || heights.Count == 0) continue;
                heights.Sort();
                double metres = heights[heights.Count / 2] * unitMetres;
                int best = 1;
                for (int b = 1; b < lib.Lands; b++)
                    if (Math.Abs(lib.BandHeights[b] - metres) < Math.Abs(lib.BandHeights[best] - metres)) best = b;
                v.Band[s] = best;
            }
            return v;
        }

        internal static ValleySides Paper(bool wraps) => new ValleySides { Sea = new[] { false, false, wraps, wraps } };
    }

    public sealed class SquareCoast
    {
        public bool[] Open = new bool[4];
        public int[] Depth = new int[4];
        public int[] Notch = new int[4];
        internal int[] Dv = { -1, -1, -1, -1 }, Nv = { -1, -1, -1, -1 };
    }

    public sealed class PlanLandmark
    {
        public int Square;
        public string Kind;
        public string Intent, Biome;
        public int SiteX, SiteY, ChunkX, ChunkY;
        public bool Port;
    }

    public sealed class PlanCrossing
    {
        public int Square, SiteX, SiteY, ChunkX, ChunkY;
        public string Kind, Over;
        public int Size, Group;
        public bool Used;
    }

    public sealed class PlanDestination
    {
        public int Square;
        public string Kind;
        public int SiteX = -1, SiteY = -1;
    }

    public sealed class PlanRoad
    {
        public string Kind;
        public PlanDestination From, To;
        public List<int> Path = new List<int>(), Sides = new List<int>(), Slots = new List<int>();
        public int Cost;
        public bool IsMain => Kind == WorldPlan.MainRoad;
    }

    public sealed class PlanSight
    {
        public int Square, SiteX, SiteY, ChunkX, ChunkY, Road;
        public string Kind;
    }

    public sealed partial class WorldPlan
    {
        public const int SquareCells = 576, ChunkCells = 64, ChunksPerSquare = SquareCells / ChunkCells;
        public const string VillageIntent = "village-with-landmark", MainRoad = "main road";
        public const int Version = 3;
        const int Sight = 4;

        public readonly PlanInputs Inputs;
        public readonly SquareLibrary Library;
        public readonly PlanGrid Grid;
        public readonly int Seed;
        public readonly bool Half;
        public readonly int Valley;

        public byte[] Wall;
        public long[] Cold;
        public bool[] ForcedSea;
        public int[] Raw, Band;
        public int[] Inland;
        public List<int[]> InlandRegions = new List<int[]>();
        public SquareCoast[] Coasts;
        public SquareOption[] Options;
        public List<EdgeFeature>[][] Features;
        public List<int> Fillers = new List<int>();
        public int Steps, Picks, Backtracks;
        public bool[] Lake;
        public (int x, int y)[] Hub;
        public List<PlanLandmark> Landmarks = new List<PlanLandmark>();
        public List<PlanCrossing> Crossings = new List<PlanCrossing>();
        public List<PlanRoad> Roads = new List<PlanRoad>();
        public List<PlanSight> Waystations = new List<PlanSight>(), Outcrops = new List<PlanSight>();
        public PlanDestination[] Destinations;
        public List<string> Problems = new List<string>();
        public int[] MustSquares = Array.Empty<int>();
        public List<string> MustMisses = new List<string>();
        public BiomeField Biomes;
        public int[][] BiomeCounts;
        public uint Hash;

        WorldPlan(PlanInputs inputs, SquareLibrary lib)
        {
            Inputs = inputs;
            Library = lib;
            int nw = inputs.Width, nh = inputs.Height;
            var wrap = inputs.Wrap;
            if (nw < 1 || nh < 1) throw new ArgumentException("a world needs a world square");
            string mode = wrap switch { PlanWrap.Torus => "t", PlanWrap.EastWest => "c", _ => "r" };
            Seed = (int)Fnv(inputs.Seed + "|" + (nw == nh ? nw.ToString(System.Globalization.CultureInfo.InvariantCulture) : nw + "x" + nh) + "|" + mode);
            int w = wrap == PlanWrap.None ? nw + 1 : nw, h = wrap switch { PlanWrap.None => nh + 1, PlanWrap.EastWest => nh + 2, _ => nh };
            Grid = new PlanGrid(w, h, wrap);
            Half = inputs.HalfProfile && wrap != PlanWrap.Torus;
            int vy = ((h - 1) * 71 + 50) / 100;
            if (wrap == PlanWrap.None) vy = nh - 1;
            else if (Half) vy = ((h - 1) * 6 + 5) / 10;
            Valley = vy * w + (wrap == PlanWrap.None ? 1 : 0);
            held = Hold(out int heldValley);
            if (heldValley >= 0) Valley = heldValley;
        }

        readonly int[] held;

        int[] Hold(out int valley)
        {
            var at = Enumerable.Repeat(-1, Grid.Count).ToArray();
            valley = -1;
            var inner = Inputs.Held;
            if (inner == null) return at;
            if (Inputs.Wrap != PlanWrap.None || inner.Inputs.Wrap != PlanWrap.None) throw new ArgumentException("a pinned block is a plan that doesn't wrap, in one that doesn't");
            for (int j = 0; j < inner.Count; j++)
            {
                if (!inner.InMap(j)) continue;
                int x = Inputs.HeldAt.x + inner.Grid.X(j) - 1, y = Inputs.HeldAt.y + inner.Grid.Y(j);
                if (x < 1 || y < 0 || x >= Grid.W || y >= Grid.H - 1) throw new ArgumentException($"the pinned block at [{Inputs.HeldAt.x}, {Inputs.HeldAt.y}] runs off the map");
                at[Grid.Index(x, y)] = j;
                if (j == inner.Valley) valley = Grid.Index(x, y);
            }
            return at;
        }

        public int HeldSquare(int i) => held[i];

        public ForcedSquare ForcedAt(int i) => Inputs.Forced.TryGetValue((Grid.X(i), Grid.Y(i)), out var f) ? f : null;

        public List<string> ForcedMisses = new List<string>();

        public static WorldPlan Solve(PlanInputs inputs, SquareLibrary lib)
        {
            var plan = new WorldPlan(inputs, lib);
            plan.LayBands();
            plan.LayCoasts();
            plan.ReadBiomes();
            new PlanSolver(plan).Run();
            plan.Finish();
            return plan;
        }

        public int Count => Grid.Count;

        public SquareTemplate TemplateAt(int i) => Options[i].Template;

        bool IsLand(int i) => Band[i] > 0 && Wall[i] == 0;

        public bool InMap(int i) => Inputs.Wrap != PlanWrap.None || (Grid.X(i) > 0 && Grid.Y(i) < Grid.H - 1);

        internal string VillageLandmarkOf(SquareTemplate t)
        {
            string lm = t.Lm;
            if (lm != "land" || Library.Village == null) return null;
            return Library.Village.LandmarkOf(FormOf(t.Biome));
        }

        void LayBands()
        {
            int n = Count, w = Grid.W, h = Grid.H;
            const long one = BiomeMath.One;
            var wrap = Inputs.Wrap;
            Wall = new byte[n];
            if (wrap == PlanWrap.EastWest)
                for (int x = 0; x < w; x++)
                {
                    Wall[x] = 1;
                    Wall[(h - 1) * w + x] = (byte)(Half ? 2 : 1);
                }
            int vy = Grid.Y(Valley);
            Cold = new long[n];
            for (int i = 0; i < n; i++)
            {
                int y = Grid.Y(i);
                long c;
                long eq = (h - 1) * one / 2;
                if (wrap == PlanWrap.None)
                    c = Half ? BiomeMath.Clamp01(one * 4 / 10 + (vy - y) * one / Math.Max(1, h - 1))
                             : BiomeMath.Clamp01(Math.Abs(y * one - (vy * one - eq * 4 / 10)) * one / Math.Max(1, eq));
                else
                    c = Half ? (h - 1 - y) * one / Math.Max(1, h - 1) : Math.Abs(y * one - eq) * one / Math.Max(1, eq);
                Cold[i] = c;
            }
            int latticeX = Math.Max(1, (Inputs.Width + 1) / 3), latticeY = Math.Max(1, (Inputs.Height + 1) / 3);
            long s = Inputs.Latitude * one / 100;
            var v = new long[n];
            ForcedSea = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int x = Grid.X(i), y = Grid.Y(i);
                long c = Cold[i];
                long val = (72 * Noise(Seed ^ 0xb00b, x * one / 3, y * one / 3, Grid.WrapX ? latticeX : 0, Grid.WrapY ? latticeY : 0)
                    + 28 * Unit(Seed ^ 0x51, x, y)) / 100;
                if (Grid.WrapX)
                    val += s * 30 * Math.Max(0, c - 55 * one / 100) / (45 * one) - s * 10 * Math.Max(0, 25 * one / 100 - c) / (25 * one);
                v[i] = val;
                if (wrap == PlanWrap.None && (x == 0 || y == h - 1)) ForcedSea[i] = true;
            }
            var sides = Inputs.Valley ?? ValleySides.Paper(Grid.WrapX);
            var vLand = new List<(int i, int band)> { (Valley, 1) };
            var vSea = new List<int>();
            for (int k = 0; k < 4; k++)
            {
                int j = Grid.Neighbour(Valley, k);
                if (j < 0) continue;
                if (sides.Sea[k]) vSea.Add(j);
                else vLand.Add((j, Math.Max(1, sides.Band[k])));
            }
            for (int c = 0; c < 4; c++)
            {
                int d = Grid.Diagonal(Valley, c);
                if (d < 0) continue;
                int a = PlanGrid.CornerSides[c][0], b = PlanGrid.CornerSides[c][1];
                if (!sides.Sea[a] && !sides.Sea[b]) vLand.Add((d, 1));
                else if (sides.Sea[a] && sides.Sea[b]) vSea.Add(d);
            }
            foreach (var (i, _) in vLand) ForcedSea[i] = false;
            foreach (int i in vSea) if (vLand.All(l => l.i != i)) ForcedSea[i] = true;
            for (int i = 0; i < n; i++)
            {
                if (held[i] >= 0) ForcedSea[i] = Inputs.Held.Band[held[i]] == 0;
                else if (ForcedAt(i) is ForcedSquare f) ForcedSea[i] = f.Band == 0;
            }
            var inner = new List<long>();
            for (int i = 0; i < n; i++) if (Wall[i] == 0 && !ForcedSea[i]) inner.Add(v[i]);
            inner.Sort();
            long Q(long per10k) => inner.Count == 0 ? 0 : inner[(int)Math.Max(0, Math.Min(inner.Count - 1, per10k * inner.Count / 10000))];
            long sea = Inputs.Sea, mt = Inputs.Mountains;
            long t0 = sea > 0 ? Q(sea * 100) : long.MinValue, t2 = Q(sea * 100 + (100 - sea) * (100 - mt) / 2);
            long t3 = mt > 0 ? Q(10000 - (100 - sea) * mt) : long.MaxValue, t4 = mt > 0 ? Q(10000 - (100 - sea) * mt * 4 / 10) : long.MaxValue;
            int top = Library.Lands - 1;
            Raw = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (ForcedSea[i]) continue;
                long x = v[i];
                int raw = 4;
                if (x < t0) raw = 0;
                else if (x < t2) raw = 1;
                else if (x < t3) raw = 2;
                else if (x < t4) raw = 3;
                Raw[i] = Math.Min(top, raw);
            }
            Raw[Valley] = 1;
            foreach (var (i, band) in vLand) Raw[i] = Math.Max(Raw[i], i == Valley ? 1 : band);
            ReadSketch(vLand);
            for (int i = 0; i < n; i++)
            {
                if (held[i] >= 0) Raw[i] = Inputs.Held.Band[held[i]];
                else if (ForcedAt(i) is ForcedSquare f) Raw[i] = f.Band;
            }
            PlaceInlandSeas(i => (wrap == PlanWrap.None && (Grid.X(i) == 0 || Grid.Y(i) == h - 1)) || Wall[i] != 0 || ForcedSea[i] || SketchOn(i)
                || held[i] >= 0 || ForcedAt(i) != null);
            Band = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (Wall[i] != 0) { Band[i] = Library.WallBand; continue; }
                int m = Raw[i];
                for (int j = 0; j < n; j++)
                {
                    if (Wall[j] != 0) continue;
                    int b = Raw[j] + Library.Step * Grid.Distance(i, j);
                    if (b < m) m = b;
                }
                Band[i] = held[i] >= 0 ? Inputs.Held.Band[held[i]] : m;
            }
        }

        void PlaceInlandSeas(Func<int, bool> edge)
        {
            int n = Count;
            Inland = new int[n];
            int want = Inputs.InlandSeas;
            if (want <= 0) return;
            var r = new PlanRng(Seed ^ 0x1a7d);
            var keepLand = new HashSet<int> { Valley, Grid.Neighbour(Valley, 0), Grid.Neighbour(Valley, 1) };
            var owner = new int[n];
            IEnumerable<int> Ring(int i)
            {
                for (int k = 0; k < 4; k++) yield return Grid.Neighbour(i, k);
                for (int k = 0; k < 4; k++) yield return Grid.Diagonal(i, k);
            }
            bool Allowed(int i) => !edge(i) && !keepLand.Contains(i) && Ring(i).All(j => j >= 0 && !edge(j));
            bool Free(int i, int id) => Allowed(i) && owner[i] == 0 && Ring(i).All(j => owner[j] == 0 || owner[j] == id);
            for (int k = 0; k < want; k++)
            {
                int id = k + 1;
                var cand = Enumerable.Range(0, n).Where(i => Free(i, id) && Grid.Distance(i, Valley) >= 2).ToList();
                if (cand.Count == 0) cand = Enumerable.Range(0, n).Where(i => Free(i, id)).ToList();
                if (cand.Count == 0) break;
                int start = cand[r.Below(cand.Count)];
                if (InlandRegions.Count > 0)
                {
                    long best = -1;
                    foreach (int c in cand)
                    {
                        long d = InlandRegions.SelectMany(g => g).Min(j => Grid.Distance(c, j)) * 10L + r.Below(15);
                        if (d > best) { best = d; start = c; }
                    }
                }
                var reg = new List<int> { start };
                owner[start] = id;
                while (reg.Count < Inputs.InlandSize)
                {
                    var fr = new List<int>();
                    foreach (int i in reg)
                        for (int s = 0; s < 4; s++)
                        {
                            int j = Grid.Neighbour(i, s);
                            if (j >= 0 && !fr.Contains(j) && Free(j, id)) fr.Add(j);
                        }
                    if (fr.Count == 0) break;
                    fr.Sort();
                    var ws = fr.Select(j => 1 + 2 * Enumerable.Range(0, 4).Count(s => Grid.Neighbour(j, s) >= 0 && owner[Grid.Neighbour(j, s)] == id)).ToArray();
                    int t = r.Below(ws.Sum()), pick = fr[0];
                    for (int q = 0; q < fr.Count; q++)
                    {
                        t -= ws[q];
                        if (t < 0) { pick = fr[q]; break; }
                    }
                    owner[pick] = id;
                    reg.Add(pick);
                }
                reg.Sort();
                InlandRegions.Add(reg.ToArray());
            }
            for (int i = 0; i < n; i++)
                if (owner[i] != 0)
                {
                    Raw[i] = 0;
                    Inland[i] = owner[i];
                }
            for (int i = 0; i < n; i++)
                if (owner[i] != 0)
                    foreach (int j in Ring(i))
                        if (j >= 0 && owner[j] == 0) Raw[j] = Math.Max(Raw[j], 1);
        }

        void ReadBiomes()
        {
            if (Library.Biomes == null) return;
            Biomes = WorldBiomes.Field(Library.Biomes, this);
            BiomeCounts = new int[Count][];
            int rows = Library.Biomes.Rows.Count, top = Grid.H * SquareCells - 1;
            for (int i = 0; i < Count; i++)
            {
                if (!IsLand(i)) continue;
                var counts = new int[rows];
                int x0 = Grid.X(i) * SquareCells, y0 = Grid.Y(i) * SquareCells;
                for (int b = 0; b < ChunksPerSquare; b++)
                    for (int a = 0; a < ChunksPerSquare; a++)
                        counts[Biomes.At(x0 + a * ChunkCells + ChunkCells / 2, top - (y0 + b * ChunkCells + ChunkCells / 2)).Dominant]++;
                BiomeCounts[i] = counts;
            }
        }

        void LayCoasts()
        {
            int n = Count;
            var r = new PlanRng(Seed ^ 0xc0a57);
            var parent = new List<int>();
            Coasts = new SquareCoast[n];
            for (int i = 0; i < n; i++)
            {
                var c = new SquareCoast();
                Coasts[i] = c;
                if (Band[i] <= 0 || Wall[i] != 0) continue;
                for (int s = 0; s < 4; s++)
                {
                    int j = Grid.Neighbour(i, s);
                    c.Open[s] = j >= 0 && Band[j] == 0;
                }
                for (int k = 0; k < 4; k++)
                {
                    int a = PlanGrid.CornerSides[k][0], b = PlanGrid.CornerSides[k][1], d = Grid.Diagonal(i, k);
                    if (!c.Open[a] && !c.Open[b] && d >= 0 && Band[d] == 0) c.Nv[k] = -2;
                }
                for (int s = 0; s < 4; s++)
                    if (c.Open[s])
                    {
                        c.Dv[s] = parent.Count;
                        parent.Add(parent.Count);
                    }
                for (int k = 0; k < 4; k++)
                    if (c.Nv[k] == -2)
                    {
                        c.Nv[k] = parent.Count;
                        parent.Add(parent.Count);
                    }
            }
            int Find(int a)
            {
                while (parent[a] != a)
                {
                    parent[a] = parent[parent[a]];
                    a = parent[a];
                }
                return a;
            }
            for (int i = 0; i < n; i++)
            {
                if (!IsLand(i)) continue;
                foreach (int s in new[] { 1, 2 })
                {
                    int j = Grid.Neighbour(i, s);
                    if (j < 0 || !IsLand(j)) continue;
                    int o = (s + 2) % 4;
                    for (int e = 0; e < 2; e++)
                    {
                        int a = EndVar(i, s, PlanGrid.SideCorners[s][e]), b = EndVar(j, o, PlanGrid.SideCorners[o][e]);
                        if (a < 0 || b < 0) continue;
                        int ra = Find(a), rb = Find(b);
                        if (ra != rb) parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }
                }
            }
            var cls = new int[parent.Count];
            for (int k = 0; k < parent.Count; k++)
            {
                int root = Find(k);
                if (root == k) cls[k] = Library.Shore[r.Below(100) < 55 ? 0 : Library.Shore.Length - 1];
            }
            for (int i = 0; i < n; i++)
            {
                var c = Coasts[i];
                for (int s = 0; s < 4; s++) if (c.Dv[s] >= 0) c.Depth[s] = cls[Find(c.Dv[s])];
                for (int k = 0; k < 4; k++) if (c.Nv[k] >= 0) c.Notch[k] = cls[Find(c.Nv[k])];
            }
        }

        int EndVar(int i, int side, int k)
        {
            var c = Coasts[i];
            int other = PlanGrid.CornerSides[k][0] == side ? PlanGrid.CornerSides[k][1] : PlanGrid.CornerSides[k][0];
            if (c.Open[other]) return c.Dv[other];
            return c.Nv[k] >= 0 ? c.Nv[k] : -1;
        }

        public int EndOffset(int i, int side, int e)
        {
            var c = Coasts[i];
            int k = PlanGrid.SideCorners[side][e];
            int other = PlanGrid.CornerSides[k][0] == side ? PlanGrid.CornerSides[k][1] : PlanGrid.CornerSides[k][0];
            return c.Open[other] ? c.Depth[other] : c.Notch[k];
        }

        public static uint Fnv(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s)
                {
                    h ^= ch;
                    h *= 16777619;
                }
                return h;
            }
        }

        internal static long Unit(int seed, int a, int b) => WorldHash.Hash4(seed, 0x0de1, a, b) >> 16;

        internal static uint H(int seed, int a, int b) => WorldHash.Hash4(seed, 0x0de2, a, b);

        static long Noise(int seed, long xQ, long yQ, int px, int py)
        {
            const long one = BiomeMath.One;
            long ix = BiomeMath.FloorDiv(xQ, one), iy = BiomeMath.FloorDiv(yQ, one);
            long u = BiomeMath.Smoothstep(xQ - ix * one), v = BiomeMath.Smoothstep(yQ - iy * one);
            int X(long a) => px > 0 ? PlanGrid.Mod((int)a, px) : (int)a;
            int Y(long a) => py > 0 ? PlanGrid.Mod((int)a, py) : (int)a;
            long a0 = Unit(seed, X(ix), Y(iy)), b0 = Unit(seed, X(ix + 1), Y(iy));
            long c0 = Unit(seed, X(ix), Y(iy + 1)), d0 = Unit(seed, X(ix + 1), Y(iy + 1));
            long top = a0 + (b0 - a0) * u / one, bottom = c0 + (d0 - c0) * u / one;
            return top + (bottom - top) * v / one;
        }
    }

    internal sealed class PlanRng
    {
        readonly int seed;
        int n;

        public PlanRng(int seed) => this.seed = seed;

        public PlanRng(uint seed) => this.seed = unchecked((int)seed);

        public uint Next() => WorldHash.Hash4(seed, 0x5eed, n++, 0);

        public int Below(int k) => k <= 0 ? 0 : (int)((ulong)Next() * (uint)k >> 32);

        public long Below(long k) => k <= 0 ? 0 : (long)((((ulong)Next() << 32) | Next()) % (ulong)k);
    }
}
