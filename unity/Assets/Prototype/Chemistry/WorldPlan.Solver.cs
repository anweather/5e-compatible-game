// The world plan's template solve (docs/design/world-squares.md section 5.3), ported from the paper prototype's Solver
// (tools/world-paper/index.html): wave-function style over the whole plan. The square with the fewest options left goes
// first (ties by index), a seeded weighted roll picks its option, its neighbours' options are pruned to the ones whose
// sides meet it, and a contradiction backs up a few picks. Past the budget, or a few backtracks from one contradiction,
// a filler takes every square left with nothing, so the solve never fails. A template used before weighs a quarter as
// much each time, and no two copies stand within 3 world squares (so none in any 3 × 3). Every weight is an integer and
// every roll a PlanRng, so the plan depends only on the seed and the inputs.
using System;
using System.Collections.Generic;

namespace Chem
{
    internal sealed class PlanSolver
    {
        /// <summary>The coldness (Q16, 0 desert to 1 ice) each anchor biome likes best; the paper's LAT_PEAK.</summary>
        static readonly Dictionary<string, long> LatitudePeak = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["salt-flat"] = 0,
            ["grassland"] = BiomeMath.One * 30 / 100,
            ["ashlands"] = BiomeMath.One * 40 / 100,
            ["temperate"] = BiomeMath.One * 45 / 100,
            ["foggy-marsh"] = BiomeMath.One * 45 / 100,
            ["mountains"] = BiomeMath.One * 78 / 100,
            ["tundra"] = BiomeMath.One * 88 / 100,
        };

        /// <summary>How near (Chebyshev, in squares) two copies of one template may not stand.</summary>
        internal const int RepeatGap = 3;

        readonly WorldPlan plan;
        readonly SquareLibrary lib;
        readonly PlanGrid grid;
        readonly int n;
        readonly PlanRng rng;
        readonly int[][] near;
        int[][] dom;
        bool[] col;
        Dictionary<string, int> used = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<int> fillers = new List<int>();
        readonly Stack<Frame> stack = new Stack<Frame>();
        int steps, picks, backtracks;

        sealed class Frame
        {
            public int[][] Dom;
            public bool[] Col;
            public Dictionary<string, int> Used;
            public int Fillers, X, O;
        }

