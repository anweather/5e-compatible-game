using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The packed chunk's prototype (World.Pack.cs; docs/design/sparse-world.md, "Measured"): a chunk packed and unpacked
    /// again is what it was, the hash and the save read a packed chunk as they read it unpacked, and a peer that packs
    /// other chunks at other ticks hashes the same as one that never packs.
    /// </summary>
    [Category("Simulation")]
    public class PackTests
    {
        /// <summary>A world's cells as they stood when it was made: the source a packed chunk's changes are against.</summary>
        internal sealed class KeptSource : IChunkSource
        {
            readonly int w;
            readonly Cell[] kept;

            public KeptSource(World world)
            {
                w = world.W;
                kept = world.AllCells().Select(c =>
                {
                    var k = new Cell();
                    Copy(c, k);
                    return k;
                }).ToArray();
            }

            public string Version => "kept-1";

            public void Make(int x, int y, Cell cell) => Copy(kept[y * w + x], cell);
        }

        /// <summary>Every field of a cell into another.</summary>
        internal static void Copy(Cell from, Cell to)
        {
            to.T = from.T;
            to.Water = from.Water;
            to.Liquid = from.Liquid;
            to.Frozen = from.Frozen;
            to.Fire = from.Fire;
            to.Height = from.Height;
            to.Cover = from.Cover;
            to.Gas = from.Gas;
            to.Sets = from.Sets;
            to.Fuel = from.Fuel;
            to.GasAmt = from.GasAmt;
            to.Charge = from.Charge;
            to.Life = from.Life;
            to.DampUntil = from.DampUntil;
            to.Rust = from.Rust;
            to.Heat = from.Heat;
            to.Lit = from.Lit;
            to.Slick = from.Slick;
            to.Star = from.Star;
            to.Cracked = from.Cracked;
        }

        /// <summary>Every field of a cell, the ones the hash leaves out (a dry cell's Liquid, DampUntil) too.</summary>
        static string Fields(Cell c) =>
            $"{c.T},{c.Water},{c.Liquid},{c.Frozen},{c.Fire},{c.Height},{c.Cover},{c.Gas},{c.Sets},{c.Fuel},{c.GasAmt},{c.Charge},{c.Life},"
            + $"{c.DampUntil},{c.Rust},{c.Heat},{c.Lit},{c.Slick},{c.Star},{c.Cracked}";

        static string AllFields(World w) => string.Join(";", w.AllCells().Select(Fields));

        // ChunkTests' valley (4 x 3 chunks, fires crossing chunk edges) a while into its script: fire, ash, ice, moved earth.
        static (World w, KeptSource source) Worked(int ticks = 150)
        {
            var w = ChunkTests.Valley();
            var source = new KeptSource(w);
            ChunkTests.ValleyScript(w, ticks);
            return (w, source);
        }

        [Test]
        public void AChunkPackedAndUnpackedIsWhatItWas()
        {
            var (w, source) = Worked();
            w.SetAllActive(false);
            string fields = AllFields(w), hash = w.CellHash();
            var saved = w.ChunkCoords.Select(c => w.SaveChunkOf(c.cx, c.cy, source).ToString()).ToList();
            var strings = new PackStrings();
            var packed = w.ChunkCoords.Select(c => w.Pack(c.cx, c.cy, source, strings)).ToList();
            Assert.That(packed.Sum(p => p.Count), Is.GreaterThan(1000), "the script changed cells");
            Assert.That(w.ChunkCoords.All(c => w.IsPacked(c.cx, c.cy)));
            Assert.That(w.PackPoolCount, Is.EqualTo(packed.Count));
            foreach (var p in packed) w.Unpack(p.X, p.Y);
            Assert.That(w.PackPoolCount, Is.Zero, "unpacking took the blocks back");
            Assert.That(w.ReadUnpacks, Is.Zero);
            Assert.That(AllFields(w), Is.EqualTo(fields), "every field of every cell");
            Assert.That(w.CellHash(), Is.EqualTo(hash));
            Assert.That(w.ChunkCoords.Select(c => w.SaveChunkOf(c.cx, c.cy, source).ToString()), Is.EqualTo(saved), "the save's records, fire generations too");
        }

        [Test]
        public void TheHashAndTheSaveReadAPackedChunkAsItWas()
        {
            var (w, source) = Worked();
            w.SetAllActive(false);
            string hash = w.CellHash();
            var chunkHashes = w.ChunkCoords.ToDictionary(c => c, c => w.ChunkHash(c.cx, c.cy));
            var saved = w.ChunkCoords.ToDictionary(c => c, c => w.SaveChunkOf(c.cx, c.cy, source).ToString());
            var strings = new PackStrings();
            var packed = new Dictionary<long, PackedChunk>();
            // Every other chunk, so the walk crosses from packed to unpacked along every row.
            foreach (var (cx, cy) in w.ChunkCoords.Where(c => (c.cx + c.cy) % 2 == 0)) packed[Chunks.Key(cx, cy)] = w.Pack(cx, cy, source, strings);
            Assert.That(w.PackedCellHash(), Is.EqualTo(hash));
            Assert.That(w.ReadUnpacks, Is.Zero, "the hash read the packed chunks without unpacking them");
            foreach (var p in packed.Values)
            {
                Assert.That(w.PackedChunkHash(p, source, strings), Is.EqualTo(chunkHashes[(p.X, p.Y)]), $"chunk ({p.X}, {p.Y})");
                Assert.That(w.SavePacked(p, source, strings).ToString(), Is.EqualTo(saved[(p.X, p.Y)]), $"chunk ({p.X}, {p.Y})'s save");
            }
        }

        [Test]
        public void AReadUnpacksAPackedChunk()
        {
            var (w, source) = Worked(10);
            w.SetAllActive(false);
            string fields = Fields(w.At(70, 70));
            w.Pack(1, 1, source, new PackStrings());
            Assert.That(Fields(w.At(70, 70)), Is.EqualTo(fields));
            Assert.That((w.IsPacked(1, 1), w.ReadUnpacks), Is.EqualTo((false, 1)));
        }

        [Test]
        public void OnlyAnInactiveChunkPacks()
        {
            var (w, source) = Worked(10);
            Assert.That(() => w.Pack(0, 0, source, new PackStrings()), Throws.InvalidOperationException);
        }

        // ---------- a peer packing differently (sparse-world.md, step 2's test, against the prototype) ----------

        // The chunks the script lets go and takes back, by tick: a column at a time, while the fires are still burning.
        static readonly (int tick, int cx, bool active)[] Switches =
        {
            (25, 3, false), (40, 0, false), (90, 3, true), (120, 1, false), (150, 0, true), (200, 1, true), (210, 2, false), (260, 2, true),
        };

        /// <summary>
        /// One peer never packs; the other packs each inactive chunk at its own ticks (one pattern for every seed of
        /// <paramref name="pattern"/>) and unpacks it before it activates or at other ticks. The two must hash the same at
        /// every tick, the packed peer through PackedCellHash, and run on the same.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void APeerPackingDifferentlyHashesTheSame(int pattern)
        {
            var plain = ChunkTests.Valley();
            var packing = ChunkTests.Valley();
            var source = new KeptSource(packing);
            var strings = new PackStrings();
            int packs = 0, unpacks = 0;
            for (int t = 0; t < 300; t++)
            {
                foreach (var s in Switches.Where(s => s.tick == t))
                    for (int cy = 0; cy < 3; cy++)
                    {
                        plain.SetActive(s.cx, cy, s.active);
                        // Activating unpacks first (the design's "what unpacks a packed chunk").
                        if (s.active && packing.IsPacked(s.cx, cy))
                        {
                            packing.Unpack(s.cx, cy);
                            unpacks++;
                        }
                        packing.SetActive(s.cx, cy, s.active);
                    }
                // Pack and unpack the inactive chunks at ticks of the pattern's own, never at the plain peer's.
                foreach (var (cx, cy) in packing.ChunkCoords.Where(c => !packing.IsActive(c.cx, c.cy)))
                {
                    int beat = (t * pattern + cx * 7 + cy * 3) % (5 + pattern);
                    if (beat == 0 && !packing.IsPacked(cx, cy))
                    {
                        packing.Pack(cx, cy, source, strings);
                        packs++;
                    }
                    else if (beat == 2 && packing.IsPacked(cx, cy))
                    {
                        packing.Unpack(cx, cy);
                        unpacks++;
                    }
                }
                Assert.That(packing.PackedCellHash(), Is.EqualTo(plain.CellHash()), $"tick {t}");
                ChunkTests.ValleyScriptTick(plain, t);
                ChunkTests.ValleyScriptTick(packing, t);
            }
            TestContext.Out.WriteLine($"PACK pattern {pattern}: {packs} packs, {unpacks} unpacks between ticks, {packing.ReadUnpacks} by a read");
            foreach (var (cx, cy) in packing.ChunkCoords) packing.Unpack(cx, cy);
            Assert.That(packs, Is.GreaterThan(20), "it packed");
            Assert.That(unpacks, Is.GreaterThan(10), "and unpacked");
            Assert.That(packing.CellHash(), Is.EqualTo(plain.CellHash()));
            Assert.That(packing.RngState, Is.EqualTo(plain.RngState));
            Assert.That(AllFields(packing), Is.EqualTo(AllFields(plain)));
        }
    }
}
