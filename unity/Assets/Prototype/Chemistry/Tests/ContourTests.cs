using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The view's smooth noise and the rounded outlines of water and slicks (ValueNoise, CellContour).</summary>
    public class ContourTests
    {
        static readonly ContourSpec Smooth = new ContourSpec(4, 0.5f, 0f, 7);
        static readonly ContourSpec Wobbly = new ContourSpec(4, 0.5f, 0.3f, 7);

        [Test]
        public void NoiseIsSmoothBoundedAndRepeatable()
        {
            var rng = new Random(3);
            for (int n = 0; n < 500; n++)
            {
                float x = (float)rng.NextDouble() * 200f - 100f, y = (float)rng.NextDouble() * 200f - 100f;
                float v = ValueNoise.Twice(x, y, 5);
                Assert.That(v, Is.InRange(0f, 1f));
                Assert.AreEqual(v, ValueNoise.Twice(x, y, 5));
                Assert.Less(Math.Abs(ValueNoise.At(x + 0.01f, y, 5) - ValueNoise.At(x, y, 5)), 0.05f);
            }
            Assert.AreNotEqual(ValueNoise.At(0.5f, 0.5f, 1), ValueNoise.At(0.5f, 0.5f, 2), "seeds differ");
        }

        static ContourMesh Outline(ContourSpec spec, HashSet<(int x, int y)> cells)
        {
            var mesh = new ContourMesh();
            if (cells.Count == 0)
            {
                new CellContour(spec).Build(0, 0, -1, -1, (x, y) => 0f, mesh);
                return mesh;
            }
            new CellContour(spec).Build(cells.Min(c => c.x), cells.Min(c => c.y), cells.Max(c => c.x), cells.Max(c => c.y),
                (x, y) => cells.Contains((x, y)) ? 1f : 0f, mesh);
            return mesh;
        }

        static float Area(ContourMesh m)
        {
            float area = 0f;
            for (int t = 0; t < m.Triangles.Count; t += 3)
            {
                var (ax, ay) = m.Points[m.Triangles[t]];
                var (bx, by) = m.Points[m.Triangles[t + 1]];
                var (cx, cy) = m.Points[m.Triangles[t + 2]];
                float cross = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                Assert.Greater(cross, 0f, "every triangle winds clockwise seen from above");
                area += cross / 2f;
            }
            return area;
        }

        static void AssertSameMesh(ContourMesh expected, ContourMesh actual, string what)
        {
            CollectionAssert.AreEqual(expected.Points, actual.Points, what + ": points");
            CollectionAssert.AreEqual(expected.Values, actual.Values, what + ": values");
            CollectionAssert.AreEqual(expected.Triangles, actual.Triangles, what + ": triangles");
        }

        // Patches scattered over a wide box, in-between values as a bank has, some on the box's edge.
        static Dictionary<(int x, int y), float> Patches(int seed)
        {
            var rng = new Random(seed);
            var cells = new Dictionary<(int x, int y), float>();
            for (int n = 0; n < 40; n++)
            {
                int cx = rng.Next(0, 48), cy = rng.Next(0, 40), r = rng.Next(1, 4);
                float v = new[] { 1f, 1f, 0.45f, 0.34f }[rng.Next(4)];
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                        if (rng.Next(4) > 0) cells[(x, y)] = v;
            }
            return cells;
        }

        // The specs water and slicks really use, one whose noise alone would reach the iso value (so
        // nothing may be skipped), and one with a coarser lattice and a lower iso value.
        static readonly ContourSpec[] SkippingSpecs =
        {
            new ContourSpec(4, 0.5f, 0.7f, 31), new ContourSpec(4, 0.5f, 0.8f, 47),
            new ContourSpec(4, 0.5f, 1.2f, 9), new ContourSpec(3, 0.3f, 0.5f, 5),
        };

        [Test]
        public void SkippingEmptyLatticeLeavesTheOutlineAsItWas([Values(1, 2, 3, 4, 5, 6)] int seed, [Values(0, 1, 2, 3)] int which,
            [Values(false, true)] bool tiled, [Values(false, true)] bool calmed)
        {
            var cells = Patches(seed);
            var spec = SkippingSpecs[which];
            float Value(int x, int y) => cells.TryGetValue((x, y), out var v) ? v : 0f;
            Func<int, int, float> wobble = calmed ? (x, y) => (x * 7 + y * 3) % 5 == 0 ? 0f : 1f : (Func<int, int, float>)null;
            var skipped = new ContourMesh();
            var whole = new ContourMesh();
            new CellContour(spec) { Tiled = tiled }.Build(0, 0, 47, 39, Value, skipped, wobble);
            new CellContour(spec) { Tiled = tiled, EveryPoint = true }.Build(0, 0, 47, 39, Value, whole, wobble);
            Assert.Greater(whole.Triangles.Count, 0);
            AssertSameMesh(whole, skipped, $"wobble {spec.Wobble}, tiled {tiled}");
        }

        [Test]
        public void AReusedContourDrawsWhatAFreshOneDoes()
        {
            // Big box, then small, then big: the arrays kept from the first must not leak into the rest.
            Func<int, int, float> value = (x, y) => (x - 8) * (x - 8) + (y - 4) * (y - 4) < 12 ? 1f : 0f;
            var kept = new CellContour(Wobbly);
            var mesh = new ContourMesh();
            foreach (var (x1, y1) in new[] { (20, 12), (15, 9), (20, 12), (16, 14) })
            {
                var fresh = new ContourMesh();
                new CellContour(Wobbly).Build(0, 0, x1, y1, value, fresh);
                kept.Build(0, 0, x1, y1, value, mesh);
                AssertSameMesh(fresh, mesh, $"box to {x1}, {y1}");
            }
        }

        [Test]
        public void TilesSideBySideCoverWhatOneTileDoes()
        {
            // A wobbly blob across where two tiles meet (x 0..7 and 8..15): together they cover
            // what one tile over both does, so they neither overlap nor leave a gap.
            Func<int, int, float> value = (x, y) => (x - 8) * (x - 8) + (y - 4) * (y - 4) < 12 ? 1f : 0f;
            float Tile(int x0, int x1)
            {
                var m = new ContourMesh();
                new CellContour(Wobbly) { Tiled = true }.Build(x0, 0, x1, 7, value, m);
                return Area(m);
            }
            float left = Tile(0, 7), right = Tile(8, 15), both = Tile(0, 15);
            Assert.Greater(left, 1f);
            Assert.Greater(right, 1f);
            Assert.AreEqual(both, left + right, 1e-3f);
        }

        [Test]
        public void NothingToOutlineGivesNoTriangles()
        {
            Assert.IsEmpty(Outline(Smooth, new HashSet<(int x, int y)>()).Triangles);
        }

        [Test]
        public void OneCellIsARoundBlobInsideItsNeighbourhood()
        {
            var m = Outline(Smooth, new HashSet<(int x, int y)> { (5, 5) });
            float area = Area(m);
            Assert.That(area, Is.InRange(0.3f, 1f), "about a disc of half a cell's radius");
            foreach (var (x, y) in m.Points) Assert.Less(Math.Max(Math.Abs(x - 5.5f), Math.Abs(y - 5.5f)), 1f);
            int rim = m.Values.Count(v => Math.Abs(v - 0.5f) < 1e-6f);
            Assert.Greater(rim, 8, "a round edge has many points, a square four");
        }

        [Test]
        public void ABlockCoversAboutItsCellsWithRoundedCorners()
        {
            var cells = new HashSet<(int x, int y)>();
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++) cells.Add((x, y));
            var m = Outline(Smooth, cells);
            Assert.That(Area(m), Is.InRange(7f, 9f), "9 cells, minus the rounded corners");
            Assert.That(m.Values, Has.All.GreaterThanOrEqualTo(0.5f - 1e-6f));
            float corner = m.Points.Min(p => Math.Abs(p.x - 3f) + Math.Abs(p.y - 3f));
            Assert.Greater(corner, 0.2f, "the outline cuts the block's corner");
        }

        [Test]
        public void WobbleBendsTheEdgeButKeepsThePatch()
        {
            var cells = new HashSet<(int x, int y)>();
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++) cells.Add((x, y));
            var smooth = Outline(Smooth, cells);
            var wobbly = Outline(Wobbly, cells);
            Assert.AreNotEqual(Area(smooth), Area(wobbly));
            Assert.AreEqual(Area(smooth), Area(wobbly), 4f);
            var again = Outline(Wobbly, cells);
            Assert.AreEqual(wobbly.Points, again.Points, "the same cells give the same outline");
        }

        [Test]
        public void CalmCellsHoldTheEdgeWhereTheValuesPutIt()
        {
            Func<int, int, float> block = (x, y) => x >= 0 && x < 6 && y >= 0 && y < 6 ? 1f : 0f;
            var smooth = new ContourMesh();
            var calm = new ContourMesh();
            var wobbly = new ContourMesh();
            new CellContour(Smooth).Build(0, 0, 5, 5, block, smooth);
            new CellContour(Wobbly).Build(0, 0, 5, 5, block, calm, (x, y) => 0f);
            new CellContour(Wobbly).Build(0, 0, 5, 5, block, wobbly, (x, y) => 1f);
            Assert.AreEqual(smooth.Points, calm.Points, "no wobble anywhere: the smooth outline");
            Assert.AreEqual(Outline(Wobbly, Block6()).Points, wobbly.Points, "full wobble: as with none given");
        }

        static HashSet<(int x, int y)> Block6()
        {
            var cells = new HashSet<(int x, int y)>();
            for (int y = 0; y < 6; y++)
                for (int x = 0; x < 6; x++) cells.Add((x, y));
            return cells;
        }
    }
}
