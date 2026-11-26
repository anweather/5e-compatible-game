// The far ring's core half (docs/design/world-squares.md sections 3 and 4): the known chunks round a character (its
// dominant biome and land on a 9 × 9 grid, the same land a made chunk gets), stopping one chunk past the coast, handed
// over at a tick boundary in chunk order; and the plan's far landmarks (world/horizon.json), each listed from a
// character exactly when within its seenFrom radius. Both the same from the same seed on two loads.
// FAR_RING_TIMES=1 dotnet test core --filter "FullyQualifiedName~FarRingTests" prints the load cost.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class FarRingTests
    {
        internal static string NineDir => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", "nine");

        static GeneratedMap nine;

        internal static GeneratedMap Nine => nine ??= GeneratedMap.Load(NineDir);

        // The whole map is dropped once the fixture is done, so a full EditMode run doesn't keep it (#211).
        [OneTimeTearDown]
        public void DropTheMap()
        {
            nine = null;
        }

        // The valley's middle chunk (the valley is chunks 0..8, 18..26 of the nine).
        internal static readonly (int x, int y) Valley = (4, 22);

        [Test]
        public void AKnownChunkIsTheMadeChunksLandAtEveryEighthCell()
        {
            var gen = Nine.Gen;
            // A valley chunk, one beside it, the far corner, and every chunk a landmark's stamps reach.
            var chunks = new List<(int x, int y)> { Valley, (9, 22), (26, 0), (13, 13) };
            foreach (var rows in gen.LandmarkRows.Where(r => r != null && r.Stamps.Count > 0))
                chunks.Add((rows.Centre.x / ChunkGen.Size, rows.Centre.y / ChunkGen.Size));
            Assert.That(chunks.Count, Is.GreaterThan(4), "the nine has a stamped landmark");
            foreach (var (cx, cy) in chunks)
            {
                var made = gen.Make(cx, cy);
                var known = gen.Know(cx, cy);
                for (int j = 0; j < KnownChunk.Side - 1; j++)
                    for (int i = 0; i < KnownChunk.Side - 1; i++)
                    {
                        int at = j * KnownChunk.Step * ChunkGen.Size + i * KnownChunk.Step;
                        Assert.That(known.Heights[j * KnownChunk.Side + i], Is.EqualTo(made.Land[at]), $"chunk ({cx}, {cy}) sample ({i}, {j})");
                        char cell = made.Cells[at];
                        bool wet = cell == 'w' || cell == 'r';
                        Assert.That(known.Water[j * KnownChunk.Side + i] != KnownChunk.Dry, Is.EqualTo(wet), $"chunk ({cx}, {cy}) sample ({i}, {j}) is {cell}");
                    }
                // The last row and column are the next chunk's first: two known chunks side by side meet.
                if (cx + 1 < gen.ChunksW)
                {
                    var east = gen.Know(cx + 1, cy);
                    for (int j = 0; j < KnownChunk.Side; j++)
                        Assert.That(known.Heights[j * KnownChunk.Side + KnownChunk.Side - 1], Is.EqualTo(east.Heights[j * KnownChunk.Side]), $"chunk ({cx}, {cy}) east edge");
                }
                if (gen.IsPinned(cx, cy)) Assert.That(known.Biome, Is.EqualTo(made.Biome));
            }
        }

        // The far ring paints each sample by its own biome (FarMeshes.Known), so a biome's edge shows where it runs, not as
        // a whole chunk of one colour: a flat tundra chunk drew as a white wedge against its neighbours.
        [Test]
        public void EverySampleNamesItsBiomeAndTwoChunksAgreeWhereTheyMeet()
        {
            var gen = Nine.Gen;
            int mixed = 0;
            for (int cy = 0; cy < gen.ChunksH; cy += 3)
                for (int cx = 0; cx < gen.ChunksW; cx += 3)
                {
                    var known = gen.Know(cx, cy);
                    Assert.That(known.Biomes, Has.None.Null, $"chunk ({cx}, {cy})");
                    if (known.Biomes.Distinct().Count() > 1) mixed++;
                    if (gen.IsPinned(cx, cy)) continue;
                    Assert.That(known.Biomes, Has.Member(known.Biome), $"chunk ({cx}, {cy})");
                    if (cx + 1 < gen.ChunksW)
                    {
                        var east = gen.Know(cx + 1, cy);
                        for (int j = 0; j < KnownChunk.Side; j++)
                            Assert.That(known.Biomes[j * KnownChunk.Side + KnownChunk.Side - 1], Is.EqualTo(east.Biomes[j * KnownChunk.Side]), $"chunk ({cx}, {cy}) east edge");
                    }
                }
            Assert.That(mixed, Is.GreaterThan(0), "some chunk on the nine holds more than one biome");
        }
    }
}
