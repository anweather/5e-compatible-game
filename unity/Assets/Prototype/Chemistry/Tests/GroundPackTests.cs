// The ground pack as a file (docs/design/maps-as-data.md): what's saved comes back to the bit, and the drawn ground is
// held to the land.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class GroundPackTests
    {
        static GroundChunk Chunk(int i, int res, int texels, int detail, int layers, int grasses, Random rng)
        {
            var paint = new byte[texels * texels * layers];
            var details = new byte[detail * detail * grasses];
            rng.NextBytes(paint);
            rng.NextBytes(details);
            return new GroundChunk
            {
                I = i,
                J = 2,
                Resolution = res,
                Texels = texels,
                DetailResolution = detail,
                Heights = Enumerable.Range(0, res * res).Select(_ => (ushort)rng.Next(GroundPack.HeightSteps + 1)).ToArray(),
                Paint = paint,
                Details = details,
                Trees = new[] { new GroundTree { X = 0.25f, Y = 0.1f, Z = 0.75f, Width = 1.5f, Height = 2f, Rotation = 3f, Color = 0xff00ff00, LightmapColor = 7, Prototype = 1 } },
            };
        }

        [Test]
        public void WhatIsSavedComesBack()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ground-" + Guid.NewGuid().ToString("N"));
            try
            {
                var rng = new Random(1);
                var pack = new GroundPack
                {
                    Bottom = -22.5,
                    Top = 277.5,
                    Layers = new[] { "grass", "dirt" },
                    Grasses = new[] { "tall" },
                    TreeKinds = new[] { "pine", "oak" },
                    ChannelCells = new[] { 5, 9 },
                    ChannelDepths = new[] { 0.25f, 1.5f },
                };
                pack.Chunks.Add(Chunk(0, 9, 8, 4, 2, 1, rng));
                pack.Chunks.Add(Chunk(1, 5, 4, 4, 2, 1, rng));
                Assert.That(GroundPack.In(dir), Is.False);
                pack.Save(dir);
                Assert.That(GroundPack.In(dir));
                Assert.That(File.ReadAllText(Path.Combine(dir, GroundPack.FolderName, GroundPack.FileName)), Does.Not.Contain("\r"),
                    "ground.json is LF on every machine, or a Windows peer's map hash differs from a Mac's");

                var back = GroundPack.Load(dir);
                Assert.That((back.Bottom, back.Top), Is.EqualTo((-22.5, 277.5)));
                Assert.That(back.Layers, Is.EqualTo(pack.Layers));
                Assert.That(back.TreeKinds, Is.EqualTo(pack.TreeKinds));
                Assert.That(back.ChannelCells, Is.EqualTo(pack.ChannelCells));
                Assert.That(back.ChannelDepths, Is.EqualTo(pack.ChannelDepths));
                for (int k = 0; k < 2; k++)
                {
                    Assert.That(back.Chunks[k].Heights, Is.EqualTo(pack.Chunks[k].Heights));
                    Assert.That(back.Chunks[k].Paint, Is.EqualTo(pack.Chunks[k].Paint));
                    Assert.That(back.Chunks[k].Details, Is.EqualTo(pack.Chunks[k].Details));
                    Assert.That(back.Chunks[k].Trees, Is.EqualTo(pack.Chunks[k].Trees));
                }
                Assert.That(back.Metres(back.Chunks[0], 0, 0), Is.EqualTo(-22.5 + 300.0 * pack.Chunks[0].Heights[0] / GroundPack.HeightSteps));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TheValleyHasItsGround()
        {
            var pack = GroundPack.Load(ChemistryTests.WorldDir());
            var layout = TerrainGrid.Valley();
            Assert.That(pack.Chunks.Count, Is.EqualTo(layout.ChunksX * layout.ChunksZ));
            foreach (var c in pack.Chunks)
            {
                Assert.That(c.Resolution, Is.EqualTo(layout.Resolution(c.I, c.J)));
                Assert.That(c.Texels, Is.EqualTo(layout.TexelResolution(c.I, c.J)));
            }
            Assert.That(pack.Layers, Is.Not.Empty);
            Assert.That(pack.Chunks.Sum(c => c.Trees.Length), Is.GreaterThan(0));
            Assert.That(pack.ChannelCells, Is.Not.Empty);
        }

        [Test]
        public void AMomentUnderTheGroundIsNoFall()
        {
            var watch = new FallWatch();
            for (int k = 0; k < 9; k++) watch.Feed(3f, 0.1f);
            watch.Feed(-1f, 0.1f);
            for (int k = 0; k < 9; k++) watch.Feed(3f, 0.1f);
            Assert.That(watch.Fell, Is.False);
            Assert.That(watch.Deepest, Is.EqualTo(3f));
        }

        [Test]
        public void ASecondWellUnderIsAFall()
        {
            var watch = new FallWatch();
            for (int k = 0; k < 11; k++) watch.Feed(4400f, 0.1f);
            Assert.That(watch.Fell);
        }

        [Test]
        public void AFewCentimetresUnderIsNothing()
        {
            var watch = new FallWatch();
            for (int k = 0; k < 100; k++) watch.Feed(0.5f, 0.1f);
            Assert.That(watch.Fell, Is.False);
        }

        [Test]
        public void GroundThatAgreesWithTheLandSaysNothing()
        {
            var land = Enumerable.Range(0, 64 * 64).Select(i => i % 50).ToArray();
            Assert.That(LandCheck.Disagreements((x, y) => land[y * 64 + x] * 0.125f + 0.05f, land, 64, 64, 0.125, 0.125), Is.Empty);
        }

        [Test]
        public void GroundThatHasDroppedIsSaid()
        {
            var land = new int[64 * 64];
            Assert.That(LandCheck.Disagreements((x, y) => -38f, land, 64, 64, 0.125, 0.125), Has.Count.EqualTo(LandCheck.Cells));
        }
    }
}
