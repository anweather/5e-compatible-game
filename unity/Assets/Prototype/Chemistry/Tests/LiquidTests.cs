using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Liquids (docs/design/primitives.md §3, P1; Rules.Liquids.cs, World.Liquids.cs): a mod's honey is
    /// thick, slow and heaps; a red paint runs as water does but stays paint; two kinds never share a
    /// cell; a liquid's touch lets the reactions decide where it meets another; the kind travels by id
    /// through saves and rule switches, and lapses with its mod, the same on every peer.
    /// </summary>
    [Category("Simulation")]
    public class LiquidTests
    {
        static Rules R => ChemistryTests.R;
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        const string HoneyRow = @"""honey"": { ""name"": ""Honey"", ""thick"": 4, ""heap"": 6, ""soaks"": false, ""floats"": false, ""speed"": 0.4,
            ""tags"": [""sticky""], ""look"": { ""color"": [0.85, 0.6, 0.1] } }";
        const string PaintRow = @"""red-paint"": { ""name"": ""Red paint"", ""soaks"": false, ""tags"": [""paint""], ""look"": { ""color"": [0.8, 0.1, 0.1] } }";
        const string LavaRow = @"""molten"": { ""name"": ""Molten rock"", ""thick"": 2, ""heap"": 3, ""soaks"": false, ""floats"": false, ""touch"": ""lava"" }";

        // A bare patch: one liquids table, and soaking off so a pour keeps every unit.
        static Rules With(params string[] rows) =>
            R.WithMods(new[] { ModPatch.Parse(@"{ ""liquids"": { " + string.Join(", ", rows) + @" }, ""sim"": { ""water"": { ""soakPass"": 0 } } }") });

        // Ground dug a step deeper every 4 cells eastward from x = 8, to the floor (-6) at x = 32.
        static World Slope(Rules rules)
        {
            var w = new World(rules, 64, 64, 7, "grass");
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++) w.At(x, y).Height = -Math.Min(6, Math.Max(0, x - 8) / 4);
            return w;
        }

        static World Flat(Rules rules) => new World(rules, 64, 64, 7, "grass");

        static int Wet(World w, string liquid) => w.Cells.Count(c => c.Water > 0 && w.LiquidId(c) == liquid);
        static int Units(World w, string liquid) => w.Cells.Where(c => c.Water > 0 && w.LiquidId(c) == liquid).Sum(c => c.Water);
        static int Deepest(World w, string liquid) => w.Cells.Where(c => c.Water > 0 && w.LiquidId(c) == liquid).Select(c => c.Water).DefaultIfEmpty().Max();
        static int Farthest(World w, string liquid)
        {
            int far = 0;
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++)
                    if (w.At(x, y).Water > 0 && w.LiquidId(w.At(x, y)) == liquid) far = Math.Max(far, x);
            return far;
        }

        // Units in eights round a 2 by 2 patch from (x, y), so none is over a cell's most.
        static void Pour(World w, string liquid, int x, int y, int units)
        {
            for (int k = 0; k < units; k += 8) Assert.That(w.AddLiquid(x + (k / 8 & 1), y + (k / 16 & 1), liquid, 8), Is.True);
        }

        [Test]
        public void TheBaseRulesHaveWaterAlone()
        {
            Assert.That(R.Liquids.Select(l => l.Id), Is.EqualTo(new[] { "water" }));
            Assert.That(R.LiquidIndex("water"), Is.Zero);
            Assert.That(R.ToJson(), Does.Contain("\"liquids\": {}"), "the table is there, empty");
            Assert.That(R.WithMods(new[] { ModPatch.Parse(@"{ ""liquids"": {} }") }).Fingerprint, Is.EqualTo(R.Fingerprint), "an empty table isn't in the fingerprint");
        }

        [Test]
        public void HoneyIsThickSlowAndHeaps()
        {
            var rules = With(HoneyRow);
            var honey = Slope(rules);
            var water = Slope(rules);
            Pour(honey, "honey", 12, 32, 64);
            Pour(water, "water", 12, 32, 64);
            for (int t = 0; t < 40; t++)
            {
                honey.Step();
                water.Step();
            }
            Assert.That(Farthest(honey, "honey"), Is.LessThan(Farthest(water, "water")), "honey runs down the slope more slowly");
            for (int t = 0; t < 560; t++)
            {
                honey.Step();
                water.Step();
            }
            Assert.That(Units(honey, "honey"), Is.EqualTo(64), "none soaks in or is lost");
            Assert.That(Wet(honey, "honey"), Is.LessThan(Wet(water, "water")), "honey stands in a mound where water spreads");
            Assert.That(Deepest(honey, "honey"), Is.GreaterThan(Deepest(water, "water")));
            Assert.That(honey.Cells.Count(c => c.Water > 0 && c.Liquid != rules.LiquidIndex("honey")), Is.Zero, "it's honey wherever it went");
            TestContext.WriteLine($"honey {Wet(honey, "honey")} cells, {Deepest(honey, "honey")} deep, to x {Farthest(honey, "honey")}; water {Wet(water, "water")}, {Deepest(water, "water")}, {Farthest(water, "water")}");
            Assert.That(honey.CellHash(), Is.EqualTo("4a7f3f69"), "the honey golden");
        }

        [Test]
        public void RedPaintRunsAsWaterDoesAndStaysPaint()
        {
            var rules = With(PaintRow);
            var paint = Slope(rules);
            var water = Slope(rules);
            Pour(paint, "red-paint", 12, 32, 48);
            Pour(water, "water", 12, 32, 48);
            for (int t = 0; t < 300; t++)
            {
                paint.Step();
                water.Step();
            }
            int kind = rules.LiquidIndex("red-paint");
            Assert.That(paint.Cells.Select(c => c.Water), Is.EqualTo(water.Cells.Select(c => c.Water)), "thick 1 and heap 2: water's own run, cell for cell");
            Assert.That(paint.Cells.Where(c => c.Water > 0).All(c => c.Liquid == kind), Is.True);
            Assert.That(Farthest(paint, "red-paint"), Is.GreaterThan(16), "it ran down the slope");
            Assert.That(paint.CellHash(), Is.Not.EqualTo(water.CellHash()), "the kind is in the hash");
            Assert.That(paint.CellText(), Does.Contain(",qred-paint;"), "by its id");
            Assert.That(paint.CellHash(), Is.EqualTo("681d5aa9"), "the paint golden");
        }

        [Test]
        public void TwoKindsNeverShareACell()
        {
            var rules = With(PaintRow, HoneyRow);
            var w = Flat(rules);
            Pour(w, "red-paint", 30, 32, 48);
            Pour(w, "water", 34, 32, 48);
            Assert.That(w.AddLiquid(34, 32, "red-paint", 1), Is.False, "paint can't go onto water");
            for (int t = 0; t < 200; t++) w.Step();
            Assert.That(Units(w, "red-paint"), Is.EqualTo(48));
            Assert.That(Units(w, "water"), Is.EqualTo(48), "water is a wall to paint, paint to water: neither runs into the other");
            Assert.That(Wet(w, "red-paint"), Is.GreaterThan(1));
            Assert.That(Wet(w, "water"), Is.GreaterThan(1));
        }

        [Test]
        public void RainAndSpringsDontFallIntoAnotherLiquid()
        {
            var rules = With(HoneyRow);
            var w = Flat(rules);
            Pour(w, "honey", 10, 10, 8);
            w.AddWater(10, 10, 4);
            Assert.That(w.At(10, 10).Water, Is.EqualTo(8), "water onto honey is refused");
            Assert.That(w.LiquidId(w.At(10, 10)), Is.EqualTo("honey"));
            w.At(10, 10).Water = 0;
            w.AddWater(10, 10, 4);
            Assert.That(w.LiquidId(w.At(10, 10)), Is.EqualTo("water"), "a dry cell's old kind means nothing");
        }

        [Test]
        public void MoltenRockTouchingAPondSetsItsEdgeToRock()
        {
            // A mod's liquid lava (J-062: the base lava stays terrain): its touch is the lava element, and the
            // base rows decide: lava wells up on the wet cell and the water there quenches it into rock.
            var rules = With(LavaRow);
            var w = Flat(rules);
            for (int y = 28; y < 36; y++)
                for (int x = 36; x < 44; x++) w.At(x, y).Height = -2;
            for (int y = 28; y < 36; y++)
                for (int x = 36; x < 44; x++) w.AddWater(x, y, 8);
            Pour(w, "molten", 34, 32, 32);
            for (int t = 0; t < 120; t++) w.Step();
            Assert.That(w.Cells.Count(c => c.T == "rock"), Is.GreaterThan(0), "the pond's edge set to rock");
            Assert.That(w.Cells.Count(c => c.Water > 0 && c.Liquid != 0 && c.T == "rock"), Is.Zero);
            Assert.That(w.CellHash(), Is.EqualTo("81a98542"), "the molten-rock golden");
        }

        [Test]
        public void SkippingStillChunksChangesNothingWithAThickLiquid()
        {
            // The still-water pass waits out the thickest liquid's turn before it trusts a chunk to be still.
            string Run(bool skip)
            {
                var rules = With(HoneyRow, PaintRow);
                var w = new World(rules, 192, 128, 3, "grass") { SkipStillWater = skip };
                Pour(w, "honey", 60, 60, 64);
                Pour(w, "red-paint", 130, 70, 40);
                for (int t = 0; t < 400; t++)
                {
                    if (t == 200) Pour(w, "honey", 63, 63, 16);
                    w.Step();
                }
                return w.CellHash();
            }
            Assert.That(Run(true), Is.EqualTo(Run(false)));
        }

        [Test]
        public void TheKindComesThroughASaveByItsId()
        {
            var rules = With(HoneyRow, PaintRow);
            var w = Flat(rules);
            Pour(w, "red-paint", 20, 20, 16);
            Pour(w, "honey", 40, 40, 16);
            for (int t = 0; t < 30; t++) w.Step();
            string save = w.Save(new ComboTests.Blank());
            Assert.That(save, Does.Contain("\"honey\"").And.Contain("\"red-paint\""));
            var back = World.Load(save, rules, new ComboTests.Blank());
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            for (int t = 0; t < 30; t++)
            {
                w.Step();
                back.Step();
            }
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
        }

        [Test]
        public void ARuleSwitchRemapsTheKindsById()
        {
            var paintOnly = With(PaintRow);
            var both = With(PaintRow, HoneyRow);
            var w = Flat(both);
            Pour(w, "honey", 20, 20, 8);
            Pour(w, "red-paint", 40, 40, 8);
            Assert.That(both.LiquidIndex("honey"), Is.EqualTo(2));
            var honeyFirst = With(HoneyRow, PaintRow);
            w.ApplyRules(honeyFirst);
            Assert.That(honeyFirst.LiquidIndex("honey"), Is.EqualTo(1));
            Assert.That(w.LiquidId(w.At(20, 20)), Is.EqualTo("honey"), "the index moved, the liquid didn't");
            Assert.That(w.LiquidId(w.At(40, 40)), Is.EqualTo("red-paint"));
            w.ApplyRules(paintOnly);
            Assert.That(w.At(20, 20).Water, Is.Zero, "a liquid the rules lost dries");
            Assert.That(w.LiquidId(w.At(40, 40)), Is.EqualTo("red-paint"));
            w.ApplyRules(R);
            Assert.That(w.Cells.Count(c => c.Water > 0), Is.Zero);
        }

        [TestCase(@"""water"": { ""thick"": 2 }", "water is built in")]
        [TestCase(@"""goo"": { ""heap"": 1 }", "heap must be a whole number from 2")]
        [TestCase(@"""goo"": { ""thick"": 1.5 }", "thick must be a whole number")]
        [TestCase(@"""goo"": { ""wet"": 1 }", "unknown field \"wet\"")]
        [TestCase(@"""goo"": { ""boils"": ""vapour"" }", "boils vapour isn't a gas")]
        [TestCase(@"""goo"": { ""touch"": ""glue"" }", "touch glue isn't an element")]
        [TestCase(@"""goo"": { ""lapse"": { ""into"": ""honey"" } }", "lapse.into honey must be water")]
        public void ABadLiquidIsRefused(string row, string message)
        {
            var e = Assert.Throws<ModException>(() => With(row));
            Assert.That(string.Join("; ", e.Errors), Does.Contain(message));
        }

        [Test]
        public void AReactionNamesALiquidThatIsThere()
        {
            var e = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(@"{ ""reactions"": { ""add"": [ { ""id"": ""pour-goo"", ""element"": ""water"",
                ""do"": [{ ""addWater"": 2, ""liquid"": ""goo"" }] } ] } }") }));
            Assert.That(string.Join("; ", e.Errors), Does.Contain("liquid goo unknown"));
        }

        // ---------- in a room: a honey spill the way Claude would write it ----------

        const int Lapse = 900;

        static string HoneyMod(int after = Lapse) => @"{ ""title"": ""Honey spill"", ""idea"": ""A spell that spills a pool of thick honey, gone again in a while."", ""patch"": {
            ""elements"": [""honey""],
            ""liquids"": { ""honey"": { ""name"": ""Honey"", ""thick"": 4, ""heap"": 6, ""soaks"": false, ""floats"": false, ""speed"": 0.4, ""tags"": [""sticky""],
                ""look"": { ""color"": [0.85, 0.6, 0.1] }, ""lapse"": { ""after"": " + after + @" } } },
            ""reactions"": { ""add"": [ { ""id"": ""honey-pours"", ""element"": ""honey"", ""if"": { ""notTerrainTag"": ""blocking"" },
                ""do"": [{ ""addWater"": ""power"", ""liquid"": ""honey"" }], ""log"": ""Honey oozes out"", ""stop"": true } ] },
            ""spells"": [
                { ""id"": ""honey-spill"", ""group"": ""water"", ""name"": ""Honey Spill"", ""look"": { ""family"": ""splash"", ""color"": [0.85, 0.6, 0.1] },
                  ""level"": 1, ""shape"": ""square"", ""size"": 3, ""element"": ""honey"", ""power"": 6, ""blurb"": ""Level 1. Spills thick honey that oozes downhill and stands in a mound."" },
                { ""id"": ""honey-drip"", ""group"": ""water"", ""name"": ""Honey Drip"", ""look"": { ""family"": ""splash"", ""color"": [0.85, 0.6, 0.1] },
                  ""level"": 1, ""shape"": ""point"", ""element"": ""honey"", ""power"": 4, ""blurb"": ""Level 1. A drip of honey on one cell."" } ] } }";

        static World Room()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            return w;
        }

        static int Honey(World w) => Wet(w, "honey");

        static string State(World w) => $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {w.RngState}";

        const int Switched = 1, Lapses = Switched + Lapse; // the mod goes on between ticks 1 and 2

        static TickBatch HoneyBatch(int t)
        {
            var batch = new TickBatch(t);
            if (t == Switched + 1) batch.Room.Add(RoomCommand.Apply("Bo", "honey", HoneyMod()));
            if (t == Switched + 2) batch.Casts.Add(new CastCommand("honey-spill", 32, 32, 0));
            if (t == Switched + 40) batch.Casts.Add(new CastCommand("honey-drip", 20, 40, 0));
            return batch;
        }

        static void CheckHoney(World a, World b, int t)
        {
            if (t % 50 == 0 || t >= Lapses - 2) Assert.That(State(b), Is.EqualTo(State(a)), $"tick {t}");
            if (t == Switched + 2) Assert.That(Honey(a), Is.GreaterThan(0), Last(a));
            if (t == Lapses - 1) Assert.That(Honey(a), Is.GreaterThan(9), "the honey stands until the lapse");
            if (t == Lapses) Assert.That(a.Cells.Count(c => c.Water > 0), Is.Zero, "it dries at the lapse, everywhere");
        }

        [Test]
        public void TwoPeersSpillHoneyAlikeAndItLapses()
        {
            var a = Room();
            var b = Room();
            var ra = a.RoomRules;
            var rb = b.RoomRules;
            for (int t = 1; t <= Lapses + 5; t++)
            {
                ra = HoneyBatch(t).RunOn(a, ra, out _);
                rb = HoneyBatch(t).RunOn(b, rb, out _);
                CheckHoney(a, b, t);
            }
            Assert.That(a.Rules.Fingerprint, Is.EqualTo(R.Fingerprint));
            Assert.That(a.Room.Retired.Select(r => $"{r.Table} {r.Id}"), Is.EqualTo(new[] { "liquids honey" }));
            Assert.That(State(b), Is.EqualTo(State(a)));
        }

        [Test]
        public void HoneyThatLapsesIntoWaterBecomesWater()
        {
            var w = Room();
            var done = w.ApplyRoom(RoomCommand.Apply("Bo", "honey", HoneyMod().Replace(@"""lapse"": { ""after"": " + Lapse + " }", @"""lapse"": { ""into"": ""water"" }")));
            Assert.That(done.Done, Is.True, done.Line);
            w.Cast("honey-spill", 32, 32, 0);
            w.Step();
            int units = w.Cells.Sum(c => c.Water);
            Assert.That(Honey(w), Is.GreaterThan(0));
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Bo", "honey")).Done, Is.True);
            Assert.That(Honey(w), Is.Zero);
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(units), "every unit is water now");
            Assert.That(w.Cells.Where(c => c.Water > 0).All(c => c.Liquid == 0), Is.True);
        }

        static string Last(World w) => w.Log.Count == 0 ? "" : w.Log[w.Log.Count - 1].Text;
    }
}
