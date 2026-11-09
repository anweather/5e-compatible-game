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
    /// <summary>What a plan is solved from, besides the library: the seed and the world's shape.</summary>
    public sealed class PlanInputs
    {
        public string Seed = "valley-1";
        /// <summary>World squares a side, not counting the sea column and row of an unwrapped world or the wall rows.</summary>
        public int Size = 3;
        public PlanWrap Wrap = PlanWrap.None;
        /// <summary>Ice to the north, desert to the south (Andrew's start), or ice at both ends with desert between.</summary>
        public bool HalfProfile = true;
        /// <summary>How strongly the climate follows latitude, in percent.</summary>
        public int Latitude = 60;
        /// <summary>The open sea's share of the land squares, in percent.</summary>
        public int Sea;
        /// <summary>The share of land that's high ground or peaks, in percent.</summary>
        public int Mountains = 30;
        /// <summary>How often a landmark square keeps its village, in percent.</summary>
        public int Towns = 35;
        /// <summary>At most this many main roads; -1 for about one per two squares a side, six at most.</summary>
        public int MainRoads = -1;
        public int InlandSeas, InlandSize = 1;
        /// <summary>The valley's sides read from its land; null for the prototype's (sea to the south and west).</summary>
        public ValleySides Valley;
        /// <summary>The valley's own spawn, cells within its square (its map.json's); null to place one as any square's.</summary>
        public (int x, int y)? ValleySpawn;
        /// <summary>
        /// The far landmarks the world must hold (section 5's must-haves, "a volcano"): each a village form's landmark. The
        /// solve gives one square inside the map a template whose village comes out as it, and keeps that village.
        /// </summary>
        public List<string> Musts = new List<string>();
        /// <summary>Backtracks in all, and per contradiction, before a filler takes a square.</summary>
        public int Budget = 3000, LocalBacktracks = 40;

        /// <summary>The prototype's defaults for a world of this size: a 3 × 3 doesn't wrap and has no open sea.</summary>
        public static PlanInputs For(string seed, int size, PlanWrap wrap, ValleySides valley = null) => new PlanInputs
        {
            Seed = seed,
            Size = size,
            Wrap = size <= 3 ? PlanWrap.None : wrap,
            Sea = size <= 3 ? 0 : 12,
            InlandSize = size <= 3 ? 1 : 4,
            Valley = valley,
        };

        /// <summary>The most main roads this world may have.</summary>
        public int MainRoadCap => MainRoads >= 0 ? MainRoads : Math.Max(1, Math.Min(6, (Size + 1) / 2));
    }

    /// <summary>What the valley's land says about each of its sides (n e s w): mostly sea or not, and its band.</summary>
    public sealed class ValleySides
    {
        public bool[] Sea = new bool[4];
        /// <summary>The band of the side's land, nearest by height; -1 for a sea side.</summary>
        public int[] Band = { -1, -1, -1, -1 };
        /// <summary>Per side, which of its slots are sea.</summary>
        public bool[][] SeaSlots = { new bool[9], new bool[9], new bool[9], new bool[9] };

        /// <summary>
        /// The sides of a <paramref name="size"/>² land (land units of <paramref name="unitMetres"/>): a slot is sea when most
        /// of its border cells are below <paramref name="seaLevel"/>, a side is when most of its slots are, and a land side's
        /// band is the one whose height is nearest the middle height of its land cells.
        /// </summary>
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

    /// <summary>A world square's coast: which sides face the sea, the shoreline's offset class there, and corner notches.</summary>
    public sealed class SquareCoast
    {
        public bool[] Open = new bool[4];
        /// <summary>The shoreline's offset from an open side, in cells.</summary>
        public int[] Depth = new int[4];
        /// <summary>A corner's sea notch (only its diagonal neighbour is sea), its radius in cells; 0 for none.</summary>
        public int[] Notch = new int[4];
        internal int[] Dv = { -1, -1, -1, -1 }, Nv = { -1, -1, -1, -1 };
    }

    /// <summary>A landmark of the plan: its square, kind and site (cells within the square).</summary>
    public sealed class PlanLandmark
    {
        public int Square;
        /// <summary>"castle", "spire", "cave", or a village intent's form ("temperate", "mountains", "foggy-marsh").</summary>
        public string Kind;
        public string Intent, Biome;
        public int SiteX, SiteY, ChunkX, ChunkY;
        public bool Port;
    }

    /// <summary>A bridge or ford the plan allows over a canyon or river.</summary>
    public sealed class PlanCrossing
    {
        public int Square, SiteX, SiteY, ChunkX, ChunkY;
        /// <summary>"bridge" or "ford"; over "canyon" or "river".</summary>
        public string Kind, Over;
        /// <summary>How many squares the canyon or river it crosses runs through, and which one it is.</summary>
        public int Size, Group;
        public bool Used;
    }

    /// <summary>A place a road leads to.</summary>
    public sealed class PlanDestination
    {
        public int Square;
        /// <summary>"castle", "village", "cave", "shore", "pass" or "junction".</summary>
        public string Kind;
        /// <summary>Its site in the square, or (-1, -1) for the square's middle.</summary>
        public int SiteX = -1, SiteY = -1;
    }

    /// <summary>A road: its route through squares and the side and slot it leaves each by.</summary>
    public sealed class PlanRoad
    {
        /// <summary>"main road", "lesser road" or "trail".</summary>
        public string Kind;
        public PlanDestination From, To;
        public List<int> Path = new List<int>(), Sides = new List<int>(), Slots = new List<int>();
        public int Cost;
        public bool IsMain => Kind == WorldPlan.MainRoad;
    }

    /// <summary>A waystation (an inn or a shop) on a main road, or an outcrop (a sight) along any road.</summary>
    public sealed class PlanSight
    {
        public int Square, SiteX, SiteY, ChunkX, ChunkY, Road;
        public string Kind;
    }

    public sealed partial class WorldPlan
    {
        public const int SquareCells = 576, ChunkCells = 64, ChunksPerSquare = SquareCells / ChunkCells;
        public const string VillageIntent = "village-with-landmark", MainRoad = "main road";
        /// <summary>The generator's version, in the hash: a change that alters plans is a new version.</summary>
        public const int Version = 2;
        /// <summary>sim.json streaming's `sight`, in chunks: an outcrop is placed within it of its road.</summary>
        const int Sight = 4;

        public readonly PlanInputs Inputs;
        public readonly SquareLibrary Library;
        public readonly PlanGrid Grid;
        public readonly int Seed;
        public readonly bool Half;
        /// <summary>The pinned valley's square.</summary>
        public readonly int Valley;

        /// <summary>Per square: 0, or the wall it is (1 ice, 2 a dune sea).</summary>
        public byte[] Wall;
        /// <summary>Per square: its coldness in Q16, 0 desert to 1 ice, from its row.</summary>
        public long[] Cold;
        public bool[] ForcedSea;
        public int[] Raw, Band;
        /// <summary>Per square: 0, or the inland sea (from 1) it's part of.</summary>
        public int[] Inland;
        public List<int[]> InlandRegions = new List<int[]>();
        public SquareCoast[] Coasts;
        /// <summary>Per square, the option it took.</summary>
        public SquareOption[] Options;
        /// <summary>Per square and side, the features crossing it, roads included, by slot.</summary>
        public List<EdgeFeature>[][] Features;
        public List<int> Fillers = new List<int>();
        public int Steps, Picks, Backtracks;
        /// <summary>Per square: a lake ends (or starts) a river in it.</summary>
        public bool[] Lake;
        /// <summary>Per square: where its features meet, in cells within it.</summary>
        public (int x, int y)[] Hub;
        public List<PlanLandmark> Landmarks = new List<PlanLandmark>();
        public List<PlanCrossing> Crossings = new List<PlanCrossing>();
        public List<PlanRoad> Roads = new List<PlanRoad>();
        public List<PlanSight> Waystations = new List<PlanSight>(), Outcrops = new List<PlanSight>();
        /// <summary>Per square, the destination there (null for none).</summary>
        public PlanDestination[] Destinations;
        /// <summary>What the checks found wrong: empty for a good plan.</summary>
        public List<string> Problems = new List<string>();
        /// <summary>Per must-have of the inputs, in order: the square holding it, or -1 where the land doesn't allow one.</summary>
        public int[] MustSquares = Array.Empty<int>();
        /// <summary>Why a must-have has no square (empty when every one has).</summary>
        public List<string> MustMisses = new List<string>();
        /// <summary>FNV-1a over the plan in a fixed order: squares, coasts, landmarks, crossings, roads, sights, spawns.</summary>
        public uint Hash;

        WorldPlan(PlanInputs inputs, SquareLibrary lib)
        {
            Inputs = inputs;
            Library = lib;
            int n = inputs.Size;
            var wrap = inputs.Wrap;
            if (n < 1) throw new ArgumentException("a world needs a world square");
            string mode = wrap switch { PlanWrap.Torus => "t", PlanWrap.EastWest => "c", _ => "r" };
            Seed = (int)Fnv(inputs.Seed + "|" + n + "|" + mode);
            // An unwrapped world has a sea column west and a sea row south; an east-west one a wall row north and south.
            int w = wrap == PlanWrap.None ? n + 1 : n, h = wrap switch { PlanWrap.None => n + 1, PlanWrap.EastWest => n + 2, _ => n };
            Grid = new PlanGrid(w, h, wrap);
            Half = inputs.HalfProfile && wrap != PlanWrap.Torus;
            // The valley stays at a temperate row (its coldness about 0.4).
            int vy = ((h - 1) * 71 + 50) / 100;
            if (wrap == PlanWrap.None) vy = n - 1;
            else if (Half) vy = ((h - 1) * 6 + 5) / 10;
            Valley = vy * w + (wrap == PlanWrap.None ? 1 : 0);
        }

        /// <summary>Solves a plan. Never fails: a square nothing fits takes its band's filler.</summary>
        public static WorldPlan Solve(PlanInputs inputs, SquareLibrary lib)
        {
            var plan = new WorldPlan(inputs, lib);
            plan.LayBands();
            plan.LayCoasts();
            new PlanSolver(plan).Run();
            plan.Finish();
            return plan;
        }

        public int Count => Grid.Count;

        public SquareTemplate TemplateAt(int i) => Options[i].Template;

        bool IsLand(int i) => Band[i] > 0 && Wall[i] == 0;

        /// <summary>Whether square <paramref name="i"/> is in the map (an unwrapped world's sea column and row aren't).</summary>
        public bool InMap(int i) => Inputs.Wrap != PlanWrap.None || (Grid.X(i) > 0 && Grid.Y(i) < Grid.H - 1);

        /// <summary>The far landmark a template's village comes out as ("volcano", "great tree"), or null; a village by the
        /// water may turn a fishing village, so it's none.</summary>
        internal string VillageLandmarkOf(SquareTemplate t)
        {
            string lm = t.Lm;
            if (lm != "land" || Library.Village == null) return null;
            return Library.Village.LandmarkOf(FormOf(t.Biome));
        }

        // ---------- bands ----------

        void LayBands()
        {
            int n = Count, w = Grid.W, h = Grid.H, size = Inputs.Size;
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
            // The raw field: low-frequency noise whose period divides a wrapped axis, and the latitude's pull.
            int lattice = Math.Max(1, (size + 1) / 3);
            long s = Inputs.Latitude * one / 100;
            var v = new long[n];
            ForcedSea = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int x = Grid.X(i), y = Grid.Y(i);
                long c = Cold[i];
                long val = (72 * Noise(Seed ^ 0xb00b, x * one / 3, y * one / 3, Grid.WrapX ? lattice : 0, Grid.WrapY ? lattice : 0)
                    + 28 * Unit(Seed ^ 0x51, x, y)) / 100;
                if (Grid.WrapX)
                    val += s * 30 * Math.Max(0, c - 55 * one / 100) / (45 * one) - s * 10 * Math.Max(0, 25 * one / 100 - c) / (25 * one);
                v[i] = val;
                if (wrap == PlanWrap.None && (x == 0 || y == h - 1)) ForcedSea[i] = true;
            }
            // The valley's sides, from its land: land beside its land sides (and between two), sea beside its sea sides.
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
            // The band thresholds are quantiles of the inner squares, so the sea share and the mountain cover are what's asked.
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
            PlaceInlandSeas(i => (wrap == PlanWrap.None && (Grid.X(i) == 0 || Grid.Y(i) == h - 1)) || Wall[i] != 0 || ForcedSea[i]);
            // The envelope: band(s) = min over t of raw(t) + step × distance(s, t), so neighbours differ by at most a step.
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
                Band[i] = m;
            }
        }

        // Inland seas: a region of sea squares with land on all eight sides of each (never the world's edge, a wall or the
        // outer sea). Its ring is raised to land, and its coasts are solved like any other.
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

        // ---------- coasts ----------

        // A coast is laid with the bands: open sides face sea, corner notches where only the diagonal is sea, and the
        // shoreline's offset class agrees wherever a shore crosses a side (union-find over the ends that meet).
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

        // The variable of a shoreline's end where side `side` of square i meets corner k: the open side round the corner's
        // depth, or the corner's notch.
        int EndVar(int i, int side, int k)
        {
            var c = Coasts[i];
            int other = PlanGrid.CornerSides[k][0] == side ? PlanGrid.CornerSides[k][1] : PlanGrid.CornerSides[k][0];
            if (c.Open[other]) return c.Dv[other];
            return c.Nv[k] >= 0 ? c.Nv[k] : -1;
        }

        /// <summary>The shoreline's offset where side <paramref name="side"/> of a square ends at its end <paramref name="e"/> (0 or 1), or 0.</summary>
        public int EndOffset(int i, int side, int e)
        {
            var c = Coasts[i];
            int k = PlanGrid.SideCorners[side][e];
            int other = PlanGrid.CornerSides[k][0] == side ? PlanGrid.CornerSides[k][1] : PlanGrid.CornerSides[k][0];
            return c.Open[other] ? c.Depth[other] : c.Notch[k];
        }

        // ---------- integers: hashing, noise, rolls ----------

        /// <summary>FNV-1a over a string's UTF-16 units.</summary>
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

        /// <summary>A hash in Q16 [0, 1) of (seed, a, b).</summary>
        internal static long Unit(int seed, int a, int b) => WorldHash.Hash4(seed, 0x0de1, a, b) >> 16;

        internal static uint H(int seed, int a, int b) => WorldHash.Hash4(seed, 0x0de2, a, b);

        // Value noise in Q16 at a point in Q16 lattice units, the lattice repeating every px (py) points when not 0.
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

    /// <summary>A seeded stream of rolls on WorldHash: the n-th roll is a pure function of (seed, n).</summary>
    internal sealed class PlanRng
    {
        readonly int seed;
        int n;

        public PlanRng(int seed) => this.seed = seed;

        public PlanRng(uint seed) => this.seed = unchecked((int)seed);

        public uint Next() => WorldHash.Hash4(seed, 0x5eed, n++, 0);

        /// <summary>A whole number in [0, k).</summary>
        public int Below(int k) => k <= 0 ? 0 : (int)((ulong)Next() * (uint)k >> 32);

        /// <summary>A whole number in [0, k) for a large k.</summary>
        public long Below(long k) => k <= 0 ? 0 : (long)((((ulong)Next() << 32) | Next()) % (ulong)k);
    }
}
