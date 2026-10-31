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
            Assert.That(w.GateLinks.Select(l => (l.Gate, l.Machine)), Is.EqualTo(new[] { (w.Idx(gate.x, gate.y), w.Idx(coil.x, coil.y)) }), "the gate is linked to its own coil");
            w.Step();
            Assert.That(w.At(gate.x, gate.y).T, Is.EqualTo("gate"), "still shut before the bolt");

            // A coil somewhere else in the valley, powered, doesn't open the puzzle's gate.
            w.At(40, 40).T = "coil";
            w.At(40, 40).Charge = 2;
            w.Step();
            Assert.That(w.At(gate.x, gate.y).T, Is.EqualTo("gate"), "another coil doesn't open it");

            w.Cast("lightning-bolt", pond.x, pond.y, 1);
            Assert.That(w.At(coil.x, coil.y).Charge, Is.GreaterThan(0), "the charge ran through the pond to the coil");
            w.Step();
            Assert.That(w.At(gate.x, gate.y).T, Is.EqualTo("gate_open"));
            Assert.That(w.Log.Any(l => l.Text == "The gate grinds open, powered by the lightning coil"));
        }

        [Test]
        public void TwoLinkedPuzzlesDontOpenEachOther()
        {
            // A coil linked to its gate (World.LinkGate) opens that gate alone, and no gate no one linked; a coil no one
            // linked opens only the gates no one linked. Mirrors sandbox/test/machines.test.ts, with its golden.
            var w = new World(R, 20, 9, 12, "dirt");
            w.At(4, 1).T = "coil";
            w.At(9, 1).T = "gate";
            w.At(4, 7).T = "coil";
            w.At(9, 7).T = "gate";
            w.At(16, 1).T = "coil";
            w.At(16, 7).T = "gate";
            w.LinkGate(9, 1, 4, 1);
            w.LinkGate(9, 7, 4, 7);
            w.Step();

            w.At(4, 1).Charge = 2;
            w.Step();
            Assert.That(w.At(9, 1).T, Is.EqualTo("gate_open"), "A's coil opens A's gate");
            Assert.That(w.At(9, 7).T, Is.EqualTo("gate"), "but not B's");
            Assert.That(w.At(16, 7).T, Is.EqualTo("gate"), "nor the gate no one linked");
            Assert.That(w.GatesOpen, Is.False);
            Assert.That(w.Log.Any(l => l.Text == "The gate grinds open, powered by the lightning coil"));

            w.At(16, 1).Charge = 2;
            w.Step();
            Assert.That(w.At(16, 7).T, Is.EqualTo("gate_open"), "the coil no one linked opens the gate no one linked");
            Assert.That(w.At(9, 7).T, Is.EqualTo("gate"), "and still not B's");

            // Through a save, the links hold.
            var back = World.Load(w.Save(new Blank()), R, new Blank());
            back.At(4, 7).Charge = 2;
            back.Step();
            Assert.That(back.At(9, 7).T, Is.EqualTo("gate_open"), "B's coil opens B's gate");

            for (int i = 0; i < 30; i++) w.Step();
            Assert.That(w.At(9, 1).T, Is.EqualTo("gate"), "A's shuts once its coil's hold runs out");
            Assert.That(w.At(16, 7).T, Is.EqualTo("gate"));
            Assert.That(w.Log.Any(l => l.Text == "The lightning coil falls quiet and its gate slams shut"));
            Assert.That($"{w.CellHash()} {w.RngState} {back.CellHash()}", Is.EqualTo("390cf655 12 82311aad"), "the same in the sandbox");
        }

        [Test]
        public void APuzzleGateMustOpenByOneMachine()
        {
            var bad = WorldPuzzles.FromJson("{ 'puzzles': [ { 'name': 'x', 'at': [0, 0], 'legend': { 'c': { 't': 'coil' }, 'g': { 't': 'gate', 'opensBy': 'c' } }, 'rows': [ 'cgc' ] } ] }");
            Assert.That(bad.Check(R).Single(), Does.Contain("opensBy 'c'"), "two coils for one gate");
            var good = WorldPuzzles.FromJson("{ 'puzzles': [ { 'name': 'x', 'at': [0, 0], 'legend': { 'c': { 't': 'coil' }, 'g': { 't': 'gate', 'opensBy': 'c' } }, 'rows': [ 'c g' ] } ] }");
            Assert.That(good.Check(R), Is.Empty);
            Assert.That(good.All[0].Links().Single(), Is.EqualTo((2, 0, 0, 0)));
        }

        sealed class Blank : IChunkSource
        {
            public string Version => "blank";
            public void Make(int x, int y, Cell cell) { }
        }
    }
}
