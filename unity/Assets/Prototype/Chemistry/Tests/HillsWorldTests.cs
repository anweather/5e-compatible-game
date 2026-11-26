// HillsTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in HillsTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class HillsWorldTests
    {
        string root;

        [OneTimeSetUp]
        public void MakeTheRoot()
        {
            root = Path.Combine(Path.GetTempPath(), "hills-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [OneTimeTearDown]
        public void DropThem()
        {
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        static Rules R => ChemistryTests.R;

        [Test]
        public void ItPlaysFromItsRowsAlone()
        {
            string dir = Path.Combine(root, "hills-12x10");
            Assert.That(MapBake.EnsureRows(HillsTests.HillsDir, dir), Is.True);
            Assert.That(GeneratedMap.IsRowsOnly(dir), Is.True);
            Assert.That(MapBake.EnsureRows(HillsTests.HillsDir, dir), Is.False, "written once for these inputs");
            Assert.That(MapCheck.Errors(dir), Is.Empty);
            var source = new GeneratedSource(GeneratedMap.Load(HillsTests.HillsDir), R);
            var w = GeneratedSource.MapWorld(source, dir, R, 7, out var start);
            Assert.That((w.W, w.H, w.WrapX), Is.EqualTo((6912, 5760, true)));
            Assert.That(w.ChunkCoords.Count, Is.EqualTo(108 * 90));
            Assert.That(start, Is.EqualTo((6880, 5601)));
            Assert.That(source.Made, Is.LessThan(20), "nothing is laid, so the build makes next to nothing");
            w.KeepStart();
            ChunkStreaming.Apply(w, ChunkStreaming.StartPlan(w, new[] { start }));

            // A walk east over the seam: the chunks ahead made on the worker, none read packed.
            var planner = new ChunkPlanner();
            var me = new (int x, int y)[] { start };
            ChunkStreaming.Apply(w, planner.Plan(w, me));
            w.Step();
            long onRead = source.MadeOnRead;
            for (int t = 0; t < 4 * Chunks.Size; t++)
            {
                me[0] = (w.FoldX(start.x + t), start.y);
                ChunkStreaming.Apply(w, planner.Plan(w, me));
                w.Step();
            }
            Assert.That(me[0].x, Is.LessThan(4 * Chunks.Size), "the walk crossed the east seam");
            Assert.That(w.IsActive(Chunks.ChunkOf(me[0].x), Chunks.ChunkOf(start.y)), Is.True);
            Assert.That(w.ReadUnpacks, Is.Zero, "the tick never read a packed chunk");
            TestContext.Out.WriteLine($"HILLS walk over the east seam: {source.Made} chunks made, {source.MadeOnRead - onRead} on a read, {source.MadeAhead} ahead");
            Assert.That(w.At(w.FoldX(-1), start.y).T, Is.EqualTo(w.At(w.W - 1, start.y).T));
        }
    }
}
