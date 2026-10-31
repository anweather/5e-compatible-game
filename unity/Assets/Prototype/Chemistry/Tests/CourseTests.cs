using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The Gauntlet's rules (docs/BACKLOG.md 25; World.Course.cs): spell zones, checkpoints, the clock, and the booths' material verbs.</summary>
    public class CourseTests
    {
        static Rules R => ChemistryTests.R;
        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        static string Last(World w) => w.Log[w.Log.Count - 1].Text;

        static string WorldDir() => Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\')), "world");

        // A little course, run east to west: the gate, a lava pit (water and air), a Misty Step booth, the bell.
        // The same text is in sandbox/test/course.test.ts.
        const string Little = @"{
            ""name"": ""The little gauntlet"", ""area"": [18, 0, 54, 11], ""gate"": [50, 2, 52, 8], ""bell"": [20, 2, 22, 8],
            ""booths"": [
                { ""id"": ""lava-pit"", ""name"": ""lava pit"", ""area"": [40, 0, 49, 11], ""checkpoint"": [48, 5], ""allow"": { ""elements"": [""water"", ""air""] } },
                { ""id"": ""misty"", ""name"": ""Misty Step booth"", ""area"": [30, 0, 39, 11], ""checkpoint"": [38, 5], ""allow"": { ""spells"": [""misty-step""] } }
            ] }";

        static World LittleCourse()
        {
            var w = new World(R, 60, 12, 3, "dirt");
            for (int x = 42; x <= 46; x++) w.At(x, 5).T = "lava";
            w.SetCourse(CourseLayout.FromJson(Little));
            return w;
        }

        [Test]
        public void ColdCrustsLavaThatCracksBackFireThawsAnIceWallIntoASlideAndAVentLifts()
        {
            // Mirrors sandbox/test/course.test.ts.
            var w = new World(R, 24, 8, 3, "dirt");
            for (int x = 3; x <= 7; x++) w.At(x, 3).T = "lava";
            for (int y = 2; y <= 4; y++)
            {
                w.At(12, y).T = "ice-wall";
                for (int x = 13; x <= 16; x++) w.At(x, y).T = "chute";
            }
            w.At(20, 3).T = "updraft";
            w.Cast("frost", 2, 3, 1);
            Assert.That(Enumerable.Range(3, 5).All(x => w.At(x, 3).T == "lava-crust"), "Frost crusts the lava");
            Assert.That(w.IsBlocking(w.At(5, 3)), Is.False, "walkable");
            Run(w, 35);
            Assert.That(w.At(5, 3).T, Is.EqualTo("lava-crust"), "still crusted after 35 ticks");
            Run(w, 1);
            Assert.That(Enumerable.Range(3, 5).All(x => w.At(x, 3).T == "lava"), "cracked back into lava (crust.for 36)");
            Assert.That(w.Log.Any(l => l.Text == "The lava crusts over into rock you can walk on, for now"));
            Assert.That(w.Log.Any(l => l.Text == "The crust cracks and the lava wells back up"));

            w.Cast("fire-bolt", 12, 3, 1);
            Assert.That(w.At(12, 3).T, Is.EqualTo("dirt"), "thawed");
            Assert.That(Last(w), Is.EqualTo("The ice wall melts and its meltwater rushes out"));
            Run(w, 30);
            Assert.That(w.At(12, 2).T, Is.EqualTo("ice-wall"), "only the cell the bolt hit");
            Assert.That(w.At(13, 3).Frozen && w.At(13, 3).Water > 0 && w.IsSlippery(w.At(13, 3)), "the meltwater froze in the chute: a slide");
            Assert.That(w.LiftAt(20, 3), Is.EqualTo(6));
            Assert.That(w.LiftAt(19, 3), Is.EqualTo(0));
            Assert.That(w.LiftAt(-1, 3), Is.EqualTo(0));
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("a11f39a 3"), "the same in the sandbox");
        }

        [Test]
        public void ZonesSmotherWhatTheyDontAllowAndTheClockRunsGateToBellWithCheckpoints()
        {
            // Mirrors sandbox/test/course.test.ts.
            var w = LittleCourse();
            w.Cast("fireball", 44, 5, 1);
            Assert.That(Last(w), Is.EqualTo("The lava pit's charm smothers your Fireball"));
            Assert.That(w.At(44, 5).Fire, Is.EqualTo(0), "nothing landed");
            w.Cast("frost", 47, 5, 3);
            Assert.That(w.At(44, 5).T, Is.EqualTo("lava-crust"), "water then air is allowed");
            w.Cast("frost", 35, 5, 3);
            Assert.That(Last(w), Is.EqualTo("The Misty Step booth's charm smothers your Frost"));
            w.Cast("misty-step", 35, 5, 3);
            Assert.That(Last(w), Does.Not.Contain("smothers"), "Misty Step is allowed by id");
            w.Cast("fire-bolt", 25, 5, 3);
            Assert.That(Last(w), Does.Not.Contain("smothers"), "no zone between the booths and the bell");

            Assert.That(w.CourseRespawn("ann", 45, 5), Is.Null, "not on a run yet");
            w.ApplyCourse(new CourseCommand("ann", 51, 5));
            Assert.That(Last(w), Is.EqualTo("ann is through the gate: the clock is running"));
            Assert.That(w.CourseRespawn("ann", 51, 5), Is.EqualTo((51, 5)), "downed before the first booth: the gate");
            Run(w, 40);
            w.ApplyCourse(new CourseCommand("ann", 45, 5));
            Assert.That(Last(w), Is.EqualTo("ann reaches the checkpoint at the lava pit"));
            Assert.That(w.CourseRespawn("ann", 45, 5), Is.EqualTo((48, 5)));
            w.ApplyCourse(new CourseCommand("bob", 45, 5));
            Assert.That(w.CourseRespawn("bob", 45, 5), Is.Null, "bob never went through the gate");
            Run(w, 50);
            w.ApplyCourse(new CourseCommand("ann", 35, 5));
            w.ApplyCourse(new CourseCommand("ann", 45, 5));
            Assert.That(w.CourseRespawn("ann", 25, 5), Is.EqualTo((38, 5)), "back is no checkpoint");
            Assert.That(w.CourseRespawn("ann", 10, 5), Is.Null, "outside the course: the yard");
            Run(w, 30);
            w.ApplyCourse(new CourseCommand("ann", 21, 5));
            Assert.That(Last(w), Is.EqualTo("ann rings the bell in 20.0 s: a new best!"));
            Assert.That(w.CourseRespawn("ann", 45, 5), Is.Null, "the run is over");

            w.ApplyCourse(new CourseCommand("ann", 50, 5));
            Run(w, 150);
            w.ApplyCourse(new CourseCommand("ann", 20, 3));
            Assert.That(Last(w), Is.EqualTo("ann rings the bell in 25.0 s (best 20.0 s)"));
            Assert.That($"{w.CourseText()}{w.ZoneText()} {w.CourseHash()} {w.CellHash()} {w.RngState}", Is.EqualTo("|rann:-1:-1:120:150|rbob:-1:-1:0:0|zlava-pit:40:0:49:11:water,air:|zmisty:30:0:39:11::misty-step e965d3a4 c036fca2 3"), "the same in the sandbox");
        }

        [Test]
        public void RunsComeThroughASaveAndTheBatchAndAreInTheChecksum()
        {
            var w = LittleCourse();
            string before = w.CastHash();
            new TickBatch(1) { Course = { new CourseCommand("ann", 51, 5) } }.RunOn(w, RuleSet.Plain(R), out _);
            Assert.That(w.CourseRuns["ann"].Running, "the batch's step started the clock");
            Assert.That(w.CastHash(), Is.Not.EqualTo(before), "the run is hashed");
            Assert.That(new World(R, 60, 12, 3, "dirt").CastHash(), Is.Not.EqualTo(LittleCourse().CastHash()), "and so are the zones");
            w.ApplyCourse(new CourseCommand("ann", 45, 5));
            var back = World.Load(w.Save(new Blank()), R, new Blank());
            Assert.That(back.CourseText(), Is.EqualTo(w.CourseText()));
        }

        [Test]
        public void TheGauntletReadsAndStandsOnTheLakesFarShoreClearOfEverythingElse()
        {
            var course = CourseLayout.Load(WorldDir());
            Assert.That(course.Check(R), Is.Empty);
            Assert.That(course.Booths.Select(b => b.Zone.Id), Is.EqualTo(new[] { "lava-pit", "misty-gaps", "ice-wall", "geyser-launch" }));
            Assert.That(CourseLayout.Load(Path.Combine(WorldDir(), "no-such-folder")), Is.Null);
            var puzzle = WorldPuzzles.Load(WorldDir()).All.Single(p => p.Name == "The Gauntlet");
            foreach (var (x, y, _, _) in puzzle.Cells()) Assert.That(course.Area.Contains(x, y), $"({x}, {y}) is in the course");

            var valley = TerrainGrid.Valley();
            var land = LandFile.Read(File.ReadAllText(Path.Combine(WorldDir(), "land.json")), valley.GridW, valley.GridH);
            for (int y = course.Area.Y; y < course.Area.Bottom; y++)
                for (int x = course.Area.X; x < course.Area.Right; x++)
                    Assert.That(land[y * valley.GridW + x], Is.InRange(-2, 12), $"({x}, {y}) is dry, flat shore");
            Assert.That(Enumerable.Range(course.Area.X, course.Area.W).Any(x => land[(course.Area.Y - 10) * valley.GridW + x] < -4), "the lake is just north");

            var golf = GolfCommand.Load(WorldDir()).Value;
            int m = R.Sim.Golf.Margin;
            Assert.That(course.Area.Y, Is.GreaterThan(Math.Max(golf.TeeY, golf.HoleY) + m + 40), "far from spell golf");
            var coil = WorldPuzzles.Load(WorldDir()).All.Single(p => p.Name == "The meadow coil");
            Assert.That(course.Area.Y - coil.Y, Is.GreaterThan(100), "far from the meadow coil's gate");
            foreach (var v in VillageLayout.Load(WorldDir()))
                Assert.That(Math.Max(Math.Abs(course.Area.X + course.Area.W / 2 - v.X), Math.Abs(course.Area.Y - v.Y)), Is.GreaterThan(60), v.Name);
        }

        [Test]
        public void OnTheRealShoreFrostCrustsThePitAndFireTurnsTheWallIntoASlide()
        {
            var valley = TerrainGrid.Valley();
            var w = new World(R, valley.GridW, valley.GridH, 1, "grass");
            WorldPuzzles.Load(WorldDir()).LayInto(w);
            w.SetLand(LandFile.Read(File.ReadAllText(Path.Combine(WorldDir(), "land.json")), valley.GridW, valley.GridH));
            w.SetCourse(CourseLayout.Load(WorldDir()));
            w.Cast("frost", 307, 433, 3);
            Assert.That(Enumerable.Range(300, 7).All(x => w.At(x, 433).T == "lava-crust"), "a crust across the pit");
            w.Cast("fireball", 280, 433, 3);
            Assert.That(Last(w), Does.Not.Contain("smothers"), "fire is allowed at the ice wall");
            Run(w, 60);
            Assert.That(w.At(279, 433).T, Is.Not.EqualTo("ice-wall"), "the wall is gone where it hit");
            int slide = Enumerable.Range(274, 5).SelectMany(x => Enumerable.Range(430, 7).Select(y => w.At(x, y))).Count(c => w.IsSlippery(c));
            Assert.That(slide, Is.GreaterThanOrEqualTo(6), "the chute froze into a slide");
            w.Cast("geyser", 268, 433, 3);
            Assert.That(w.Log.Any(l => l.Text == "The water boils up in a roaring jet"), "the geyser goes off in its pool");
            Assert.That(w.LiftAt(264, 433), Is.GreaterThan(0), "the vents lift you on to the bell");
        }

        sealed class Blank : IChunkSource
        {
            public string Version => "blank";
            public void Make(int x, int y, Cell cell) { }
        }
    }
}
