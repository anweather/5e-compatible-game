using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Spells that place (docs/design/events-and-placing.md 2a): Placement.Pick, a spell's place of a thing, an
    /// animal, a terrain or a cover, World.Placed lapsing, World.Summoned, the checks and budgets, and the save.
    /// </summary>
    public class PlaceTests
    {
        static Rules R => ChemistryTests.R;
        static readonly ModBudgets Budgets = ModBudgets.Load(ChemistryTests.RulesDir());

        static string Spell(string id, string place) =>
            ("{ 'spells': [ { 'id': '" + id + "', 'group': 'nature', 'name': 'Test " + id + "', 'level': 2, 'shape': 'burst', 'radius': 2, 'look': { 'family': 'blast', 'color': [0.4, 0.8, 0.3] }, 'place': " + place + " } ] }").Replace('\'', '"');

        const string Barrels = "{ 'thing': 'barrel', 'count': 3, 'within': 4, 'spacing': 1, 'lasts': 30 }";
        const string Child = "{ 'animal': 'human-child', 'count': 1, 'within': 0 }";
        const string Trees = "{ 'terrain': 'tree', 'count': 5, 'within': 6, 'spacing': 2, 'onto': ['soil'] }";

        static World With(string mod, uint seed = 11)
        {
            var w = new World(R.WithMods(new[] { ModPatch.Parse(mod) }), 64, 64, seed, "dirt");
            return w;
        }

        static string Cells(System.Collections.Generic.IEnumerable<(int x, int y)> cells) => string.Join(" ", cells.Select(c => $"{c.x},{c.y}"));

        static string Digest(string s)
        {
            uint h = 2166136261;
            foreach (char ch in s) h = unchecked((h ^ ch) * 16777619);
            return h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>PickGolden (design section 6): Pick's cells for fixed seeds and areas, pinned. It's pure, so this is exact.</summary>
        [Test]
        public void PickGolden()
        {
            bool Open(int x, int y) => (x * 7 + y * 3) % 5 != 0;
            var picks = new[]
            {
                Placement.Pick(Placement.Around(20, 20, 6), 8, 0, Open, 1, 0),
                Placement.Pick(Placement.Around(20, 20, 6), 8, 2, Open, 1, 0),
                Placement.Pick(Placement.Around(-5, 40, 12), 50, 1, Open, 7, 99),
                Placement.Pick(Placement.Around(0, 0, 0), 1, 0, (x, y) => true, 3, 5),
                Placement.Pick(Placement.Around(30, 30, 32), 64, 2, Open, unchecked((int)0xdeadbeef), -17),
            };
            Assert.That(Cells(picks[0]), Is.EqualTo("23,17 22,20 14,20 26,20 21,18 20,14 21,19 24,22"));
            Assert.That(Cells(picks[3]), Is.EqualTo("0,0"));
            Assert.That(picks.Select(p => p.Count), Is.EqualTo(new[] { 8, 8, 50, 1, 64 }));
            Assert.That(Digest(string.Join("/", picks.Select(Cells))), Is.EqualTo("55dee1af"));
        }

        [Test]
        public void PickKeepsItsSpacingAndOnlyOpenCells()
        {
            bool Open(int x, int y) => x % 3 != 0;
            var picked = Placement.Pick(Placement.Around(10, 10, 8), 20, 2, Open, 4, 4);
            Assert.That(picked, Is.Not.Empty);
            Assert.That(picked.All(p => Open(p.x, p.y)));
            foreach (var a in picked)
                foreach (var b in picked.Where(b => b != a))
                    Assert.That(System.Math.Max(System.Math.Abs(a.x - b.x), System.Math.Abs(a.y - b.y)), Is.GreaterThan(2));
            Assert.That(Placement.Pick(new[] { (1, 1), (1, 1) }, 5, 0, (x, y) => true, 1, 1), Has.Count.EqualTo(1), "a cell listed twice counts once");
            Assert.That(Placement.Around(0, 0, 32), Has.Count.InRange(3200, 3240), "the design's 3 200 candidates");
        }

        /// <summary>PlaceGolden (design section 6): a spell placing 3 things; CastHash and the ids pinned, gone after lasts.</summary>
        [Test]
        public void PlaceGolden()
        {
            var w = With(Spell("barrels", Barrels));
            int before = w.ThingsNow.Count;
            string quiet = w.CastHash();
            w.Cast("barrels", 30, 30, 0);
            Assert.That(w.Placed, Has.Count.EqualTo(3));
            Assert.That(w.ThingsNow.Count, Is.EqualTo(before + 3));
            Assert.That(w.Placed.All(t => t.Placed && t.Kind == "barrel" && t.Lapses == 30 && !t.FromMod));
            Assert.That(w.Placed.All(t => w.ThingsNow.Find(t.Id) == t));
            Assert.That(string.Join(" ", w.Placed.Select(t => $"{t.Id}@{t.CellX},{t.CellY}/{t.Rotation}")), Is.EqualTo("0,0#placed 0@32,28/180 0,0#placed 1@32,31/180 0,0#placed 2@29,30/90"));
            Assert.That(w.CastHash(), Is.EqualTo("9351f671"));
            for (int t = 0; t < 29; t++) w.Step();
            Assert.That(w.Placed, Has.Count.EqualTo(3), "still there at tick 29");
            w.Step();
            Assert.That(w.Placed, Is.Empty, "lapsed at tick 30");
            Assert.That(w.ThingsNow.Count, Is.EqualTo(before));
            Assert.That(w.CastHash(), Is.Not.EqualTo(quiet), "the actions run stay in the hash");
        }

        [Test]
        public void AWorldThatPlacesNothingHashesAsItDid()
        {
            var w = With(Spell("barrels", Barrels));
            Assert.That(w.PlacedText(), Is.Empty);
            w.Cast("fireball", 30, 30, 0);
            Assert.That(w.PlacedText(), Is.Empty);
            Assert.That(R.Spells.All(s => s.Place == null), "no base spell places");
        }

        [Test]
        public void EveryPeerPicksTheSameAndAnotherSeedPicksOthers()
        {
            string Run(uint seed)
            {
                var w = With(Spell("barrels", Barrels), seed);
                w.Cast("barrels", 30, 30, 0);
                w.Step();
                w.Cast("barrels", 30, 30, 0);
                return string.Join(" ", w.Placed.Select(t => $"{t.Id}@{t.CellX},{t.CellY}")) + " " + w.CastHash() + " " + w.CellHash();
            }
            Assert.That(Run(11), Is.EqualTo(Run(11)));
            Assert.That(Run(12), Is.Not.EqualTo(Run(11)));
        }

        [Test]
        public void PlacedThingsKeepClearOfEachOtherAndTheOldestLapsesFirst()
        {
            var w = With(Spell("barrels", "{ 'thing': 'barrel', 'count': 8, 'within': 8 }".Replace('\'', '"')));
            for (int i = 0; i < 10; i++) w.Cast("barrels", 30, 30, 0);
            Assert.That(w.Placed, Has.Count.EqualTo(World.DefaultMaxPlaced));
            Assert.That(w.Placed.Select(t => (t.CellX, t.CellY)).Distinct().Count(), Is.EqualTo(w.Placed.Count), "one thing a cell");
            Assert.That(w.Placed[0].Id.PlacedN, Is.EqualTo(80 - World.DefaultMaxPlaced), "the first 16 lapsed");
            Assert.That(w.Placed.All(t => t.Lapses == 0), "no lasts: it stays");
        }

        [Test]
        public void ASummonedAnimalComesOutOnTheAimForTheView()
        {
            var w = With(Spell("child-dock", Child));
            w.Cast("child-dock", 20, 22, 0);
            Assert.That(w.Summoned, Has.Count.EqualTo(1));
            var s = w.Summoned[0];
            Assert.That((s.Species, s.X, s.Y, s.N), Is.EqualTo(("human-child", 20, 22, 0)));
            Assert.That(s.Individual & 1u, Is.EqualTo(1u));
            Assert.That(w.ThingsNow.Count, Is.Zero, "an animal is the view's, not a thing");
            w.Step();
            Assert.That(w.Summoned, Is.Empty, "cleared each tick");
            Assert.That(w.SummonedAfter(-1).Select(x => x.N), Is.EqualTo(new[] { 0 }));
            w.Cast("child-dock", 25, 22, 0);
            Assert.That(w.SummonedAfter(0).Single().Individual, Is.Not.EqualTo(s.Individual));
        }

        [Test]
        public void ASpellGrowsTreesOnSoilOnly()
        {
            var w = With(Spell("grove", Trees));
            for (int x = 20; x < 40; x++) w.At(x, 30).T = "stone";
            w.Cast("grove", 30, 30, 0);
            var trees = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).Where(c => w.At(c.x, c.y).T == "tree").ToList();
            Assert.That(trees, Has.Count.EqualTo(5));
            Assert.That(trees.All(c => c.y != 30), "not on the stone");
            Assert.That(w.CastHash(), Is.Not.EqualTo(With(Spell("grove", Trees)).CastHash()));
        }

        [Test]
        public void ACoverIsLaidFreshAndTheSpellsCellsAreTheAreaWithoutWithin()
        {
            var w = With(Spell("snowfall", "{ 'cover': 'snow', 'count': 4 }".Replace('\'', '"')));
            w.Cast("snowfall", 30, 30, 0);
            var snowy = Enumerable.Range(0, 64 * 64).Select(i => (x: i % 64, y: i / 64)).Where(c => w.At(c.x, c.y).Cover == "snow").ToList();
            Assert.That(snowy, Has.Count.EqualTo(4));
            Assert.That(snowy.All(c => System.Math.Abs(c.x - 30) <= 2 && System.Math.Abs(c.y - 30) <= 2), "within the burst's cells");
        }

        [Test]
        public void ThePlacedThingsAndCountsSaveAndLoad()
        {
            var w = With(Spell("barrels", Barrels));
            w.Cast("barrels", 30, 30, 0);
            w.Step();
            var source = new Flat();
            var loaded = World.Load(w.Save(source), w.Rules, source);
            Assert.That(loaded.CastHash(), Is.EqualTo(w.CastHash()));
            Assert.That(loaded.ThingsNow.Fingerprint, Is.EqualTo(w.ThingsNow.Fingerprint));
            w.Cast("barrels", 10, 10, 0);
            loaded.Cast("barrels", 10, 10, 0);
            Assert.That(loaded.CastHash(), Is.EqualTo(w.CastHash()), "the counts carry on");
        }

        sealed class Flat : IChunkSource
        {
            public string Version => "test-flat";
            public void Make(int x, int y, Cell cell) => cell.T = "dirt";
        }

        [Test]
        public void TheChecksAndBudgetsHoldAPlace()
        {
            string Problems(string place) => string.Join("\n", ModValidator.Validate(R, Budgets, Spell("p", place)));
            Assert.That(Problems(Barrels.Replace('\'', '"')), Is.Empty);
            Assert.That(Problems(Child.Replace('\'', '"')), Is.Empty, "a spell with only a place does something");
            Assert.That(Problems("{ \"thing\": \"barrel\", \"animal\": \"deer\" }"), Does.Contain("exactly one of"));
            Assert.That(Problems("{ \"thing\": \"nope\" }"), Does.Contain("neither an entity nor an item"));
            Assert.That(Problems("{ \"animal\": \"dragon\" }"), Does.Contain("not a species"));
            Assert.That(Problems("{ \"terrain\": \"tree\", \"onto\": [\"nowhere\"] }"), Does.Contain("no terrain has"));
            Assert.That(Problems("{ \"terrain\": \"tree\", \"lasts\": 5 }"), Does.Contain("only a thing lasts"));
            Assert.That(Problems("{ \"thing\": \"barrel\", \"count\": 9 }"), Does.Contain("count of 9"));
            Assert.That(Problems("{ \"thing\": \"barrel\", \"within\": 9 }"), Does.Contain("within of 9"));
            Assert.That(Problems("{ \"thing\": \"barrel\", \"lasts\": 30000 }"), Does.Contain("lasts of 30000"));
            Assert.That(Problems("{ \"thing\": \"barrel\", \"far\": 2 }"), Does.Contain("no field \\\"far\\\"").Or.Contain("no field \"far\""));
        }
    }
}
