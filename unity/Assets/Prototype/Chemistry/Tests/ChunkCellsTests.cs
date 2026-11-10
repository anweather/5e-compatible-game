using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The sparse World, step 3 (ChunkCells.cs; docs/design/sparse-world.md): the core's per-cell state beside the cells is
    /// held a chunk at a time. The land reads the same from a packed chunk's bytes as from an unpacked chunk's block, and a
    /// stamp or a road's cut moves a packed chunk's land without unpacking it; the starting state holds only its changes;
    /// and a big world holds nothing the size of its map.
    /// </summary>
    [Category("Simulation")]
    public class ChunkCellsTests
    {
        static Rules R => ChemistryTests.R;

        // Rolling land with a few wild values (the codec's widest steps) and a lake marked NoLand by its water.
        static int Units(int x, int y)
        {
            if ((x * 31 + y * 17) % 997 == 0) return (x % 2 == 0 ? 1 : -1) * (int.MaxValue - x);
            return (int)(40 * Math.Sin(x / 23.0) + 30 * Math.Cos(y / 17.0)) + (x * y) % 3;
        }

        static World Hills(int w = 200, int h = 175)
        {
            var world = new World(R, w, h, 3, "grass");
            for (int y = 20; y < 90; y++)
                for (int x = 30; x < 140; x++)
                    if ((x - 85) * (x - 85) + 3 * (y - 55) * (y - 55) < 2500) world.AddWater(x, y, 8);
            world.SetLand((y, row) =>
            {
                for (int x = 0; x < row.Length; x++) row[x] = Units(x, y);
            });
            return world;
        }

        static int[] Land(World w)
        {
            var land = new int[w.W * w.H];
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++) land[w.Idx(x, y)] = w.LandUnitsAt(x, y);
            return land;
        }

        [Test]
        public void TheLandReadsTheSamePackedOrNot()
        {
            var w = Hills();
            var land = Land(w);
            Assert.That(land.Count(v => v == World.NoLand), Is.GreaterThan(1000), "the lake is NoLand");
            Assert.That(land[w.Idx(5, 150)], Is.EqualTo(Units(5, 150)));
            Assert.That(land.Any(v => v > 1 << 30), "a wild value");
            // As SetLand(int[]) sets it.
            var whole = new World(R, w.W, w.H, 3, "grass");
            for (int y = 20; y < 90; y++)
                for (int x = 30; x < 140; x++)
                    if ((x - 85) * (x - 85) + 3 * (y - 55) * (y - 55) < 2500) whole.AddWater(x, y, 8);
            whole.SetLand(Enumerable.Range(0, w.W * w.H).Select(i => Units(i % w.W, i / w.W)).ToArray());
            Assert.That(Land(whole), Is.EqualTo(land), "by rows or whole");

            w.SetAllActive(false);
            foreach (var (cx, cy) in w.ChunkCoords) w.Pack(cx, cy);
            Assert.That(Land(w), Is.EqualTo(land), "packed");
            Assert.That(w.UnpackedCount, Is.Zero, "reading the land unpacks nothing");
            foreach (var (cx, cy) in w.ChunkCoords) w.Unpack(cx, cy);
            Assert.That(Land(w), Is.EqualTo(land), "unpacked again");

            w.SetLand((int[])null);
            Assert.That(Land(w).All(v => v == World.NoLand), "flat again");
        }

        [Test]
        public void ACutMovesAPackedChunksLandWithoutUnpackingIt()
        {
            var plain = Hills();
            var packed = Hills();
            plain.SetAllActive(false);
            packed.SetAllActive(false);
            foreach (var (cx, cy) in packed.ChunkCoords) packed.Pack(cx, cy);
            // A road's cut across every chunk, the lake's edge too.
            var cut = Enumerable.Range(0, 200).SelectMany(x => new[] { (x, 60 + x / 4, -3), (x, 61 + x / 4, -2) }).ToList();
            plain.LayLand(cut);
            packed.LayLand(cut);
            Assert.That(packed.UnpackedCount, Is.Zero, "the cut unpacked nothing");
            Assert.That(Land(packed), Is.EqualTo(Land(plain)));
            Assert.That(packed.LandLaidCells(), Is.EqualTo(plain.LandLaidCells()), "row by row");
            Assert.That(packed.LandMovedAt(10, 62), Is.EqualTo(-3));
            foreach (var (cx, cy) in packed.ChunkCoords) packed.Unpack(cx, cy);
            Assert.That(Land(packed), Is.EqualTo(Land(plain)), "unpacked, the land is the cut's");
            Assert.That(packed.Save(packed.Source), Is.EqualTo(plain.Save(plain.Source)), "and the save");
        }

        [Test]
        public void AWorldWithLandRunsTheSamePackingOrNot()
        {
            var never = R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"packsPerTick\": 0 } } }") });
            var soon = R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"streaming\": { \"packTicks\": 0, \"packsPerTick\": 4 } } }") });
            var plain = Hills(512, 175);
            var packing = Hills(512, 175);
            plain.ApplyRules(never);
            packing.ApplyRules(soon);
            for (int t = 0; t < 200; t++)
            {
                if (t == 10)
                    foreach (var w in new[] { plain, packing })
                    {
                        w.Cast("create-water", 20, 140, 0);
                        w.Cast("flood", 150, 150, 0);
                        w.Cast("fire-bolt", 180, 20, 0);
                        w.Cast("flood", 420, 100, 0);
                    }
                // The east columns let go (the far two pack, not beside an active chunk), then one taken back.
                if (t == 40)
                    foreach (var w in new[] { plain, packing })
                        for (int cx = 5; cx < 8; cx++)
                            for (int cy = 0; cy < 3; cy++) w.SetActive(cx, cy, false);
                if (t == 120)
                    foreach (var w in new[] { plain, packing })
                        for (int cy = 0; cy < 3; cy++) w.SetActive(5, cy, true);
                plain.Step();
                packing.Step();
                Assert.That(packing.CellHash(), Is.EqualTo(plain.CellHash()), $"tick {t}");
            }
            Assert.That(packing.Packs, Is.GreaterThan(0), "it packed");
            Assert.That(Land(packing), Is.EqualTo(Land(plain)));
        }

        [Test]
        public void TheStartingStateHoldsOnlyItsChanges()
        {
            var w = new World(R, 64 * 30, 64 * 30, 5, "grass");
            for (int x = 100; x < 160; x++) w.Paint(x, 300, "stone");
            w.KeepStart();
            Assert.That(w.StartShapes, Is.EqualTo(60), "the wall's cells, the rest as the source makes them");
            Assert.That(w.UnpackedCount, Is.EqualTo(2), "keeping it unpacked nothing past the two the wall did");
            w.SetAllActive(false);
            w.SetActive(1, 4, true);
            w.SetActive(2, 4, true);
            for (int x = 100; x < 160; x++) w.Paint(x, 300, "grass");
            for (int t = 0; t < 4; t++) w.Step();
            Assert.That(w.ChangedCount, Is.EqualTo(60), "the audit counts the wall gone against the start");
        }

        [Test]
        public void ABigWorldHoldsNothingTheSizeOfItsMap()
        {
            var w = new World(R, 64 * 40, 64 * 40, 5, "grass");
            w.SetLand((y, row) =>
            {
                for (int x = 0; x < row.Length; x++) row[x] = (int)(60 * Math.Sin(x / 50.0) * Math.Cos(y / 70.0));
            });
            w.LayLand(Enumerable.Range(0, 300).Select(x => (x + 400, 900, -2)));
            w.KeepStart();
            w.SetAllActive(false);
            w.SetActive(10, 10, true);
            for (int t = 0; t < 30; t++) w.Step();
            long cells = (long)w.W * w.H;
            TestContext.Out.WriteLine($"CHUNKCELLS 1600 chunks: {w.MapArrayBytes / 1024} KB beside the blocks, {w.UnpackedCount} unpacked; an int a cell would be {cells * 4 / 1024} KB");
            Assert.That(w.MapArrayBytes, Is.LessThan(cells), "under a byte a cell for rolling land, a cut and the start");
        }
    }
}
