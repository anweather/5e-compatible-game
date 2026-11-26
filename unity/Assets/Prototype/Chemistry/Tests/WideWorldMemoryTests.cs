// The two 12 x 10 maps played from their rows (hills-12x10, world-12x10): the World built on them holds Cell objects only for the chunks made or changed, so its memory follows the active region and the drawn tier, never the map's size (#204, J-086); and a made chunk, a changed one and one never read give the cells their source makes, packed or not.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    [NonParallelizable]
    public class WideWorldMemoryTests
    {
        static Rules R => ChemistryTests.R;

        string root;

        [OneTimeSetUp]
        public void MakeRoom()
        {
            root = Path.Combine(Path.GetTempPath(), "wide-memory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [OneTimeTearDown]
        public void ClearRoom()
        {
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        static double Mb(long bytes) => bytes / (1024.0 * 1024.0);

#pragma warning disable S1215
        static long Heap() => GC.GetTotalMemory(true);
#pragma warning restore S1215

        static string MapDir(string id) => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", id);

        string RowsOf(string id)
        {
            string dir = Path.Combine(root, id);
            GeneratedMap.EnsureRows(MapDir(id), dir);
            return dir;
        }

        static World Started(GeneratedSource source, string rows)
        {
            var w = GeneratedSource.MapWorld(source, rows, R, 7, out var start);
            w.KeepStart();
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));
            w.Step();
            return w;
        }

        [TestCase("hills-12x10")]
        [TestCase("world-12x10")]
        public void ItsWorldHoldsCellsOnlyForTheChunksMadeOrChanged(string id)
        {
            string rows = RowsOf(id);
            var source = new GeneratedSource(GeneratedMap.Load(MapDir(id)), R);
            long before = Heap();
            var w = Started(source, rows);
            long after = Heap();
            long mapCells = (long)w.W * w.H;
            TestContext.Out.WriteLine($"WIDE {id}: {w.ChunkCoords.Count} chunks, {mapCells} cells in the map; {w.UnpackedCount} unpacked and {w.PackPoolCount} pooled, "
                + $"{w.HeldCellCount} Cell objects held; {source.HeldCount} chunks held made; the World and its source {Mb(after - before):0.0} MB; "
                + $"the map-sized arrays {Mb(w.MapArrayBytes):0.00} MB");
            Assert.That(w.HeldCellCount, Is.LessThanOrEqualTo((long)(w.UnpackedCount + World.PoolKeep) * Chunks.Area));
            Assert.That(w.HeldCellCount, Is.LessThan(mapCells / 20), "cells held for the chunks made or changed, not for the map");
            Assert.That(after - before, Is.LessThan(256L << 20), "the World on a 12 x 10 in MB, not the 3.4 GB of a Cell for every cell of the map");
            GC.KeepAlive(w);
        }

        [TestCase("hills-12x10")]
        [TestCase("world-12x10")]
        public void AMadeAChangedAndANeverReadChunkGiveTheirSourcesCells(string id)
        {
            string rows = RowsOf(id);
            var w = Started(new GeneratedSource(GeneratedMap.Load(MapDir(id)), R), rows);
            var fresh = new GeneratedSource(GeneratedMap.Load(MapDir(id)), R);
            int cy = w.H / Chunks.Size / 2;
            var made = (cx: 20, cy);
            var changed = (cx: 40, cy);
            var neverRead = (cx: 60, cy);
            Assert.That(new[] { made, changed, neverRead }.All(c => w.IsPacked(c.cx, c.cy) && !w.IsActive(c.cx, c.cy)), "far from the spawn: inactive and packed");
            string hash = w.CellHash();

            w.Unpack(made.cx, made.cy);
            Assert.That(CellsOf(w, made), Is.EqualTo(SourceCells(fresh, made)), "a chunk made from its source");
            w.Pack(made.cx, made.cy);
            Assert.That(w.PackedOf(made.cx, made.cy).Count, Is.Zero, "packed again it holds no changes");

            int x = changed.cx * Chunks.Size + 10, y = changed.cy * Chunks.Size + 10;
            w.Paint(x, y, "stone");
            w.AddWater(x + 1, y, 3);
            string[] changedCells = CellsOf(w, changed);
            Assert.That(changedCells, Is.Not.EqualTo(SourceCells(fresh, changed)), "the chunk changed");
            string changedHash = w.CellHash();
            Assert.That(changedHash, Is.Not.EqualTo(hash), "and the hash saw it");
            Assert.That(w.Pack(changed.cx, changed.cy).Count, Is.GreaterThan(0), "packed, it holds its changes");
            Assert.That(w.CellHash(), Is.EqualTo(changedHash), "the hash reads it packed");
            w.Unpack(changed.cx, changed.cy);
            Assert.That(CellsOf(w, changed), Is.EqualTo(changedCells), "unpacked again, the changed chunk is what it was");

            Assert.That(w.IsPacked(neverRead.cx, neverRead.cy), Is.True);
            Assert.That(CellsOf(w, neverRead), Is.EqualTo(SourceCells(fresh, neverRead)), "a chunk never read until now gives its source's cells");
            Assert.That(w.CellHash(), Is.EqualTo(changedHash), "reading chunks moved no shared state");
        }

        static string[] CellsOf(World w, (int cx, int cy) c)
        {
            var cells = new string[Chunks.Area];
            for (int i = 0; i < Chunks.Area; i++) cells[i] = PackTests.Fields(w.At(c.cx * Chunks.Size + (i & Chunks.Mask), c.cy * Chunks.Size + (i >> Chunks.Shift)));
            return cells;
        }

        static string[] SourceCells(GeneratedSource source, (int cx, int cy) c)
        {
            var cells = new string[Chunks.Area];
            for (int i = 0; i < Chunks.Area; i++)
            {
                var cell = new Cell();
                source.Make(c.cx * Chunks.Size + (i & Chunks.Mask), c.cy * Chunks.Size + (i >> Chunks.Shift), cell);
                cells[i] = PackTests.Fields(cell);
            }
            return cells;
        }
    }
}
