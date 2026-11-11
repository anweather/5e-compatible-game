using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// core-review-0 (docs/design/core-review-0.md): the lockstep cases the review found untested. Each runs the same
    /// inputs on two peers, one never packing and one packing and unpacking its inactive chunks at ticks of its own
    /// (as a peer's drawn hint and hold make it do), and asks for the same CellHash every tick: a thick liquid across a
    /// rule switch that renumbers the liquids, the simulation across the east-west seam, and the dragon's reset.
    /// </summary>
    [Category("Simulation")]
    public class CoreReviewTests
    {
        static Rules R => ChemistryTests.R;

        static Rules Streaming(Rules rules, string streaming) =>
            RuleSet.Plain(rules).WithMods(new[] { new ActiveMod("streaming", "{ \"patch\": { \"sim\": { \"streaming\": " + streaming + " } } }") }).Effective;

        static Rules Never(Rules rules) => Streaming(rules, "{ \"packsPerTick\": 0 }");

        static Rules Soon(Rules rules) => Streaming(rules, "{ \"packTicks\": 0, \"packsPerTick\": 3 }");

        // The packing peer packs and unpacks each inactive chunk at beats of its own, never at the other's.
        static int PackAtBeats(World w, int t)
        {
            int moved = 0;
            foreach (var (cx, cy) in w.ChunkCoords.Where(c => !w.IsActive(c.cx, c.cy)))
            {
                int beat = (t * 3 + cx * 7 + cy * 5) % 7;
                if (beat == 0 && !w.IsPacked(cx, cy)) w.Pack(cx, cy);
                else if (beat == 3 && w.IsPacked(cx, cy)) w.Unpack(cx, cy);
                else continue;
                moved++;
            }
            return moved;
        }

        // Both peers run the same step; the packing one packs between ticks; they hash the same every tick.
        static int RunBoth(World plain, World packing, int ticks, Action<World, int> step)
        {
            int moved = 0;
            for (int t = 0; t < ticks; t++)
            {
                moved += PackAtBeats(packing, t);
                Assert.That(packing.CellHash(), Is.EqualTo(plain.CellHash()), $"tick {t}");
                step(plain, t);
                step(packing, t);
            }
            Assert.That(packing.CellHash(), Is.EqualTo(plain.CellHash()), "at the end");
            Assert.That(packing.RngState, Is.EqualTo(plain.RngState));
            return moved;
        }

        const string Honey = @"""honey"": { ""name"": ""Honey"", ""thick"": 4, ""heap"": 6, ""soaks"": false, ""floats"": false }";
        const string Magma = @"""magma"": { ""name"": ""Magma"", ""thick"": 2, ""heap"": 3, ""soaks"": false, ""floats"": false, ""sets"": { ""into"": ""rock"", ""after"": 300 } }";

        static Rules Liquids(Rules over, params string[] rows) =>
            RuleSet.Plain(over).WithMods(new[] { new ActiveMod("liquids", "{ \"patch\": { \"liquids\": { " + string.Join(", ", rows) + " } } }") }).Effective;

        // Thick liquids sit out turns (World.StillWater's UnchangedFor), and a pack drops a chunk's water snapshot; a rule
        // switch that renumbers the liquids remaps an inactive chunk's cells by id only when it activates (World.Fit.cs).
        // Neither may show between a peer that packs and one that doesn't.
        [Test]
        public void AThickLiquidAndARenumberingHashTheSameWhateverEachPeerPacked()
        {
            Rules First(Rules over) => Liquids(over, Honey, Magma);
            Rules Swapped(Rules over) => Liquids(over, Magma, Honey);
            var plain = new World(First(Never(R)), 4 * Chunks.Size, 2 * Chunks.Size, 21, "grass");
            var packing = new World(First(Soon(R)), 4 * Chunks.Size, 2 * Chunks.Size, 21, "grass");
            Assert.That(plain.Rules.LiquidIndex("honey"), Is.EqualTo(1));
            foreach (var w in new[] { plain, packing })
            {
                for (int y = 30; y < 100; y += 9)
                {
                    w.AddLiquid(60 + y % 7, y, "honey", 12);
                    w.AddLiquid(130 + y % 5, y, "magma", 10);
                    w.AddLiquid(200, y, "magma", 8);
                }
            }
            int moved = RunBoth(plain, packing, 240, (w, t) =>
            {
                if (t == 30) w.SetActive(3, 0, false);
                if (t == 40) w.SetActive(2, 1, false);
                if (t == 90) w.ApplyRules(Swapped(w == plain ? Never(R) : Soon(R)));
                if (t == 150) w.SetActive(3, 0, true);
                if (t == 180) w.SetActive(2, 1, true);
                if (t == 120) w.AddLiquid(70, 70, "honey", 16);
                w.Step();
            });
            Assert.That(plain.Rules.LiquidIndex("honey"), Is.EqualTo(2), "the switch renumbered the liquids");
            Assert.That(moved, Is.GreaterThan(10), "the packing peer packed and unpacked");
        }

        // A world that wraps: fire, water and gas cross the seam (sim-across-the-seam) while the chunks beside it are let go
        // and taken back, packed on one peer and not on the other.
        [Test]
        public void TheSeamRunsTheSameWhateverEachPeerPacked()
        {
            const int w = 6 * Chunks.Size, h = 2 * Chunks.Size;
            World Make(Rules rules)
            {
                var world = new World(rules, w, h, 33, "grass", wrapX: true);
                for (int y = 0; y < h; y += 5)
                    for (int x = (y * 7) % 13; x < w; x += 13) world.Paint(x, y, "tree");
                return world;
            }
            var plain = Make(Never(R));
            var packing = Make(Soon(R));
            int moved = RunBoth(plain, packing, 260, (world, t) =>
            {
                if (t == 5) world.Cast("fire-bolt", 2, 40, 3);
                if (t == 8) world.Cast("fire-bolt", w - 3, 90, 1);
                if (t == 12) world.Cast("create-water", w - 2, 20, 1);
                if (t == 30) world.SetActive(4, 0, false);
                if (t == 50) world.SetActive(1, 1, false);
                if (t == 70) world.SetActive(5, 1, false);
                if (t == 140) world.SetActive(4, 0, true);
                if (t == 160) world.SetActive(5, 1, true);
                if (t == 200) world.SetActive(1, 1, true);
                world.Step();
            });
            Assert.That(moved, Is.GreaterThan(10), "the packing peer packed and unpacked");
        }

        // The dragon's reset puts every chunk back to its start (World.Dragon.cs): a packed chunk's start is its changes as
        // they stood, an unpacked one's is diffed from its block. Peers that packed differently must come out the same.
        [Test]
        public void TheDragonsResetIsTheSameWhateverEachPeerPacked()
        {
            var plain = ChunkTests.Valley();
            var packing = ChunkTests.Valley();
            plain.ApplyRules(Never(plain.Rules));
            packing.ApplyRules(Soon(packing.Rules));
            plain.SetActive(3, 0, false);
            packing.SetActive(3, 0, false);
            plain.KeepStart();
            packing.KeepStart();
            bool flew = false;
            int moved = RunBoth(plain, packing, 700, (w, t) =>
            {
                ChunkTests.ValleyScriptTick(w, t);
                if (t == 40) w.ApplyDragon(new DragonCommand());
                if (t == 300) w.SetActive(3, 0, true);
                if (t == 320) w.SetActive(2, 2, false);
                flew |= w.Dragon != null;
            });
            Assert.That(flew, Is.True, "the dragon came");
            Assert.That(moved, Is.GreaterThan(10), "the packing peer packed and unpacked");
        }
    }
}
