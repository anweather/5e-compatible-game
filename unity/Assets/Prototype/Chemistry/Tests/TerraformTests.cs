using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
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
            var w = Room(16, 8, with: ChemistryTests.NoSoak);
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
            var w = Room(16, 16, with: ChemistryTests.NoSoak);
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
            Assert.That(w.At(2, 3).Height, Is.EqualTo(10));
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
                    { 'id': 'c', 'name': 'C', 'level': 1, 'shape': 'square', 'effect': 'terraform', 'dig': 1.5, 'terrain': 'magma', 'taper': 'yes', 'blurb': '' },
                    { 'id': 'd', 'name': 'D', 'level': 1, 'shape': 'burst', 'effect': 'terraform', 'raise': 0, 'terrain': 'bedrock', 'blurb': '' },
                    { 'id': 'f', 'name': 'F', 'level': 1, 'shape': 'line', 'effect': 'terraform', 'height': 9, 'blurb': '' },
                    { 'id': 'g', 'name': 'G', 'level': 1, 'shape': 'line', 'effect': 'terraform', 'height': -1.5, 'dig': 1, 'blurb': '' },
                    { 'id': 'e', 'name': 'E', 'level': 1, 'shape': 'point', 'effect': 'quake', 'blurb': '' } ] }".Replace('\'', '"')) })).Errors;
            // validateRules' output for the same patch (terraform.test.ts).
            Assert.That(errors, Is.EqualTo(new[]
            {
                "terrain mud: dugTo clay unknown",
                "spell a: terraform needs exactly one of raise, dig or height",
                "spell b: terraform needs exactly one of raise, dig or height",
                "spell c: dig must be a whole number of steps from 1",
                "spell c: terrain magma unknown",
                "spell c: taper must be true or false",
                "spell d: raise must be a whole number of steps from 1",
                "spell d: terrain bedrock is blocking (walls come from raise)",
                "spell f: height must be a whole number from -6 to 8 (sim.height)",
                "spell g: terraform needs exactly one of raise, dig or height",
                "spell g: height must be a whole number from -6 to 8 (sim.height)",
                "spell e: unknown effect quake",
            }));
        }

        [Test]
        public void GoldenChargedCastsMatchTs()
        {
            // The same casts as "charged casts dig deeper..." in terraform.test.ts.
            var w = Room(24, 12, 3);
            Pond(w, 2, 4, 2, 9);
            w.Cast("mold-earth-heap", 8, 3, 1, 3);
            Assert.That(w.At(8, 3).Height, Is.EqualTo(3), "Raise Earth held to the full: three steps, a wall");
            w.Cast("mold-earth", 12, 3, 1, 2);
            Assert.That(w.At(12, 3).Height, Is.EqualTo(-2), "Lower Earth held halfway: two steps down");
            w.Cast("wall-of-stone", 16, 2, 2, 2);
            Assert.That(w.At(16, 3).Height, Is.EqualTo(5), "Wall of Stone held: two steps taller");
            w.Cast("ray-of-frost", 5, 6, 3, 3);
            Assert.That(w.At(2, 6).Frozen, "a held Ray of Frost reaches 10 squares, across the pond");
            for (int x = 16; x <= 22; x++)
                for (int y = 6; y <= 10; y++) w.At(x, y).T = "grass";
            Assert.That(w.SpellCells(R.Spell("fire-bolt"), 18, 8, 1, 3).Count, Is.EqualTo(13), "a held Fire Bolt spreads two squares round its spot");
            w.Cast("fire-bolt", 18, 8, 1, 3);
            Run(w, 60);
            // The hash changed when a charged spot, square or burst began to grow (World.ChargedGrowth): the bolt lights 13 squares, not 1.
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("1e7f4b73 3022804953"));
        }

        [Test]
        public void ChargedAreasGrowButEarthShapingKeepsItsPatch()
        {
            // The same as "charged spots, squares and bursts grow..." in terraform.test.ts.
            var w = Room(24, 24, 3);
            Assert.That(w.SpellCells(R.Spell("fireball"), 12, 12, 1, 3).Count, Is.EqualTo(9 * 9), "radius 2 + 2");
            Assert.That(w.SpellCells(R.Spell("grease"), 12, 12, 1, 2).Count, Is.EqualTo(3 * 3), "side 2 + 1");
            Assert.That(w.SpellCells(R.Spell("shocking-grasp"), 12, 12, 1, 2).Count, Is.EqualTo(5), "a spot and its four sides");
            Assert.That(w.SpellCells(R.Spell("mold-earth"), 12, 12, 1, 3).Count, Is.EqualTo(2 * 2), "Lower Earth digs deeper instead");
            Assert.That(w.SpellCells(R.Spell("move-earth"), 12, 12, 1, 3).Count, Is.EqualTo(7 * 7), "Move Earth heaves higher instead");
            Assert.That(w.SpellCells(R.Spell("misty-step"), 12, 12, 1, 3).Count, Is.EqualTo(1), "a target stays one square");
        }

        // A row of raised rock (hardness 3) and a row of metal plate (hardness 4) across a room of dirt.
        static World HardRoom()
        {
            var w = Room(20, 12, 9);
            for (int x = 2; x <= 17; x++)
            {
                w.At(x, 3).T = "rock";
                w.At(x, 6).T = "metal";
            }
            return w;
        }

        [Test]
        public void ShapeEarthDigsDirtButNotRockOrMetal()
        {
            var w = HardRoom();
            w.Cast("mold-earth", 4, 3, 1);
            w.Cast("mold-earth", 4, 6, 1);
            w.Cast("mold-earth", 4, 9, 1);
            Assert.That(w.At(4, 3).Height, Is.Zero, "Lower Earth can't dig rock");
            Assert.That(w.At(4, 6).Height, Is.Zero, "nor metal");
            Assert.That(w.At(4, 9).Height, Is.EqualTo(-1), "dirt digs");
            Assert.That(w.Log, Has.Some.Matches<LogEntry>(l => l.Text == "The raised rock is too hard for Lower Earth"), "the refusal names the spell");
            Assert.That(w.Log, Has.Some.Matches<LogEntry>(l => l.Text == "The metal plate can't be dug"), "metal never digs");
        }

        [Test]
        public void ABlastDigsAsHardAGroundAsItsStrength()
        {
            var w = HardRoom();
            w.Cast("fireball", 10, 3, 0);
            Assert.That(w.At(10, 3).Height, Is.Zero, "Fireball's crater (strength 2) can't dig rock");
            w.Cast("meteor", 14, 4, 0);
            Assert.That(w.At(14, 3).Height, Is.LessThan(0), "Meteor's crater (strength 3) digs rock");
            Assert.That(w.At(14, 3).T, Is.EqualTo("rubble"), "rock is dug to rubble");
            Assert.That(w.At(14, 6).Height, Is.Zero, "metal never digs");
            Assert.That(w.At(14, 5).Height, Is.LessThan(0), "the dirt between digs");
        }

        [Test]
        public void HardnessAndStrengthAreChecked()
        {
            IReadOnlyList<string> Errors(string patch) =>
                Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(patch.Replace('\'', '"')) })).Errors;
            Assert.That(Errors("{ 'terrain': { 'granite': { 'name': 'Granite', 'tags': ['ground'], 'hardness': -1 } } }"), Does.Contain("terrain granite: hardness must be a whole number from 0 (0 never digs)"));
            Assert.That(Errors("{ 'spells': [{ 'id': 'chisel', 'group': 'earth', 'name': 'Chisel', 'level': 1, 'shape': 'point', 'effect': 'terraform', 'dig': 1, 'strength': 1.5, 'radial': false, 'blurb': 'b' }] }"),
                Does.Contain("spell chisel: strength must be a whole number from 1"));
            Assert.That(Errors("{ 'spells': [{ 'id': 'pop', 'group': 'fire', 'name': 'Pop', 'level': 1, 'shape': 'burst', 'radius': 1, 'element': 'fire', 'crater': { 'depth': 1, 'strength': 0 }, 'radial': false, 'blurb': 'b' }] }"),
                Does.Contain("spell pop: crater needs a whole depth from 1, radius from 0 and strength from 1"));
        }

        [Test]
        public void StoneShapeDigsRockButNothingDigsMetalOrBedrock()
        {
            // BACKLOG 4: Lower Earth (strength 1) digs dirt, sand and mud; rock (hardness 3) needs Stone Shape
            // (strength 3); metal and bedrock (hardness 0) never dig, and each refusal says why.
            var w = new World(R, 12, 5, 1, "dirt");
            w.At(2, 2).T = "sand";
            w.At(4, 2).T = "mud";
            w.At(6, 2).T = "rock";
            w.At(8, 2).T = "metal";
            w.At(10, 2).T = "bedrock";
            foreach (int x in new[] { 2, 4, 6, 8 }) w.Cast("mold-earth", x, 2, 1);
            Assert.That(new[] { 2, 4, 6, 8 }.Select(x => w.At(x, 2).Height), Is.EqualTo(new[] { -1, -1, 0, 0 }), "sand and mud dig; rock and metal don't");
            Assert.That(w.Log.Any(l => l.Text == "The raised rock is too hard for Lower Earth"));
            foreach (int x in new[] { 6, 8, 10 }) w.Cast("stone-shape", x, 2, 1);
            Assert.That(w.At(6, 2).Height, Is.EqualTo(-1), "Stone Shape digs rock");
            Assert.That(w.At(6, 2).T, Is.EqualTo("rubble"));
            Assert.That(w.At(8, 2).Height, Is.Zero, "but not metal");
            Assert.That(w.Log.Any(l => l.Text == "The metal plate can't be dug"));
            Assert.That(w.At(10, 2).T, Is.EqualTo("bedrock"), "nor bedrock");
            Assert.That(w.Log.Any(l => l.Text == "The bedrock can't be dug"));
            Run(w, 20);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("f57b067a 1"), "the same in the sandbox");
        }

        [Test]
        public void APickDigsRockThroughAnEffect()
        {
            // BACKLOG 4: the pick (strength 3) digs what Stone Shape digs; it stops at metal and bedrock.
            var w = new World(R, 10, 3, 1, "dirt");
            w.At(2, 1).T = "rock";
            w.At(4, 1).T = "metal";
            w.At(6, 1).T = "bedrock";
            foreach (int x in new[] { 2, 4, 6, 8 }) w.ApplyEffect(new EffectCommand(EffectKind.Dig, x, 1, "pick"));
            Assert.That(w.At(2, 1).Height, Is.EqualTo(-1));
            Assert.That(w.At(2, 1).T, Is.EqualTo("rubble"));
            Assert.That(w.At(4, 1).Height, Is.Zero);
            Assert.That(w.At(8, 1).Height, Is.EqualTo(-1), "dirt digs too");
            Assert.That(w.Log.Any(l => l.Text == "The metal plate can't be dug"));
            Assert.That(w.Log.Any(l => l.Text == "The bedrock can't be dug"));
            w.ApplyEffect(new EffectCommand(EffectKind.Dig, 3, 1, "ball"));
            Assert.That(w.At(3, 1).Height, Is.Zero, "an item that doesn't dig does nothing");
            Run(w, 20);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("1c4e549b 1"), "the same in the sandbox");
        }

        [Test]
        public void DigToolsNeedAStrength()
        {
            IReadOnlyList<string> Errors(string patch) =>
                Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(patch.Replace('\'', '"')) })).Errors;
            Assert.That(Errors("{ 'items': { 'spade': { 'name': 'Spade', 'use': 'dig', 'strength': 0 } } }"), Does.Contain("item spade: use dig needs a whole strength from 1"));
        }

        [Test]
        public void EveryGroundSaysHowHardItIs()
        {
            var terrain = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "materials.json")))["terrain"];
            foreach (var p in terrain.Properties())
            {
                var t = R.Terrain[p.Name];
                if ((t.Has("ground") || t.Has("low")) && !t.Has("machine"))
                    Assert.That(p.Value["dig"]?["hardness"], Is.Not.Null, p.Name);
            }
        }

        [Test]
        public void GoldenHardnessMatchesTs()
        {
            // The same as "a dig weaker than the ground..." in terraform.test.ts.
            var w = HardRoom();
            w.Cast("mold-earth", 4, 3, 1);
            w.Cast("earthen-trench", 3, 9, 1);
            w.Cast("fireball", 10, 3, 0);
            w.Cast("meteor", 14, 4, 0);
            Run(w, 30);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("48b09445 2346426195"));
        }

        [Test]
        public void GoldenBurningHandsConeMatchesTs()
        {
            // The same as "Burning Hands is a cone..." in terraform.test.ts.
            var w = Room(20, 14, 4);
            for (int x = 1; x < 19; x++)
                for (int y = 1; y < 13; y++) w.At(x, y).T = "grass";
            var s = R.Spell("burning-hands");
            Assert.That(w.SpellCells(s, 4, 6, 1), Is.EqualTo(new[] { (4, 6), (5, 5), (5, 6), (5, 7), (6, 4), (6, 5), (6, 6), (6, 7), (6, 8) }));
            Assert.That(w.SpellCells(s, 4, 6, 1, 3).Count, Is.EqualTo(1 + 3 + 5 + 7 + 9 + 11), "charged to 3: six rows");
            w.Cast("burning-hands", 4, 6, 1, 2);
            Run(w, 40);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("d1f5f85a 1912747678"));
        }

        [Test]
        public void GoldenRiverRefillsMatchesTs()
        {
            // The same as "a river refills..." in terraform.test.ts.
            var w = Room(24, 12, 6);
            for (int x = 1; x <= 10; x++)
            {
                w.At(x, 6).T = "river";
                w.At(x, 6).Water = 4;
            }
            w.Cast("earthen-trench", 12, 6, 1);
            Run(w, 60);
            Assert.That(w.At(3, 6).Water, Is.EqualTo(4), "the river is full again");
            Assert.That(new[] { 12, 14, 16 }.All(x => w.At(x, 6).Water > 0), "water ran down the trench");
            Assert.That(TotalWater(w), Is.GreaterThan(40), "more water than the river held: the spring keeps giving");
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("64047114 6"));
        }

        [Test]
        public void GoldenFireballCraterMatchesTs()
        {
            // The same as "Fireball blows a small crater..." in terraform.test.ts.
            var w = Room(20, 12, 8);
            for (int x = 1; x < 19; x++)
                for (int y = 1; y < 11; y++) w.At(x, y).T = "grass";
            w.Cast("fireball", 10, 6, 1);
            Assert.That(w.At(10, 6).Height, Is.EqualTo(-2), "two steps down in the middle");
            Assert.That(w.At(11, 7).Height, Is.EqualTo(-1), "one step round it");
            Assert.That(w.At(12, 6).Height, Is.EqualTo(0), "the blast's edge isn't dug");
            Assert.That(w.At(12, 6).Fire, Is.GreaterThan(0), "but it burns");
            Run(w, 40);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("7b41057e 1796687091"));
        }

        [Test]
        public void ABarrelBlastDigsAShallowPit()
        {
            var w = Room(12, 12, 2);
            w.Explode(6, 6, R.Entity("barrel"));
            Assert.That(w.At(6, 6).Height, Is.EqualTo(-1));
            Assert.That(w.At(7, 7).Height, Is.EqualTo(-1));
            Assert.That(w.At(8, 6).Height, Is.EqualTo(0));
            Assert.That(w.At(6, 6).Cover, Is.EqualTo("oil"), "the oil lands in the pit");
        }

        [Test]
        public void GoldenSlantedTrenchMatchesTs()
        {
            // The same as "a slanted trench runs off the grid..." in terraform.test.ts.
            var w = Room(24, 14, 9);
            for (int x = 2; x <= 4; x++)
                for (int y = 3; y <= 10; y++)
                {
                    w.At(x, y).T = "river";
                    w.At(x, y).Water = 4;
                }
            var s = R.Spell("earthen-trench");
            var cells = w.SpellCells(s, 6, 6, 1, 1, 6, 2);
            Assert.That(cells[0], Is.EqualTo((5, 6)), "it starts under the caster on the river's bank");
            for (int i = 1; i < cells.Count; i++)
                Assert.That(System.Math.Abs(cells[i].x - cells[i - 1].x) + System.Math.Abs(cells[i].y - cells[i - 1].y), Is.EqualTo(1), "each square shares a side with the last");
            Assert.That(cells[cells.Count - 1].y, Is.GreaterThan(6), "it slants south as it goes east");
            Assert.That(w.SpellCells(s, 6, 6, 1, 1, 12, 1), Is.EqualTo(w.SpellCells(s, 6, 6, 1)), "within 5 degrees of east it's the straight trench");
            w.Cast("earthen-trench", 6, 6, 1, 1, 6, 2);
            Run(w, 60);
            Assert.That(cells.Take(8).All(c => w.At(c.x, c.y).Water > 0), "water ran along it, round its corners, as far as along a straight one");
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("99221231 9"));
        }

        [Test]
        public void ASlantedConeFansAlongTheAim()
        {
            var w = Room(20, 14, 4);
            Assert.That(w.SpellCells(R.Spell("burning-hands"), 4, 6, 1, 1, 3, 2),
                Is.EqualTo(new[] { (4, 6), (5, 6), (6, 6), (7, 6), (5, 7), (6, 7), (5, 8), (6, 8), (5, 9) }));
        }

        [Test]
        public void GoldenTopplingTreesMatchesTs()
        {
            // The same as "earth shaping topples trees..." in terraform.test.ts.
            var w = Room(20, 12, 10);
            for (int x = 1; x < 19; x++)
                for (int y = 1; y < 11; y++) w.At(x, y).T = "grass";
            foreach (var (x, y) in new[] { (5, 5), (6, 5), (7, 6), (13, 3), (15, 8) }) w.At(x, y).T = "tree";
            w.Cast("move-earth", 6, 5, 0);
            Assert.That(w.At(6, 5).T, Is.EqualTo("deadfall"), "the tree in the heave toppled");
            Assert.That(w.At(6, 5).Height, Is.GreaterThan(0), "and the ground rose under it");
            Assert.That(w.Log.Count(e => e.Text.Contains("tree topples")), Is.EqualTo(1), "one line for the cast, however many trees");
            w.Cast("wall-of-stone", 13, 2, 2);
            Assert.That(w.At(13, 3).T, Is.EqualTo("rock"), "a wall raised through a tree goes up as rock");
            w.Cast("fireball", 15, 8, 0);
            Assert.That(w.At(15, 8).T, Is.EqualTo("deadfall"), "a crater fells the tree it lands on");
            w.Cast("fire-bolt", 5, 5, 0);
            Run(w, 40);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("aa411832 2208810063"));
        }

        [Test]
        public void HeightRangeNumbersAreChecked()
        {
            System.Collections.Generic.IReadOnlyList<string> Errors(string sim) =>
                Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(("{ 'sim': { 'height': " + sim + " } }").Replace('\'', '"')) })).Errors;
            Assert.That(Errors("{ 'min': 1 }"), Does.Contain("sim height.min must be at most 0"));
            Assert.That(Errors("{ 'max': -1 }"), Does.Contain("sim height.max must be at least 0"));
            Assert.That(Errors("{ 'max': -1 }"), Does.Contain("spell wall-of-stone: height must be a whole number from -6 to -1 (sim.height)"));
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
            Assert.That(w.CellHash(), Is.EqualTo("7175c6c6"));
            Assert.That(w.RngState, Is.EqualTo(3719496534u));
            Assert.That(w.At(5, 4).Height, Is.EqualTo(-2), "the trench starts at the caster's feet");
            Assert.That(w.IsRaisedWall(w.At(12, 4)), "the wall closes the trench");
            CollectionAssert.AreEqual(new[]
            {
                "0|info|1|You cast Earthen Trench",
                "20|info|1|You cast Wall of Stone",
                "20|info|1|You cast Move Earth",
                "20|info|1|You cast Create Water",
                "20|info|1|You cast Lower Earth",
                "20|info|1|You cast Fire Bolt",
                "20|info|1|Fire takes hold of the grass",
                "20|info|1|You cast Shocking Grasp",
                "20|notable|1|Lightning arcs through everything conductive that touches",
                "20|info|1|You cast Raise Earth",
            }, w.Log.Select(l => $"{l.Tick}|{l.Kind.ToString().ToLowerInvariant()}|{l.Count}|{l.Text}").ToArray());
        }

        // A whirlwind cast south of a cell and heading north, 15 ticks on: its eye has crossed it.
        static World Crossing(System.Action<Cell> set)
        {
            var w = new World(R, 9, 12, 1, "dirt");
            set(w.At(4, 5));
            w.Cast("whirlwind", 4, 8, 0);
            Run(w, 15);
            return w;
        }

        [Test]
        public void AWhirlwindTakesOnWhatItCrosses()
        {
            Assert.That(Crossing(c => { c.Fire = 3; c.Fuel = 40; }).Whirls[0].Carrying, Is.EqualTo("fire"));
            Assert.That(Crossing(c => { c.T = "pit"; c.Water = 4; }).Whirls[0].Carrying, Is.EqualTo("water"));
            Assert.That(Crossing(c => c.T = "sand").Whirls[0].Carrying, Is.EqualTo("sand"));
            Assert.That(Crossing(c => { c.Gas = "smoke"; c.GasAmt = 6; }).Whirls[0].Carrying, Is.EqualTo("smoke"));
            Assert.That(Crossing(c => c.Cover = "snow").Whirls[0].Carrying, Is.EqualTo("snow"));
            Assert.That(Crossing(c => { }).Whirls[0].Carrying, Is.Null, "bare ground gives it nothing");
            Assert.That(Crossing(c => c.T = "sand").Log.Any(l => l.Text == "The whirlwind whips the sand into a sandstorm"));
        }

        [Test]
        public void AWhirlwindGoesWithTheWindOrTheWayItWasCastAndAWallStopsIt()
        {
            var cast = new World(R, 12, 9, 1, "dirt");
            cast.Cast("whirlwind", 4, 4, 1);
            Run(cast, 12);
            Assert.That((cast.Whirls[0].X, cast.Whirls[0].Y), Is.EqualTo((6, 4)), "east, as cast, a cell every 6 ticks");
            var windy = new World(R, 12, 9, 1, "dirt");
            windy.Wind = (2, 100);
            windy.Cast("whirlwind", 4, 2, 1);
            Run(windy, 12);
            Assert.That((windy.Whirls[0].X, windy.Whirls[0].Y), Is.EqualTo((4, 4)), "the wind wins: south");
            var walled = new World(R, 12, 9, 1, "dirt");
            walled.At(5, 4).T = "bedrock";
            walled.Cast("whirlwind", 4, 4, 1);
            Run(walled, 12);
            Assert.That(walled.Whirls[0].X, Is.EqualTo(4));
        }

        [Test]
        public void AWaterspoutDropsItsWaterWhereItDiesOut()
        {
            var w = new World(R, 20, 9, 1, "dirt");
            w.At(4, 4).T = "pit";
            w.At(4, 4).Water = 4;
            w.Cast("whirlwind", 4, 4, 1);
            Run(w, 60);
            Assert.That(w.Whirls, Is.Empty);
            Assert.That(w.At(14, 4).Water, Is.GreaterThanOrEqualTo(2));
            Assert.That(w.Log.Any(l => l.Text == "The waterspout dies out and drops what it carried"));
        }

        [Test]
        public void GoldenFireTornadoMatchesTs()
        {
            // It starts on a fire, heads east over dry grass, lights what it passes and drops fire where it ends.
            var w = new World(R, 20, 9, 3, "grass");
            w.At(4, 4).Fire = 3;
            w.At(4, 4).Fuel = 8;
            w.Cast("whirlwind", 4, 4, 1);
            Run(w, 30);
            string mid = $"{w.WhirlState()} {w.CellHash()} {w.RngState}";
            Run(w, 30);
            Assert.That(w.Whirls, Is.Empty);
            Assert.That($"{mid} | {w.CellHash()} {w.RngState}", Is.EqualTo("|9:4:30:fire e6fa9db8 2713091359 | 47709c01 321794052"), "the same in the sandbox");
        }

        [Test]
        public void GoldenDustFlashMatchesTs()
        {
            // A dust cloud goes up as one from a spark at its edge, all of it used up; a cloud apart from it doesn't.
            var w = new World(R, 16, 9, 1, "dirt");
            for (int y = 0; y < 9; y++)
                for (int x = 10; x < 16; x++) w.At(x, y).T = "grass";
            w.Cast("dust-storm", 6, 4, 0);
            w.At(13, 4).Gas = "dust";
            w.At(13, 4).GasAmt = 3;
            w.Apply(4, 4, "fire", 1, -1, true);
            Assert.That(w.Cells.Count(c => c.Gas == "dust"), Is.EqualTo(1), "only the far cloud is left");
            Assert.That(w.At(6, 4).Fire, Is.EqualTo(3));
            Assert.That(w.Log.Any(l => l.Text == "The dust goes up in a flash"));
            Assert.That(w.FlashesSince(-1).Count(), Is.GreaterThan(1), "the view hears of each cell");
            Run(w, 20);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("28826ce6 2354002487"), "the same in the sandbox");
        }

        [Test]
        public void DustDriftingOverAFireFlashes()
        {
            var w = new World(R, 8, 8, 1, "dirt");
            w.At(3, 3).Fire = 2;
            w.At(3, 3).Fuel = 6;
            w.At(3, 3).Gas = "dust";
            w.At(3, 3).GasAmt = 2;
            w.At(4, 3).Gas = "dust";
            w.At(4, 3).GasAmt = 2;
            w.Step();
            Assert.That(w.At(3, 3).Gas, Is.Null);
            Assert.That(w.At(4, 3).Gas, Is.Null);
            Assert.That(w.At(4, 3).Fire, Is.GreaterThan(0));
        }

        [Test]
        public void ASandstormsDustGoesUpToo()
        {
            var w = new World(R, 14, 9, 1, "sand");
            w.Cast("whirlwind", 4, 4, 1);
            Run(w, 9);
            Assert.That(w.Whirls[0].Carrying, Is.EqualTo("sand"));
            int x = w.Whirls[0].X;
            Assert.That(w.At(x, 4).Gas, Is.EqualTo("dust"));
            w.Apply(x, 4, "fire", 1, -1, true);
            Assert.That(w.At(x, 4).Gas, Is.Null);
            Assert.That(w.Log.Any(l => l.Text == "The dust goes up in a flash"));
        }

        [Test]
        public void CarriesAndFlashesAreChecked()
        {
            IReadOnlyList<string> Errors(string patch) =>
                Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(patch.Replace('\'', '"')) })).Errors;
            Assert.That(Errors("{ 'gases': { 'flour': { 'name': 'Flour', 'burn': { 'flash': { 'fire': 0, 'push': -1 } } } } }"),
                Is.EquivalentTo(new[] { "gas flour: burn.flash.fire must be a whole number from 1", "gas flour: burn.flash.push must be a whole number from 0" }));
            Assert.That(Errors("{ 'spells': [{ 'id': 'gyre', 'group': 'storm', 'name': 'Gyre', 'level': 1, 'shape': 'burst', 'element': 'air', 'radial': false, 'blurb': 'b', 'travelEvery': 0, 'carries': [{ 'id': 'x', 'if': { 'hue': 1 }, 'sheds': 'nothing' }] }] }"),
                Is.EquivalentTo(new[] { "spell gyre: travelEvery must be a whole number from 1", "spell gyre: carries x sheds unknown element nothing", "spell gyre: carries x has unknown condition \"hue\"" }));
        }
    }
}
