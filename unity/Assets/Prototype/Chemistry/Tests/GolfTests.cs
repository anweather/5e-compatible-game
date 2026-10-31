using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>Spell golf (docs/BACKLOG.md 24; World.Golf.cs): the ball, the strokes and the course in world/golf.json.</summary>
    public class GolfTests
    {
        static Rules R => ChemistryTests.R;
        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        static string WorldDir() => Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\')), "world");

        // A dirt strip with a rise to climb (x 11-12) and a pond across the way (x 17-18), the tee at (4, 5) and the hole at (30, 5).
        static World Strip()
        {
            var w = new World(R, 40, 12, 3, "dirt");
            for (int y = 3; y <= 7; y++)
            {
                w.At(11, y).Height = 1;
                w.At(12, y).Height = 1;
                foreach (int x in new[] { 17, 18 })
                {
                    w.At(x, y).T = "pit";
                    w.At(x, y).Height = -1;
                    w.At(x, y).Water = 3;
                }
            }
            w.ApplyGolf(new GolfCommand(4, 5, 30, 5));
            return w;
        }

        static (int, int) Stroke(World w, string spell, int x, int y)
        {
            w.Cast(spell, x, y, 1);
            Run(w, 24);
            return (w.Golf.X, w.Golf.Y);
        }

        [Test]
        public void ATeeShotAPenaltyACarryOverThePondAndHoledInSix()
        {
            // Gust of Wind pushes the ball (its force push), Raise Earth under it throws it (its force throw); water
            // puts it back on the tee a stroke down. Mirrors sandbox/test/terraform.test.ts.
            var w = Strip();
            Assert.That(Stroke(w, "gust-of-wind", 2, 5), Is.EqualTo((14, 5)), "the tee shot rolls over the rise");
            Assert.That(Stroke(w, "gust-of-wind", 12, 5), Is.EqualTo((4, 5)), "into the pond: back on the tee");
            Assert.That(w.Golf.Strokes, Is.EqualTo(3), "a stroke's penalty");
            Assert.That(Stroke(w, "gust-of-wind", 2, 5), Is.EqualTo((14, 5)));
            Assert.That(Stroke(w, "mold-earth-heap", 14, 5), Is.EqualTo((22, 5)), "Raise Earth throws it over the pond");
            Assert.That(Stroke(w, "gust-of-wind", 20, 5), Is.EqualTo((30, 5)));
            Assert.That(w.Golf.Holed && w.Log.Any(l => l.Text == "Holed in 6!"));
            Assert.That($"{w.GolfText()} {w.GolfHash()} {w.CellHash()} {w.RngState}", Is.EqualTo("|g30:5:6:1:1:0:0:8 afbde760 4fbe03ad 3"), "the same in the sandbox");
        }

        [Test]
        public void TheBallComesThroughASaveAndIsInTheChecksum()
        {
            var w = Strip();
            string before = w.CastHash();
            w.Cast("gust-of-wind", 2, 5, 1);
            Run(w, 3);
            Assert.That(w.Golf.Moving, "still rolling");
            Assert.That(w.CastHash(), Is.Not.EqualTo(before), "the ball is hashed");
            var back = World.Load(w.Save(new Blank()), R, new Blank());
            Assert.That(back.GolfText(), Is.EqualTo(w.GolfText()));
            Run(w, 20);
            Run(back, 20);
            Assert.That(back.GolfText(), Is.EqualTo(w.GolfText()), "and rolls on the same");
            Assert.That(new World(R, 8, 8, 1, "dirt").GolfText(), Is.Empty, "a world without golf hashes as it did");
        }

        [Test]
        public void ItBouncesOffRockRollsDownhillAndOutOfBoundsCostsAStroke()
        {
            var w = new World(R, 40, 12, 3, "dirt");
            w.At(6, 5).T = "stone";
            w.ApplyGolf(new GolfCommand(4, 5, 30, 5));
            w.Cast("gust-of-wind", 2, 5, 1);
            Run(w, 24);
            Assert.That(w.Log.Any(l => l.Text == "The ball bounces back off the rock"));
            Assert.That(w.Golf.X, Is.LessThan(6), "back the way it came");
            // Out of the world to the west: back on the tee, a stroke's penalty.
            Assert.That(w.Log.Any(l => l.Text.StartsWith("The ball is out of bounds", StringComparison.Ordinal)));
            Assert.That((w.Golf.X, w.Golf.Y, w.Golf.Strokes), Is.EqualTo((4, 5, 2)));

            // A roll that runs out at the top of a slope runs on down it: ten cells from the tee is (12, 5), and the ground falls away east of it.
            var hill = new World(R, 30, 10, 3, "dirt");
            for (int x = 13; x < 30; x++) hill.At(x, 5).Height = x == 13 ? -1 : -2;
            hill.ApplyGolf(new GolfCommand(2, 5, 25, 2));
            hill.Cast("gust-of-wind", 0, 5, 1);
            Run(hill, 24);
            Assert.That(hill.Golf.Moving, Is.False);
            Assert.That((hill.Golf.X, hill.Golf.Y), Is.EqualTo((15, 5)), "it ran on down the slope and a cell on at its foot");
        }

        [Test]
        public void TheCourseIsOnOpenGroundClearOfTheVillages()
        {
            var course = GolfCommand.Load(WorldDir());
            Assert.That(course, Is.Not.Null, "world/golf.json");
            var c = course.Value;
            double metres = 1.5 * Math.Sqrt(Math.Pow(c.HoleX - c.TeeX, 2) + Math.Pow(c.HoleY - c.TeeY, 2));
            Assert.That(metres, Is.InRange(60, 100), "tee to hole");
            var valley = TerrainGrid.Valley();
            Assert.That(valley.InGrid(c.TeeX, c.TeeY) && valley.InGrid(c.HoleX, c.HoleY));
            foreach (var v in VillageLayout.Load(WorldDir()))
                foreach (var (x, y) in new[] { (c.TeeX, c.TeeY), (c.HoleX, c.HoleY) })
                    Assert.That(Math.Max(Math.Abs(x - v.X), Math.Abs(y - v.Y)), Is.GreaterThan(40), v.Name);
        }

        [Test]
        public void GustKnocksARaiderBackOnceAndRaiseEarthThrowsOneOffItsFeet()
        {
            // The force the ball takes acts on raiders the same way (spells.json force). Mirrors sandbox/test/terraform.test.ts.
            var w = new World(R, 20, 8, 5, "dirt");
            var hound = w.SpawnRaider("bone-hound", 5, 3, 6);
            var crab = w.SpawnRaider("ash-crab", 10, 6, 9);
            w.Cast("gust-of-wind", 2, 3, 1);
            Assert.That((hound.X, hound.Y), Is.EqualTo((7, 3)), "force.push 2, once: not again on the cells it's knocked onto");
            w.Cast("mold-earth-heap", 10, 6, 0);
            Assert.That((crab.X, crab.Y, crab.StatusOf(World.ThrownStatus)), Is.EqualTo((10, 6, 2)), "force.throw 2");
            Assert.That(w.At(10, 6).Height, Is.EqualTo(1));
            Run(w, 3);
            Assert.That($"{w.CellHash()} {w.RngState} {hound.X},{hound.Y} {crab.StatusOf(World.ThrownStatus)}", Is.EqualTo("65a33bbd 5 7,3 0"), "the same in the sandbox");
        }

        sealed class Blank : IChunkSource
        {
            public string Version => "blank";
            public void Make(int x, int y, Cell cell) { }
        }
    }
}
