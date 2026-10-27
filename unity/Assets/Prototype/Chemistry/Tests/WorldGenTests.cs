using System;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The world generator's foundations (D11): hashing, value noise, exact trig, the solver's stream,
    /// noisy ellipses and meandering curves. The expected values are bit patterns from running
    /// PocketTactics' own TypeScript (src/core/world/hash.ts, noise.ts, theme/geometry.ts) on the same
    /// inputs, so these pin the port to the original bit for bit.
    /// </summary>
    public class WorldGenTests
    {
        static string Bits(double x) => BitConverter.DoubleToInt64Bits(x).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);

        [Test]
        public void HashesMatchPocketTactics()
        {
            Assert.That((uint)WorldHash.Fmix32(12345), Is.EqualTo(1011272156u));
            Assert.That((uint)WorldHash.Fmix32(-7), Is.EqualTo(214441827u));
            Assert.That(WorldHash.Hash4(1, 2, 3, 4), Is.EqualTo(311103910u));
            Assert.That(WorldHash.Hash4(-99, 0x54484d45, -12, 700), Is.EqualTo(3615421795u));
            Assert.That(WorldHash.Hash4(0x7fffffff, -1, int.MinValue, 5), Is.EqualTo(2097883692u));
            Assert.That(Bits(WorldHash.HashUnit(5, 6, -7, 8)), Is.EqualTo("3feea0f39b600000"));
        }

        [Test]
        public void ValueNoiseMatchesPocketTactics()
        {
            Assert.That(Bits(WorldNoise.Value(9, 10, 3.25, -17.5, 7)), Is.EqualTo("3fd40e5eeab1fdc2"));
            Assert.That(Bits(WorldNoise.Value(-4, 99, -123.75, 45.125, 12)), Is.EqualTo("3fe2588a13fb4297"));
            Assert.That(Bits(WorldNoise.Fractal(9, 10, 3.25, -17.5, 14, 2)), Is.EqualTo("3fde774a8ac9bafd"));
            Assert.That(Bits(WorldNoise.Fractal(77, 0x44495448, 100, -250, 7, 3)), Is.EqualTo("3fe3a4298c05f92d"));
            Assert.That(Bits(WorldNoise.Quantile(2, 0.75)), Is.EqualTo("3fe3b0f32cfe7d4f"));
            Assert.That(Bits(WorldNoise.Quantile(3, 0.1)), Is.EqualTo("3fd59ada2d3e17c8"));
            Assert.That(Bits(WorldNoise.ToUniform(2, 0.6)), Is.EqualTo("3fe70bd70a3d70a4"));
            Assert.That(Bits(WorldNoise.ToUniform(2, 0.31)), Is.EqualTo("3fb6deb851eb851f"));
        }

        [Test]
        public void ExactTrigMatchesPocketTactics()
        {
            Assert.That(Bits(ThemeGeometry.Sin(0.7)), Is.EqualTo("3fe49d6e694619b8"));
            Assert.That(Bits(ThemeGeometry.Sin(-5.3)), Is.EqualTo("3feaa1ef54bc123b"));
            Assert.That(Bits(ThemeGeometry.Cos(2.9)), Is.EqualTo("bfef1216dba340db"));
            Assert.That(Bits(ThemeGeometry.Atan2(-3, 4)), Is.EqualTo("bfe4978fa3269ee2"));
            Assert.That(Bits(ThemeGeometry.Atan2(2, -0.5)), Is.EqualTo("3ffd0d6a1369bd34"));
            Assert.That(Bits(ThemeGeometry.Hypot(3.3, 4.4)), Is.EqualTo("4016000000000000"));
            Assert.That(ThemeGeometry.Sin(0.7), Is.EqualTo(Math.Sin(0.7)).Within(1e-12), "and it is a sine");
        }

        [Test]
        public void TheSolverStreamBlobsAndCurvesMatchPocketTactics()
        {
            var rng = new ThemeRng(WorldHash.Hash4(42, 0x54484d45, 99, 0));
            Assert.That(Bits(rng.Next()), Is.EqualTo("3fe23d7951600000"));
            Assert.That(Bits(rng.Range(-1, 1)), Is.EqualTo("bfe3bdb3ca000000"));
            Assert.That(rng.Below(100), Is.EqualTo(95));

            var blob = ThemeGeometry.MakeBlob(rng, 10, -20, 60, 30, 0.6, 0.14);
            var h = string.Join(" ", blob.Harmonics.Select(x => $"{x.K}:{Bits(x.Amp)}:{Bits(x.Phase)}"));
            Assert.That(h, Is.EqualTo("2:3fb35f2f3410624e:40074b5496ef4661 3:3f9ae01ed8676c8c:3fd23b93db67ea10 5:3f96ceef94c962fd:400672d23581e06b"));
            Assert.That(Bits(ThemeGeometry.Rho(blob, 33, -5)), Is.EqualTo("3fdf57f708ffdc57"));
            Assert.That(Bits(ThemeGeometry.Rho(blob, -40, -60)), Is.EqualTo("3ff29ae39a6ca7ab"));

            var curve = ThemeGeometry.MeanderCurve(rng, new[] { new Pt(-100, -80), new Pt(0, 10), new Pt(120, 90) }, 0.7, 3);
            ulong cs = 0;
            unchecked
            {
                foreach (var p in curve)
                    cs = cs * 31 + (ulong)BitConverter.DoubleToInt64Bits(p.X) + 7 * (ulong)BitConverter.DoubleToInt64Bits(p.Y);
            }
            string hex = cs.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
            Assert.That($"{curve.Count} {hex} {Bits(curve[5].X)} {Bits(curve[5].Y)}", Is.EqualTo("97 287890eb117a2e2 c0578132a3ef3169 c050b5c4207bf2b6"));
            Assert.That(Bits(rng.Next()), Is.EqualTo("3fe16a1803600000"), "the stream is where TypeScript's is after it");
        }
    }
}
