// The biome comes from the world, not the square template (J-085): a world sketch drawn in map.json (WorldSketch, the
// paper's "Draw first" JSON) or, without one, latitude and the plan's bands (WorldBiomes) say what the land is, smoothly
// across world squares and the east-west seam, and a square template's biome only weighs which template the plan picks.
// BIOME_PNG_OUT=<folder> dotnet test core --filter "FullyQualifiedName~BiomeSketchTests" draws the 12 x 10's and the
// nine's biome maps (docs/design/world-sketch/).
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class BiomeSketchTests
    {
        static string MapDir(string id) => Path.Combine(LandmarkIntentTests.GameWorldDir(), "maps", id);

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

        // The biome at every step-th cell, sea and wall rows painted over, the world square grid in faint lines.
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
                    else if (gen.IsPinned(cx / ChunkGen.Size, cy / ChunkGen.Size)) col = (30, 96, 40); // the valley, its own map
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

        static SquareLibrary library;
        static SquareLibrary Lib => library ??= SquareLibrary.Load(LandmarkIntentTests.GameWorldDir());

        // One big forest over the middle of a 12 x 10, its edges 0.4 of a world square inside squares 2..7 east and
        // 2..6 south (more than the warp and the soft edge), so those 30 world squares are wholly drawn.
        internal const string ForestSketch = @"{ ""worldSketch"": 1, ""name"": ""One big forest"", ""world"": { ""w"": 12, ""h"": 10, ""wrap"": ""ew"" },
            ""soft"": 96, ""shapes"": [ { ""biome"": ""temperate"", ""pts"": [[1.6, 1.6], [8.4, 1.6], [8.4, 7.4], [1.6, 7.4]] } ] }";

        static WorldPlan ForestPlan()
        {
            var inputs = PlanInputs.For("valley-1", 12, 8, PlanWrap.EastWest);
            inputs.Sketch = WorldSketch.Read(JObject.Parse(ForestSketch));
            return WorldPlan.Solve(inputs, Lib);
        }

        static ChunkGen GenOf(WorldPlan plan) => new ChunkGen(plan, Lib.Biomes, null, 0, 0, plan.Grid.W, plan.Grid.H);

        [Test]
        public void AForestDrawnOverManySquaresIsForestLandWhateverTheTemplates()
        {
            var plan = ForestPlan();
            var gen = GenOf(plan);
            int other = 0;
            for (int sy = 2; sy <= 6; sy++)
                for (int sx = 2; sx <= 7; sx++)
                {
                    int i = plan.Grid.Index(sx, sy), forest = 0, all = 0;
                    if (plan.Band[i] == 0) continue; // a square the world keeps as sea has no land to be forest
                    for (int y = 8; y < WorldPlan.SquareCells; y += 16)
                        for (int x = 8; x < WorldPlan.SquareCells; x += 16)
                        {
                            all++;
                            if (gen.BiomeAt(sx * WorldPlan.SquareCells + x, sy * WorldPlan.SquareCells + y) == "temperate") forest++;
                        }
                    string template = plan.TemplateAt(i).Biome;
                    if (template != "temperate") other++;
                    TestContext.Out.WriteLine($"({sx}, {sy}) {plan.TemplateAt(i).Id} ({template}): {100 * forest / all}% forest");
                    Assert.That(forest * 10, Is.GreaterThanOrEqualTo(all * 9), $"({sx}, {sy}), template {plan.TemplateAt(i).Id}, is forest land");
                }
            Assert.That(other, Is.GreaterThan(0), "some of the drawn forest's squares took a template of another biome, and their land is forest all the same");
            var chunk = gen.Make(4 * WorldPlan.ChunksPerSquare + 4, 4 * WorldPlan.ChunksPerSquare + 4);
            Assert.That(chunk.Biome, Is.EqualTo("temperate"), "a chunk made in it is forest");
        }

        [Test]
        public void TheSketchAsksTheBandsItsBiomesStandOn()
        {
            var plan = ForestPlan();
            var (lo, hi) = Lib.BandsOf("temperate");
            for (int sy = 2; sy <= 6; sy++)
                for (int sx = 2; sx <= 7; sx++)
                {
                    int i = plan.Grid.Index(sx, sy);
                    Assert.That(plan.Sketch[i].On && plan.Sketch[i].Land == "temperate", $"({sx}, {sy}) reads as drawn forest");
                    Assert.That(plan.Sketch[i].Ask, Is.InRange(lo, hi), $"({sx}, {sy}) asks a band forest templates come in");
                }
        }

        [Test]
        public void NoTemplateRepeatsWithinThreeWorldSquares()
        {
            foreach (var plan in new[] { ForestPlan(), GeneratedMap.Load(MapDir("world-12x10")).Plan, GeneratedMap.Load(MapDir("nine")).Plan })
                for (int i = 0; i < plan.Count; i++)
                    for (int j = i + 1; j < plan.Count; j++)
                    {
                        var t = plan.TemplateAt(i);
                        if (t.Repeats || t != plan.TemplateAt(j)) continue;
                        Assert.That(plan.Grid.Distance(i, j), Is.GreaterThanOrEqualTo(3), $"{t.Id} at squares {i} and {j}");
                    }
        }

        [Test]
        public void TheBiomesRunOnAcrossTheSeam()
        {
            var gen = GeneratedMap.Load(MapDir("world-12x10")).Gen;
            var field = gen.Field;
            int w = gen.CellsW, worst = 0;
            for (int y = 0; y < gen.CellsH; y += 8)
            {
                // A world round is the same bit for bit, and the seam's two sides differ no more than neighbours do.
                foreach (int x in new[] { 0, 1, w / 2, w - 1 })
                {
                    var a = field.At(x, y);
                    var b = field.At(x + w, y);
                    var c = field.At(x - w, y);
                    for (int s = 0; s < 4; s++)
                        Assert.That((b.Id(s), b.Weight(s), c.Id(s), c.Weight(s)), Is.EqualTo((a.Id(s), a.Weight(s), a.Id(s), a.Weight(s))), $"({x}, {y}) a world round");
                }
                worst = Math.Max(worst, Jump(field.At(w - 1, y), field.At(w, y)));
                Assert.That(gen.BiomeAt(w - 1, y) == gen.BiomeAt(0, y) || Jump(field.At(w - 1, y), field.At(w, y)) < BiomeMath.One / 8,
                    $"row {y}: the biome either side of the seam is the same, or they meet in a blend");
            }
            TestContext.Out.WriteLine($"SEAM the most any biome's weight changes across it: {100.0 * worst / BiomeMath.One:F1}%");
            Assert.That(worst, Is.LessThan(BiomeMath.One / 8), "no step at the seam");
        }

        // The most any biome's weight differs between two blends, Q16.
        static int Jump(BiomeBlend a, BiomeBlend b)
        {
            long Of(BiomeBlend w, int id)
            {
                long t = 0;
                for (int s = 0; s < 4; s++)
                    if (w.Weight(s) > 0 && w.Id(s) == id) t += w.Weight(s);
                return t;
            }
            long worst = 0;
            for (int s = 0; s < 4; s++)
            {
                worst = Math.Max(worst, Math.Abs(Of(a, a.Id(s)) - Of(b, a.Id(s))));
                worst = Math.Max(worst, Math.Abs(Of(a, b.Id(s)) - Of(b, b.Id(s))));
            }
            return (int)worst;
        }

        // Goldens: a change that alters a plan is made on purpose (a new WorldPlan.Version), or the goldens are re-picked.
        [Test]
        public void TheForestPlanIsGolden()
        {
            var plan = ForestPlan();
            TestContext.Out.WriteLine($"PLAN GOLDEN forest sketch 0x{plan.Hash:x8}");
            Assert.That(plan.Hash, Is.EqualTo(ForestGolden));
            Assert.That(ForestPlan().Hash, Is.EqualTo(plan.Hash), "the same every time");
        }

        const uint ForestGolden = 0xdd04151au;

        [Test]
        public void Pictures()
        {
            int a = BiomeMap(GeneratedMap.Load(MapDir("world-12x10")), "world-12x10-biomes.png", 32);
            int b = BiomeMap(GeneratedMap.Load(MapDir("nine")), "nine-biomes.png", 12);
            Assert.That(a + b, Is.GreaterThanOrEqualTo(0));
        }
    }
}

