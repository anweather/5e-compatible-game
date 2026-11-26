// The per-chunk cell hash (World.ChunkHash.cs, #168): goldens, a change stales only its chunk, the digest is the sum of its chunks, a quiet tick hashes nothing, and a peer's desync names the chunk.
using System;
using System.Collections.Generic;
using System.Linq;
using Chem.Lockstep;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class ChunkHashTests
    {
        static Rules R => ChemistryTests.R;

        static World Field()
        {
            var w = new World(R, 128, 128, 5, "grass");
            for (int x = 10; x < 20; x++) w.Paint(x, 12, "stone");
            for (int x = 80; x < 86; x++)
                for (int y = 90; y < 94; y++)
                {
                    w.Paint(x, y, "pit");
                    w.AddWater(x, y, 4);
                }
            w.Paint(100, 30, "tree");
            return w;
        }

        static List<ChunkHashEntry> Entries(World w)
        {
            w.RefreshChunkHashes();
            return w.ChunkCoords.Select(c => w.CachedChunkHash(c.cx, c.cy)).ToList();
        }

        static string Hashes(World w) => string.Join(" ", w.ChunkCoords.Select(c => w.CachedChunkHash(c.cx, c.cy).ToString()));

        static ulong DigestFromScratch(World w)
        {
            ulong sum = 0;
            foreach (var (cx, cy) in w.ChunkCoords)
            {
                var made = w.MakeChunkHashOf(cx, cy);
                if (made.Held) sum = unchecked(sum + World.DigestPart(cx, cy, made.Hash));
            }
            return sum;
        }

        [Test]
        public void TheChunkHashesOfAFieldArePinned()
        {
            var w = Field();
            w.RefreshChunkHashes();
            Assert.That(Hashes(w), Is.EqualTo("(0, 0) 8ffd0dc5 (1, 0) 46fc855d (0, 1) cdf4bdc5 (1, 1) e9f1b695"));
            Assert.That(w.ChunkDigest.ToString("x16", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("453325640a2b8a93"));
            Assert.That(w.ChunkDigestHash(), Is.EqualTo("224118ee"));
        }

        [Test]
        public void APlainWorldsChunkHashIsItsChunkHashFingerprint()
        {
            var w = Field();
            w.RefreshChunkHashes();
            foreach (var (cx, cy) in w.ChunkCoords)
                Assert.That(w.CachedChunkHash(cx, cy).Hash.ToString("x", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(w.ChunkHash(cx, cy)), $"chunk ({cx}, {cy})");
        }

        [Test]
        public void OneCellChangedStalesOnlyItsChunk()
        {
            var w = Field();
            w.RefreshChunkHashes();
            string before = Hashes(w);
            w.At(70, 10).Water = 3;
            Assert.That(w.StaleChunkHashes, Is.EqualTo(1));
            var changed = w.RefreshChunkHashes();
            Assert.That(changed.Select(c => (c.X, c.Y)), Is.EqualTo(new[] { (1, 0) }));
            Assert.That(changed[0].Hash, Is.Not.EqualTo(Field().MakeChunkHashOf(1, 0).Hash));
            var after = Entries(w);
            var was = Entries(Field());
            Assert.That(Enumerable.Range(0, was.Count).Where(i => was[i] != after[i]).ToList(), Is.EqualTo(new[] { 1 }), "only chunk (1, 0) of the four");
            Assert.That(Hashes(w), Is.Not.EqualTo(before));
        }

        [Test]
        public void ACellSetBackToWhatItWasChangesNoHash()
        {
            var w = Field();
            w.RefreshChunkHashes();
            w.At(70, 10).Water = 3;
            w.At(70, 10).Water = 0;
            Assert.That(w.RefreshChunkHashes(), Is.Empty, "made again, the same");
            w.At(5, 5).T = "grass";
            Assert.That(w.StaleChunkHashes, Is.Zero, "a write of the value it holds stales nothing");
        }

        [Test]
        public void TheDigestIsTheSumOfEveryChunksHashMadeFromScratch()
        {
            var w = Field();
            w.At(82, 12).Fire = 3;
            w.At(30, 100).Fire = 3;
            long refreshed = 0;
            for (int t = 0; t < 40; t++)
            {
                w.Step();
                long made = w.ChunkHashesMade;
                w.RefreshChunkHashes();
                refreshed += w.ChunkHashesMade - made;
                Assert.That(w.ChunkDigest, Is.EqualTo(DigestFromScratch(w)), $"tick {w.Tick}");
            }
            Assert.That(refreshed, Is.GreaterThan(40), "the fire changed chunks");
            Assert.That(refreshed, Is.LessThanOrEqualTo(40 * 4L));
        }

        [Test]
        public void TheDigestFollowsTheWholeCellHash()
        {
            World a = Field(), b = Field();
            a.RefreshChunkHashes();
            b.RefreshChunkHashes();
            Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()));
            Assert.That(a.ChunkDigestHash(), Is.EqualTo(b.ChunkDigestHash()));
            b.At(120, 120).Fire = 1;
            b.RefreshChunkHashes();
            Assert.That(a.CellHash(), Is.Not.EqualTo(b.CellHash()));
            Assert.That(a.ChunkDigestHash(), Is.Not.EqualTo(b.ChunkDigestHash()));
            b.At(120, 120).Fire = 0;
            b.RefreshChunkHashes();
            Assert.That(a.ChunkDigestHash(), Is.EqualTo(b.ChunkDigestHash()));
        }

        [Test]
        public void AQuietTickHashesNothing()
        {
            var w = new World(R, 128, 128, 5, "grass");
            for (int x = 10; x < 20; x++) w.Paint(x, 12, "stone");
            w.RefreshChunkHashes();
            w.Step();
            w.RefreshChunkHashes();
            long made = w.ChunkHashesMade;
            for (int t = 0; t < 30; t++)
            {
                w.Step();
                Assert.That(w.StaleChunkHashes, Is.Zero, $"tick {w.Tick}");
                Assert.That(w.RefreshChunkHashes(), Is.Empty);
            }
            Assert.That(w.ChunkHashesMade, Is.EqualTo(made));
        }

        [Test]
        public void UnpackingAndPackingAChunkStalesNothing()
        {
            var w = new World(R, 256, 256, 5, "grass");
            w.RefreshChunkHashes();
            long made = w.ChunkHashesMade;
            Assert.That(w.At(200, 200).T, Is.EqualTo("grass"));
            w.Paint(10, 10, "stone");
            Assert.That(w.StaleChunkHashes, Is.EqualTo(1), "the paint's chunk, not the one unpacked to be read");
            w.RefreshChunkHashes();
            Assert.That(w.ChunkHashesMade, Is.EqualTo(made + 1));
        }

        sealed class Plain : IGeneratedSource
        {
            public string Version => "plain-test";
            public int W => 192;
            public int H => 192;
            public readonly HashSet<(int, int)> Made = new HashSet<(int, int)>();

            public void Make(int x, int y, Cell cell)
            {
                Made.Add((Chunks.ChunkOf(x), Chunks.ChunkOf(y)));
                cell.T = (x + y) % 7 == 0 ? "stone" : "grass";
            }

            public int LandAt(int x, int y) => 0;
            public void Ahead(IEnumerable<(int cx, int cy)> chunks) { }
            public void MakeNow(IEnumerable<(int cx, int cy)> chunks) { }
            public void LandInto(int cx, int cy, int[] into) => Array.Clear(into, 0, into.Length);
            public void Keep(int cx, int cy, bool kept) { }
        }

        static World Generated()
        {
            var w = new World(R, 192, 192, 5, "grass");
            w.MakeFrom(new Plain());
            return w;
        }

        [Test]
        public void AGeneratedChunkReadButUnchangedCountsForNothing()
        {
            World reader = Generated(), other = Generated();
            Assert.That(reader.At(150, 151).T, Is.EqualTo("stone"));
            reader.RefreshChunkHashes();
            other.RefreshChunkHashes();
            Assert.That(reader.CachedChunkHash(2, 2).Held, Is.False);
            Assert.That(reader.ChunkDigest, Is.EqualTo(other.ChunkDigest));
            Assert.That(reader.ChunkDigest, Is.Zero, "no chunk differs from the source");
            reader.Paint(150, 151, "pit");
            Assert.That(reader.RefreshChunkHashes().Select(c => (c.X, c.Y, c.Held)), Is.EqualTo(new[] { (2, 2, true) }));
            Assert.That(reader.ChunkDigest, Is.EqualTo(DigestFromScratch(reader)));
            Assert.That(reader.ChunkDigest, Is.Not.EqualTo(other.ChunkDigest));
        }

        static LockstepSession Peer(LoopbackPeer link, List<LockstepDesync> desyncs)
        {
            var s = new LockstepSession(link, new LockstepWorld(R, Field), link.Id);
            s.Desynced += desyncs.Add;
            link.Session = s;
            return s;
        }

        [Test]
        public void APeerWithOneWrongCellNamesItsChunk()
        {
            var loop = new LockstepLoopback();
            var hostDesyncs = new List<LockstepDesync>();
            var desyncs = new List<LockstepDesync>();
            var host = Peer(loop.AddHost(), hostDesyncs);
            var link = loop.AddClient("client");
            var client = Peer(link, desyncs);
            client.RecoverDesyncs = false;
            loop.Connect(link);
            double dt = 1.0 / Math.Max(1, R.Sim.TicksPerSecond);
            int paintedAt = -1;
            int frame = 0;
            while (host.NetTick < 200)
            {
                Assert.That(frame++, Is.LessThan(1000));
                if (paintedAt < 0 && client.NetTick >= 100)
                {
                    paintedAt = client.NetTick;
                    client.World.World.Paint(70, 100, "stone");
                }
                host.Update(dt);
                client.Update(0.5);
                loop.Pump();
            }
            Assert.That(hostDesyncs, Is.Empty);
            Assert.That(desyncs.Count, Is.EqualTo(1));
            Assert.That(paintedAt, Is.LessThan(120));
            Assert.That(desyncs[0].Tick, Is.EqualTo(120), "the first window after the paint");
            Assert.That(desyncs[0].Parts, Has.Member("cells"));
            Assert.That(desyncs[0].Chunks, Is.EqualTo(new[] { (1, 1) }));
            StringAssert.Contains("in chunk (1, 1)", desyncs[0].Text);
        }
    }
}
