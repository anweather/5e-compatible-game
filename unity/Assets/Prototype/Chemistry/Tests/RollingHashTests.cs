using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The online desync check's rolling fingerprint (RollingHash), and the running hash under CellHash.</summary>
    public class RollingHashTests
    {
        const int Side = 96;

        static World NewWorld(uint seed = 7)
        {
            var world = new World(ChemistryTests.R, Side, Side, seed);
            for (int x = 10; x < 30; x++)
                for (int y = 10; y < 14; y++) world.At(x, y).Water = 3;
            return world;
        }

        // The windows closed by running the ticks, each cell put wrong where the map says, by tick.
        static List<string> Windows(World world, int ticks, Dictionary<int, System.Action<World>> at = null)
        {
            var rolling = new RollingHash();
            var closed = new List<string>();
            for (int tick = 1; tick <= ticks; tick++)
            {
                if (at != null && at.TryGetValue(tick, out var poke)) poke(world);
                world.Step();
                string hash = rolling.Add(world, tick);
                if (hash != null) closed.Add(hash);
            }
            return closed;
        }

        // A window's length is the world's size alone, so peers agree: the valley's 30 ticks, longer for a world of more than
        // two valleys' cells, so a tick still hashes about two valleys' worth (chunk-hash; the 3 x 3's 6 ms a tick under Mono).
        [Test]
        public void AWindowIsLongerOnlyForABigWorldAndBySizeAlone()
        {
            Assert.That(RollingHash.WindowFor(576 * 576), Is.EqualTo(RollingHash.Every), "the valley's stays 30 ticks");
            Assert.That(RollingHash.WindowFor(Side * Side), Is.EqualTo(RollingHash.Every), "a small world's too");
            Assert.That(RollingHash.WindowFor(2 * 576 * 576), Is.EqualTo(RollingHash.Every), "two valleys' fits a window");
            Assert.That(RollingHash.WindowFor(2 * 576 * 576 + 1), Is.EqualTo(RollingHash.Every + 1), "one cell more and it grows");
            Assert.That(RollingHash.WindowFor(1728 * 1728), Is.EqualTo(135), "a 3 x 3: 135 ticks, about 22 s");
            Assert.That(RollingHash.WindowFor(new World(ChemistryTests.R, 1152, 64, 1)), Is.EqualTo(RollingHash.WindowFor(1152 * 64)), "a world's is its size's");
        }

        // A big world's window covers every cell once, so on a world nobody changes it closes on the full hash, on the same
        // tick for two peers with the same size and edits.
        [Test]
        public void ABigWorldsWindowClosesOnTheFullHash()
        {
            const int big = 1152;
            int window = RollingHash.WindowFor(big * big);
            Assert.That(window, Is.EqualTo(60));
            World Made(uint seed)
            {
                var w = new World(ChemistryTests.R, big, big, seed, "grass");
                for (int k = 0; k < 400; k++)
                {
                    int x = (int)(WorldHash.Hash4(3, 1, k, 0) % big), y = (int)(WorldHash.Hash4(3, 2, k, 0) % big);
                    w.At(x, y).Water = 1 + k % 4;
                    w.At((x + 7) % big, y).Fuel = k % 9;
                }
                return w;
            }
            var a = Made(7);
            var b = Made(7);
            var ra = new RollingHash();
            var rb = new RollingHash();
            string closedA = null, closedB = null;
            for (int tick = 1; tick <= window; tick++)
            {
                string ha = ra.Add(a, tick), hb = rb.Add(b, tick);
                Assert.That(ha == null, Is.EqualTo(tick < window), $"only the window's last tick closes it ({tick})");
                closedA ??= ha;
                closedB ??= hb;
            }
            Assert.That(closedA, Is.EqualTo(a.CellHash()), "every cell once, as the full hash has them");
            Assert.That(closedB, Is.EqualTo(closedA), "the same on both peers");
        }

        [Test]
        public void TheRunningHashAgreesWithTheTextEvenWithACrackedThing()
        {
            var world = NewWorld();
            world.At(3, 4).Height = -2;
            world.At(5, 6).Life = 9;
            world.At(7, 8).Cover = "ash";
            world.At(9, 9).Gas = "smoke";
            world.At(9, 9).GasAmt = 2;
            world.ApplyEffect(new EffectCommand(EffectKind.Crack, 0, 0, "", new ThingId(0, 0, 3)));
            world.ApplyEffect(new EffectCommand(EffectKind.Crack, 0, 0, "", new ThingId(2, 1, 40)));
            var text = Fnv1aSum.Start();
            text.Add(world.CellText());
            Assert.That(world.CellHash(), Is.EqualTo(text.ToString()));
        }

        [Test]
        public void TwoPeersOnTheSameTicksCloseTheSameWindows()
        {
            var first = Windows(NewWorld(), RollingHash.Every * 3 + 5);
            Assert.That(first, Has.Count.EqualTo(3));
            Assert.That(Windows(NewWorld(), RollingHash.Every * 3 + 5), Is.EqualTo(first));
        }

        [Test]
        public void AWindowSeenOnlyInPartGivesNoFingerprint()
        {
            var world = NewWorld();
            var rolling = new RollingHash();
            for (int tick = RollingHash.Every - 4; tick <= RollingHash.Every; tick++) Assert.That(rolling.Add(world, tick), Is.Null);
            for (int tick = RollingHash.Every + 1; tick < RollingHash.Every * 2; tick++) Assert.That(rolling.Add(world, tick), Is.Null);
            Assert.That(rolling.Add(world, RollingHash.Every * 2), Is.Not.Null);
        }

        [TestCase(0, 0)]
        [TestCase(Side - 1, Side - 1)]
        [TestCase(70, 40)]
        [TestCase(0, Side / 2)]
        public void AWrongCellAnywhereIsCaughtWithinTwoWindows(int x, int y)
        {
            var fine = Windows(NewWorld(), RollingHash.Every * 3);
            void Wrong(World w) => w.At(x, y).Fuel += 7;
            var wrong = Windows(NewWorld(), RollingHash.Every * 3, new Dictionary<int, System.Action<World>> { [RollingHash.Every + 12] = Wrong });
            Assert.That(wrong[0], Is.EqualTo(fine[0]), "the window before it");
            Assert.That(wrong[1] != fine[1] || wrong[2] != fine[2], Is.True, "the window it fell in, or at worst the next");
            Assert.That(wrong[2], Is.Not.EqualTo(fine[2]), "a whole window after it, every slice has seen it");
        }

        [Test]
        public void EveryCellIsInExactlyOneSlice()
        {
            for (int cells = 1; cells < 2000; cells += 137)
            {
                int covered = 0;
                for (int slice = 0; slice < RollingHash.Every; slice++)
                    covered += (int)((long)cells * (slice + 1) / RollingHash.Every) - (int)((long)cells * slice / RollingHash.Every);
                Assert.That(covered, Is.EqualTo(cells));
            }
        }

        [Test]
        public void TheSliceComesFromTheTickAlone()
        {
            Assert.That(RollingHash.SliceOf(1), Is.EqualTo(0));
            Assert.That(RollingHash.SliceOf(RollingHash.Every), Is.EqualTo(RollingHash.Every - 1));
            Assert.That(RollingHash.SliceOf(RollingHash.Every + 1), Is.EqualTo(0));
        }
    }
}
