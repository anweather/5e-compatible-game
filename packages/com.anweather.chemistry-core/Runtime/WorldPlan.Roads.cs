// Roads have a reason (docs/design/world-squares.md, "Roads have a reason"; the paper prototype's solveRoads, placeCaves,
// checkRoads and placeOutcrops). Points of interest first: the landmarks, caves, passes, a spire's shore, and the
// crossings nature allows (one or two bridges a canyon, one to three fords or bridges a river; a range only at a pass).
// Then a few long main roads between the major places far apart, longest first, kept apart from each other, with an inn
// or a shop every two or three squares. Then lesser roads and trails from the network out to every place not on it yet,
// nearest first. A road network that spans fewer than three world squares is dropped (no stubs). The slots a road crosses
// sides by come from its route, and sights (outcrops) are seeded along every road within sight of it. Costs are whole
// numbers per square stepped into; peaks and sea are never entered and a band step costs a lot.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed partial class WorldPlan
    {
        // The paper's ROAD costs: a step, a band step (main, lesser), the high band (main, lesser), a river or range on the
        // side crossed, a side that offers a road, a crossing, a main road beside or on another, and the jitter.
        const int StepCost = 10, BandCost = 30, LesserBandCost = 18, HighCost = 30, LesserHighCost = 12, RiverCost = 4, RangeCost = 20;
        const int OfferCost = 3, CrossCost = 6, BesideCost = 35, OverlapCost = 30, Jitter = 3, NetworkCost = 4;
        const int OutcropEvery = 6, OutcropSpacing = 3, LeastRoadSquares = 3;
        static readonly int[] SlotOrder = { 4, 3, 5, 2, 6, 1, 7, 0, 8 };
        static readonly (int x, int y)[] Around = { (55, 0), (39, 39), (0, 55), (-39, 39), (-55, 0), (-39, -39), (0, -55), (39, -39) };

        bool[] roadOk, barRiver, barCanyon, crossRiver, crossCanyon;
        (int x, int y)?[] roadEnd;

        /// <summary>Whether a road may enter a square: low, hills or high land, and not across a range but at a pass.</summary>
        public bool RoadMayEnter(int i) => i >= 0 && roadOk[i];

        /// <summary>Whether a road may pass through a square: no river or canyon runs through it, or it has a crossing.</summary>
        public bool Passable(int i) => !(barRiver[i] && !crossRiver[i]) && !(barCanyon[i] && !crossCanyon[i]);

        void SolveRoads()
        {
            int n = Count;
            roadOk = new bool[n];
            barRiver = new bool[n];
            barCanyon = new bool[n];
            for (int i = 0; i < n; i++)
            {
                int ranges = 0, rivers = 0, canyons = 0;
                foreach (var side in Features[i])
                    foreach (var f in side)
                        if (f.Kind == EdgeKind.Range) ranges++;
                        else if (f.Kind == EdgeKind.River) rivers++;
                        else if (f.Kind == EdgeKind.Canyon) canyons++;
                // A held square's roads are its own plan's: none comes in from this one.
                roadOk[i] = Band[i] >= 1 && Band[i] <= 3 && Wall[i] == 0 && !Options[i].Sea && (ranges < 2 || Options[i].Template.Pass) && held[i] < 0;
                if (Band[i] == 0 || Options[i].Sea) continue;
                barRiver[i] = rivers >= 2;
                barCanyon[i] = canyons >= 2;
            }
            // 1. Points of interest.
            PlaceCaves();
            Destinations = new PlanDestination[n];
            void AddDest(int i, string kind, int sx, int sy)
            {
                if (!RoadMayEnter(i) || Destinations[i] != null) return;
                Destinations[i] = new PlanDestination { Square = i, Kind = kind, SiteX = sx, SiteY = sy };
            }
            foreach (var l in Landmarks)
            {
                if (l.Kind == "spire")
                {
                    int best = -1, bb = int.MaxValue;
                    for (int q = 0; q < 8; q++)
                    {
                        int j = q < 4 ? Grid.Neighbour(l.Square, q) : Grid.Diagonal(l.Square, q - 4);
                        if (RoadMayEnter(j) && Band[j] < bb && !barRiver[j] && !barCanyon[j])
                        {
                            bb = Band[j];
                            best = j;
                        }
                    }
                    if (best >= 0) AddDest(best, "shore", -1, -1);
                    continue;
                }
                AddDest(l.Square, l.Kind == "castle" || l.Kind == "cave" ? l.Kind : "village", l.SiteX, l.SiteY);
            }
            for (int i = 0; i < n; i++) if (Options[i].Template.Pass) AddDest(i, "pass", -1, -1);
            var dests = Enumerable.Range(0, n).Where(i => Destinations[i] != null).ToList();
            PlaceCrossings();
            // 2. Main roads; 3. lesser roads and trails.
            var entry = Enumerable.Repeat(-1, n).ToArray();
            var mainSq = new bool[n];
            var net = new bool[n];
            int Cost(int u, int s, bool main)
            {
                int v = Grid.Neighbour(u, s);
                if (!RoadMayEnter(v)) return -1;
                int db = Math.Abs(Band[u] - Band[v]);
                if (db > 1) return -1;
                if (!Passable(v) && entry[v] >= 0 && entry[v] != u) return -1;
                int os = (s + 2) % 4;
                int c = StepCost + (main ? BandCost : LesserBandCost) * db;
                if (Band[v] == 3) c += main ? HighCost : LesserHighCost;
                var f = Features[u][s];
                if (f.Any(x => x.Kind == EdgeKind.River)) c += RiverCost;
                if (f.Any(x => x.Kind == EdgeKind.Range)) c += RangeCost;
                if (Options[u].RoadOffer[s].Length > 0 || Options[v].RoadOffer[os].Length > 0) c -= OfferCost;
                if (crossRiver[v] || crossCanyon[v]) c -= CrossCost;
                if (main)
                {
                    if (mainSq[v] && Destinations[v] == null) c += OverlapCost;
                    else if (Enumerable.Range(0, 4).Any(k => Grid.Neighbour(v, k) >= 0 && mainSq[Grid.Neighbour(v, k)])) c += BesideCost;
                }
                else if (net[v]) c = Math.Min(c, NetworkCost);
                bool own = s == 1 || s == 2;
                return c + (int)(H(Seed ^ 0x70ad, own ? u : v, own ? s : os) % Jitter);
            }
            (int[] dist, int[] prev) Dijkstra(IReadOnlyCollection<int> srcs, bool main)
            {
                var dist = Enumerable.Repeat(int.MaxValue, n).ToArray();
                var prev = Enumerable.Repeat(-1, n).ToArray();
                var done = new bool[n];
                var isSrc = new bool[n];
                foreach (int s in srcs)
                {
                    dist[s] = 0;
                    isSrc[s] = true;
                }
                while (true)
                {
                    int u = -1, bd = int.MaxValue;
                    for (int k = 0; k < n; k++)
                        if (!done[k] && dist[k] < bd)
                        {
                            bd = dist[k];
                            u = k;
                        }
                    if (u < 0) break;
                    done[u] = true;
                    // A square a river or canyon runs through with no crossing is a dead end: reached, never passed through.
                    if (!Passable(u) && !isSrc[u]) continue;
                    for (int s = 0; s < 4; s++)
                    {
                        int v = Grid.Neighbour(u, s);
                        if (v < 0) continue;
                        if (isSrc[u] && !Passable(u) && entry[u] >= 0 && v != entry[u]) continue;
                        int c = Cost(u, s, main);
                        if (c < 0) continue;
                        if (bd + c < dist[v])
                        {
                            dist[v] = bd + c;
                            prev[v] = u;
                        }
                    }
                }
                return (dist, prev);
            }
            List<int> Trace(int[] prev, int t, Func<int, bool> isSrc)
            {
                var p = new List<int> { t };
                while (!isSrc(p[p.Count - 1])) p.Add(prev[p[p.Count - 1]]);
                p.Reverse();
                return p;
            }
            void AddRoad(PlanRoad r)
            {
                Roads.Add(r);
                foreach (int i in r.Path)
                {
                    net[i] = true;
                    if (r.IsMain) mainSq[i] = true;
                }
                foreach (int e in new[] { 0, r.Path.Count - 1 })
                    if (!Passable(r.Path[e]) && r.Path.Count > 1) entry[r.Path[e]] = r.Path[e == 0 ? 1 : r.Path.Count - 2];
            }
            var majors = dests.Where(d => Destinations[d].Kind == "castle" || Destinations[d].Kind == "village").ToList();
            bool big = Inputs.Size >= 12;
            int cap = Inputs.MainRoadCap, minLen = big ? 5 : LeastRoadSquares;
            var par = dests.ToDictionary(d => d, d => d);
            int Find(int x)
            {
                while (par[x] != x)
                {
                    par[x] = par[par[x]];
                    x = par[x];
                }
                return x;
            }
            var deg = new Dictionary<int, int>();
            var mains = new List<PlanRoad>();
            int BesideRun(List<int> p, HashSet<int> other)
            {
                var own = new HashSet<int>(p);
                int run = 0, max = 0;
                foreach (int q in p)
                {
                    if (other.Contains(q)) { run = 0; continue; }
                    bool beside = Enumerable.Range(0, 4).Any(k => Grid.Neighbour(q, k) >= 0 && other.Contains(Grid.Neighbour(q, k)) && !own.Contains(Grid.Neighbour(q, k)));
                    run = beside ? run + 1 : 0;
                    max = Math.Max(max, run);
                }
                return max;
            }
            int OverlapRun(List<int> p, HashSet<int> other)
            {
                int run = 0, max = 0;
                foreach (int q in p)
                {
                    run = other.Contains(q) && Destinations[q] == null ? run + 1 : 0;
                    max = Math.Max(max, run);
                }
                return max;
            }
            void TryMains(List<int> pool, int minFar)
            {
                var pairs = new List<(int a, int b, long v)>();
                for (int x = 0; x < pool.Count; x++)
                    for (int y = x + 1; y < pool.Count; y++)
                    {
                        int a = pool[x], b = pool[y], d = Grid.Distance(a, b);
                        if (d < minFar) continue;
                        bool castle = Destinations[a].Kind == "castle" || Destinations[b].Kind == "castle";
                        bool port = Landmarks.Any(l => l.Port && (l.Square == a || l.Square == b));
                        pairs.Add((a, b, d * 100L + (castle ? 150 : 0) + (port ? 100 : 0) + (Unit(Seed ^ 0x3a1, a, b) * 50 >> 16)));
                    }
                pairs = pairs.OrderByDescending(p => p.v).ThenBy(p => p.a).ThenBy(p => p.b).ToList();
                foreach (var (a, b, _) in pairs)
                {
                    if (mains.Count >= cap) break;
                    if (Find(a) == Find(b) || deg.GetValueOrDefault(a) >= 3 || deg.GetValueOrDefault(b) >= 3) continue;
                    var (dist, prev) = Dijkstra(new[] { a }, true);
                    if (dist[b] == int.MaxValue) continue;
                    var path = Trace(prev, b, x => x == a);
                    if (path.Count < minLen) continue;
                    var set = new HashSet<int>(path);
                    if (mains.Any(m => BesideRun(path, new HashSet<int>(m.Path)) > 1 || BesideRun(m.Path, set) > 1 || OverlapRun(path, new HashSet<int>(m.Path)) > 1)) continue;
                    var road = new PlanRoad { Kind = MainRoad, From = Destinations[a], To = Destinations[b], Path = path, Cost = dist[b] };
                    mains.Add(road);
                    AddRoad(road);
                    deg[a] = deg.GetValueOrDefault(a) + 1;
                    deg[b] = deg.GetValueOrDefault(b) + 1;
                    foreach (int q in path)
                        if (par.ContainsKey(q))
                        {
                            int r1 = Find(q), r2 = Find(a);
                            if (r1 != r2) par[Math.Max(r1, r2)] = Math.Min(r1, r2);
                        }
                }
            }
            TryMains(majors, big ? 4 : 2);
            if (mains.Count == 0) TryMains(dests, 2);
            if (mains.Count == 0) TryMains(dests, 1);
            if (Roads.Count == 0)
            {
                int seed0 = dests.Where(d => Destinations[d].Kind == "castle").Concat(majors).Concat(dests).DefaultIfEmpty(-1).First();
                if (seed0 >= 0) net[seed0] = true;
            }
            var seeded = new HashSet<int>();
            for (int guard = 0; guard < dests.Count + 4; guard++)
            {
                var left = dests.Where(d => !net[d]).ToList();
                if (left.Count == 0) break;
                var srcs = Enumerable.Range(0, n).Where(i => net[i] && Passable(i)).ToList();
                var srcSet = new HashSet<int>(srcs);
                var (dist, prev) = Dijkstra(srcs, false);
                int t = -1, bd = int.MaxValue;
                foreach (int d in left)
                    if (dist[d] < bd)
                    {
                        bd = dist[d];
                        t = d;
                    }
                if (t < 0)
                {
                    // Nothing left is reachable from this network: a village that can't be reached starts its own.
                    int v = left.Where(d => Destinations[d].Kind == "village" && !seeded.Contains(d) && Passable(d)).DefaultIfEmpty(-1).First();
                    if (v < 0) break;
                    seeded.Add(v);
                    net[v] = true;
                    continue;
                }
                var path = Trace(prev, t, srcSet.Contains);
                var to = Destinations[t];
                int start = path[0];
                var from = Destinations[start] ?? new PlanDestination { Square = start, Kind = "junction" };
                bool trail = to.Kind == "cave" || to.Kind == "shore" || to.Kind == "pass";
                // A road that only goes on from where another lesser road ends is the same road, longer.
                var before = Roads.FirstOrDefault(r => !r.IsMain && r.Path[r.Path.Count - 1] == start);
                if (before != null && Roads.Count(r => r.Path.Contains(start)) == 1)
                {
                    before.Path.AddRange(path.Skip(1));
                    before.To = to;
                    before.Cost += dist[t];
                    if (!trail) before.Kind = "lesser road";
                    foreach (int i in path) net[i] = true;
                    if (!Passable(t)) entry[t] = path[path.Count - 2];
                    continue;
                }
                AddRoad(new PlanRoad { Kind = trail ? "trail" : "lesser road", From = from, To = to, Path = path, Cost = dist[t] });
            }
            DropStubs();
            // 5. The slots a road crosses sides by come from its route.
            var slotOf = new Dictionary<int, (int slot, EdgeFeature a, EdgeFeature b)>();
            foreach (var r in Roads)
            {
                bool main = r.IsMain, trail = r.Kind == "trail";
                for (int k = 0; k + 1 < r.Path.Count; k++)
                {
                    int i = r.Path[k], j = r.Path[k + 1], s = Grid.SideTo(i, j), os = (s + 2) % 4;
                    int key = s == 1 || s == 2 ? i * 4 + s : j * 4 + os;
                    if (!slotOf.TryGetValue(key, out var e))
                    {
                        var taken = new HashSet<int>(Features[i][s].Select(f => f.Slot));
                        int slot = Options[i].RoadOffer[s].Concat(Options[j].RoadOffer[os]).Concat(SlotOrder).Where(q => !taken.Contains(q)).DefaultIfEmpty(SlotOrder[0]).First();
                        e = (slot, new EdgeFeature { Kind = EdgeKind.Road, Slot = slot, Main = main, Trail = trail }, new EdgeFeature { Kind = EdgeKind.Road, Slot = slot, Main = main, Trail = trail });
                        slotOf[key] = e;
                        Features[i][s].Add(e.a);
                        Features[j][os].Add(e.b);
                    }
                    else
                    {
                        if (main) e.a.Main = e.b.Main = true;
                        if (!trail) e.a.Trail = e.b.Trail = false;
                    }
                    r.Sides.Add(s);
                    r.Slots.Add(e.slot);
                }
            }
            roadEnd = new (int, int)?[n];
            foreach (var r in Roads)
                foreach (var d in new[] { r.From, r.To })
                    if (Destinations[d.Square] != null)
                        roadEnd[d.Square] = d.SiteX >= 0 ? (d.SiteX, d.SiteY) : Hub[d.Square];
            foreach (var x in Crossings) x.Used = Roads.Any(r => r.Path.Contains(x.Square));
            PlaceWaystations(mains);
            CheckRoads();
        }

        // A network of roads (roads that share a square) spanning fewer than three world squares leads nowhere: drop it.
        void DropStubs()
        {
            var group = Enumerable.Range(0, Roads.Count).ToArray();
            int Root(int a)
            {
                while (group[a] != a) a = group[a] = group[group[a]];
                return a;
            }
            for (int a = 0; a < Roads.Count; a++)
                for (int b = a + 1; b < Roads.Count; b++)
                    if (Roads[a].Path.Intersect(Roads[b].Path).Any()) group[Root(b)] = Root(a);
            var squares = new Dictionary<int, HashSet<int>>();
            for (int a = 0; a < Roads.Count; a++)
            {
                int g = Root(a);
                if (!squares.TryGetValue(g, out var set)) squares[g] = set = new HashSet<int>();
                set.UnionWith(Roads[a].Path);
            }
            var keep = new List<PlanRoad>();
            for (int a = 0; a < Roads.Count; a++) if (squares[Root(a)].Count >= LeastRoadSquares) keep.Add(Roads[a]);
            Roads.Clear();
            Roads.AddRange(keep);
        }

        // Caves: a small destination, most often in the hills or high ground (or where a template asks for one), apart.
        void PlaceCaves()
        {
            int n = Count, land = 0;
            var marked = new HashSet<int>(Landmarks.Select(l => l.Square));
            var cand = new List<(int i, long key)>();
            for (int i = 0; i < n; i++)
            {
                if (!IsLand(i) || Options[i].Sea) continue;
                land++;
                if (!RoadMayEnter(i) || marked.Contains(i) || barRiver[i] || barCanyon[i]) continue;
                long key = Unit(Seed ^ 0xca7e, i, 3) * (Band[i] >= 2 ? 10 : 35);
                if (Options[i].Template.AsksFor(CaveIntent)) key /= 2;
                cand.Add((i, key));
            }
            cand.Sort((a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.i.CompareTo(b.i));
            int want = Math.Max(1, (land + 7) / 14), gap = Inputs.Size >= 12 ? 3 : 2;
            var caves = new List<int>();
            foreach (var (i, _) in cand)
            {
                if (caves.Count >= want) break;
                if (caves.Any(j => Grid.Distance(j, i) < gap)) continue;
                caves.Add(i);
                var r = new PlanRng(H(Seed, i, 0xcafe));
                var l = new PlanLandmark { Square = i, Kind = "cave", Intent = CaveIntent, Biome = Options[i].Template.Biome, SiteX = 150 + r.Below(277), SiteY = 150 + r.Below(277) };
                (l.ChunkX, l.ChunkY) = ChunkOf(i, l.SiteX, l.SiteY);
                Landmarks.Add(l);
            }
        }

        // The crossings nature allows, per canyon or river (squares it runs through, joined where it crosses a side): one
        // or two bridges a canyon, one to three fords or bridges a river, spread apart and drawn to places and low ground.
        void PlaceCrossings()
        {
            int n = Count, group = 0;
            crossRiver = new bool[n];
            crossCanyon = new bool[n];
            foreach (var kind in new[] { EdgeKind.Canyon, EdgeKind.River })
            {
                var bar = kind == EdgeKind.Canyon ? barCanyon : barRiver;
                var cross = kind == EdgeKind.Canyon ? crossCanyon : crossRiver;
                var seen = new bool[n];
                for (int i0 = 0; i0 < n; i0++)
                {
                    if (!bar[i0] || seen[i0]) continue;
                    var comp = new List<int> { i0 };
                    seen[i0] = true;
                    group++;
                    for (int q = 0; q < comp.Count; q++)
                    {
                        int i = comp[q];
                        for (int s = 0; s < 4; s++)
                        {
                            int j = Grid.Neighbour(i, s);
                            if (j < 0 || seen[j] || !bar[j] || Features[i][s].All(f => f.Kind != kind)) continue;
                            seen[j] = true;
                            comp.Add(j);
                        }
                    }
                    comp.Sort();
                    var cands = comp.Where(RoadMayEnter).ToList();
                    if (cands.Count == 0) continue;
                    int want = Math.Min(cands.Count, CrossingsFor(kind, comp.Count));
                    var chosen = new List<int>();
                    while (chosen.Count < want)
                    {
                        int best = -1;
                        long bs = -1;
                        foreach (int i in cands)
                        {
                            if (chosen.Contains(i)) continue;
                            int md = chosen.Count > 0 ? chosen.Min(j => Grid.Distance(i, j)) : 0;
                            if (chosen.Count > 0 && md < 2) continue;
                            var d = Destinations[i];
                            long sc = md * 100L + (Band[i] == 1 ? 50 : 0) + (Unit(Seed ^ 0xb41d, i, (int)kind) * 50 >> 16);
                            if (d != null) sc += d.Kind == "castle" ? 300 : 150;
                            if (sc > bs)
                            {
                                bs = sc;
                                best = i;
                            }
                        }
                        if (best < 0) break;
                        chosen.Add(best);
                    }
                    foreach (int i in chosen)
                    {
                        cross[i] = true;
                        int off = Crossings.Any(x => x.Square == i) ? 46 : 0;
                        var c = new PlanCrossing
                        {
                            Square = i,
                            Kind = kind == EdgeKind.Canyon || Band[i] > 1 || Lake[i] ? "bridge" : "ford",
                            Over = kind == EdgeKind.Canyon ? "canyon" : "river",
                            SiteX = Hub[i].x + off,
                            SiteY = Hub[i].y - off * 2 / 5,
                            Size = comp.Count,
                            Group = group,
                        };
                        (c.ChunkX, c.ChunkY) = ChunkOf(i, c.SiteX, c.SiteY);
                        Crossings.Add(c);
                    }
                }
            }
        }

        // An inn or a shop every two or three squares along each main road, never on a destination or a crossing.
        void PlaceWaystations(List<PlanRoad> mains)
        {
            var at = new HashSet<int>();
            for (int ri = 0; ri < mains.Count; ri++)
            {
                var road = mains[ri];
                if (!Roads.Contains(road)) continue;
                var r = new PlanRng(H(Seed ^ 0x3a7, ri, road.Path[0]));
                int next = 2 + r.Below(2);
                for (int k = 1; k < road.Path.Count - 1; k++)
                {
                    if (k < next) continue;
                    int i = road.Path[k];
                    if (Destinations[i] != null || at.Contains(i) || Crossings.Any(c => c.Square == i))
                    {
                        next = k + 1;
                        continue;
                    }
                    at.Add(i);
                    string kind = r.Below(2) == 0 ? "inn" : "shop";
                    var (ox, oy) = Around[r.Below(Around.Length)];
                    var w = new PlanSight { Square = i, Kind = kind, SiteX = Hub[i].x + ox, SiteY = Hub[i].y + oy, Road = Roads.IndexOf(road) };
                    (w.ChunkX, w.ChunkY) = ChunkOf(i, w.SiteX, w.SiteY);
                    Waystations.Add(w);
                    next = k + 2 + r.Below(2);
                }
            }
        }

        // No road passes through a river or canyon but at a crossing (a dead end is reached from one bank only), and no
        // main road runs beside another for more than one square.
        void CheckRoads()
        {
            var bank = new SortedDictionary<int, HashSet<int>>();
            foreach (var r in Roads)
            {
                var p = r.Path;
                if (r.IsMain && p.Count < LeastRoadSquares) Problem(p[0], "a main road shorter than three squares");
                for (int q = 0; q < p.Count; q++)
                {
                    if (q + 1 < p.Count && Grid.SideTo(p[q], p[q + 1]) < 0) Problem(p[q], "a road jumps a square");
                    if (Passable(p[q])) continue;
                    if (q != 0 && q != p.Count - 1)
                    {
                        Problem(p[q], "a road crosses a river or canyon with no crossing");
                        continue;
                    }
                    if (!bank.TryGetValue(p[q], out var from)) bank[p[q]] = from = new HashSet<int>();
                    from.Add(p[q == 0 ? 1 : q - 1]);
                }
            }
            foreach (var kv in bank) if (kv.Value.Count > 1) Problem(kv.Key, "roads reach a dead end from both banks");
            var mains = Roads.Where(r => r.IsMain).ToList();
            for (int a = 0; a < mains.Count; a++)
                for (int b = 0; b < mains.Count; b++)
                {
                    if (a == b) continue;
                    var own = new HashSet<int>(mains[a].Path);
                    var other = new HashSet<int>(mains[b].Path);
                    int run = 0, max = 0;
                    foreach (int p in mains[a].Path)
                    {
                        if (other.Contains(p)) { run = 0; continue; }
                        run = Enumerable.Range(0, 4).Any(k => Grid.Neighbour(p, k) >= 0 && other.Contains(Grid.Neighbour(p, k)) && !own.Contains(Grid.Neighbour(p, k))) ? run + 1 : 0;
                        max = Math.Max(max, run);
                    }
                    if (max > 1) Problem(mains[a].Path[0], "two main roads run side by side");
                }
        }

        // A canyon takes one bridge, two when it is long; a river one to three fords or bridges by its length.
        static int CrossingsFor(EdgeKind kind, int squares)
        {
            if (kind == EdgeKind.Canyon) return squares >= 5 ? 2 : 1;
            if (squares <= 3) return 1;
            return squares <= 7 ? 2 : 3;
        }

        // ---------- outcrops ----------

        static (int x, int y) SlotPoint(int s, int slot)
        {
            return Inside(s, slot * ChunkCells + ChunkCells / 2, 0);
        }

        static (int x, int y) Inward(int s) => (-PlanGrid.Dx[s], -PlanGrid.Dy[s]);

        // A seeded sight every six chunks or so along each road, off to one side and within sight of it: a waterfall where
        // a river meets a band step, a view over the sea, a ruin, a rock spire or crag on high ground, a lone great tree.
        void PlaceOutcrops()
        {
            var marks = Landmarks.Select(l => (l.ChunkX, l.ChunkY)).Concat(Waystations.Select(w => (w.ChunkX, w.ChunkY)))
                .Concat(Crossings.Where(c => c.Used).Select(c => (c.ChunkX, c.ChunkY))).ToList();
            for (int ri = 0; ri < Roads.Count; ri++)
            {
                var road = Roads[ri];
                var r = new PlanRng(H(Seed ^ 0x0c40, ri, road.Path[0]));
                int last = road.Path.Count - 1;
                long acc = 0, next = 2 * ChunkCells + r.Below(2 * ChunkCells + 1);
                for (int k = 0; k <= last; k++)
                {
                    int i = road.Path[k];
                    var mid = roadEnd[i].HasValue && (k == 0 || k == last || Destinations[i] != null) ? roadEnd[i].Value : Hub[i];
                    var pts = new List<(int x, int y)>();
                    if (k > 0) pts.Add(SlotPoint((road.Sides[k - 1] + 2) % 4, road.Slots[k - 1]));
                    pts.Add(mid);
                    if (k < last) pts.Add(SlotPoint(road.Sides[k], road.Slots[k]));
                    for (int q = 0; q + 1 < pts.Count; q++)
                    {
                        var (x0, y0) = pts[q];
                        var (x1, y1) = pts[q + 1];
                        long dx = x1 - x0, dy = y1 - y0, len = BiomeMath.Sqrt(dx * dx + dy * dy);
                        if (len == 0) continue;
                        while (acc + len >= next)
                        {
                            long t = next - acc;
                            OutcropAt(ri, i, ((int)(x0 + dx * t / len), (int)(y0 + dy * t / len)), (dx, dy, len), r, marks);
                            next += OutcropEvery * ChunkCells * (80 + r.Below(41)) / 100;
                        }
                        acc += len;
                    }
                }
            }
        }

        void OutcropAt(int road, int i, (int x, int y) at, (long x, long y, long len) dir, PlanRng r, List<(int x, int y)> marks)
        {
            var (px, py) = at;
            var (dx, dy, len) = dir;
            int Clamp(long v) => (int)Math.Max(28, Math.Min(SquareCells - 28, v));
            int side = r.Below(2) == 0 ? -1 : 1, off = 70 + r.Below(71);
            int sx = Clamp(px - dy * off * side / len), sy = Clamp(py + dx * off * side / len);
            const long sightCells = (long)Sight * ChunkCells;
            bool Near(int x, int y) => (long)(x - px) * (x - px) + (long)(y - py) * (y - py) <= sightCells * sightCells;
            var falls = new List<(int x, int y)>();
            var views = new List<(int x, int y)>();
            var c = Coasts[i];
            for (int s = 0; s < 4; s++)
            {
                int j = Grid.Neighbour(i, s);
                var (ix, iy) = Inward(s);
                foreach (var f in Features[i][s])
                    if (f.Kind == EdgeKind.River && j >= 0 && Band[j] > 0 && Band[j] != Band[i])
                    {
                        var (qx, qy) = SlotPoint(s, f.Slot);
                        if (Near(qx + ix * 30, qy + iy * 30)) falls.Add((qx + ix * 30, qy + iy * 30));
                    }
                if (c.Open[s])
                {
                    int d = c.Depth[s] * 145 / 100 + 24;
                    var pt = Inside(s, s == 0 || s == 2 ? Clamp(px) : Clamp(py), d);
                    if (Near(pt.Item1, pt.Item2)) views.Add(pt);
                }
            }
            string kind;
            if (falls.Count > 0 && r.Below(4) < 3)
            {
                kind = "waterfall";
                (sx, sy) = falls[r.Below(falls.Count)];
            }
            else if (views.Count > 0 && r.Below(5) < 3)
            {
                kind = "sea view";
                (sx, sy) = views[r.Below(views.Count)];
            }
            else
            {
                var pool = new List<string> { "ruin" };
                if (Band[i] >= 2) pool.AddRange(new[] { "rock spire", "crag" });
                string biome = Options[i].Template.Biome;
                if ((biome == "temperate" || biome == "grassland") && Band[i] <= 2) pool.Add("lone great tree");
                kind = pool[r.Below(pool.Count)];
            }
            if (InSea(i, sx, sy)) return;
            var (cx, cy) = ChunkOf(i, sx, sy);
            foreach (var (mx, my) in marks) if (Grid.ChunkDistance(cx, cy, mx, my) < OutcropSpacing) return;
            marks.Add((cx, cy));
            Outcrops.Add(new PlanSight { Square = i, Kind = kind, SiteX = sx, SiteY = sy, ChunkX = cx, ChunkY = cy, Road = road });
        }

        /// <summary>Whether a site (cells within square <paramref name="i"/>) is out past the shore, by the coast's depths.</summary>
        public bool InSea(int i, int x, int y)
        {
            if (Band[i] == 0) return true;
            var c = Coasts[i];
            int[] from = { y, SquareCells - x, SquareCells - y, x };
            for (int s = 0; s < 4; s++) if (c.Open[s] && from[s] < c.Depth[s]) return true;
            for (int k = 0; k < 4; k++)
            {
                if (c.Notch[k] == 0) continue;
                int a = PlanGrid.CornerSides[k][0], b = PlanGrid.CornerSides[k][1];
                if ((long)from[a] * from[a] + (long)from[b] * from[b] < (long)c.Notch[k] * c.Notch[k]) return true;
            }
            return false;
        }
    }
}
