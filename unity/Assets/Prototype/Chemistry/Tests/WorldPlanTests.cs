// The world plan (J-080 phase 2, docs/design/world-squares.md section 5): world/bands.json and world/squares/ solved into
// a grid of world squares from a seed, the valley pinned at the south-west with its sides read from its land. The checks
// are the paper prototype's (tools/world-paper/index.html): every shared side and corner agrees, coasts run on across
// sides, rivers run downhill to the sea or a lake, roads go round peaks, cross rivers and canyons only at crossings and
// lead somewhere, copies of a template stand 3 squares apart, a 12 × 12 torus solves well under a second, and a plan's
// hash depends only on its seed and inputs (goldens). WORLD_PLAN_PNG_OUT=<folder> dotnet test core --filter
// "FullyQualifiedName~WorldPlanTests" writes ten seeds as top-down diagrams (WorldPlanPicture).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class WorldPlanTests
    {
        static SquareLibrary library;
        static ValleySides valley;

        static SquareLibrary Lib => library ??= SquareLibrary.Load(LandmarkIntentTests.GameWorldDir());

        // The valley's sides, read from its land at the borders.
        static ValleySides Valley
        {
            get
            {
                if (valley != null) return valley;
                string folder = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "valley");
                var map = MapFile.Load(folder);
                var land = LandFile.Read(File.ReadAllText(Path.Combine(folder, MapFile.LandName)), WorldPlan.SquareCells, WorldPlan.SquareCells);
                return valley = ValleySides.Read(land, WorldPlan.SquareCells, map.SeaLevel, 0.125, Lib);
            }
        }

        static WorldPlan Nine(string seed) => WorldPlan.Solve(PlanInputs.For(seed, 3, PlanWrap.None, Valley), Lib);

        static WorldPlan Big(string seed, PlanWrap wrap) => WorldPlan.Solve(PlanInputs.For(seed, 12, wrap, Valley), Lib);

        static IEnumerable<string> Seeds(int count) => Enumerable.Range(1, count).Select(k => "seed-" + k);

        static IEnumerable<WorldPlan> ManyPlans()
        {
            foreach (string s in Seeds(40)) yield return Nine(s);
            foreach (string s in Seeds(6)) yield return Big(s, PlanWrap.EastWest);
            foreach (string s in Seeds(3)) yield return Big(s, PlanWrap.Torus);
        }

        static string Where(WorldPlan p, int i) => $"{p.Inputs.Seed} {p.Inputs.Size}² {p.Inputs.Wrap} ({p.Grid.X(i)}, {p.Grid.Y(i)})";

        [Test]
        public void TheLibraryHasTwelveTemplatesAndAFillerPerBand()
        {
            var lib = Lib;
            Assert.That(lib.BandIds, Is.EqualTo(new[] { "sea", "low", "hills", "high", "peaks" }));
            var curated = lib.Templates.Where(t => !t.Filler && !t.Pinned && t.Wall == 0 && !t.Bands.Contains(0)).ToList();
            Assert.That(curated.Count, Is.GreaterThanOrEqualTo(12), "the curated land templates");
            foreach (var t in lib.Templates)
            {
                Assert.That(t.Landmarks.Count, Is.LessThanOrEqualTo(2), t.Id);
                foreach (var l in t.Landmarks) Assert.That(l.Intent, Is.Not.Null.And.Not.Empty, t.Id);
            }
            for (int b = 0; b < lib.Lands; b++) Assert.That(lib.FillerOption[b], Is.GreaterThanOrEqualTo(0), lib.BandIds[b]);
            Assert.That(lib.WallOption[1], Is.GreaterThanOrEqualTo(0), "the ice wall");
            Assert.That(lib.WallOption[2], Is.GreaterThanOrEqualTo(0), "the dune sea");
            Assert.That(lib.ValleyOption, Is.GreaterThanOrEqualTo(0));
            foreach (int b in Enumerable.Range(0, lib.Lands)) Assert.That(lib.ByBand[b], Is.Not.Empty, $"{lib.BandIds[b]} has options");
        }

        [Test]
        public void ATurnMovesASlotRoundTheSquare()
        {
            // Slots run west to east on n and s, north to south on e and w: a quarter turn clockwise takes n's west end to
            // e's north end, and e's north end to s's east end.
            Assert.That(Lib.TurnSide(0, 0, 1, out int a), Is.EqualTo(1));
            Assert.That(a, Is.EqualTo(0));
            Assert.That(Lib.TurnSide(1, 0, 1, out int b), Is.EqualTo(2));
            Assert.That(b, Is.EqualTo(8));
            Assert.That(Lib.TurnSide(2, 3, 4, out int c), Is.EqualTo(2), "four turns go all the way round");
            Assert.That(c, Is.EqualTo(3));
        }

        [Test]
        public void TheGridWrapsThroughOneFunction()
        {
            var flat = new PlanGrid(4, 4, PlanWrap.None);
            Assert.That(flat.Neighbour(0, 3), Is.EqualTo(-1));
            Assert.That(flat.Neighbour(0, 0), Is.EqualTo(-1));
            var ew = new PlanGrid(4, 4, PlanWrap.EastWest);
            Assert.That(ew.Neighbour(0, 3), Is.EqualTo(3), "west of the first column is the last");
            Assert.That(ew.Neighbour(0, 0), Is.EqualTo(-1), "north stays firm");
            Assert.That(ew.Distance(0, 3), Is.EqualTo(1));
            var torus = new PlanGrid(4, 4, PlanWrap.Torus);
            Assert.That(torus.Neighbour(0, 0), Is.EqualTo(12), "north of the first row is the last");
            Assert.That(torus.Diagonal(0, 0), Is.EqualTo(15));
            Assert.That(torus.Distance(0, 15), Is.EqualTo(1));
            Assert.That(torus.ChunkDistance(0, 0, 4 * WorldPlan.ChunksPerSquare - 1, 0), Is.EqualTo(1));
        }

        [Test]
        public void TheValleysSidesComeFromItsLand()
        {
            var v = Valley;
            for (int s = 0; s < 4; s++)
            {
                Assert.That(v.Sea[s] || v.Band[s] >= 1, $"side {s} is sea or has a land band");
                Assert.That(v.SeaSlots[s].Length, Is.EqualTo(Lib.Slots));
            }
            TestContext.Out.WriteLine("VALLEY SIDES sea " + string.Join(",", v.Sea) + " bands " + string.Join(",", v.Band));
            var p = Nine("valley-1");
            Assert.That(p.Options[p.Valley].Template.Pinned, "the valley is pinned");
            Assert.That(p.Grid.X(p.Valley), Is.EqualTo(1), "the valley is at the west of the land");
            Assert.That(p.Grid.Y(p.Valley), Is.EqualTo(p.Inputs.Size - 1), "and at its south");
            for (int s = 0; s < 4; s++)
            {
                int j = p.Grid.Neighbour(p.Valley, s);
                if (j < 0) continue;
                Assert.That(p.Band[j] == 0, Is.EqualTo(v.Sea[s]), $"the valley's side {s} faces the sea as its land does");
            }
        }

        [Test]
        public void EveryPlanPassesItsChecks()
        {
            foreach (var p in ManyPlans())
            {
                Assert.That(p.Problems, Is.Empty, $"{p.Inputs.Seed} {p.Inputs.Size}² {p.Inputs.Wrap}");
                Assert.That(p.Options.All(o => o != null));
            }
        }

        [Test]
        public void TouchingSquaresDifferByAtMostOneBand()
        {
            foreach (var p in ManyPlans())
                for (int i = 0; i < p.Count; i++)
                {
                    if (p.Wall[i] != 0) continue;
                    for (int q = 0; q < 8; q++)
                    {
                        int j = q < 4 ? p.Grid.Neighbour(i, q) : p.Grid.Diagonal(i, q - 4);
                        if (j < 0 || p.Wall[j] != 0) continue;
                        Assert.That(Math.Abs(p.Band[i] - p.Band[j]), Is.LessThanOrEqualTo(1), Where(p, i));
                    }
                }
        }

        [Test]
        public void CopiesOfATemplateStandThreeSquaresApart()
        {
            foreach (var p in ManyPlans())
                for (int i = 0; i < p.Count; i++)
                {
                    var t = p.Options[i].Template;
                    if (t.Repeats) continue;
                    for (int j = i + 1; j < p.Count; j++)
                        if (p.Options[j].Template == t)
                            Assert.That(p.Grid.Distance(i, j), Is.GreaterThanOrEqualTo(3), $"{t.Id} at {Where(p, i)} and {Where(p, j)}");
                }
        }

        // The nine: the 3 × 3 with the valley at its south-west. (The valley's land is hills to its west and north, so the
        // squares beside it there are land too, but outside the nine.)
        [Test]
        public void TheNineHasNoRepeats()
        {
            foreach (string s in Seeds(40))
            {
                var p = Nine(s);
                int vx = p.Grid.X(p.Valley), vy = p.Grid.Y(p.Valley);
                var ids = new List<string>();
                for (int y = vy - 2; y <= vy; y++)
                    for (int x = vx; x <= vx + 2; x++)
                    {
                        var t = p.Options[p.Grid.Index(x, y)].Template;
                        if (!t.Repeats) ids.Add(t.Id);
                    }
                Assert.That(ids, Is.Unique, s);
            }
        }

        [Test]
        public void CoastsRunOnAcrossEverySide()
        {
            foreach (var p in ManyPlans())
                for (int i = 0; i < p.Count; i++)
                    foreach (int s in new[] { 1, 2 })
                    {
                        int j = p.Grid.Neighbour(i, s);
                        if (j < 0 || p.Band[i] == 0 || p.Band[j] == 0 || p.Wall[i] != 0 || p.Wall[j] != 0) continue;
                        var (seaA, offA) = p.CoastProfile(i, s);
                        var (seaB, offB) = p.CoastProfile(j, (s + 2) % 4);
                        Assert.That(seaA, Is.EqualTo(seaB), $"{Where(p, i)} side {s}: the same slots are sea");
                        Assert.That(offA, Is.EqualTo(offB), $"{Where(p, i)} side {s}: the shore meets at the same offset");
                    }
        }

        [Test]
        public void RiversFlowDownhillAndEndAtTheSeaOrALake()
        {
            foreach (var p in ManyPlans())
                for (int i = 0; i < p.Count; i++)
                {
                    if (p.Band[i] == 0 || p.Wall[i] != 0) continue;
                    int into = 0, outOf = 0;
                    for (int s = 0; s < 4; s++)
                        foreach (var f in p.Features[i][s].Where(f => f.Kind == EdgeKind.River))
                        {
                            int j = p.Grid.Neighbour(i, s);
                            if (f.Flow < 0) outOf++;
                            else into++;
                            if (f.Flow < 0) Assert.That(p.Band[j], Is.LessThanOrEqualTo(p.Band[i]), $"{Where(p, i)} flows out downhill");
                            else Assert.That(p.Band[j], Is.GreaterThanOrEqualTo(p.Band[i]), $"{Where(p, i)} is fed from above");
                        }
                    if (into > 0 && outOf == 0) Assert.That(p.Lake[i], $"{Where(p, i)}: a river that goes nowhere ends in a lake");
                    if (outOf > 0 && into == 0) Assert.That(p.Band[i] >= 2 || p.Lake[i], $"{Where(p, i)}: a river springs in the hills or a lake");
                }
        }

        [Test]
        public void RoadsLeadSomewhereAndGoRoundThePeaks()
        {
            int mains = 0;
            foreach (var p in ManyPlans())
            {
                Assert.That(p.Roads.Count(r => r.IsMain), Is.LessThanOrEqualTo(p.Inputs.MainRoadCap), p.Inputs.Seed);
                // A road network spans at least three world squares.
                var on = new HashSet<int>();
                foreach (var r in p.Roads)
                {
                    on.UnionWith(r.Path);
                    if (r.IsMain)
                    {
                        mains++;
                        Assert.That(r.Path.Count, Is.GreaterThanOrEqualTo(3), $"{p.Inputs.Seed}: a main road spans three squares");
                        Assert.That(r.From.Square, Is.Not.EqualTo(r.To.Square));
                    }
                    Assert.That(r.Sides.Count, Is.EqualTo(r.Path.Count - 1));
                    for (int k = 0; k < r.Path.Count; k++)
                    {
                        int i = r.Path[k];
                        Assert.That(p.Band[i], Is.InRange(1, 3), $"{Where(p, i)}: no road on the sea or the peaks");
                        if (k == 0) continue;
                        Assert.That(Math.Abs(p.Band[i] - p.Band[r.Path[k - 1]]), Is.LessThanOrEqualTo(1), $"{Where(p, i)}: a band step at a time");
                        int s = r.Sides[k - 1];
                        Assert.That(p.Features[r.Path[k - 1]][s].Any(f => f.Kind == EdgeKind.Road && f.Slot == r.Slots[k - 1]), "the road crosses at its slot");
                    }
                }
                foreach (var group in Networks(p)) Assert.That(group.Count, Is.GreaterThanOrEqualTo(3), $"{p.Inputs.Seed}: no stubs");
                foreach (var w in p.Waystations) Assert.That(on.Contains(w.Square), "a waystation stands on a road");
                foreach (var o in p.Outcrops) Assert.That(p.InSea(o.Square, o.SiteX, o.SiteY), Is.False, "an outcrop stands on land");
            }
            Assert.That(mains, Is.GreaterThan(0), "some plans have main roads");
        }

        static List<HashSet<int>> Networks(WorldPlan p)
        {
            var groups = new List<HashSet<int>>();
            foreach (var r in p.Roads)
            {
                var set = new HashSet<int>(r.Path);
                foreach (var g in groups.Where(g => g.Overlaps(set)).ToList())
                {
                    set.UnionWith(g);
                    groups.Remove(g);
                }
                groups.Add(set);
            }
            return groups;
        }

        [Test]
        public void ThePlanIsTheSameEveryTime()
        {
            var a = Nine("again");
            var b = Nine("again");
            Assert.That(a.Hash, Is.EqualTo(b.Hash));
            var hashes = Seeds(20).Select(s => Nine(s).Hash).ToList();
            Assert.That(hashes.Distinct().Count(), Is.GreaterThan(15), "seeds make different plans");
            var c = Big("again", PlanWrap.EastWest);
            Assert.That(Big("again", PlanWrap.EastWest).Hash, Is.EqualTo(c.Hash));
        }

        // Goldens: a change that alters a plan is made on purpose (a new WorldPlan.Version), or the goldens are re-picked.
        [TestCase("valley-1", 3, PlanWrap.None, 0x2a58d027u)]
        [TestCase("seed-7", 3, PlanWrap.None, 0x0003a9c9u)]
        [TestCase("seed-2", 12, PlanWrap.EastWest, 0xc1bc5b9cu)]
        [TestCase("seed-3", 12, PlanWrap.Torus, 0xd81ce0e6u)]
        public void ItsHashIsGolden(string seed, int size, PlanWrap wrap, uint expected)
        {
            var p = WorldPlan.Solve(PlanInputs.For(seed, size, wrap, Valley), Lib);
            TestContext.Out.WriteLine($"PLAN GOLDEN {seed} {size} {wrap} 0x{p.Hash:x8}");
            Assert.That(p.Hash, Is.EqualTo(expected));
        }

        [Test]
        [NonParallelizable] // it times itself: it needs the machine to itself
        public void ATwelveByTwelveTorusSolvesWellUnderASecond()
        {
            Big("warm-up", PlanWrap.Torus);
            var watch = Stopwatch.StartNew();
            const int runs = 3;
            for (int k = 0; k < runs; k++) Big("timed-" + k, PlanWrap.Torus);
            long ms = watch.ElapsedMilliseconds / runs;
            var p = Big("timed-0", PlanWrap.Torus);
            TestContext.Out.WriteLine($"PLAN TIME 12x12 torus {ms} ms a plan; {p.Picks} picks, {p.Backtracks} backtracks, {p.Fillers.Count} fillers, {p.Roads.Count} roads");
            Assert.That(ms, Is.LessThan(1000));
        }

        [Test]
        public void TenSeedsAsPictures()
        {
            var plans = Seeds(9).Select(Nine).Select(p => (p, 96)).Append((Big("seed-1", PlanWrap.EastWest), 48)).ToList();
            string folder = Environment.GetEnvironmentVariable("WORLD_PLAN_PNG_OUT");
            foreach (var (p, px) in plans)
            {
                var cv = WorldPlanPicture.Draw(p, px);
                Assert.That(cv.Rgb.Length, Is.EqualTo(p.Grid.W * px * p.Grid.H * px * 3));
                if (string.IsNullOrEmpty(folder)) continue;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"plan-{p.Inputs.Seed}-{p.Inputs.Size}.png");
                File.WriteAllBytes(path, LandmarkPicture.Png(cv.Rgb, cv.W, cv.H));
                string templates = string.Join(" ", Enumerable.Range(0, p.Count).Where(i => p.Wall[i] == 0 && p.Band[i] > 0)
                    .Select(i => $"{p.Options[i].Template.Id}/{p.Options[i].Turn}"));
                TestContext.Out.WriteLine($"PLAN PICTURE {path} 0x{p.Hash:x8}: {p.Landmarks.Count} landmarks, {p.Roads.Count} roads " +
                    $"({p.Roads.Count(r => r.IsMain)} main), {p.Crossings.Count} crossings, {p.Fillers.Count} fillers; {templates}");
            }
        }
    }
}
