using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// THROWAWAY spike for docs/FINER_GROUND.md (branch finer-spike, never merged): option 2,
    /// finer height only. Each 1.5 m cell keeps four 0.75 m sub-square heights packed in one int,
    /// water reads their sum (exact in water units), a slanted trench is dug along the true aim
    /// on the finer grid, and a cell whose four are alike hashes as today.
    /// </summary>
    public class FinerSpikeTests
    {
        /// <summary>Four sub-square heights (steps, −128..127) in one int: NW, NE, SW, SE.</summary>
        readonly struct SubHeights
        {
            public SubHeights(int packed) => Packed = packed;

            public int Packed { get; }

            public static SubHeights Uniform(int h) => new SubHeights(0).With(0, h).With(1, h).With(2, h).With(3, h);

            public int this[int i] => (sbyte)(Packed >> (8 * i));

            public SubHeights With(int i, int h) => new SubHeights((Packed & ~(0xff << (8 * i))) | ((h & 0xff) << (8 * i)));

            public int Sum => this[0] + this[1] + this[2] + this[3];

            public bool IsUniform => this[0] == this[1] && this[1] == this[2] && this[2] == this[3];
        }

        /// <summary>What water reads: elevation and the four heights in water units. With unitsPerStep 4 it is exact: the sum.</summary>
        static int ElevationUnits(int elevation, SubHeights h, int unitsPerStep) =>
            elevation * unitsPerStep + FloorDiv(h.Sum * unitsPerStep, 4);

        static int FloorDiv(int n, int d) => n >= 0 ? n / d : -((-n + d - 1) / d);

        /// <summary>Today's: (elevation + Height) in water units.</summary>
        static int TodaysUnits(int elevation, int height, int unitsPerStep) => (elevation + height) * unitsPerStep;

        /// <summary>
        /// World.SlantCells' walk (each step shares a side with the last) on a grid of the given
        /// size in metres, from the origin along (aimX, aimY), out to lengthMetres: the squares it
        /// visits, as centres in metres.
        /// </summary>
        static List<(double x, double y)> Slant(double square, int aimX, int aimY, double lengthMetres)
        {
            int length = (int)Math.Round(lengthMetres / square);
            bool alongX = Math.Abs(aimX) >= Math.Abs(aimY);
            int major = Math.Max(Math.Abs(aimX), Math.Abs(aimY));
            long reach = (long)length * length * major * major, size = (long)aimX * aimX + (long)aimY * aimY;
            var visited = new List<(int, int)>();
            int px = 0, py = 0;
            for (int k = 0; (long)k * k * size < reach; k++)
            {
                int cx = RoundDiv(k * aimX, major), cy = RoundDiv(k * aimY, major);
                if (k > 0 && cx != px && cy != py) visited.Add(alongX ? (cx, py) : (px, cy));
                visited.Add((cx, cy));
                (px, py) = (cx, cy);
            }
            return visited.Select(v => (v.Item1 * square, v.Item2 * square)).ToList();
        }

        static int RoundDiv(int n, int d) => (int)Math.Floor((2.0 * n + d) / (2.0 * d));

        /// <summary>How far, at worst, a dug square's centre is from the aim's true line, in metres.</summary>
        static double Worst(List<(double x, double y)> centres, int aimX, int aimY)
        {
            double len = Math.Sqrt(aimX * aimX + aimY * aimY);
            return centres.Max(c => Math.Abs(c.x * aimY - c.y * aimX) / len);
        }

        [Test]
        public void FourHeightsPackIntoOneIntAndUniformOnesAreTodaysHeight()
        {
            var h = SubHeights.Uniform(-3).With(1, 2);
            Assert.That((h[0], h[1], h[2], h[3]), Is.EqualTo((-3, 2, -3, -3)));
            Assert.That(h.Sum, Is.EqualTo(-7));
            Assert.That(h.IsUniform, Is.False);
            Assert.That(SubHeights.Uniform(5).IsUniform, Is.True);
            for (int height = -6; height <= 8; height++)
                for (int elevation = -2; elevation <= 2; elevation++)
                    Assert.That(ElevationUnits(elevation, SubHeights.Uniform(height), 4), Is.EqualTo(TodaysUnits(elevation, height, 4)),
                        "four alike: water reads exactly what it reads today, so the goldens stand");
        }

        [Test]
        public void WaterReadsTheMeanHeightExactlyInWholeUnits()
        {
            // A cell half dug: two sub-squares at -2, two at 0. Its mean is -1 step: 4 units down.
            var half = SubHeights.Uniform(0).With(0, -2).With(1, -2);
            Assert.That(ElevationUnits(0, half, 4), Is.EqualTo(-4));
            // One sub-square dug a step: a quarter step, one unit. Exact, no rounding, at unitsPerStep 4.
            Assert.That(ElevationUnits(0, SubHeights.Uniform(0).With(3, -1), 4), Is.EqualTo(-1));
        }

        [Test]
        public void ASlantedTrenchFollowsItsAimTwiceAsClosely()
        {
            // A trench 15 m long at a shallow slant (3 across, 1 up), as Mold Earth's slanted line digs.
            var squares = Slant(1.5, 3, 1, 15);
            var subs = Slant(0.75, 3, 1, 15);
            double coarse = Worst(squares, 3, 1), fine = Worst(subs, 3, 1);
            TestContext.WriteLine($"worst off the aim: 1.5 m squares {coarse:0.00} m ({squares.Count} squares); 0.75 m sub-squares {fine:0.00} m ({subs.Count} subs)");
            Assert.That(fine, Is.LessThanOrEqualTo(coarse / 2 + 1e-9), "half the step, half the stagger");
            Assert.That(subs.Count, Is.InRange(squares.Count * 2 - 2, squares.Count * 2 + 2), "twice the steps along the same length");
        }
    }
}
