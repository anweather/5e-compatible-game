// A hills map's rivers (docs/design/alpine.md, "Rivers"): worked out once when the generator is made, from map data
// alone, on a coarse grid of the land (every Grid cells round the whole torus), in integers only, so every peer, thread
// and late joiner has the same rivers and every chunk carves its own part of them when it's made.
//
// - Depression filling: priority-flood (Barnes et al. 2014) from the lowest point of the map (a torus has no edge to
//   drain to), each node's fill level the higher of its own land and the level it was reached at, ties first come first
//   served, so water crossing a filled flat runs toward where it spills.
// - Flow routing: each node drains to the node that reached it in the flood (D8: one of its 8 neighbours), so every
//   node's way down ends at the lowest point, never in a pit.
// - Flow accumulation: each node's catchment in nodes, summed up the flood's order backwards, so it only grows downstream.
// - Rivers: from each spring (HillsMassif.Springs) down the flow lines. A filled depression on the way (nodes filled
//   level over their land) big enough is a lake at its fill level; a smaller one the river cuts through (its bed keeps
//   falling). The first lake big enough past the massif's foot ends the river (a lake with no outlet: its level a little
//   under its spill). A second spring's river runs into the first's.
// - The river's surface falls along it: at each node no higher than the one before, its land less a depth that grows
//   with its catchment, and no lower than the next lake down. Its half width grows with the catchment too. Its line is
//   the nodes', smoothed (corner cutting), unwrapped so it runs on over a seam.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Chem
{
    /// <summary>The rivers and lakes of a hills map, worked out on a coarse grid when the generator is made.</summary>
    public sealed class HillsRivers
    {
        const long One = BiomeMath.One;
        /// <summary>The grid's spacing in cells.</summary>
        public const int Grid = 16;
        /// <summary>No lake at a cell.</summary>
        public const int NoLake = int.MinValue;

        /// <summary>map.json's "hills" "rivers".</summary>
        public sealed class Settings
        {
            /// <summary>Springs, and how far out from the meadow's edge they rise, in cells.</summary>
            public int Springs = 2, SpringUp = 180;
            /// <summary>The meadow lake's depth over the meadow's lowest land (land units): the gorge's floor.</summary>
            public int LakeDepth = 32;
            /// <summary>The gorge's half width in cells and its floor's fall past the crest, in percent.</summary>
            public int GorgeWidth = 110, GorgeFall = 4;
            /// <summary>A river's depth (land units) at its spring and the most it adds with its catchment.</summary>
            public int Depth = 4, DepthMost = 16;
            /// <summary>The widest a river's half is, in cells.</summary>
            public int HalfMost = 5;
            /// <summary>Nodes a filled depression needs to be a lake on the way, and to end the river past the massif.</summary>
            public int MinLake = 24, MinEndLake = 120;
            /// <summary>The end lake's size in nodes: it fills its basin from the bottom up to this, well under its spill.</summary>
            public int EndLake = 300;

            public static Settings Read(JObject o)
            {
                var d = new Settings();
                if (o == null) return d;
                int I(string key, int fallback) => (int?)o[key] ?? fallback;
                return new Settings
                {
                    Springs = I("springs", d.Springs),
                    SpringUp = I("springUp", d.SpringUp),
                    LakeDepth = I("lakeDepth", d.LakeDepth),
                    GorgeWidth = I("gorgeWidth", d.GorgeWidth),
                    GorgeFall = I("gorgeFall", d.GorgeFall),
                    Depth = I("depth", d.Depth),
                    DepthMost = I("depthMost", d.DepthMost),
                    HalfMost = I("halfMost", d.HalfMost),
                    MinLake = I("minLake", d.MinLake),
                    MinEndLake = I("minEndLake", d.MinEndLake),
                    EndLake = I("endLake", d.EndLake),
                };
            }
        }

        /// <summary>A lake: its level (land units), its nodes' count, and whether it ends the river.</summary>
        public sealed class Lake
        {
            public int Id, Level, Nodes;
            public bool End;
            /// <summary>A node of it (map cells), for a still or a walk to look at.</summary>
            public int X, Y;
        }

        /// <summary>One spring's river, from its spring down to where it ends (a lake, or the river it joins).</summary>
        public sealed class River
        {
            /// <summary>Its line, Q16 map cells, unwrapped (it runs on past a seam), spring first.</summary>
            public long[] X, Y;
            /// <summary>Its water's surface at each point (land units), never rising downstream, and its half width (cells).</summary>
            public int[] Surface, Half;
            /// <summary>The catchment (grid nodes) at each point, growing downstream.</summary>
            public int[] Catchment;
            /// <summary>Its line's bounding box in cells, unwrapped.</summary>
            public int MinX, MinY, MaxX, MaxY;
            /// <summary>Whether it joins another river (else it ends in the end lake, or wherever the flow lines ended).</summary>
            public bool Joins;
            /// <summary>Its length in cells along its line.</summary>
            public int Length;
        }

        public IReadOnlyList<River> Rivers => rivers;
        public IReadOnlyList<Lake> Lakes => lakes;
        /// <summary>The grid's size in nodes.</summary>
        public readonly int GridW, GridH;
        readonly Settings set;
        readonly int w, h;
        readonly int[] land, fill, down, catchment, lakeOf;
        readonly List<River> rivers = new List<River>();
        readonly List<Lake> lakes = new List<Lake>();
        // The nodes an end lake may not take (near a main road): one that would isn't the end, and the river runs on.
        bool[] dry;

        /// <summary>
        /// Works the rivers out over <paramref name="landAt"/> (map cells to land units) on a map of
        /// <paramref name="cellsW"/> × <paramref name="cellsH"/> cells (a torus), from <paramref name="springs"/> (map cells),
        /// a lake ending them only farther than <paramref name="past"/>'s radius in cells from its centre (the massif's).
        /// </summary>
        public HillsRivers(Func<int, int, int> landAt, int cellsW, int cellsH, IReadOnlyList<(int x, int y)> springs, Settings settings, (int cx, int cy, int radius) past,
            (IReadOnlyList<(long[] x, long[] y)> lines, int cells) keepDry = default)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (cellsW % Grid != 0 || cellsH % Grid != 0) throw new FormatException($"{MapFile.FileName}: a hills map's rivers need its size a multiple of {Grid} cells");
            set = settings ?? new Settings();
            w = cellsW;
            h = cellsH;
            GridW = w / Grid;
            GridH = h / Grid;
            int n = GridW * GridH;
            land = new int[n];
            for (int j = 0; j < GridH; j++)
                for (int i = 0; i < GridW; i++) land[j * GridW + i] = landAt(i * Grid, j * Grid);
            fill = new int[n];
            down = new int[n];
            catchment = new int[n];
            lakeOf = new int[n];
            for (int k = 0; k < n; k++) lakeOf[k] = -1;
            if (keepDry.lines != null) Dry(keepDry.lines, keepDry.cells);
            var order = Flood();
            for (int k = 0; k < n; k++) catchment[k] = 1;
            for (int k = n - 1; k >= 0; k--)
            {
                int node = order[k];
                if (down[node] >= 0) catchment[down[node]] += catchment[node];
            }
            Trace(springs, past.cx, past.cy, past.radius);
            BuildMs = timer.ElapsedMilliseconds;
        }

        /// <summary>How long working the rivers out took, in milliseconds (a diagnostic: nothing reads it to decide anything).</summary>
        public long BuildMs { get; }

        /// <summary>A node's land, fill level, the node it drains to (-1 for the lowest) and its catchment.</summary>
        public (int land, int fill, int down, int catchment) Node(int i, int j)
        {
            int k = PlanGrid.Mod(j, GridH) * GridW + PlanGrid.Mod(i, GridW);
            return (land[k], fill[k], down[k], catchment[k]);
        }

        int Id(int i, int j) => PlanGrid.Mod(j, GridH) * GridW + PlanGrid.Mod(i, GridW);

        // Priority-flood from the lowest node, 8 neighbours round the torus; the nodes in the order they were taken.
        int[] Flood()
        {
            int n = land.Length;
            int seed = 0;
            for (int k = 1; k < n; k++)
                if (land[k] < land[seed]) seed = k;
            var seen = new bool[n];
            var order = new int[n];
            var heap = new MinHeap(n);
            long count = 0;
            fill[seed] = land[seed];
            down[seed] = -1;
            seen[seed] = true;
            heap.Push(fill[seed], count++, seed);
            int taken = 0;
            while (heap.Count > 0)
            {
                int c = heap.Pop();
                order[taken++] = c;
                int ci = c % GridW, cj = c / GridW;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int m = Id(ci + di, cj + dj);
                        if (seen[m]) continue;
                        seen[m] = true;
                        fill[m] = Math.Max(land[m], fill[c]);
                        down[m] = c;
                        heap.Push(fill[m], count++, m);
                    }
            }
            return order;
        }

        // A binary min-heap of nodes by (level, then the order they came in): deterministic, no ties left to chance.
        sealed class MinHeap
        {
            readonly long[] keys;
            readonly int[] nodes;
            public int Count { get; private set; }

            public MinHeap(int size)
            {
                keys = new long[size];
                nodes = new int[size];
            }

            public void Push(int level, long count, int node)
            {
                long key = ((long)level << 32) + count;
                int at = Count++;
                while (at > 0)
                {
                    int up = (at - 1) / 2;
                    if (keys[up] <= key) break;
                    keys[at] = keys[up];
                    nodes[at] = nodes[up];
                    at = up;
                }
                keys[at] = key;
                nodes[at] = node;
            }

            public int PeekLevel() => (int)(keys[0] >> 32);

            public int Pop()
            {
                int top = nodes[0];
                long key = keys[--Count];
                int node = nodes[Count];
                int at = 0;
                while (true)
                {
                    int l = 2 * at + 1;
                    if (l >= Count) break;
                    int r = l + 1, least = r < Count && keys[r] < keys[l] ? r : l;
                    if (keys[least] >= key) break;
                    keys[at] = keys[least];
                    nodes[at] = nodes[least];
                    at = least;
                }
                keys[at] = key;
                nodes[at] = node;
                return top;
            }
        }

        // The filled depression a ponded node is in: the nodes of the same fill level over their land, joined 8 ways.
        List<int> Depression(int start)
        {
            int level = fill[start];
            var list = new List<int> { start };
            var seen = new HashSet<int> { start };
            for (int q = 0; q < list.Count; q++)
            {
                int c = list[q], ci = c % GridW, cj = c / GridW;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int m = Id(ci + di, cj + dj);
                        if (fill[m] != level || land[m] >= level || !seen.Add(m)) continue;
                        list.Add(m);
                    }
            }
            list.Sort();
            return list;
        }

        // Each spring's way down: its nodes, the lakes on it and where it ends; then its surface and its line.
        void Trace(IReadOnlyList<(int x, int y)> springs, int cx, int cy, int pastRadius)
        {
            var surfaceAt = new Dictionary<int, int>();
            var tried = new Dictionary<int, int>(); // a ponded node: its depression's lake id, or -1 for too small
            foreach (var (sx, sy) in springs)
            {
                var path = new List<int>();
                int at = Id((int)BiomeMath.FloorDiv(sx + Grid / 2, Grid), (int)BiomeMath.FloorDiv(sy + Grid / 2, Grid));
                bool joins = false;
                var visited = new HashSet<int>();
                while (true)
                {
                    path.Add(at);
                    if (!visited.Add(at)) break;
                    if (surfaceAt.ContainsKey(at) && path.Count > 1)
                    {
                        // It joins a river already laid: on one more node, so the two run together.
                        joins = true;
                        if (down[at] >= 0 && surfaceAt.ContainsKey(down[at])) path.Add(down[at]);
                        break;
                    }
                    if (fill[at] > land[at])
                    {
                        if (!tried.TryGetValue(at, out int lake)) lake = TryLake(at, tried, cx, cy, pastRadius);
                        if (lake >= 0 && lakes[lake].End)
                        {
                            // Down to the end lake in its basin's hollow: the steepest way, or over the basin to the hollow it's in.
                            foreach (int m in wayTo.TryGetValue(at, out var way) ? way : Descend(at))
                            {
                                if (lakeOf[at] == lake) break;
                                path.Add(m);
                                at = m;
                            }
                            break;
                        }
                    }
                    if (down[at] < 0) break;
                    at = down[at];
                }
                Surface(path, surfaceAt);
                rivers.Add(Line(path, surfaceAt, joins));
            }
        }

        // Whether the depression a ponded node is in is a lake: a lake id, or -1. On the way, a depression big enough is a
        // lake at its fill level (it spills on). Past the massif, one big enough ends the river: a lake with no outlet,
        // filled from its basin's bottom up to EndLake nodes, so it stands well under its spill.
        int TryLake(int at, Dictionary<int, int> tried, int cx, int cy, int pastRadius)
        {
            var nodes = Depression(at);
            int id = -1;
            foreach (int m in nodes) tried[m] = -1;
            if (nodes.Count < set.MinLake) return id;
            long dx = Delta(at % GridW * Grid - cx, w), dy = Delta(at / GridW * Grid - cy, h);
            bool end = dx * dx + dy * dy > (long)pastRadius * pastRadius && nodes.Count >= set.MinEndLake;
            id = lakes.Count;
            var lake = new Lake { Id = id, Level = fill[at], Nodes = nodes.Count, End = end, X = at % GridW * Grid, Y = at / GridW * Grid };
            if (end)
            {
                // The hollow the river comes down to the steepest way, else the nearest other hollow of the basin whose
                // lake keeps clear of the main roads; none, and the river runs on through.
                var basin = new HashSet<int>(nodes);
                var bottom = Bottom(basin, Descend(at));
                if (!Clear(bottom))
                {
                    bottom = null;
                    foreach (int pit in Hollows(basin, at))
                    {
                        var b = Bottom(basin, new List<int> { pit });
                        if (!Clear(b)) continue;
                        bottom = b;
                        break;
                    }
                    if (bottom == null) return -1;
                    wayTo[at] = Way(at, bottom, fill[at]);
                }
                int top = int.MinValue;
                foreach (int m in bottom) top = Math.Max(top, land[m]);
                lake.Level = top + 1;
                lake.Nodes = bottom.Count;
                lake.X = bottom[0] % GridW * Grid;
                lake.Y = bottom[0] / GridW * Grid;
                nodes = bottom;
            }
            lakes.Add(lake);
            foreach (int m in nodes)
            {
                lakeOf[m] = id;
                tried[m] = id;
            }
            return id;
        }

        readonly Dictionary<int, List<int>> wayTo = new Dictionary<int, List<int>>();

        bool Clear(List<int> lake) => dry == null || !lake.Exists(m => dry[m]);

        // The nodes within a distance of the lines (Q16 map cells), round the torus: kept dry of an end lake.
        void Dry(IReadOnlyList<(long[] x, long[] y)> lines, int cells)
        {
            dry = new bool[GridW * GridH];
            long far = (long)cells * One;
            foreach (var (lx, ly) in lines)
                for (int k = 0; k + 1 < lx.Length; k++)
                {
                    long ax = lx[k], ay = ly[k], bx = lx[k + 1], by = ly[k + 1];
                    int i0 = (int)BiomeMath.FloorDiv(Math.Min(ax, bx) - far, (long)Grid * One), i1 = (int)BiomeMath.FloorDiv(Math.Max(ax, bx) + far, (long)Grid * One) + 1;
                    int j0 = (int)BiomeMath.FloorDiv(Math.Min(ay, by) - far, (long)Grid * One), j1 = (int)BiomeMath.FloorDiv(Math.Max(ay, by) + far, (long)Grid * One) + 1;
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            int m = Id(i, j);
                            if (dry[m]) continue;
                            dry[m] = HillsMassif.SegmentDistance((long)i * Grid * One, (long)j * Grid * One, ax, ay, bx, by) < far;
                        }
                }
        }

        // A basin's hollows (nodes with no lower neighbour), nearest a node first (over the basin, 8 ways), at most 400.
        List<int> Hollows(HashSet<int> inBasin, int from)
        {
            var list = new List<int>();
            var seen = new HashSet<int> { from };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0 && list.Count < 400)
            {
                int c = queue.Dequeue(), ci = c % GridW, cj = c / GridW;
                bool hollow = true;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int m = Id(ci + di, cj + dj);
                        if (m == c) continue;
                        if (land[m] < land[c]) hollow = false;
                        if (inBasin.Contains(m) && seen.Add(m)) queue.Enqueue(m);
                    }
                if (hollow) list.Add(c);
            }
            return list;
        }

        // The shortest way over a basin (its nodes at its fill level, 8 ways) from a node to a lake's nodes, the node left out.
        List<int> Way(int from, List<int> lake, int level)
        {
            var goal = new HashSet<int>(lake);
            var back = new Dictionary<int, int> { [from] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(from);
            int found = goal.Contains(from) ? from : -1;
            while (queue.Count > 0 && found < 0)
            {
                int c = queue.Dequeue(), ci = c % GridW, cj = c / GridW;
                for (int dj = -1; dj <= 1 && found < 0; dj++)
                    for (int di = -1; di <= 1 && found < 0; di++)
                    {
                        int m = Id(ci + di, cj + dj);
                        if (back.ContainsKey(m) || (fill[m] != level && !goal.Contains(m))) continue;
                        back[m] = c;
                        if (goal.Contains(m)) found = m;
                        else queue.Enqueue(m);
                    }
            }
            var way = new List<int>();
            for (int k = found; k >= 0 && k != from; k = back[k]) way.Add(k);
            way.Reverse();
            return way;
        }

        // Steepest descent over the land from a node to the first pit (no lower neighbour; ties to the lower index).
        List<int> Descend(int from)
        {
            var way = new List<int>();
            int at = from;
            while (true)
            {
                int ci = at % GridW, cj = at / GridW, next = at;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int m = Id(ci + di, cj + dj);
                        if (land[m] < land[next] || (land[m] == land[next] && m < next && m != at && land[m] < land[at])) next = m;
                    }
                if (next == at) return way;
                way.Add(next);
                at = next;
            }
        }

        // An end lake: from the pit a river comes down to in its basin, the lowest nodes of the basin next to what's
        // taken, until EndLake of them (the lake fills its hollow and on up, as water would).
        List<int> Bottom(HashSet<int> inBasin, List<int> down)
        {
            int low = down.Count > 0 ? down[down.Count - 1] : inBasin.Min();
            var taken = new List<int>();
            var seen = new HashSet<int> { low };
            var heap = new MinHeap(inBasin.Count + 1);
            long count = 0;
            heap.Push(land[low], count++, low);
            int lastLand = int.MinValue;
            while (heap.Count > 0)
            {
                // EndLake nodes, and every node at the last one's level too, so none beside the lake is under its level.
                if (taken.Count >= set.EndLake && heap.PeekLevel() > lastLand) break;
                int c = heap.Pop();
                lastLand = land[c];
                taken.Add(c);
                int ci = c % GridW, cj = c / GridW;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int m = Id(ci + di, cj + dj);
                        if ((!inBasin.Contains(m) && m != low) || !seen.Add(m)) continue;
                        heap.Push(land[m], count++, m);
                    }
            }
            return taken;
        }

        static long Delta(long d, long n)
        {
            d = BiomeMath.Mod(d, n);
            return d >= n / 2 ? d - n : d;
        }

        int Depth(int node) => set.Depth + Math.Min(set.DepthMost, (int)BiomeMath.Sqrt(catchment[node]) / 4);

        int HalfOf(int node) => 1 + Math.Min(set.HalfMost - 1, (int)BiomeMath.Sqrt(catchment[node]) / 12);

        // The surface along a path: in a lake its level; else no higher than upstream, its land less its depth, and no lower
        // than the next lake down (or where it joins a river already laid).
        void Surface(List<int> path, Dictionary<int, int> surfaceAt)
        {
            int count = path.Count;
            var floor = new int[count];
            int below = int.MinValue;
            for (int k = count - 1; k >= 0; k--)
            {
                int node = path[k];
                if (surfaceAt.TryGetValue(node, out int laid)) below = Math.Max(below, laid);
                else if (lakeOf[node] >= 0) below = lakes[lakeOf[node]].Level;
                floor[k] = below;
            }
            int last = int.MaxValue;
            for (int k = 0; k < count; k++)
            {
                int node = path[k];
                if (surfaceAt.TryGetValue(node, out int laid))
                {
                    last = Math.Min(last, laid);
                    continue;
                }
                int s = lakeOf[node] >= 0 ? lakes[lakeOf[node]].Level : Math.Max(floor[k], Math.Min(last, land[node] - Depth(node)));
                s = Math.Min(s, last);
                surfaceAt[node] = s;
                last = s;
            }
        }

        // A path's line: its nodes unwrapped, corners cut twice (the ends kept), its surface, half width and catchment.
        River Line(List<int> path, Dictionary<int, int> surfaceAt, bool joins)
        {
            int count = path.Count;
            var px = new List<long>(count);
            var py = new List<long>(count);
            var ps = new List<long>(count);
            var ph = new List<long>(count);
            var pc = new List<long>(count);
            long x = path[0] % GridW * Grid, y = path[0] / GridW * Grid;
            for (int k = 0; k < count; k++)
            {
                int node = path[k];
                if (k > 0)
                {
                    x += Delta(node % GridW * Grid - x, w);
                    y += Delta(node / GridW * Grid - y, h);
                }
                px.Add(x * One + One / 2);
                py.Add(y * One + One / 2);
                ps.Add((long)surfaceAt[node] * One);
                ph.Add((long)Math.Max(k > 0 ? (int)(ph[k - 1] / One) : 0, HalfOf(node)) * One);
                // Down to an end lake's hollow the nodes are off the flow lines: the river carries what it had.
                pc.Add(Math.Max(k > 0 ? pc[k - 1] : 0, catchment[node]));
            }
            for (int pass = 0; pass < 2; pass++) Cut(px, py, ps, ph, pc);
            var r = new River
            {
                X = px.ToArray(),
                Y = py.ToArray(),
                Surface = new int[px.Count],
                Half = new int[px.Count],
                Catchment = new int[px.Count],
                Joins = joins,
                MinX = int.MaxValue,
                MinY = int.MaxValue,
                MaxX = int.MinValue,
                MaxY = int.MinValue,
            };
            long length = 0;
            for (int k = 0; k < px.Count; k++)
            {
                r.Surface[k] = (int)BiomeMath.FloorDiv(ps[k], One);
                r.Half[k] = (int)((ph[k] + One / 2) / One);
                r.Catchment[k] = (int)pc[k];
                int cxk = (int)BiomeMath.FloorDiv(px[k], One), cyk = (int)BiomeMath.FloorDiv(py[k], One);
                r.MinX = Math.Min(r.MinX, cxk);
                r.MaxX = Math.Max(r.MaxX, cxk);
                r.MinY = Math.Min(r.MinY, cyk);
                r.MaxY = Math.Max(r.MaxY, cyk);
                if (k > 0) length += BiomeMath.Length(px[k] - px[k - 1], py[k] - py[k - 1]);
            }
            r.Length = (int)(length / One);
            return r;
        }

        // Chaikin's corner cutting on every list alike: each corner replaced by points a quarter and three quarters along
        // its two sides; the ends kept. Linear, so a surface that never rises still never rises.
        static void Cut(params List<long>[] lists)
        {
            int count = lists[0].Count;
            if (count < 3) return;
            foreach (var list in lists)
            {
                var cut = new List<long>(2 * count) { list[0] };
                for (int k = 0; k + 1 < count; k++)
                {
                    long a = list[k], b = list[k + 1];
                    if (k > 0) cut.Add(a + (b - a) / 4);
                    if (k + 2 < count) cut.Add(a + 3 * (b - a) / 4);
                }
                cut.Add(list[count - 1]);
                list.Clear();
                list.AddRange(cut);
            }
        }

        /// <summary>The highest level of a lake on a river round a cell (the grid nodes at its square's corners), or
        /// <see cref="NoLake"/>: a pit cell there is that lake's water (the view draws its surface flat).</summary>
        public int LakeNear(int x, int y)
        {
            int i = (int)BiomeMath.FloorDiv(x, Grid), j = (int)BiomeMath.FloorDiv(y, Grid);
            int best = NoLake;
            for (int dj = 0; dj <= 1; dj++)
                for (int di = 0; di <= 1; di++)
                {
                    int id = lakeOf[Id(i + di, j + dj)];
                    if (id >= 0 && lakes[id].Level > best) best = lakes[id].Level;
                }
            return best;
        }

        /// <summary>A lake's grid nodes, as (i, j): its cells are the grid squares round them.</summary>
        public List<(int i, int j)> LakeNodes(int id)
        {
            var list = new List<(int, int)>();
            for (int k = 0; k < lakeOf.Length; k++)
                if (lakeOf[k] == id) list.Add((k % GridW, k / GridW));
            return list;
        }

        /// <summary>A lake's level at a cell whose land (without the rivers) is <paramref name="natural"/>: the level where the
        /// cell is under a lake on a river, else <see cref="NoLake"/>. A pure function of the cell.</summary>
        public int LakeAt(int x, int y, int natural)
        {
            int i = (int)BiomeMath.FloorDiv(x, Grid), j = (int)BiomeMath.FloorDiv(y, Grid);
            int best = NoLake;
            for (int dj = 0; dj <= 1; dj++)
                for (int di = 0; di <= 1; di++)
                {
                    int id = lakeOf[Id(i + di, j + dj)];
                    if (id < 0) continue;
                    int level = lakes[id].Level;
                    if (natural < level && level > best) best = level;
                }
            return best;
        }

        /// <summary>A river placed on the map: its line's offset in cells (a whole map's width or height round a seam), and the
        /// pieces of its line that may reach the box it was placed for.</summary>
        public struct Placed
        {
            public River River;
            public int Ox, Oy;
            public List<int> Pieces;
        }

        /// <summary>How far past a river's line it touches the land, in cells: its widest half, its bank and a ford's ramp.</summary>
        public int Reach => set.HalfMost + Bank + 2;

        /// <summary>How far past a river's line a road's ramp down to a ford may start, in cells.</summary>
        public int FordReach => set.HalfMost + FordRampMost + 2;

        /// <summary>Cells a river's bank leans back over, and the least and most a road's slope down to a ford runs.</summary>
        public const int Bank = 4, FordRamp = 10, FordRampMost = 120;

        /// <summary>Every river that may touch a cell of [x0, x1] × [y0, y1] (map cells, unwrapped), at its copy round the seams,
        /// with the pieces of it that may (within <see cref="Reach"/>).</summary>
        public List<Placed> Near(int x0, int y0, int x1, int y1) => Near(x0, y0, x1, y1, Reach);

        /// <summary>The same, for pieces within <paramref name="reach"/> cells of the box.</summary>
        public List<Placed> Near(int x0, int y0, int x1, int y1, int reach)
        {
            var near = new List<Placed>();
            foreach (var r in rivers)
                for (int ky = -1; ky <= 1; ky++)
                    for (int kx = -1; kx <= 1; kx++)
                    {
                        int ox = kx * w, oy = ky * h;
                        if (ox + r.MaxX + reach < x0 || ox + r.MinX - reach > x1 || oy + r.MaxY + reach < y0 || oy + r.MinY - reach > y1) continue;
                        var pieces = new List<int>();
                        long bx0 = (long)(x0 - ox - reach) * One, bx1 = (long)(x1 - ox + reach + 1) * One;
                        long by0 = (long)(y0 - oy - reach) * One, by1 = (long)(y1 - oy + reach + 1) * One;
                        for (int k = 0; k + 1 < r.X.Length; k++)
                            if (Math.Max(r.X[k], r.X[k + 1]) >= bx0 && Math.Min(r.X[k], r.X[k + 1]) <= bx1 && Math.Max(r.Y[k], r.Y[k + 1]) >= by0 && Math.Min(r.Y[k], r.Y[k + 1]) <= by1)
                                pieces.Add(k);
                        if (pieces.Count > 0) near.Add(new Placed { River = r, Ox = ox, Oy = oy, Pieces = pieces });
                    }
            return near;
        }

        /// <summary>The nearest point on a river to a cell: its distance (Q16 cells, long.MaxValue for none in reach), the
        /// surface there and the half width there.</summary>
        public struct Hit
        {
            public long Distance;
            public int Surface, Half;
        }

        /// <summary>The nearest river line to a map cell's middle among <paramref name="near"/>.</summary>
        public static Hit Nearest(List<Placed> near, int x, int y, int reach)
        {
            long qx = (long)x * One + One / 2, qy = (long)y * One + One / 2;
            var hit = new Hit { Distance = long.MaxValue };
            long far = (long)reach * One;
            foreach (var p in near)
            {
                var r = p.River;
                long ox = (long)p.Ox * One, oy = (long)p.Oy * One;
                foreach (int k in p.Pieces)
                {
                    long ax = r.X[k] + ox, ay = r.Y[k] + oy, bx = r.X[k + 1] + ox, by = r.Y[k + 1] + oy;
                    if (qx < Math.Min(ax, bx) - far || qx > Math.Max(ax, bx) + far || qy < Math.Min(ay, by) - far || qy > Math.Max(ay, by) + far) continue;
                    long vx = (bx - ax) / 16, vy = (by - ay) / 16, len2 = vx * vx + vy * vy;
                    long t = len2 == 0 ? 0 : Math.Max(0, Math.Min(One, ((qx - ax) / 16 * vx + (qy - ay) / 16 * vy) * One / len2));
                    long px = ax + (bx - ax) * t / One, py = ay + (by - ay) * t / One;
                    long d = BiomeMath.Length(qx - px, qy - py);
                    if (d >= hit.Distance || d >= far) continue;
                    long s = (long)r.Surface[k] * One + (long)(r.Surface[k + 1] - r.Surface[k]) * t;
                    long half = (long)r.Half[k] * One + (long)(r.Half[k + 1] - r.Half[k]) * t;
                    hit = new Hit { Distance = d, Surface = (int)BiomeMath.FloorDiv(s, One), Half = (int)((half + One / 2) / One) };
                }
            }
            return hit;
        }
    }
}
