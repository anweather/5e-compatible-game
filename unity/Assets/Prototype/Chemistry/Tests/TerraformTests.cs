using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Earth shaping (terraform spells). Mirrors sandbox/test/terraform.test.ts; the golden
    /// numbers come from running the same casts there.
    /// </summary>
    public class TerraformTests
    {
        static Rules rules;

        static Rules R => rules ??= Rules.Load(ChemistryTests.RulesDir());

        static World Room(int w, int h, uint seed = 7, Rules with = null) => ChemistryTests.Room(w, h, seed, with ?? R);

        /// <summary>A pond of pits holding a full step of water.</summary>
        static void Pond(World w, int x0, int x1, int y0, int y1)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    w.At(x, y).T = "pit";
                    w.At(x, y).Water = 4;
                }
        }

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }
        static int TotalWater(World w) => w.Cells.Sum(c => c.Water);
        static int[] Row(World w, int y, params int[] xs) => xs.Select(x => w.At(x, y).Water).ToArray();
        static int[] Heights(World w, int y, params int[] xs) => xs.Select(x => w.At(x, y).Height).ToArray();

        static ModPatch HighWalls() =>
            ModPatch.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", "high-walls.json")));

        [Test]
        public void AnEarthenTrenchCutFromAPondsBankDigsFromTheCastersFeetAndTheWaterFollows()
        {
            var w = Room(20, 9);
            Pond(w, 2, 5, 2, 6);
            // The wizard stands on the dry bank at (6, 4); SpellCaster.Aim puts a self line one square out.
            w.Cast("earthen-trench", 7, 4, 1);
            Assert.That(Heights(w, 4, 5, 6, 7, 13, 14), Is.EqualTo(new[] { 0, -2, -2, -2, 0 }), "8 squares from the caster's own");
            Run(w, 20);
            // Settled: on the flat water needs a 2-unit difference to move, so the channel slopes.
            Assert.That(Row(w, 4, 6, 7, 8, 9, 10, 11, 12, 13), Is.EqualTo(new[] { 5, 4, 3, 2, 1, 1, 0, 0 }));
            Assert.That(w.At(8, 3).Water, Is.EqualTo(0), "the banks stay dry");
            Assert.That(TotalWater(w), Is.EqualTo(80));
        }

        [Test]
        public void OtherLineSpellsStillStartOneSquareOutFromTheCaster()
        {
            var w = Room(20, 9);
            Assert.That(w.SpellCells(R.Spell("ray-of-frost"), 7, 4, 1)[0], Is.EqualTo((7, 4)));
            Assert.That(w.SpellCells(R.Spell("gust-of-wind"), 7, 4, 1)[0], Is.EqualTo((7, 4)));
            Assert.That(w.SpellCells(R.Spell("earthen-trench"), 7, 4, 1).Take(2), Is.EqualTo(new[] { (6, 4), (7, 4) }));
        }

        [Test]
        public void LightningRunsAlongAFloodedTrench()
        {
            var w = Room(20, 9);
            Pond(w, 2, 5, 2, 6);
            w.Cast("earthen-trench", 7, 4, 1);
            Run(w, 20);
            w.Cast("shocking-grasp", 2, 2, 1);
            Assert.That(w.At(11, 4).Charge, Is.GreaterThan(0), "the far wet end of the channel is charged");
            Assert.That(w.At(12, 4).Charge, Is.EqualTo(0), "the dry end isn't");
        }

        [TestCase(false, 54)]
        [TestCase(true, 0)]
        public void AWallOfStoneDamsTheFlow(bool wall, int behindExpected)
        {
            var w = Room(16, 8);
            if (wall) w.Cast("wall-of-stone", 6, 1, 2);
            for (int y = 1; y <= 6; y++)
                for (int x = 1; x <= 3; x++) w.At(x, y).Water = 16;
            Run(w, 300);
            int behind = 0;
            for (int y = 1; y <= 6; y++)
                for (int x = 7; x <= 14; x++) behind += w.At(x, y).Water;
            Assert.That(behind, Is.EqualTo(behindExpected), "dry behind the wall; without it the water spreads past x=6");
            Assert.That(TotalWater(w), Is.EqualTo(288));
            if (!wall) return;
            Assert.That(w.At(6, 3).T, Is.EqualTo("rock"));
            Assert.That(w.IsBlocking(w.At(6, 3)), "three steps up is a wall");
        }

        [Test]
        public void MoveEarthHeavesUpAHillHighestInTheMiddle()
        {
            var w = new World(R, 17, 17, 3);
            w.Cast("move-earth", 8, 8, 1);
            Assert.That(Heights(w, 8, Enumerable.Range(0, 17).ToArray()),
                Is.EqualTo(new[] { 0, 0, 0, 0, 0, 1, 1, 2, 2, 2, 1, 1, 0, 0, 0, 0, 0 }));
            Assert.That(w.At(5, 5).Height, Is.EqualTo(1), "the corners of the burst are edge too");
            Assert.That(!w.IsBlocking(w.At(8, 8)), "a hill you can walk up");
        }

        [Test]
        public void RaisingAndDiggingConserveWaterAndSoDoesTheFlowAfter()
        {
            var w = Room(16, 16);
            Pond(w, 4, 10, 4, 10);
            w.At(6, 6).Frozen = true;
            w.Cast("move-earth", 7, 7, 1);
            w.Cast("wall-of-stone", 3, 7, 1);
            w.Cast("mold-earth", 12, 12, 1);
            w.Cast("mold-earth-heap", 5, 5, 1);
            Assert.That(TotalWater(w), Is.EqualTo(196));
            Run(w, 150);
            Assert.That(TotalWater(w), Is.EqualTo(196));
        }

        [Test]
        public void DiggingStripsGrassAndCoversAndHeightStaysInRange()
        {
            var w = Room(8, 8);
            w.At(3, 3).T = "grass";
            w.At(3, 3).Cover = "oil";
            w.Cast("mold-earth", 3, 3, 1);
            Assert.That((w.At(3, 3).T, w.At(3, 3).Cover, w.At(3, 3).Height), Is.EqualTo(("dirt", (string)null, -1)));
            for (int i = 0; i < 6; i++) w.Reshape(5, 5, -1);
            Assert.That(w.At(5, 5).Height, Is.EqualTo(R.Sim.Height.Min));
            for (int i = 0; i < 9; i++) w.Reshape(5, 5, 2);
            Assert.That(w.At(5, 5).Height, Is.EqualTo(R.Sim.Height.Max));
            Assert.That(w.Reshape(0, 0, 1), Is.False, "bedrock can't be shaped");
            w.At(2, 2).Water = 6;
            w.Reshape(2, 2, 1);
            Assert.That(new[] { (2, 2), (2, 1), (3, 2), (2, 3), (1, 2) }.Select(p => w.At(p.Item1, p.Item2).Water),
                Is.EqualTo(new[] { 0, 2, 2, 1, 1 }), "spilled round its neighbours, N E S W");
        }

        [Test]
        public void AWallOfStoneAcrossADugTrenchWallsEverySquareTrenchIncluded()
        {
            var w = Room(12, 12);
            w.Cast("earthen-trench", 2, 5, 1);
            Assert.That(w.At(5, 5).Height, Is.EqualTo(-2));
            w.Cast("wall-of-stone", 5, 2, 2);
            for (int y = 2; y <= 7; y++)
            {
                Assert.That(w.At(5, y).Height, Is.EqualTo(R.Sim.Height.BlockAt), $"(5, {y}) is raised to wall height");
                Assert.That(w.IsRaisedWall(w.At(5, y)), $"(5, {y}) is a wall");
            }
            w.At(4, 4).Height = 4;
            w.Cast("wall-of-stone", 4, 2, 2);
            Assert.That(w.At(4, 4).Height, Is.EqualTo(4), "height never lowers a square that is already higher");
        }

        [Test]
        public void AModChangesTheHeightRangeAndTheSpellsNumbers()
        {
            var modded = R.WithMods(new[] { HighWalls() });
            var w = Room(12, 12, 7, modded);
            w.Cast("wall-of-stone", 2, 1, 2);
            Assert.That(w.At(2, 3).Height, Is.EqualTo(5));
            Assert.That(w.IsBlocking(w.At(2, 3)));
            w.Cast("move-earth", 7, 6, 1);
            Assert.That(Heights(w, 6, 4, 5, 6, 7), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(!w.IsBlocking(w.At(7, 6)), "four steps is no wall when walls start at five");

            // Switching the mod off clamps heights back into the base range.
            w.ApplyRules(R);
            Assert.That(w.At(2, 3).Height, Is.EqualTo(R.Sim.Height.Max));
        }

        [Test]
        public void BadTerraformSpellsAreRefusedLikeTheSandbox()
        {
            var errors = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(@"{
                'terrain': { 'mud': { 'name': 'Mud', 'tags': ['ground'], 'dugTo': 'clay' } },
                'spells': [
                    { 'id': 'a', 'name': 'A', 'level': 1, 'shape': 'point', 'effect': 'terraform', 'blurb': '' },
                    { 'id': 'b', 'name': 'B', 'level': 1, 'shape': 'line', 'effect': 'terraform', 'raise': 2, 'dig': 1, 'blurb': '' },
                    { 'id': 'c', 'name': 'C', 'level': 1, 'shape': 'square', 'effect': 'terraform', 'dig': 1.5, 'terrain': 'lava', 'taper': 'yes', 'blurb': '' },
                    { 'id': 'd', 'name': 'D', 'level': 1, 'shape': 'burst', 'effect': 'terraform', 'raise': 0, 'terrain': 'bedrock', 'blurb': '' },
                    { 'id': 'f', 'name': 'F', 'level': 1, 'shape': 'line', 'effect': 'terraform', 'height': 5, 'blurb': '' },
                    { 'id': 'g', 'name': 'G', 'level': 1, 'shape': 'line', 'effect': 'terraform', 'height': -1.5, 'dig': 1, 'blurb': '' },
                    { 'id': 'e', 'name': 'E', 'level': 1, 'shape': 'point', 'effect': 'quake', 'blurb': '' } ] }".Replace('\'', '"')) })).Errors;
            // validateRules' output for the same patch (terraform.test.ts).
            Assert.That(errors, Is.EqualTo(new[]
            {
                "terrain mud: dugTo clay unknown",
                "spell a: terraform needs exactly one of raise, dig or height",
                "spell b: terraform needs exactly one of raise, dig or height",
                "spell c: dig must be a whole number of steps from 1",
                "spell c: terrain lava unknown",
                "spell c: taper must be true or false",
                "spell d: raise must be a whole number of steps from 1",
                "spell d: terrain bedrock is blocking (walls come from raise)",
                "spell f: height must be a whole number from -3 to 4 (sim.height)",
                "spell g: terraform needs exactly one of raise, dig or height",
                "spell g: height must be a whole number from -3 to 4 (sim.height)",
                "spell e: unknown effect quake",
            }));
        }

        [Test]
        public void HeightRangeNumbersAreChecked()
        {
            System.Collections.Generic.IReadOnlyList<string> Errors(string sim) =>
                Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(("{ 'sim': { 'height': " + sim + " } }").Replace('\'', '"')) })).Errors;
            Assert.That(Errors("{ 'min': 1 }"), Does.Contain("sim height.min must be at most 0"));
            Assert.That(Errors("{ 'max': -1 }"), Does.Contain("sim height.max must be at least 0"));
            Assert.That(Errors("{ 'max': -1 }"), Does.Contain("spell wall-of-stone: height must be a whole number from -3 to -1 (sim.height)"));
            Assert.That(Errors("{ 'blockAt': 0 }"), Does.Contain("sim height.blockAt must be at least 1"));
            Assert.DoesNotThrow(() => R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"height\": { \"min\": -5 } } }") }));
        }

        /// <summary>The same casts as earthShaping in terraform.test.ts.</summary>
        static World EarthShaping(uint seed)
        {
            var w = Room(18, 10, seed);
            Pond(w, 2, 4, 2, 6);
            for (int x = 8; x <= 15; x++)
                for (int y = 6; y <= 8; y++) w.At(x, y).T = "grass";
            w.At(9, 7).Cover = "oil";
            w.At(10, 7).Cover = "oil";
            w.Cast("earthen-trench", 6, 4, 1); // from the dry bank at (5, 4)
            Run(w, 20);
            w.Cast("wall-of-stone", 12, 1, 2); // across the trench's end
            w.Cast("move-earth", 14, 7, 1);
            w.Cast("create-water", 14, 7, 1);
            w.Cast("mold-earth", 9, 7, 1);
            w.Cast("fire-bolt", 11, 8, 1);
            w.Cast("shocking-grasp", 6, 4, 1);
            w.Cast("mold-earth-heap", 3, 3, 1);
            Run(w, 120);
            return w;
        }

        [Test]
        public void GoldenEarthShapingMatchesTs()
        {
            var w = EarthShaping(11);
            Assert.That(EarthShaping(11).CellHash(), Is.EqualTo(w.CellHash()), "deterministic");
            Assert.That(w.CellHash(), Is.EqualTo("93d9117b"));
            Assert.That(w.RngState, Is.EqualTo(3719496534u));
            Assert.That(w.At(5, 4).Height, Is.EqualTo(-2), "the trench starts at the caster's feet");
            Assert.That(w.IsRaisedWall(w.At(12, 4)), "the wall closes the trench");
            CollectionAssert.AreEqual(new[]
            {
                "0|info|1|You cast Earthen Trench",
                "20|info|1|You cast Wall of Stone",
                "20|info|1|You cast Move Earth",
                "20|info|1|You cast Create Water",
                "20|info|1|You cast Mold Earth",
                "20|info|1|You cast Fire Bolt",
                "20|info|1|Fire takes hold of the grass",
                "20|info|1|You cast Shocking Grasp",
                "20|notable|1|Lightning arcs through everything conductive that touches",
                "20|info|1|You cast Mold Earth (heap)",
            }, w.Log.Select(l => $"{l.Tick}|{l.Kind.ToString().ToLowerInvariant()}|{l.Count}|{l.Text}").ToArray());
        }
    }
}
