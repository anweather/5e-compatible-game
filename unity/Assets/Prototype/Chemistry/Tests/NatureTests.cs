using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Nature: Plant Growth regrows burnt ground (green shoots, then grass, out from the middle;
    /// saplings from old stumps that grow into trees) and Spike Growth lays thorns that burn and
    /// wither. Mirrors sandbox/test/nature.test.ts; the golden numbers come from running the same
    /// casts there. The core has no creatures, so what thorns do to a walker is the sandbox's
    /// test and the view's (SurfaceEffects); here only the numbers it reads.
    /// </summary>
    public class NatureTests
    {
        static Rules rules;

        static Rules R => rules ??= Rules.Load(ChemistryTests.RulesDir());

        static World Room(int w, int h) => ChemistryTests.Room(w, h, 7, R);

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        static int Count(World w, string t) => w.Cells.Count(c => c.T == t);

        /// <summary>Ground fire has been over: dirt under ash inside the room, with old stumps where trees stood.</summary>
        static void Burnt(World w, params (int x, int y)[] stumps)
        {
            for (int x = 1; x < w.W - 1; x++)
                for (int y = 1; y < w.H - 1; y++)
                {
                    w.At(x, y).T = "dirt";
                    w.At(x, y).Cover = "ash";
                }
            foreach (var (x, y) in stumps) w.At(x, y).T = "stump";
        }

        [Test]
        public void TheNatureSpellsAreUnderNature()
        {
            Assert.That(R.Spell("plant-growth").Group, Is.EqualTo("nature"));
            Assert.That(R.Spell("spike-growth").Group, Is.EqualTo("nature"));
            Assert.That(R.Group("nature").Name, Is.EqualTo("Nature"));
        }

        [Test]
        public void PlantGrowthReachesFurtherWithEachLevelOfCharge()
        {
            var w = Room(21, 21);
            var s = R.Spell("plant-growth");
            Assert.That(new[] { 1, 2, 3 }.Select(charge => w.SpellCells(s, 10, 10, 1, charge).Count), Is.EqualTo(new[] { 81, 121, 169 }));
        }

        [Test]
        public void PlantGrowthTurnsBurntGroundBackToGrassFromTheMiddleOut()
        {
            var w = Room(15, 15);
            Burnt(w);
            w.Cast("plant-growth", 7, 7, 1);
            Assert.That(w.At(7, 7).T, Is.EqualTo("sprouts"));
            Assert.That(w.At(7, 7).Cover, Is.Null, "the ash is cleared");
            Assert.That(w.At(11, 7).T, Is.EqualTo("sprouts"), "4 squares out");
            Assert.That(w.At(12, 7).Cover, Is.EqualTo("ash"), "5 squares out is left");
            Assert.That(w.At(7, 7).Life, Is.LessThan(w.At(11, 11).Life), "the middle ripens first");
            Run(w, 10);
            Assert.That(w.At(7, 7).T, Is.EqualTo("grass"));
            Assert.That(w.At(11, 11).T, Is.EqualTo("sprouts"));
            Run(w, 20);
            Assert.That(Count(w, "grass"), Is.EqualTo(81));
            Assert.That(w.At(12, 7).T, Is.EqualTo("dirt"));
        }

        [Test]
        public void ASaplingSpringsFromAnOldStumpAndGrowsIntoATree()
        {
            var w = Room(15, 15);
            Burnt(w, (7, 7));
            w.Cast("plant-growth", 7, 7, 1);
            var c = w.At(7, 7);
            Assert.That(c.T, Is.EqualTo("sapling"));
            Assert.That(c.Life, Is.InRange(90, 150));
            Run(w, 89);
            Assert.That(c.T, Is.EqualTo("sapling"));
            Run(w, 61);
            Assert.That(c.T, Is.EqualTo("tree"));
            Assert.That(c.Life, Is.EqualTo(0));
            Assert.That(w.Log.Any(l => l.Text == "A sapling grows into a young tree"));
        }

        [Test]
        public void NothingGrowsOnStoneWaterOrARaisedWallNorWhileItBurns()
        {
            var w = Room(15, 15);
            Burnt(w);
            w.At(6, 7).T = "stone";
            w.At(6, 7).Cover = null;
            w.At(8, 7).T = "pit";
            w.At(8, 7).Cover = null;
            w.At(8, 7).Water = 4;
            w.At(7, 6).Height = 3;
            w.At(7, 8).Fire = 3;
            w.At(7, 8).Fuel = 9;
            w.Cast("plant-growth", 7, 7, 1);
            Assert.That(w.At(6, 7).T, Is.EqualTo("stone"));
            Assert.That(w.At(8, 7).T, Is.EqualTo("pit"));
            Assert.That(w.At(7, 6).T, Is.EqualTo("dirt"), "the wall stays bare earth");
            Assert.That(w.At(7, 8).T, Is.EqualTo("dirt"), "a blaze isn't smothered");
        }

        static (World w, int saplings, string early) Grow()
        {
            var w = Room(17, 17);
            Burnt(w, (5, 5), (11, 10));
            w.Cast("plant-growth", 8, 8, 1, 2);
            int saplings = Count(w, "sapling");
            Run(w, 40);
            string early = $"{w.CellHash()} {w.RngState}";
            Run(w, 260);
            return (w, saplings, early);
        }

        [Test]
        public void ChargedPlantGrowthGoldenMatchesTs()
        {
            var (w, saplings, early) = Grow();
            Assert.That(saplings, Is.GreaterThan(2));
            Assert.That(Count(w, "tree"), Is.EqualTo(saplings), "every sapling is a tree by now");
            Assert.That(Count(w, "sapling"), Is.EqualTo(0));
            // Golden, shared with sandbox/test/nature.test.ts.
            Assert.That(early, Is.EqualTo("e22869f7 1489130935"), "40 ticks on: grass, saplings still growing");
            Assert.That($"{w.CellHash()} {w.RngState} {saplings}", Is.EqualTo("527e32aa 1489130935 24"));
        }

        [Test]
        public void SpikeGrowthLaysThornsThatLastAMinuteThenWither()
        {
            var w = Room(15, 15);
            w.Cast("spike-growth", 7, 7, 1);
            var c = w.At(9, 9);
            Assert.That(c.Cover, Is.EqualTo("thorns"));
            Assert.That(c.Life, Is.EqualTo(360));
            Assert.That(w.At(12, 7).Cover, Is.Null, "5 squares out is left");
            Assert.That(w.IsSpiky(c));
            var thorns = w.CoverOf(c);
            Assert.That(thorns.Speed, Is.EqualTo(0.5));
            Assert.That(thorns.Damage, Is.EqualTo(new[] { 1, 4 }));
            Run(w, 359);
            Assert.That(c.Cover, Is.EqualTo("thorns"));
            Run(w, 1);
            Assert.That(c.Cover, Is.Null);
            Assert.That(w.Log.Any(l => l.Text == "The thorns wither away"));
        }

        [Test]
        public void ThornsBurnAwayToAsh()
        {
            var w = Room(15, 15);
            w.Cast("spike-growth", 7, 7, 1);
            w.Cast("fire-bolt", 7, 7, 1);
            Assert.That(w.At(7, 7).Fire, Is.GreaterThan(0), "the thorns catch");
            Run(w, 120);
            Assert.That(w.Cells.Count(c => c.Cover == "ash"), Is.GreaterThan(5));
            Assert.That(w.At(7, 7).Cover, Is.EqualTo("ash"));
        }

        [Test]
        public void BrokenGrowthRulesAreRefusedWithTheTsWording()
        {
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(@"{
                'terrain': { 'weed': { 'name': 'Weed', 'tags': ['ground'], 'growsTo': 'nope' } },
                'covers': { 'nettles': { 'name': 'Nettles', 'tags': [], 'damage': [1] } },
                'reactions': { 'add': [ { 'id': 'g', 'element': 'growth', 'do': [ { 'grow': 'tree' }, { 'grow': 'sprouts', 'ticks': [5, 2] } ] } ] } }".Replace('\'', '"')) })).Errors;
            Assert.That(errors, Is.EqualTo(new[]
            {
                "terrain weed: growsTo nope unknown",
                "cover nettles: damage must be [dice, sides]",
                "rule g: grow tree is blocking (let it growsTo one)",
                "rule g: ticks must be [min, max], whole numbers from 1",
            }));
        }
    }
}
