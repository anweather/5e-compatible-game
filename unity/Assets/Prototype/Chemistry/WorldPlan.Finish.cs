// The world plan once its templates are solved (the paper prototype's finishPlan, placePorts, settleFillerRivers and
// riverViolations): the features crossing every side (a filler takes its neighbours'), lakes and the place each
// square's features meet, the landmarks and their sites, a port on each inland sea, then the roads
// (WorldPlan.Roads.cs), the spawns (WorldPlan.Spawns.cs), the plan hash, and the checks a good plan passes: every
// shared side and corner agrees, rivers run downhill from a spring or a lake to the sea or a lake, nothing crosses into
// a wall, and an inland sea is sea with land all round.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public sealed partial class WorldPlan
    {
        public const string CastleIntent = "castle", SpireIntent = "sea-spire", CaveIntent = "cave";
        static readonly string[] SideNames = { "n", "e", "s", "w" };

        void Finish()
        {
            int n = Count;
            Features = new List<EdgeFeature>[n][];
            for (int i = 0; i < n; i++)
            {
                var o = Options[i];
                Features[i] = new List<EdgeFeature>[4];
                for (int s = 0; s < 4; s++) Features[i][s] = new List<EdgeFeature>();
                if (!o.Filler)
                {
                    for (int s = 0; s < 4; s++) Features[i][s].AddRange(o.Features[s].Select(f => f.Copy()));
                    continue;
                }
                if (o.Sea) continue;
                // A filler takes whatever its neighbours bring, a river flowing in where it flows out of them.
                for (int s = 0; s < 4; s++)
                {
                    int j = Grid.Neighbour(i, s);
                    if (j < 0 || Options[j].Filler || Options[j].Sea) continue;
                    foreach (var x in Options[j].Features[(s + 2) % 4])
                    {
                        var f = x.Copy();
                        f.Flow = -x.Flow;
                        Features[i][s].Add(f);
                    }
                }
            }
            SettleFillerRivers();
            Hub = new (int, int)[n];
            Lake = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Hub[i] = (SquareCells / 2 - 70 + (int)(Unit(Seed, i, 91) * 140 >> 16), SquareCells / 2 - 70 + (int)(Unit(Seed, i, 92) * 140 >> 16));
                RiverEnds(i, out int ri, out int ro);
                Lake[i] = (ri > 0 && ro == 0) || (Options[i].Filler && !Options[i].Sea && ro > 0 && ri == 0);
            }
            PlaceLandmarks();
            PlacePorts();
            foreach (var l in Landmarks) (l.ChunkX, l.ChunkY) = ChunkOf(l.Square, l.SiteX, l.SiteY);
            CheckRivers();
            SolveRoads();
            PlaceOutcrops();
            PlaceSpawns();
            CheckSidesAndCorners();
            CheckInlandSeas();
            CheckWalls();
            Hash = HashPlan();
        }

        void RiverEnds(int i, out int ri, out int ro)
        {
            ri = ro = 0;
            foreach (var side in Features[i])
                foreach (var f in side)
                    if (f.Kind == EdgeKind.River)
                    {
                        if (f.Flow > 0) ri++;
                        else ro++;
                    }
        }

        /// <summary>The chunk a site (cells within square <paramref name="i"/>) is in.</summary>
        public (int x, int y) ChunkOf(int i, int siteX, int siteY) =>
            (Grid.X(i) * ChunksPerSquare + siteX / ChunkCells, Grid.Y(i) * ChunksPerSquare + siteY / ChunkCells);

        // A filler copies its neighbours' sides, so a river can reach it from below. Where it would, the crossing is
        // dropped on both sides, as long as the neighbour's template stays whole without it.
        void SettleFillerRivers()
        {
            foreach (int i in Fillers)
            {
                if (Band[i] == 0) continue;
                for (int s = 0; s < 4; s++)
                {
                    int j = Grid.Neighbour(i, s);
                    if (j < 0 || Options[j].Filler || Options[j].Sea) continue;
                    int os = (s + 2) % 4;
                    foreach (var f in Features[i][s].ToArray())
                    {
                        if (f.Kind != EdgeKind.River) continue;
                        bool uphill = f.Flow < 0 ? Band[j] > Band[i] : Band[i] > Band[j];
                        if (!uphill) continue;
                        var without = Features[j].Select((l, t) => (t == os ? l.Where(g => g.Slot != f.Slot) : l).ToArray()).ToArray();
                        if (!SquareLibrary.WholeInside(Options[j].Template, without, Band[j])) continue;
                        Features[j][os].RemoveAll(g => g.Slot == f.Slot);
                        Features[i][s].Remove(f);
                    }
                }
            }
        }

        // ---------- landmarks ----------

        // Not every square that may hold a village keeps one: Towns percent of them do, and at least one in the world.
        void PlaceLandmarks()
        {
            int n = Count;
            long towns = Inputs.Towns * BiomeMath.One / 100;
            long TownRoll(int i) => Unit(Seed ^ 0x70a1, i, 5);
            int firstTown = -1;
            long lowest = long.MaxValue;
            for (int i = 0; i < n; i++)
            {
                string lm = Options[i].Template.Lm;
                if ((lm == "land" || lm == "water") && TownRoll(i) < lowest)
                {
                    lowest = TownRoll(i);
                    firstTown = i;
                }
            }
            for (int i = 0; i < n; i++)
            {
                var t = Options[i].Template;
                string lm = t.Lm;
                if (lm == null) continue;
                if ((lm == "land" || lm == "water") && TownRoll(i) >= towns && i != firstTown) continue;
                var r = new PlanRng(H(Seed, i, 77));
                int sx = 150 + r.Below(277), sy = 150 + r.Below(277);
                if (lm == "castle")
                {
                    var at = t.Landmarks.First(l => l.Intent == CastleIntent).At ?? new[] { SquareCells / 2, SquareCells / 2 };
                    Landmarks.Add(new PlanLandmark { Square = i, Kind = "castle", Intent = CastleIntent, Biome = t.Biome, SiteX = at[0], SiteY = at[1] });
                    continue;
                }
                if (lm == "spire")
                {
                    Landmarks.Add(new PlanLandmark { Square = i, Kind = "spire", Intent = SpireIntent, Biome = t.Biome, SiteX = sx, SiteY = sy });
                    continue;
                }
                string biome = t.Biome;
                var c = Coasts[i];
                int open = Array.IndexOf(c.Open, true);
                if (lm == "water" && Lake[i])
                {
                    biome = "foggy-marsh";
                    (sx, sy) = (Hub[i].x, Hub[i].y + 70);
                }
                else if (lm == "water" && open >= 0)
                {
                    biome = "ocean";
                    (sx, sy) = ShoreSite(i, open, sx, sy);
                }
                Landmarks.Add(new PlanLandmark { Square = i, Kind = FormOf(biome), Intent = VillageIntent, Biome = biome, SiteX = sx, SiteY = sy });
            }
        }

        string FormOf(string biome) => Library.Village != null ? Library.Village.FormFor(biome) : biome;

        // A site 60 cells inland of the shore along side s; (along, along) keeps its place along the side.
        (int x, int y) ShoreSite(int i, int s, int ax, int ay)
        {
            return Inside(s, s == 0 || s == 2 ? ax : ay, Coasts[i].Depth[s] + 60);
        }

        // One town on each inland sea's shore: one that stands there already, else a new fishing village.
        void PlacePorts()
        {
            int n = Count;
            for (int k = 0; k < InlandRegions.Count; k++)
            {
                int id = k + 1;
                var shore = new List<(int i, int s)>();
                for (int i = 0; i < n; i++)
                {
                    if (!IsLand(i) || Band[i] > 3 || Options[i].Sea) continue;
                    for (int s = 0; s < 4; s++)
                    {
                        int j = Grid.Neighbour(i, s);
                        if (j >= 0 && Inland[j] == id)
                        {
                            shore.Add((i, s));
                            break;
                        }
                    }
                }
                if (shore.Count == 0) continue;
                var l = Landmarks.FirstOrDefault(m => m.Kind != "castle" && m.Kind != "spire" && !m.Port && shore.Any(x => x.i == m.Square));
                int side;
                if (l != null) side = shore.First(x => x.i == l.Square).s;
                else
                {
                    var empty = shore.Where(x => Landmarks.All(m => m.Square != x.i)).ToList();
                    var pool = empty.Where(x => !Options[x.i].Filler).ToList();
                    var from = pool.Count > 0 ? pool : empty;
                    if (from.Count == 0) continue;
                    var (at, s2) = from[(int)(H(Seed, k, 0x9a7) % (uint)from.Count)];
                    side = s2;
                    l = new PlanLandmark { Square = at, Kind = FormOf("foggy-marsh"), Intent = VillageIntent, Biome = "foggy-marsh" };
                    Landmarks.Add(l);
                }
                l.Port = true;
                (l.SiteX, l.SiteY) = ShoreSite(l.Square, side, SquareCells / 2, SquareCells / 2);
            }
        }

        /// <summary>The cell <paramref name="depth"/> cells in from side <paramref name="s"/>, <paramref name="along"/> it (n and s run west to east, e and w north to south).</summary>
        public static (int x, int y) Inside(int s, int along, int depth) => s switch
        {
            0 => (along, depth),
            1 => (SquareCells - depth, along),
            2 => (along, SquareCells - depth),
            _ => (depth, along),
        };

        // ---------- checks ----------

        void Problem(int i, string what) => Problems.Add($"({Grid.X(i)}, {Grid.Y(i)}) {Options[i].Template.Id}: {what}");

        // Every river crossing leaves the higher (or equal) band for the lower; it starts at a spring (hills and up) or a
        // lake, and ends at the sea or a lake.
        void CheckRivers()
        {
            for (int i = 0; i < Count; i++)
            {
                if (!IsLand(i) || Options[i].Sea) continue;
                for (int s = 0; s < 4; s++)
                    foreach (var f in Features[i][s])
                    {
                        if (f.Kind != EdgeKind.River) continue;
                        int j = Grid.Neighbour(i, s);
                        if (j < 0) { Problem(i, "a river runs off the world"); continue; }
                        if (Wall[j] != 0) { Problem(i, "a river runs into the wall"); continue; }
                        if (Band[j] == 0 || Options[j].Sea)
                        {
                            if (f.Flow > 0) Problem(i, "a river comes out of the sea");
                            continue;
                        }
                        var m = Features[j][(s + 2) % 4].FirstOrDefault(g => g.Kind == EdgeKind.River && g.Slot == f.Slot);
                        if (m == null || m.Flow == f.Flow) { Problem(i, "a river is broken at " + SideNames[s]); continue; }
                        if (f.Flow < 0 && Band[j] > Band[i]) Problem(i, "a river flows uphill " + SideNames[s]);
                    }
                RiverEnds(i, out int ri, out int ro);
                bool lakeOk = Options[i].Filler || Options[i].Template.RiverEndsInLake;
                if (ri > 0 && ro == 0 && !lakeOk) Problem(i, "a river ends on dry land");
                if (ro > 0 && ri == 0 && Band[i] < 2 && !Lake[i]) Problem(i, "a river springs too low");
            }
        }

        // Every shared side: features meet (same slots and kinds, rivers in to out, never uphill) and the shoreline's
        // offsets agree at both ends; a land side facing the sea is a coast and only a river out crosses it. Every corner:
        // the four squares agree whether it is sea.
        void CheckSidesAndCorners()
        {
            for (int i = 0; i < Count; i++)
                foreach (int s in new[] { 1, 2 })
                {
                    int j = Grid.Neighbour(i, s);
                    if (j < 0) continue;
                    int os = (s + 2) % 4;
                    var a = Options[i];
                    var b = Options[j];
                    bool ok = true;
                    if (Wall[i] != 0 || Wall[j] != 0) continue;
                    if (!a.Sea && !b.Sea)
                    {
                        ok = SidesMeet(Features[i][s], Features[j][os], Band[i], Band[j]);
                        if (EndOffset(i, s, 0) != EndOffset(j, os, 0) || EndOffset(i, s, 1) != EndOffset(j, os, 1)) ok = false;
                        if (Math.Abs(Band[i] - Band[j]) > Library.Step) ok = false;
                    }
                    else if (a.Sea != b.Sea)
                    {
                        int land = a.Sea ? j : i, ls = a.Sea ? os : s;
                        if (!Coasts[land].Open[ls]) ok = false;
                        if (Features[land][ls].Any(f => !(f.Kind == EdgeKind.River && f.Flow < 0))) ok = false;
                    }
                    if (!ok) Problem(i, "its " + SideNames[s] + " side doesn't meet its neighbour's");
                }
            for (int i = 0; i < Count; i++)
            {
                int e = Grid.Neighbour(i, 1), s = Grid.Neighbour(i, 2), se = Grid.Diagonal(i, 2);
                if (e < 0 || s < 0 || se < 0) continue;
                if (Wall[i] != 0 || Wall[e] != 0 || Wall[s] != 0 || Wall[se] != 0) continue;
                bool a = CornerIsSea(i, 2), b = CornerIsSea(e, 3), c = CornerIsSea(s, 1), d = CornerIsSea(se, 0);
                if (!(a == b && b == c && c == d)) Problem(i, "its south-east corner is sea to some and not others");
            }
        }

        static bool SidesMeet(List<EdgeFeature> a, List<EdgeFeature> b, int ba, int bb)
        {
            if (a.Count != b.Count) return false;
            foreach (var f in a)
            {
                var g = b.FirstOrDefault(x => x.Slot == f.Slot);
                if (g == null || g.Kind != f.Kind) return false;
                if (f.Kind == EdgeKind.River && !((f.Flow < 0 && g.Flow > 0 && ba >= bb) || (f.Flow > 0 && g.Flow < 0 && bb >= ba))) return false;
            }
            return true;
        }

        bool CornerIsSea(int i, int k)
        {
            if (Band[i] == 0) return true;
            var c = Coasts[i];
            return c.Open[PlanGrid.CornerSides[k][0]] || c.Open[PlanGrid.CornerSides[k][1]] || c.Notch[k] > 0;
        }

        void CheckInlandSeas()
        {
            for (int k = 0; k < InlandRegions.Count; k++)
                foreach (int i in InlandRegions[k])
                {
                    if (Band[i] != 0) Problem(i, "an inland sea isn't sea");
                    for (int q = 0; q < 8; q++)
                    {
                        int j = q < 4 ? Grid.Neighbour(i, q) : Grid.Diagonal(i, q - 4);
                        if (j < 0 || Wall[j] != 0) Problem(i, "an inland sea touches the world's edge");
                        else if (Band[j] == 0 && Inland[j] != k + 1) Problem(i, "an inland sea joins other sea");
                    }
                }
        }

        void CheckWalls()
        {
            for (int i = 0; i < Count; i++)
            {
                if (Wall[i] != 0) continue;
                for (int s = 0; s < 4; s++)
                {
                    int j = Grid.Neighbour(i, s);
                    if (j >= 0 && Wall[j] != 0 && Features[i][s].Count > 0) Problem(i, $"a {Features[i][s][0].Kind} runs into the wall");
                }
            }
            foreach (var road in Roads)
                foreach (int p in road.Path)
                    if (Wall[p] != 0) Problem(p, "a road on the wall");
        }

        // ---------- what a chunk reads ----------

        /// <summary>
        /// Side <paramref name="side"/> of square <paramref name="i"/> as a coast profile: per slot, whether it is sea, and the
        /// shoreline's offset into the square there in cells (0 where there's no shore). An open side is sea all along at its
        /// depth, easing to the offsets at its ends; a closed side's end slot is sea where a corner notch is.
        /// </summary>
        public (bool[] sea, int[] offset) CoastProfile(int i, int side)
        {
            int slots = Library.Slots;
            var sea = new bool[slots];
            var offset = new int[slots];
            var c = Coasts[i];
            if (Band[i] == 0)
            {
                for (int k = 0; k < slots; k++) sea[k] = true;
                return (sea, offset);
            }
            int e0 = EndOffset(i, side, 0), e1 = EndOffset(i, side, 1);
            if (c.Open[side])
            {
                int d = c.Depth[side];
                for (int k = 0; k < slots; k++)
                {
                    sea[k] = true;
                    // Halfway along, the side's own depth; towards each end, the depth the shore turns the corner at.
                    int half = (slots - 1) / 2;
                    offset[k] = k <= half ? e0 + (d - e0) * k / half : d + (e1 - d) * (k - half) / (slots - 1 - half);
                    if (e0 == 0 && k == 0) offset[k] = d;
                    if (e1 == 0 && k == slots - 1) offset[k] = d;
                }
                return (sea, offset);
            }
            if (e0 > 0) { sea[0] = true; offset[0] = e0; }
            if (e1 > 0) { sea[slots - 1] = true; offset[slots - 1] = e1; }
            return (sea, offset);
        }

        // ---------- the hash ----------

        uint HashPlan()
        {
            var parts = new List<int> { Version, Grid.W, Grid.H, (int)Inputs.Wrap, Valley };
            for (int i = 0; i < Count; i++)
            {
                var o = Options[i];
                var c = Coasts[i];
                parts.Add(Band[i]);
                parts.Add((int)Fnv(o.Template.Id));
                parts.Add(o.Turn);
                for (int s = 0; s < 4; s++)
                {
                    parts.Add(c.Open[s] ? c.Depth[s] : 0);
                    parts.Add(c.Notch[s]);
                    foreach (var f in Features[i][s].OrderBy(f => f.Slot).ThenBy(f => f.Kind))
                        parts.Add((int)f.Kind | f.Slot << 4 | (f.Flow + 1) << 8 | (f.Main ? 1 << 10 : 0) | (f.Trail ? 1 << 11 : 0));
                    parts.Add(-1);
                }
            }
            foreach (var l in Landmarks) parts.AddRange(new[] { l.Square, (int)Fnv(l.Kind), l.Port ? 1 : 0, l.SiteX, l.SiteY });
            foreach (var x in Crossings) parts.AddRange(new[] { x.Square, (int)Fnv(x.Kind), (int)Fnv(x.Over), x.SiteX, x.SiteY, x.Used ? 1 : 0 });
            foreach (var r in Roads)
            {
                parts.Add((int)Fnv(r.Kind));
                parts.AddRange(r.Path);
                parts.AddRange(r.Slots);
            }
            foreach (var w in Waystations.Concat(Outcrops)) parts.AddRange(new[] { w.Square, (int)Fnv(w.Kind), w.SiteX, w.SiteY });
            foreach (var s in Spawns) parts.AddRange(new[] { s.Square, (int)Fnv(s.Near), s.SiteX, s.SiteY, s.ByRoad ? 1 : 0 });
            return HashInts(parts);
        }

        /// <summary>FNV-1a over each int's four bytes, low first.</summary>
        internal static uint HashInts(IEnumerable<int> parts)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (int v in parts)
                    for (int k = 0; k < 4; k++)
                    {
                        h ^= (uint)(v >> (k * 8)) & 255;
                        h *= 16777619;
                    }
                return h;
            }
        }
    }
}
