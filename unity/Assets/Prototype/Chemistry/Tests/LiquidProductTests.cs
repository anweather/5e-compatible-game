using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// A liquid's products (docs/design/primitives.md §3, P1 and P3; Rules.Liquids.cs, World.Liquids.cs):
    /// hot {for} lights what it touches while it's hot, then cools; freezes turns it into a terrain or a
    /// cover where water would freeze; sets {into, after} hardens it into a terrain where it lies. All of
    /// them through the material verbs and the fire and cold rows, never a rule for one liquid; the
    /// base lava stays terrain (J-062).
    /// </summary>
    [Category("Simulation")]
    public class LiquidProductTests
    {
        static Rules R => ChemistryTests.R;
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        // A mod's lava-like liquid: thick, hot for a while, set into rock after.
        const string MagmaRow = @"""magma"": { ""name"": ""Magma"", ""thick"": 2, ""heap"": 3, ""soaks"": false, ""floats"": false,
            ""hot"": { ""for"": 160 }, ""sets"": { ""into"": ""rock"", ""after"": 300 }, ""look"": { ""color"": [0.9, 0.3, 0.05] } }";
        // A mod's syrup that freezes into a cover of its own.
        const string SyrupRow = @"""syrup"": { ""name"": ""Syrup"", ""thick"": 3, ""heap"": 4, ""soaks"": false, ""freezes"": ""candy"" }";
        const string CandyCover = @"""covers"": { ""candy"": { ""name"": ""Rock candy"", ""tags"": [] } }";
        const string HoneyRow = @"""honey"": { ""name"": ""Honey"", ""thick"": 4, ""heap"": 6, ""soaks"": false }";

        static Rules WithAlso(string extra, params string[] rows) =>
            R.WithMods(new[] { ModPatch.Parse(@"{ ""liquids"": { " + string.Join(", ", rows) + @" }" + extra + @", ""sim"": { ""water"": { ""soakPass"": 0 } } }") });

        static Rules With(params string[] rows) => WithAlso("", rows);

        static Rules Syrup() => WithAlso(", " + CandyCover, SyrupRow, HoneyRow);

        // Grass dug a step deeper every 4 cells eastward from x = 8, to -6 at x = 32.
        static World Slope(Rules rules)
        {
            var w = new World(rules, 64, 64, 7, "grass");
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++) w.At(x, y).Height = -System.Math.Min(6, System.Math.Max(0, x - 8) / 4);
            return w;
        }

        static int Wet(World w, string liquid) => w.AllCells().Count(c => c.Water > 0 && w.LiquidId(c) == liquid);

        static void Pour(World w, string liquid, int x, int y, int units)
        {
            for (int k = 0; k < units; k += 8) Assert.That(w.AddLiquid(x + (k / 8 & 1), y + (k / 16 & 1), liquid, 8), Is.True);
        }

        static void Run(World w, int ticks)
        {
            for (int t = 0; t < ticks; t++) w.Step();
        }

        [Test]
        public void MagmaBurnsTheGrassOnItsWayThenCoolsAndSetsIntoRock()
        {
            var w = Slope(With(MagmaRow));
            Pour(w, "magma", 12, 32, 48);
            Assert.That(w.At(12, 32).Heat, Is.EqualTo(160), "fresh, it's hot for its hot.for");
            Assert.That(w.At(12, 32).Sets, Is.EqualTo(300), "and sets in its sets.after");
            Run(w, 60);
            Assert.That(w.AllCells().Count(c => c.Fire > 0 || c.T == "dirt"), Is.GreaterThan(0), "the grass it ran over and beside caught");
            Assert.That(w.AllCells().Where(c => c.Water > 0).All(c => c.Heat > 0), Is.True, "each cell it ran to took its heat");
            Run(w, 140);
            int magma = Wet(w, "magma");
            Assert.That(magma, Is.GreaterThan(0), "it hasn't set yet");
            Assert.That(w.AllCells().Where(c => c.Water > 0).All(c => c.Heat == 0), Is.True, "it cooled");
            Run(w, 110);
            Assert.That(Wet(w, "magma"), Is.Zero, "every cell of it set");
            Assert.That(w.AllCells().Count(c => c.T == "rock"), Is.EqualTo(magma), "into rock, where it lay");
            Assert.That(w.AllCells().Count(c => c.Sets != 0 || c.Heat != 0), Is.Zero);
            Assert.That(w.AllCells().Count(c => c.T == "dirt"), Is.GreaterThan(magma), "the grass burnt beyond it too");
            Assert.That(w.Log.Any(l => l.Text == "The magma sets hard"), Is.True);
            TestContext.WriteLine($"magma set into {magma} cells of rock; {w.AllCells().Count(c => c.T == "dirt")} cells of grass burnt");
            Assert.That(w.CellHash(), Is.EqualTo("32c93ebc"), "the magma golden");
        }

        [Test]
        public void SettingComesThroughASave()
        {
            var rules = With(MagmaRow);
            var w = Slope(rules);
            Pour(w, "magma", 12, 32, 32);
            Run(w, 100);
            string save = w.Save(new ComboTests.Blank());
            var back = World.Load(save, rules, new ComboTests.Blank());
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(w.CellText(), Does.Contain(",p"), "Sets is in the hash while it counts");
            Run(w, 250);
            Run(back, 250);
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(Wet(back, "magma"), Is.Zero);
        }

        [Test]
        public void PushedOutOfACellItStillSetsAndBurns()
        {
            var w = new World(With(MagmaRow), 64, 64, 7, "grass");
            Assert.That(w.AddLiquid(20, 20, "magma", 4), Is.True);
            w.DisplaceWater(20, 20);
            var pushed = w.AllCells().Where(c => c.Water > 0).ToList();
            Assert.That(pushed, Is.Not.Empty);
            Assert.That(pushed.All(c => c.Sets == 300 && c.Heat == 160), Is.True, "each cell it was pushed onto took its count and heat");
            Run(w, 310);
            Assert.That(Wet(w, "magma"), Is.Zero, "pushed magma sets as poured magma does");
        }

        [Test]
        public void ADriedCellHoldsNoSettingInItsHashOrSave()
        {
            var rules = With(MagmaRow);
            var w = new World(rules, 64, 64, 7, "grass");
            Assert.That(w.AddLiquid(20, 20, "magma", 2), Is.True);
            var c = w.At(20, 20);
            c.Water = 0;
            Assert.That(c.Sets, Is.GreaterThan(0), "the count lingers until the timers pass clears it");
            Assert.That(w.CellText(), Does.Not.Contain(",p"));
            var back = World.Load(w.Save(new ComboTests.Blank()), rules, new ComboTests.Blank());
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()), "a peer loading now agrees");
            Assert.That(w.AddLiquid(20, 20, "magma", 2), Is.True);
            Assert.That(c.Sets, Is.EqualTo(300), "a fresh pour starts its own count, not the dried one's");
        }

        [Test]
        public void SyrupFreezesIntoItsCoverWhereColdLands()
        {
            var w = new World(Syrup(), 64, 64, 7, "grass");
            Pour(w, "syrup", 20, 20, 32);
            Pour(w, "honey", 40, 40, 16);
            Run(w, 30);
            var syrup = Enumerable.Range(0, 64 * 64).Where(k => w.At(k % 64, k / 64).Water > 0 && w.LiquidId(w.At(k % 64, k / 64)) == "syrup").ToList();
            Assert.That(syrup.Count, Is.GreaterThan(1));
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++) w.Apply(x, y, "cold", 1);
            Assert.That(Wet(w, "syrup"), Is.Zero, "the base cold-liquid row froze it (freezes: true)");
            Assert.That(syrup.All(k => w.At(k % 64, k / 64).Cover == "candy"), Is.True, "into its cover, where it lay");
            Assert.That(Wet(w, "honey"), Is.GreaterThan(0), "a liquid without freezes doesn't freeze");
            Assert.That(w.AllCells().Any(c => c.Frozen), Is.False);
            Assert.That(w.CellHash(), Is.EqualTo("b1db7115"), "the syrup golden");
        }

        [Test]
        public void SyrupFreezesOnArcticGroundAndUnderSnow()
        {
            var rules = R.WithMods(new[] { ModPatch.Parse(@"{ ""liquids"": { " + SyrupRow + " }, " + CandyCover + " }") });
            var w = new World(rules, 64, 64, 7, "grass");
            for (int y = 0; y < 64; y++)
                for (int x = 30; x < 64; x++) w.At(x, y).T = "arctic";
            Assert.That(w.AddLiquid(40, 20, "syrup", 4), Is.True);
            Assert.That(w.AddLiquid(10, 20, "syrup", 2), Is.True);
            Run(w, rules.Sim.SoakPass + 1);
            Assert.That(w.At(40, 20).Cover, Is.EqualTo("candy"), "freezing ground freezes it, as it does water");
            Assert.That(w.At(40, 20).Water, Is.Zero);
            Assert.That(w.At(10, 20).Water, Is.GreaterThan(0), "on grass it stays syrup");
            w.WeatherSet = (WeatherKind.Snow, w.Tick, w.Tick + 100000);
            for (int t = 0; t < 2000 && Wet(w, "syrup") > 0; t++) w.Step();
            Assert.That(Wet(w, "syrup"), Is.Zero, "snow freezes a thin pool of it");
        }

        [TestCase(@"""goo"": { ""hot"": { ""for"": 0 } }", "hot.for must be a whole number of ticks from 1")]
        [TestCase(@"""goo"": { ""hot"": 30 }", "hot must be an object")]
        [TestCase(@"""goo"": { ""hot"": { ""for"": 30, ""burns"": 2 } }", "hot.burns unknown")]
        [TestCase(@"""goo"": { ""sets"": { ""into"": ""rock"" } }", "sets.after must be a whole number of ticks")]
        [TestCase(@"""goo"": { ""sets"": { ""into"": ""honey"", ""after"": 60 } }", "sets.into honey isn't a terrain")]
        [TestCase(@"""goo"": { ""freezes"": ""glue"" }", "freezes glue isn't a terrain or cover")]
        public void ABadProductIsRefused(string row, string message)
        {
            var e = Assert.Throws<ModException>(() => With(row));
            Assert.That(string.Join("; ", e.Errors), Does.Contain(message));
        }

        [Test]
        public void AProductOfTheBaseIsFine()
        {
            var rules = With(@"""goo"": { ""freezes"": ""rock"", ""sets"": { ""into"": ""rubble"", ""after"": 21600 }, ""hot"": { ""for"": 1 } }");
            var goo = rules.Liquid(rules.LiquidIndex("goo"));
            Assert.That((goo.Freezes, goo.SetsInto, goo.SetsAfter, goo.HotFor), Is.EqualTo(("rock", "rubble", 21600, 1)));
        }

        // ---------- in a room: a lava flow and a syrup spill the way Claude would write them ----------

        const string LavaMod = @"{ ""title"": ""Lava flow"", ""idea"": ""A flow of lava that burns its way downhill and sets into rock."", ""patch"": {
            ""elements"": [""magma""],
            ""liquids"": { ""magma"": { ""name"": ""Magma"", ""thick"": 2, ""heap"": 3, ""soaks"": false, ""floats"": false, ""hot"": { ""for"": 160 },
                ""sets"": { ""into"": ""rock"", ""after"": 300 }, ""lapse"": { ""after"": 3000 } } },
            ""reactions"": { ""add"": [
                { ""id"": ""magma-wells"", ""element"": ""magma"", ""if"": { ""notTerrainTag"": ""blocking"" }, ""do"": [{ ""addWater"": ""power"", ""liquid"": ""magma"" }], ""stop"": true } ] },
            ""spells"": [
                { ""id"": ""lava-flow"", ""group"": ""fire"", ""name"": ""Lava Flow"", ""look"": { ""family"": ""splash"", ""color"": [0.9, 0.3, 0.05] },
                  ""level"": 2, ""shape"": ""square"", ""size"": 2, ""element"": ""magma"", ""power"": 6, ""blurb"": ""Level 2. Molten rock that burns its way downhill and sets."" },
                { ""id"": ""lava-drip"", ""group"": ""fire"", ""name"": ""Lava Drip"", ""look"": { ""family"": ""splash"", ""color"": [0.9, 0.3, 0.05] },
                  ""level"": 1, ""shape"": ""point"", ""element"": ""magma"", ""power"": 4, ""blurb"": ""Level 1. A drip of molten rock on one cell."" } ] } }";

        const string SyrupMod = @"{ ""title"": ""Syrup spill"", ""idea"": ""A pool of syrup that frost turns to rock candy."", ""patch"": {
            ""elements"": [""syrup""],
            ""covers"": { ""candy"": { ""name"": ""Rock candy"", ""tags"": [], ""lapse"": { ""after"": 3000 } } },
            ""liquids"": { ""syrup"": { ""name"": ""Syrup"", ""thick"": 3, ""heap"": 4, ""soaks"": false, ""freezes"": ""candy"", ""lapse"": { ""after"": 3000 } } },
            ""reactions"": { ""add"": [
                { ""id"": ""syrup-pours"", ""element"": ""syrup"", ""if"": { ""notTerrainTag"": ""blocking"" }, ""do"": [{ ""addWater"": ""power"", ""liquid"": ""syrup"" }], ""stop"": true } ] },
            ""spells"": [
                { ""id"": ""syrup-spill"", ""group"": ""water"", ""name"": ""Syrup Spill"", ""look"": { ""family"": ""splash"", ""color"": [0.6, 0.3, 0.1] },
                  ""level"": 1, ""shape"": ""square"", ""size"": 2, ""element"": ""syrup"", ""power"": 6, ""blurb"": ""Level 1. A pool of syrup."" },
                { ""id"": ""syrup-drip"", ""group"": ""water"", ""name"": ""Syrup Drip"", ""look"": { ""family"": ""splash"", ""color"": [0.6, 0.3, 0.1] },
                  ""level"": 1, ""shape"": ""point"", ""element"": ""syrup"", ""power"": 4, ""blurb"": ""Level 1. A drip of syrup on one cell."" } ] } }";

        static World Room()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            return w;
        }

        static string State(World w) => $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {w.RngState}";

        static TickBatch Batch(int t)
        {
            var batch = new TickBatch(t);
            if (t == 2) batch.Room.Add(RoomCommand.Apply("Bo", "lava", LavaMod));
            if (t == 2) batch.Room.Add(RoomCommand.Apply("Ana", "syrup", SyrupMod));
            if (t == 3) batch.Casts.Add(new CastCommand("lava-flow", 20, 32, 0));
            if (t == 4) batch.Casts.Add(new CastCommand("syrup-spill", 44, 20, 0));
            if (t == 60) batch.Casts.Add(new CastCommand("sleet-storm", 44, 20, 0));
            return batch;
        }

        [Test]
        public void TwoPeersRunTheLavaAndTheSyrupAlike()
        {
            var a = Room();
            var b = Room();
            var ra = a.RoomRules;
            var rb = b.RoomRules;
            for (int t = 1; t <= 420; t++)
            {
                ra = Batch(t).RunOn(a, ra, out _);
                rb = Batch(t).RunOn(b, rb, out _);
                if (t % 20 == 0) Assert.That(State(b), Is.EqualTo(State(a)), $"tick {t}");
                if (t == 5) Assert.That(Wet(a, "magma") > 0 && Wet(a, "syrup") > 0, Is.True, "both spells poured: " + string.Join(" | ", a.Log.Select(l => l.Text)));
            }
            Assert.That(Wet(a, "magma"), Is.Zero, "the lava set");
            Assert.That(a.AllCells().Count(c => c.T == "rock"), Is.GreaterThan(0));
            Assert.That(a.AllCells().Count(c => c.Cover == "candy"), Is.GreaterThan(0), "sleet froze the syrup to candy");
            Assert.That(State(b), Is.EqualTo(State(a)));
        }
    }
}
