using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The valley's puzzles (world/puzzles.json, WorldPuzzles.cs): the meadow coil by spell golf's hole.</summary>
    public class PuzzleTests
    {
        static Rules R => ChemistryTests.R;

        static string WorldDir() => Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\')), "world");

        static Puzzle MeadowCoil => WorldPuzzles.Load(WorldDir()).All.Single(p => p.Name == "The meadow coil");

        [Test]
        public void ThePuzzlesReadAndLayOnlyKnownTerrain()
        {
            var puzzles = WorldPuzzles.Load(WorldDir());
            Assert.That(puzzles.All, Is.Not.Empty);
            Assert.That(puzzles.Check(R), Is.Empty);
            Assert.That(WorldPuzzles.Load(Path.Combine(WorldDir(), "no-such-folder")).All, Is.Empty, "no file: no puzzles");
            var e = Assert.Throws<FormatException>(() => WorldPuzzles.FromJson("{ 'puzzles': [ { 'name': 'x', 'at': [0, 0], 'rows': [ 'q' ] } ] }"));
            Assert.That(e.Message, Does.Contain("'q'"));
        }

        [Test]
        public void TheMeadowCoilIsInTheValleyNearTheCourseButOffIt()
        {
            var p = MeadowCoil;
            var valley = TerrainGrid.Valley();
            Assert.That(valley.InGrid(p.X, p.Y) && valley.InGrid(p.X + p.Width - 1, p.Y + p.Height - 1));
            var course = GolfCommand.Load(WorldDir()).Value;
            int m = R.Sim.Golf.Margin;
            int x0 = Math.Min(course.TeeX, course.HoleX) - m, x1 = Math.Max(course.TeeX, course.HoleX) + m;
            int y0 = Math.Min(course.TeeY, course.HoleY) - m, y1 = Math.Max(course.TeeY, course.HoleY) + m;
            foreach (var (x, y, _, _) in p.Cells())
                Assert.That(x < x0 || x > x1 || y < y0 || y > y1, $"({x}, {y}) is on the course");
            int near = p.Cells().Min(c => Math.Max(Math.Abs(c.x - course.HoleX), Math.Abs(c.y - course.HoleY)));
            Assert.That(near, Is.LessThan(40), "within sight of the hole");
            foreach (var v in VillageLayout.Load(WorldDir()))
                Assert.That(Math.Max(Math.Abs(p.X - v.X), Math.Abs(p.Y - v.Y)), Is.GreaterThan(40), v.Name);
        }

        [Test]
        public void ABoltIntoThePondOpensThePlacedGate()
        {
            var w = new World(R, 576, 576, 1, "grass");
            WorldPuzzles.Load(WorldDir()).LayInto(w);
            var cells = MeadowCoil.Cells().ToList();
            var pond = cells.First(c => c.water > 0);
            var coil = cells.Single(c => c.t == "coil");
            var gate = cells.Single(c => c.t == "gate");
            Assert.That(w.At(gate.x, gate.y).T, Is.EqualTo("gate"), "laid shut");
            Assert.That(w.At(pond.x, pond.y).Water, Is.EqualTo(4));
            w.Step();
            Assert.That(w.GatesOpen, Is.False, "still shut before the bolt");

            w.Cast("lightning-bolt", pond.x, pond.y, 1);
            Assert.That(w.At(coil.x, coil.y).Charge, Is.GreaterThan(0), "the charge ran through the pond to the coil");
            w.Step();
            Assert.That(w.GatesOpen, Is.True);
            Assert.That(w.At(gate.x, gate.y).T, Is.EqualTo("gate_open"));
            Assert.That(w.Log.Any(l => l.Text == "The gate grinds open, powered by the lightning coil"));
        }
    }
}
