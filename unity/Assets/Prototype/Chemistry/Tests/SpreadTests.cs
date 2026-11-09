using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Covers that spread (World.Spread.cs, docs/design/primitives.md §3 P2 and §5): a cover with spread lays
    /// itself on its neighbours on the soak pass, from the world's RNG, only where its onto and notOnto let it,
    /// the same on every peer; and a world with no spreading cover is the world it was.
    /// </summary>
    [Category("Simulation")]
    public class SpreadTests
    {
        static Rules R => ChemistryTests.R;
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        const int Lapse = 1800;

        // §5's "glowing moss that creeps over the river stones", as Claude would write it: a temporary cover that
        // spreads only onto stone (the stone wall and raised rock), never onto water or sand, laid on two stones.
        // Its glow is P4's and the view's: a bare cover look takes only a colour (looks.json), so none yet.
        static string Moss(int every = 30, double chance = 0.5) => @"{ ""title"": ""River moss"", ""idea"": ""Glowing moss that creeps over the river stones."", ""patch"": {
            ""covers"": { ""river-moss"": { ""name"": ""Glowing moss"", ""tags"": [], ""look"": { ""color"": [0.3, 0.85, 0.55] },
                ""spread"": { ""every"": " + every + @", ""chance"": " + chance.ToString(System.Globalization.CultureInfo.InvariantCulture) + @", ""onto"": [""stone"", ""rock""], ""notOnto"": [""sandy""] },
                ""lapse"": { ""after"": " + Lapse + @" } } },
            ""world"": { ""lay"": [ { ""cover"": ""river-moss"", ""cells"": [[31, 12], [32, 50]], ""seed"": 3 } ] } } }";

        // A river (x 26 to 37) down a grass world; in it a bed of raised rock (x 30 to 33), stone walls at its
        // head, a sand bar on each side and a stretch of drowned rock (y 30 to 33), a step down, under the water.
        static World River()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            for (int y = 0; y < 64; y++)
                for (int x = 26; x <= 37; x++)
                {
                    var c = w.At(x, y);
                    c.T = RiverTerrain(x, y);
                    if (c.T == "river") c.Water = 4;
                    if (c.T == "rock" && y >= 30 && y <= 33) c.Height = -1;
                }
            return w;
        }

        static string RiverTerrain(int x, int y)
        {
            if (x == 29 || x == 34) return y >= 8 && y <= 56 ? "sand" : "river";
            if (x < 30 || x > 33 || y < 4 || y > 56) return "river";
            return y < 8 ? "stone" : "rock";
        }

        static int Mossy(World w) => w.Cells.Count(c => c.Cover == "river-moss");

        static string State(World w) => $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {w.RngState}";

        // Where moss is: only on stone or rock, never on sand or open water.
        static void MossOnlyOnStones(World w, string when)
        {
            var mossy = w.Cells.Where(c => c.Cover == "river-moss").ToList();
            Assert.That(mossy.Select(c => c.T).Distinct(), Is.SubsetOf(new[] { "stone", "rock" }), when);
            Assert.That(mossy.Count(c => c.Water > 0 && !c.Frozen), Is.Zero, $"{when}: no moss under water");
        }

        [Test]
        public void TheRiverMossPassesTheChecks()
        {
            Assert.That(Problems(Moss()), Is.Empty);
        }

        [Test]
        public void MossCreepsOverTheRiverStonesOnBothPeers()
        {
            var a = River();
            var b = River();
            var ra = a.RoomRules;
            var rb = b.RoomRules;
            int switched = 1, lapses = switched + Lapse;
            int before = 0;
            for (int t = 1; t <= lapses + 2; t++)
            {
                TickBatch Batch()
                {
                    var batch = new TickBatch(t);
                    if (t == switched + 1) batch.Room.Add(RoomCommand.Apply("Bo", "river-moss", Moss()));
                    return batch;
                }
                ra = Batch().RunOn(a, ra, out _);
                rb = Batch().RunOn(b, rb, out _);
                if (t % 100 == 0 || t >= lapses - 1) Assert.That(State(b), Is.EqualTo(State(a)), $"tick {t}");
                if (t % 100 == 0) MossOnlyOnStones(a, $"tick {t}");
                if (t == lapses - 1) before = Mossy(a);
            }
            Assert.That(before, Is.GreaterThan(100), "the moss crept from its two stones");
            Assert.That(Mossy(a), Is.Zero, "every cell of it is swept at the lapse");
            Assert.That(a.Cells.Count(c => c.Slick != 0), Is.Zero, "and its reach with it");
            Assert.That(State(b), Is.EqualTo(State(a)));
        }

        [Test]
        public void TheMossGoldenHolds()
        {
            // Pins the spread: a change to the turn, the roll, the order or the landing moves it.
            var w = River();
            w.ApplyRoom(RoomCommand.Apply("Bo", "river-moss", Moss()));
            for (int t = 0; t < 600; t++) w.Step();
            Assert.That(Mossy(w), Is.EqualTo(212));
            Assert.That(w.CellHash(), Is.EqualTo("9a65822b"));
            Assert.That(w.RngState, Is.EqualTo(890931262u));
        }

        [Test]
        public void AWorldWithNoSpreadingCoverDrawsNothingForIt()
        {
            // The same river with the moss laid but its spread taken off: nothing creeps and the RNG is where it was.
            var plain = River();
            var still = River();
            plain.ApplyRoom(RoomCommand.Apply("Bo", "river-moss", Moss().Replace(@"""spread"": { ""every"": 30, ""chance"": 0.5, ""onto"": [""stone"", ""rock""], ""notOnto"": [""sandy""] },", "")));
            still.ApplyRoom(RoomCommand.Apply("Bo", "river-moss", Moss().Replace(@"""spread"": { ""every"": 30, ""chance"": 0.5, ""onto"": [""stone"", ""rock""], ""notOnto"": [""sandy""] },", @"""speed"": 1,")));
            Assert.That(plain.Rules.AnySpreads, Is.False);
            Assert.That(R.AnySpreads, Is.False, "no base cover spreads, so every golden holds");
            for (int t = 0; t < 300; t++)
            {
                plain.Step();
                still.Step();
            }
            Assert.That(Mossy(plain), Is.EqualTo(2));
            Assert.That($"{still.CellHash()} {still.RngState}", Is.EqualTo($"{plain.CellHash()} {plain.RngState}"));
        }

        [Test]
        public void NotOntoStopsItWhereOntoWouldLetIt()
        {
            // Moss onto any ground but sand, on a grass world with a sand strip: it never crosses the strip.
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            for (int y = 0; y < 64; y++) w.At(20, y).T = "sand";
            const string mod = @"{ ""covers"": { ""m"": { ""name"": ""M"", ""tags"": [], ""spread"": { ""every"": 30, ""chance"": 0.5, ""onto"": [""ground""], ""notOnto"": [""sandy""] } } },
                ""world"": { ""lay"": [ { ""cover"": ""m"", ""cells"": [[10, 32]], ""seed"": 1 } ] } }";
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Bo", "m", mod)).Done, Is.True);
            for (int t = 0; t < 1200; t++) w.Step();
            Assert.That(w.Cells.Count(c => c.Cover == "m"), Is.GreaterThan(20));
            Assert.That(Enumerable.Range(0, 64).Count(y => w.At(20, y).Cover == "m" || w.At(21, y).Cover == "m"), Is.Zero);
        }

        [Test]
        public void ItsReachStopsIt()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            const string mod = @"{ ""covers"": { ""m"": { ""name"": ""M"", ""tags"": [], ""spread"": { ""every"": 30, ""chance"": 0.5, ""reach"": 3 } } },
                ""world"": { ""lay"": [ { ""cover"": ""m"", ""cells"": [[32, 32]], ""seed"": 1 } ] } }";
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Bo", "m", mod)).Done, Is.True);
            for (int t = 0; t < 1200; t++) w.Step();
            // Three cells from where it was laid, counting that one: a diamond of radius 2, 13 cells.
            Assert.That(w.Cells.Count(c => c.Cover == "m"), Is.EqualTo(13));
        }

        [Test]
        public void ATickLaysNoMoreThanTheBudget()
        {
            // A whole chunk of moss on bare ground, every cell's neighbours bare: the spread stops at the cap each tick.
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            const string mod = @"{ ""covers"": { ""m"": { ""name"": ""M"", ""tags"": [], ""spread"": { ""every"": 30, ""chance"": 0.5 } } } }";
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Bo", "m", mod)).Done, Is.True);
            for (int y = 0; y < 64; y += 2)
                for (int x = y % 4; x < 64; x += 4) w.At(x, y).Cover = "m";
            int max = (int)Budgets.Number("materials.maxSpreadPerTick", 0);
            for (int t = 0; t < 64; t++)
            {
                int was = w.Cells.Count(c => c.Cover == "m");
                w.Step();
                Assert.That(w.Cells.Count(c => c.Cover == "m") - was, Is.LessThanOrEqualTo(max), $"tick {t}");
            }
        }

        // ---------- the checks ----------

        static string[] Problems(string json) => ModValidator.Validate(R, Budgets, json).Select(p => p.Message).ToArray();

        [Test]
        public void AModsSpreadIsSlowAndUnlikely()
        {
            Assert.That(Problems(Moss(every: 8)), Has.Member("Cover river-moss spreads every 8 ticks; a mod's cover spreads every 30 ticks or slower."));
            Assert.That(Problems(Moss(chance: 0.9)), Has.Member("Cover river-moss spreads with a chance of 0.9; a mod's cover spreads with a chance of 0.5 at most (give spread.chance)."));
            Assert.That(Problems(Moss().Replace(@"""notOnto""", @"""reach"": 65, ""notOnto""")), Has.Member("Cover river-moss spreads 65 cells; a mod's cover spreads at most 64."));
        }

        [Test]
        public void OntoNamesATerrainOrATag()
        {
            Assert.That(Problems(Moss().Replace(@"""sandy""", @"""pebbles""")), Has.Some.Contains("spread.notOnto pebbles is no terrain or terrain tag"));
            Assert.That(Problems(Moss().Replace(@"[""stone"", ""rock""]", @"""rock""")), Has.Some.Contains("spread.onto must be a list of terrain ids or tags"));
            Assert.That(Problems(Moss().Replace(@"""every"": 30", @"""every"": 0")), Has.Some.Contains("spread.every must be a whole number from 1"));
        }

        [Test]
        public void SetMaterialTakesTheSpreadGroup()
        {
            Mcp.RuleFields.CheckMaterial("covers", "spread.every", 60);
            Mcp.RuleFields.CheckMaterial("covers", "spread", Newtonsoft.Json.Linq.JObject.Parse(@"{ ""every"": 60, ""chance"": 0.2, ""onto"": [""rock""], ""notOnto"": [], ""reach"": 4 }"));
            var e = Assert.Throws<Mcp.RulesToolException>(() => Mcp.RuleFields.CheckMaterial("covers", "spread.rate", 1));
            Assert.That(e.Message, Does.Contain("spread takes every, chance, onto, notOnto, reach"));
        }
    }
}