        public PlanSolver(WorldPlan plan)
        {
            this.plan = plan;
            lib = plan.Library;
            grid = plan.Grid;
            n = grid.Count;
            rng = new PlanRng(plan.Seed ^ 0x5017e);
            near = new int[n][];
            for (int i = 0; i < n; i++)
            {
                var l = new List<int>();
                for (int j = 0; j < n; j++) if (j != i && grid.Distance(i, j) < RepeatGap) l.Add(j);
                near[i] = l.ToArray();
            }
            dom = new int[n][];
            col = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (i == plan.Valley) { dom[i] = new[] { lib.ValleyOption }; continue; }
                if (plan.Wall[i] != 0)
                {
                    dom[i] = new[] { lib.WallOption[plan.Wall[i]] };
                    col[i] = true;
                    continue;
                }
                var c = plan.Coasts[i];
                var keep = new List<int>();
                foreach (int id in lib.ByBand[plan.Band[i]])
                    if (Fits(lib.Options[id], i, c)) keep.Add(id);
                dom[i] = keep.ToArray();
            }
        }

        // An option fits a square's coast: a side facing the sea must be one the template lets face it, and only a river
        // flowing out may cross it; a side facing off the edge of the world carries nothing.
        bool Fits(SquareOption o, int i, SquareCoast c)
        {
            if (o.Sea) return true;
            for (int s = 0; s < 4; s++)
            {
                var f = o.Features[s];
                if (c.Open[s])
                {
                    if (!o.Coast[s]) return false;
                    foreach (var x in f) if (!(x.Kind == EdgeKind.River && x.Flow < 0)) return false;
                }
                else if (grid.Neighbour(i, s) < 0 && f.Length > 0) return false;
            }
            return true;
        }

        public void Run()
        {
            var all = new List<int>(n);
            for (int i = 0; i < n; i++) all.Add(i);
            int bad = Propagate(all, false);
            if (bad >= 0) Resolve(bad);
            PlaceMusts();
            bool more = true;
            while (more) more = Step();
            plan.Options = new SquareOption[n];
            for (int i = 0; i < n; i++) plan.Options[i] = lib.Options[dom[i][0]];
            plan.Fillers.AddRange(fillers);
            plan.Steps = steps;
            plan.Picks = picks;
            plan.Backtracks = backtracks;
        }

        // Each must-have takes a square before the roll: of the squares in the map that could still take a template whose
        // village comes out as it (not the valley, a wall or the sea), in a seeded order, the first that collapses to one of
        // those templates (its own seeded roll among them) leaving every square something. Where none does, the first
        // takes it anyway and the squares left with nothing take their band's filler, as a solve past its budget does. A
        // must-have's square is settled before the first pick, so nothing backtracks it.
        void PlaceMusts()
        {
            var musts = plan.Inputs.Musts;
            plan.MustSquares = new int[musts.Count];
            var roll = new PlanRng(plan.Seed ^ 0x6d75);
            for (int k = 0; k < musts.Count; k++)
            {
                string want = musts[k];
                plan.MustSquares[k] = -1;
                var cands = new List<(uint key, int i)>();
                for (int i = 0; i < n; i++)
                    if (!col[i] && plan.InMap(i) && Array.Exists(dom[i], o => Makes(o, want)))
                        cands.Add((WorldPlan.H(plan.Seed ^ 0x6d75, i, k), i));
                if (cands.Count == 0)
                {
                    plan.MustMisses.Add($"{want}: no square in the map can take a template whose village is one ({Holders(want)})");
                    continue;
                }
                cands.Sort();
                foreach (var (_, i) in cands)
                {
                    var keepDom = (int[][])dom.Clone();
                    var keepCol = (bool[])col.Clone();
                    var keepUsed = new Dictionary<string, int>(used, StringComparer.Ordinal);
                    int keepFillers = fillers.Count;
                    if (Collapse(i, MustPick(i, want, roll)) < 0)
                    {
                        plan.MustSquares[k] = i;
                        break;
                    }
                    dom = keepDom;
                    col = keepCol;
                    used = keepUsed;
                    fillers.RemoveRange(keepFillers, fillers.Count - keepFillers);
                }
                if (plan.MustSquares[k] >= 0) continue;
                int at = cands[0].i;
                int bad = Collapse(at, MustPick(at, want, roll));
                if (bad >= 0) Resolve(bad);
                if (dom[at].Length == 1 && Makes(dom[at][0], want)) plan.MustSquares[k] = at;
                else plan.MustMisses.Add($"{want}: none of the {cands.Count} squares that could take one fits beside its neighbours");
            }
        }

        // A seeded weighted roll among square x's options that make the landmark, apart from the solve's own rolls.
        int MustPick(int x, string landmark, PlanRng roll) => Roll(x, Array.FindAll(dom[x], o => Makes(o, landmark)), roll);

        bool Makes(int o, string landmark) => !lib.Options[o].Sea && plan.VillageLandmarkOf(lib.Options[o].Template) == landmark;

        // The templates that hold a must-have, and their bands, for a miss's reason.
        string Holders(string landmark)
        {
            var ts = new List<string>();
            foreach (var o in lib.Options)
            {
                var t = o.Template;
                string at = $"{t.Id} ({string.Join(", ", Array.ConvertAll(t.Bands, b => lib.BandIds[b]))})";
                if (!o.Sea && plan.VillageLandmarkOf(t) == landmark && !ts.Contains(at)) ts.Add(at);
            }
            return ts.Count == 0 ? "no template makes one" : "only " + string.Join(", ", ts) + " can";
        }

        int SideSig(int o, int s)
        {
            var op = lib.Options[o];
            return op.Sea ? SquareLibrary.SeaSide : op.Sig[s];
        }

        // Prunes outward from the queue; returns a square left with nothing, or -1. With allowFill, such a square takes
        // its band's filler instead.
        int Propagate(List<int> queue, bool allowFill)
        {
            var inQ = new bool[n];
            foreach (int q in queue) inQ[q] = true;
            var sigs = new HashSet<int>();
            var keep = new List<int>();
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int x = queue[qi];
                inQ[x] = false;
                if (dom[x].Length == 0)
                {
                    if (allowFill) Fill(x);
                    else return x;
                }
                var dx = dom[x];
                for (int s = 0; s < 4; s++)
                {
                    int y = grid.Neighbour(x, s);
                    if (y < 0) continue;
                    int os = (s + 2) % 4;
                    var dy = dom[y];
                    if (dy.Length == 0) continue;
                    sigs.Clear();
                    foreach (int o in dx) sigs.Add(SideSig(o, s));
                    int bx = plan.Band[x], by = plan.Band[y];
                    keep.Clear();
                    foreach (int o in dy)
                    {
                        int sy = SideSig(o, os);
                        foreach (int sx in sigs)
                            if (lib.Compatible(sx, sy, bx, by)) { keep.Add(o); break; }
                    }
                    if (keep.Count == dy.Length) continue;
                    if (keep.Count == 0)
                    {
                        if (allowFill) Fill(y);
                        else
                        {
                            dom[y] = Array.Empty<int>();
                            return y;
                        }
                    }
                    else dom[y] = keep.ToArray();
                    if (!inQ[y])
                    {
                        queue.Add(y);
                        inQ[y] = true;
                    }
                }
            }
            return -1;
        }

        void Fill(int i)
        {
            dom[i] = new[] { lib.FillerOption[plan.Band[i]] };
            col[i] = true;
            fillers.Add(i);
        }

        long Weight(int x, int o)
        {
            var op = lib.Options[o];
            var t = op.Template;
            long w = Math.Max(1, op.Weight);
            if (!t.Repeats && used.TryGetValue(t.Id, out int k)) w >>= 2 * Math.Min(k, 15);
            int row = plan.BiomeCounts?[x] != null && t.Biome != null ? lib.Biomes.IndexOf(t.Biome) : -1;
            if (row >= 0)
            {
                // The world's biome field says what the land is; a template of the biome it puts under more of the square
                // weighs more (the paper's 0.02 + 4 × the share drawn as the template's biome).
                const long one = BiomeMath.One;
                long fit = one / 50 + 4 * one * plan.BiomeCounts[x][row] / (WorldPlan.ChunksPerSquare * WorldPlan.ChunksPerSquare);
                w = w * fit / one;
            }
            else if (plan.BiomeCounts == null && t.Biome != null && LatitudePeak.TryGetValue(t.Biome, out long peak) && plan.Inputs.Latitude > 0)
            {
                const long one = BiomeMath.One;
                long s = plan.Inputs.Latitude * one / 100;
                long d = (plan.Cold[x] - peak) * 10 / 3;
                long fit = one - s + s * (one / 5 + (BiomeMath.ExpNeg(d * d / one) >> 14)) / one;
                w = w * fit / one;
            }
            if (t.AsksFor(WorldPlan.SpireIntent) && plan.Inland[x] != 0) w *= 2;
            return Math.Max(1, w);
        }

        int Pick(int x) => Roll(x, dom[x], rng);

        int Roll(int x, int[] d, PlanRng r)
        {
            var ws = new long[d.Length];
            long total = 0;
            for (int k = 0; k < d.Length; k++)
            {
                ws[k] = Weight(x, d[k]);
                total += ws[k];
            }
            long t = r.Below(total);
            for (int k = 0; k < d.Length; k++)
            {
                t -= ws[k];
                if (t < 0) return d[k];
            }
            return d[d.Length - 1];
        }

        int Collapse(int x, int o)
        {
            dom[x] = new[] { o };
            col[x] = true;
            var t = lib.Options[o].Template;
            var q = new List<int> { x };
            int bad = -1;
            if (!t.Repeats)
            {
                used[t.Id] = (used.TryGetValue(t.Id, out int k) ? k : 0) + 1;
                foreach (int y in near[x])
                {
                    var d = dom[y];
                    int same = 0;
                    foreach (int p in d) if (lib.Options[p].Template == t) same++;
                    if (same == 0) continue;
                    var kept = new int[d.Length - same];
                    int m = 0;
                    foreach (int p in d) if (lib.Options[p].Template != t) kept[m++] = p;
                    dom[y] = kept;
                    if (kept.Length == 0 && bad < 0) bad = y;
                    q.Add(y);
                }
            }
            return bad >= 0 ? bad : Propagate(q, false);
        }

        void Resolve(int bad)
        {
            int local = 0;
            while (bad >= 0)
            {
                if (stack.Count > 0 && steps < plan.Inputs.Budget && local++ < plan.Inputs.LocalBacktracks)
                {
                    steps++;
                    backtracks++;
                    var f = stack.Pop();
                    dom = f.Dom;
                    col = f.Col;
                    used = f.Used;
                    fillers.RemoveRange(f.Fillers, fillers.Count - f.Fillers);
                    dom[f.X] = Array.FindAll(dom[f.X], p => p != f.O);
                    if (dom[f.X].Length == 0)
                    {
                        bad = f.X;
                        continue;
                    }
                    bad = Propagate(new List<int> { f.X }, false);
                }
                else
                {
                    var all = new List<int>(n);
                    for (int i = 0; i < n; i++)
                    {
                        all.Add(i);
                        if (dom[i].Length == 0) Fill(i);
                    }
                    Propagate(all, true);
                    bad = -1;
                }
            }
        }

        bool Step()
        {
            int best = -1, bs = int.MaxValue;
            for (int i = 0; i < n; i++)
                if (!col[i] && dom[i].Length < bs)
                {
                    bs = dom[i].Length;
                    best = i;
                }
            if (best < 0) return false;
            int o = Pick(best);
            stack.Push(new Frame { Dom = (int[][])dom.Clone(), Col = (bool[])col.Clone(), Used = new Dictionary<string, int>(used, StringComparer.Ordinal), Fillers = fillers.Count, X = best, O = o });
            steps++;
            picks++;
            int bad = Collapse(best, o);
            if (bad >= 0) Resolve(bad);
            return true;
        }
    }
}
