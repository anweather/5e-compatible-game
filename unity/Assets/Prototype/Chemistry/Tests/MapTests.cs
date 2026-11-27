// A map's folder and MapCheck (docs/design/maps-as-data.md): every shipped map passes, and each way a folder can quietly
// be the wrong world is said by name.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class MapTests
    {
        const uint ValleyCells = 307482015u;

        string map;

        [SetUp]
        public void CopyTheValley()
        {
            map = Path.Combine(Path.GetTempPath(), "map-tests-" + Guid.NewGuid().ToString("N"), "valley");
            Directory.CreateDirectory(map);
            foreach (string file in Directory.GetFiles(LandFolders.WorldDir())) File.Copy(file, Path.Combine(map, Path.GetFileName(file)));
        }

        [TearDown]
        public void RemoveTheCopy() => Directory.Delete(Path.GetDirectoryName(map), true);

        [Test]
        public void TheValleysCellsAreTheShapeTheyAlwaysWere()
        {
            var rows = MapFile.ReadCells(LandFolders.WorldDir()).Split('\n');
            Assert.That(rows.Length, Is.EqualTo(576));
            Assert.That(rows.All(r => r.Length == 576));
            uint hash = 2166136261;
            foreach (char c in string.Join("\n", rows)) hash = (hash ^ c) * 16777619;
            Assert.That(hash, Is.EqualTo(ValleyCells), "the cells the scene held, byte for byte");
        }

        [Test]
        public void TheValleysLayoutIsTheOneItAlwaysHad()
        {
            var file = MapFile.Load(LandFolders.WorldDir());
            TerrainGrid a = file.Layout(), b = TerrainGrid.Valley();
            Assert.That((a.CellSize, a.OriginX, a.OriginZ, a.ChunksX, a.ChunksZ, a.U0, a.V0, a.GridW, a.GridH),
                Is.EqualTo((b.CellSize, b.OriginX, b.OriginZ, b.ChunksX, b.ChunksZ, b.U0, b.V0, b.GridW, b.GridH)));
            for (int j = 0; j < b.ChunksZ; j++)
                for (int i = 0; i < b.ChunksX; i++) Assert.That(a.IsHighRes(i, j), Is.EqualTo(b.IsHighRes(i, j)), $"chunk {i},{j}");
            Assert.That(file.Origin, Is.EqualTo((279, 330)));
            Assert.That(file.Spawn, Is.EqualTo((292, 328)), "the yard's @");
        }

        [Test]
        public void AGeneratedMapTakesItsPinnedMapsFullDetailWhereItIs()
        {
            string nine = Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "nine");
            var pins = GeneratedMap.PinnedSquares(nine);
            Assert.That(pins.Count, Is.EqualTo(1));
            Assert.That((Path.GetFileName(pins[0].dir), pins[0].x, pins[0].y), Is.EqualTo(("valley", 0, 1152)));
            var valley = MapFile.Load(pins[0].dir);
            var file = MapFile.Load(nine);
            Assert.That(MapFile.ChunkShift(file.H, valley.H, pins[0].x, pins[0].y), Is.EqualTo((0, 0)), "the valley's chunk (I, J) is the nine's (I, J)");
            Assert.That(file.FullDetail, Is.EqualTo(valley.FullDetail));
            Assert.That(MapFile.ChunkShift(1728, 576, 576, 0), Is.EqualTo((9, 18)), "a valley at square [1, 0], the north edge");
            Assert.That(GeneratedMap.PinnedSquares(LandFolders.WorldDir()), Is.Empty, "the valley pins nothing");
        }
    }
}
