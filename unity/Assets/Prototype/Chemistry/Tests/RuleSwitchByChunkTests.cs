using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// A rule switch that could unfit cells makes the active chunks fit on its tick and every other chunk when it
    /// activates (World.Fit.cs, J-086, #190); an inactive chunk not yet made to fit reads as fitting would make it.
    /// </summary>
    [Category("Simulation")]
    public class RuleSwitchByChunkTests
    {
        static Rules R => ChemistryTests.R;

        static Rules moon;
        // The base rules with the Moonstone mod: one terrain more, which switching back drops.
        static Rules Moon => moon ??= RuleSet.Plain(R).WithMods(new[] { Moonstone }).Effective;

        static ActiveMod Moonstone => new ActiveMod("moonstone", File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", "moonstone.json")));

        // Three chunks by two of grass under Moonstone, a block of moonstone in chunk (0, 0) and in chunk (2, 1).
        static World Field(Rules rules)
        {
            var w = new World(rules, 3 * Chunks.Size, 2 * Chunks.Size, 4, "grass");
            for (int y = 10; y < 14; y++)
                for (int x = 10; x < 14; x++)
                {
                    w.Paint(x, y, "moonstone");
                    w.Paint(2 * Chunks.Size + x, Chunks.Size + y, "moonstone");
                }
            return w;
        }

        static Cell Far(World w) => w.At(2 * Chunks.Size + 11, Chunks.Size + 11);

        [Test]
        public void ASwitchThatDropsATerrainChangesTheActiveChunksNowAndNoInactiveOne()
        {
            var w = Field(Moon);
            w.SetActive(2, 1, false);
            string far = w.ChunkHash(2, 1);
            w.ApplyRules(R);
            Assert.That(w.At(11, 11).T, Is.EqualTo("dirt"), "an active chunk is made to fit on the switch's tick");
            Assert.That(w.ChunkHash(2, 1), Is.EqualTo(far), "an inactive chunk's cells are left as they are");
            Assert.That(Far(w).T, Is.EqualTo("moonstone"));
            Assert.That(w.FitOf(0, 0), Is.EqualTo(w.FitNumber));
            Assert.That(w.FitOf(2, 1), Is.LessThan(w.FitNumber), "it waits to be made to fit");
        }

        [Test]
        public void ASwitchThatUnfitsNothingLeavesEveryChunkFitting()
        {
            var w = Field(Moon);
            w.SetActive(2, 1, false);
            w.ApplyRules(RuleSet.Plain(Moon).WithMods(new[] { Moonstone, new ActiveMod("conduct", "{ \"patch\": { \"sim\": { \"conductLimit\": 9 } } }") }).Effective);
            Assert.That(w.FitOf(2, 1), Is.EqualTo(w.FitNumber));
            Assert.That(w.FitText(), Is.Empty);
        }

        [Test]
        public void AnInactiveChunkIsMadeToFitWhenItActivatesAsTheFullPassMade()
        {
            // The full pass the switch used to run over every cell is what an all-active world still gets.
            var all = Field(Moon);
            all.ApplyRules(R);
            var w = Field(Moon);
            w.SetActive(2, 1, false);
            w.ApplyRules(R);
            Assert.That(w.ChunkHash(2, 1), Is.Not.EqualTo(all.ChunkHash(2, 1)));
            w.SetActive(2, 1, true);
            Assert.That(Far(w).T, Is.EqualTo("dirt"));
            Assert.That(w.ChunkHash(2, 1), Is.EqualTo(all.ChunkHash(2, 1)));
            Assert.That(w.CellHash(), Is.EqualTo(all.CellHash()), "and the cell hash forgets the switch once every chunk fits");
            Assert.That(w.FitText(), Is.Empty);
        }

        [Test]
        public void SetAllActiveMakesEveryChunkFit()
        {
            var all = Field(Moon);
            all.ApplyRules(R);
            var w = Field(Moon);
            w.SetAllActive(false);
            w.ApplyRules(R);
            Assert.That(w.At(11, 11).T, Is.EqualTo("moonstone"));
            w.SetAllActive(true);
            Assert.That(w.CellHash(), Is.EqualTo(all.CellHash()));
        }

        [Test]
        public void AnUnfitInactiveCellReadsAsFittingWouldMakeIt()
        {
            var w = Field(Moon);
            w.SetActive(2, 1, false);
            Assert.That(w.IsBlocking(Far(w)), Is.True, "moonstone blocks");
            w.ApplyRules(R);
            var c = Far(w);
            Assert.That(c.T, Is.EqualTo("moonstone"), "not written");
            Assert.That(w.TerrainOf(c), Is.SameAs(R.Terrain["dirt"]), "but read as the dirt fitting makes it");
            Assert.That(w.IsBlocking(c), Is.False);
            Assert.That(w.HasTag(c, "trellis"), Is.False);
            Assert.That(w.LineOfSight(2 * Chunks.Size + 8, Chunks.Size + 11, 2 * Chunks.Size + 16, Chunks.Size + 11), Is.True, "nothing blocks a sight line across it");
            Assert.That(w.ElevationUnits(c), Is.EqualTo(w.ElevationUnits(w.At(11, 11))));
        }

        [Test]
        public void AnUnfitInactiveCellsHeightReadsClampedIntoTheNewRange()
        {
            var high = RuleSet.Plain(R).WithMods(new[] { new ActiveMod("high", "{ \"patch\": { \"sim\": { \"height\": { \"max\": 12, \"blockAt\": 12 } } } }") }).Effective;
            var w = new World(high, 2 * Chunks.Size, Chunks.Size, 4, "grass");
            var c = w.At(Chunks.Size + 5, 5);
            c.Height = 11;
            w.SetActive(1, 0, false);
            w.ApplyRules(R);
            Assert.That(c.Height, Is.EqualTo(11), "not written");
            Assert.That(w.HeightOf(c), Is.EqualTo(R.Sim.Height.Max));
            Assert.That(w.IsRaisedWall(c), Is.EqualTo(R.Sim.Height.Max >= R.Sim.Height.BlockAt));
            w.SetActive(1, 0, true);
            Assert.That(c.Height, Is.EqualTo(R.Sim.Height.Max));
        }

        [Test]
        public void ATerrainDroppedAndBroughtBackSurvivesInAChunkNobodyWasNear()
        {
            // A chunk inactive across two switches is made to fit the rules when it activates, not each set in turn.
            var w = Field(Moon);
            w.SetActive(2, 1, false);
            w.ApplyRules(R);
            w.ApplyRules(Moon);
            Assert.That(w.FitOf(2, 1), Is.LessThan(w.FitNumber), "switching it back unfits nothing, so the far chunk still waits");
            w.SetActive(2, 1, true);
            Assert.That(Far(w).T, Is.EqualTo("moonstone"));
            Assert.That(w.At(11, 11).T, Is.EqualTo("dirt"), "the active chunk lost it at the first switch");
        }

        // ---------- liquids: a chunk's kinds are indices into the liquids it was made under ----------

        const string HoneyRow = @"""honey"": { ""name"": ""Honey"", ""thick"": 4, ""heap"": 6, ""soaks"": false, ""floats"": false, ""tags"": [""sticky""] }";
        const string PaintRow = @"""red-paint"": { ""name"": ""Red paint"", ""soaks"": false, ""tags"": [""paint""] }";

        static Rules Liquids(params string[] rows) =>
            R.WithMods(new[] { ModPatch.Parse(@"{ ""liquids"": { " + string.Join(", ", rows) + @" }, ""sim"": { ""water"": { ""soakPass"": 0 } } }") });

        [Test]
        public void AnInactiveChunksLiquidsKeepTheirIdsUntilItActivates()
        {
            var both = Liquids(PaintRow, HoneyRow);
            var honeyFirst = Liquids(HoneyRow, PaintRow);
            var paintOnly = Liquids(PaintRow);
            var w = new World(both, 2 * Chunks.Size, Chunks.Size, 4, "grass");
            int far = Chunks.Size + 20;
            Assert.That(w.AddLiquid(far, 20, "honey", 8), Is.True);
            Assert.That(w.AddLiquid(far, 30, "red-paint", 8), Is.True);
            Assert.That(w.AddLiquid(20, 20, "honey", 8), Is.True);
            w.SetActive(1, 0, false);
            w.ApplyRules(honeyFirst);
            Assert.That(w.LiquidIdAt(20, 20), Is.EqualTo("honey"), "an active chunk's kinds are remapped now");
            Assert.That(w.LiquidIdAt(far, 20), Is.EqualTo("honey"), "an inactive chunk's mean what they did");
            Assert.That(w.LiquidIdAt(far, 30), Is.EqualTo("red-paint"));
            string hash = w.CellHash();
            w.ApplyRules(paintOnly);
            Assert.That(w.At(20, 20).Water, Is.Zero, "a liquid the rules lost dries in an active chunk");
            Assert.That(w.At(far, 20).Water, Is.EqualTo(8), "and waits in an inactive one");
            Assert.That(w.CellHash(), Is.Not.EqualTo(hash));
            w.SetActive(1, 0, true);
            Assert.That(w.At(far, 20).Water, Is.Zero);
            Assert.That(w.LiquidIdAt(far, 30), Is.EqualTo("red-paint"));
            Assert.That(w.At(far, 30).Liquid, Is.EqualTo(paintOnly.LiquidIndex("red-paint")));
        }

        [Test]
        public void ASaveKeepsWhichChunksWaitToBeMadeToFit()
        {
            var both = Liquids(PaintRow, HoneyRow);
            var paintOnly = Liquids(PaintRow);
            var w = new World(both, 2 * Chunks.Size, Chunks.Size, 4, "grass");
            Assert.That(w.AddLiquid(Chunks.Size + 20, 20, "honey", 8), Is.True);
            Assert.That(w.AddLiquid(Chunks.Size + 20, 30, "red-paint", 8), Is.True);
            w.SetActive(1, 0, false);
            w.ApplyRules(paintOnly);
            string save = w.Save(new ComboTests.Blank());
            var back = World.Load(save, paintOnly, new ComboTests.Blank());
            Assert.That(back.FitOf(1, 0), Is.EqualTo(w.FitOf(1, 0)));
            Assert.That(back.FitNumber, Is.EqualTo(w.FitNumber));
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(back.LiquidIdAt(Chunks.Size + 20, 20), Is.EqualTo("honey"));
            w.SetActive(1, 0, true);
            back.SetActive(1, 0, true);
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(back.Save(new ComboTests.Blank()), Is.EqualTo(w.Save(new ComboTests.Blank())));
        }

        [Test]
        public void TheCellHashCarriesTheChunksBehindTheRules()
        {
            var a = Field(Moon);
            var b = Field(Moon);
            b.ApplyRules(R);
            a.SetActive(2, 1, false);
            a.ApplyRules(R);
            b.SetActive(2, 1, false);
            // Cells alike but for the inactive chunk's, which b made to fit: the fit numbers tell them apart too.
            Assert.That(a.FitText(), Is.EqualTo($"#2,1,{a.FitNumber - 1}"));
            Assert.That(b.FitText(), Is.Empty);
            Assert.That(a.CellText(), Does.EndWith(a.FitText()));
            Assert.That(a.CellHash(), Is.Not.EqualTo(b.CellHash()));
            var sum = Fnv1aSum.Start();
            a.HashSlice(ref sum, 0, 1);
            Assert.That(a.CloseHash(sum), Is.EqualTo(a.CellHash()), "the running hash agrees");
        }
    }
}
