using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Cells a mod lays (docs/design/events-and-placing.md 2b): a "lay" row of the world section laid at the switch,
    /// skipping cells that aren't open, and taken back exactly by a revert; LaidText, the save, the checks and the budget.
    /// </summary>
    [Category("Simulation")]
    public class LayTests
    {
        static Rules R => ChemistryTests.R;
        static readonly ModBudgets Budgets = ModBudgets.Load(ChemistryTests.RulesDir());

        static readonly (int x, int y)[] Grove = Placement.Pick(Placement.Around(32, 32, 14), 50, 1, (x, y) => true, 5, 7).ToArray();

        static string Lay(string what, System.Collections.Generic.IEnumerable<(int x, int y)> cells, string key = "terrain") =>
            new JObject { ["world"] = new JObject { ["lay"] = new JArray(new JObject { [key] = what, ["cells"] = new JArray(cells.Select(c => new JArray(c.x, c.y))), ["seed"] = 7 }) } }.ToString();

        static Rules With(string mod) => R.WithMods(new[] { ModPatch.Parse(mod) });

        static World Meadow() => new World(R, 64, 64, 5, "dirt");

        static int Count(World w, string terrain) => Enumerable.Range(0, 64 * 64).Count(i => w.At(i % 64, i / 64).T == terrain);

        /// <summary>LayGolden (design section 6): 50 trees laid by a mod, CellHash pinned; reverted, back to the starting hash.</summary>
        [Test]
        public void LayGolden()
        {
            var w = Meadow();
            w.At(Grove[2].x, Grove[2].y).Cover = "ash";
            string start = w.CellHash(), quiet = w.CastHash();
            Assert.That(Grove, Has.Length.EqualTo(50));
            w.ApplyRules(With(Lay("tree", Grove)));
            Assert.That(Count(w, "tree"), Is.EqualTo(50));
            Assert.That(w.LaidCount, Is.EqualTo(50));
            Assert.That(w.CellHash(), Is.EqualTo("267b1217"));
            Assert.That(w.CastHash(), Is.Not.EqualTo(quiet), "what stood under is in the checksum");
            for (int t = 0; t < 30; t++) w.Step();
            Assert.That(Count(w, "tree"), Is.EqualTo(50), "the trees stand");
            w.ApplyRules(R);
            Assert.That(w.CellHash(), Is.EqualTo(start), "reverted, exactly");
            Assert.That(w.CastHash(), Is.EqualTo(quiet));
            Assert.That(w.LaidCount, Is.Zero);
        }

        [Test]
        public void ACellThatIsNoLongerOpenIsSkippedTheSameEverywhereAndLogged()
        {
            var w = Meadow();
            w.At(Grove[0].x, Grove[0].y).Water = 4;
            w.At(Grove[1].x, Grove[1].y).T = "brush";
            w.ApplyRules(With(Lay("tree", Grove)));
            Assert.That(w.LaidCount, Is.EqualTo(48));
            Assert.That(w.Log.Select(l => l.Text), Has.Some.EqualTo("2 of 50 tree had no room"));
            Assert.That(w.At(Grove[0].x, Grove[0].y).Water, Is.EqualTo(4));
            w.ApplyRules(R);
            Assert.That(w.At(Grove[1].x, Grove[1].y).T, Is.EqualTo("brush"), "a revert leaves what it didn't lay");
            Assert.That(Count(w, "tree"), Is.Zero);
        }

        [Test]
        public void ACoverLaysAndAnotherModOnTopKeepsTheTrees()
        {
            var w = Meadow();
            string snow = Lay("snow", Grove.Take(10), "cover").Replace("\"seed\": 7", "\"seed\": 3");
            var trees = ModPatch.Parse(Lay("tree", Grove.Skip(10)));
            w.ApplyRules(R.WithMods(new[] { trees }));
            w.ApplyRules(R.WithMods(new[] { trees, ModPatch.Parse(snow) }));
            Assert.That(Count(w, "tree"), Is.EqualTo(40));
            Assert.That(Grove.Take(10).All(c => w.At(c.x, c.y).Cover == "snow"));
            w.ApplyRules(R.WithMods(new[] { trees }));
            Assert.That(Grove.Take(10).All(c => w.At(c.x, c.y).Cover == null), "the snow goes, the trees stay");
            Assert.That(Count(w, "tree"), Is.EqualTo(40));
        }

        [Test]
        public void WhatStoodUnderSavesAndARevertAfterALoadIsExact()
        {
            var w = Meadow();
            string start = w.CellHash();
            var rules = With(Lay("tree", Grove));
            w.ApplyRules(rules);
            var source = new Flat();
            var loaded = World.Load(w.Save(source), rules, source);
            Assert.That(loaded.CastHash(), Is.EqualTo(w.CastHash()));
            Assert.That(loaded.CellHash(), Is.EqualTo(w.CellHash()));
            loaded.ApplyRules(R);
            Assert.That(loaded.CellHash(), Is.EqualTo(start));
        }

        // A late joiner's view tells the map's trees from the laid ones by what the world began as (the valley's StartTrees).
        [Test]
        public void ALoadedWorldStartedWithoutTheLaidTrees()
        {
            var w = Meadow();
            w.KeepStart();
            var rules = With(Lay("tree", Grove));
            w.ApplyRules(rules);
            var source = new Flat();
            var loaded = World.Load(w.Save(source), rules, source);
            int Trees(World x)
            {
                int n = 0;
                x.StartTerrainOf(0, 0, (i, t) => n += t == "tree" ? 1 : 0);
                return n;
            }
            Assert.That(Count(loaded, "tree"), Is.EqualTo(Grove.Length), "the laid trees stand");
            Assert.That(Trees(w), Is.Zero);
            Assert.That(Trees(loaded), Is.Zero, "but the world began without them");
        }

        sealed class Flat : IChunkSource
        {
            public string Version => "test-flat";
            public void Make(int x, int y, Cell cell) => cell.T = "dirt";
        }

        [Test]
        public void TheChecksAndTheBudgetHoldALayRow()
        {
            string Problems(string mod) => string.Join("\n", ModValidator.Validate(R, Budgets, mod));
            Assert.That(Problems(Lay("tree", Grove)), Is.Empty);
            Assert.That(Problems(Lay("nope", Grove)), Does.Contain("unknown terrain nope"));
            Assert.That(Problems(Lay("nope", Grove, "cover")), Does.Contain("unknown cover nope"));
            Assert.That(Problems("{ \"world\": { \"lay\": [ { \"terrain\": \"tree\", \"cover\": \"snow\", \"cells\": [[1, 1]] } ] } }"), Does.Contain("exactly one of terrain or cover"));
            Assert.That(Problems("{ \"world\": { \"lay\": [ { \"terrain\": \"tree\" } ] } }"), Does.Contain("needs \"cells\""));
            Assert.That(Problems("{ \"world\": { \"lay\": [ { \"terrain\": \"tree\", \"cells\": [[1, 1]], \"at\": 2 } ] } }"), Does.Contain("at isn't a field"));
            var many = Enumerable.Range(0, 257).Select(i => (i % 64, i / 64));
            Assert.That(Problems(Lay("tree", many)), Does.Contain("lays 257 cells"));
            Assert.That(R.ModLays, Is.Empty);
        }
    }
}
