// BiomeSketchTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in BiomeSketchTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class BiomeSketchWorldTests
    {

        static readonly Dictionary<string, (byte r, byte g, byte b)> Colours = new Dictionary<string, (byte, byte, byte)>
        {
            ["temperate"] = (52, 120, 52),
            ["grassland"] = (160, 190, 84),
            ["mountains"] = (126, 116, 108),
            ["tundra"] = (200, 214, 214),
            ["salt-flat"] = (232, 214, 160),
            ["foggy-marsh"] = (84, 124, 112),
            ["ashlands"] = (86, 64, 60),
        };

        static int BiomeMap(GeneratedMap map, string file, int step)
        {
            string folder = Environment.GetEnvironmentVariable("BIOME_PNG_OUT");
            if (string.IsNullOrEmpty(folder)) return 0;
            Directory.CreateDirectory(folder);
            var gen = map.Gen;
            int w = gen.CellsW / step, h = gen.CellsH / step;
            var rgb = new byte[w * h * 3];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int cx = x * step + step / 2, cy = y * step + step / 2;
                    int square = gen.Plan.Grid.Index(gen.OriginX + cx / WorldPlan.SquareCells, gen.OriginY + cy / WorldPlan.SquareCells);
                    (int, int, int) col;
                    if (gen.Plan.Wall[square] != 0) col = gen.Plan.Wall[square] == 1 ? (236, 244, 250) : (222, 190, 120);
                    else if (gen.LandAt(cx, cy) < ChunkGen.SeaLevel) col = (40, 84, 150);
                    else if (gen.IsPinned(cx / ChunkGen.Size, cy / ChunkGen.Size)) col = (30, 96, 40);
                    else
                    {
                        var (r, g, b) = Colours.TryGetValue(gen.BiomeAt(cx, cy), out var bc) ? bc : ((byte)255, (byte)0, (byte)255);
                        col = (r, g, b);
                    }
                    bool line = cx % WorldPlan.SquareCells < step || cy % WorldPlan.SquareCells < step;
                    WorldPlanPicture.Put(rgb, y * w + x, col, line ? -40 : 0);
                }
            File.WriteAllBytes(Path.Combine(folder, file), LandmarkPicture.Png(rgb, w, h));
            TestContext.Out.WriteLine($"PICTURE {Path.Combine(folder, file)} {w} x {h}");
            return w * h;
        }

        [Test]
        public void Pictures()
        {
            int a = BiomeMap(GeneratedMap.Load(BiomeSketchTests.MapDir("world-12x10")), "world-12x10-biomes.png", 32);
            int b = BiomeMap(GeneratedMap.Load(BiomeSketchTests.MapDir("nine")), "nine-biomes.png", 12);
            Assert.That(a + b, Is.GreaterThanOrEqualTo(0));
        }
    }
}