namespace Chem.Tests
{
    [Category("Unit")]
    public class WorldSketchTests
    {
        [Test]
        public void ItReadsThePapersJson()
        {
            var s = WorldSketch.Read(JObject.Parse(BiomeSketchTests.ForestSketch));
            Assert.That((s.Name, s.W, s.H, s.Wrap, s.Soft, s.Shapes.Count), Is.EqualTo(("One big forest", 12, 10, true, 96, 1)));
            Assert.That(s.Shapes[0].X, Is.EqualTo(new long[] { 160, 840, 840, 160 }), "points in hundredths of a world square");
        }

        [Test]
        public void NoneOrOffIsNoSketch()
        {
            Assert.That(WorldSketch.Read(null), Is.Null);
            Assert.That(WorldSketch.Read(JObject.Parse(@"{ ""fit"": false, ""shapes"": [] }")), Is.Null);
        }

        [TestCase(@"{ ""shapes"": [ { ""biome"": ""lava"", ""pts"": [[0,0],[1,0],[1,1]] } ] }", "lava")]
        [TestCase(@"{ ""shapes"": [ { ""biome"": ""tundra"", ""pts"": [[0,0],[1,0]] } ] }", "2 points")]
        [TestCase(@"{ ""soft"": 5, ""shapes"": [] }", "soft 5")]
        [TestCase(@"{ ""world"": { ""w"": 0, ""h"": 10 }, ""shapes"": [] }", "0 x 10")]
        public void ItRefusesWhatItCantDraw(string json, string says)
        {
            Assert.That(() => WorldSketch.Read(JObject.Parse(json)), Throws.TypeOf<FormatException>().With.Message.Contains(says));
        }
    }
}
