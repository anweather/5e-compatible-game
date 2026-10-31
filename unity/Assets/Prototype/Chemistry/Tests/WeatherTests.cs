using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The clock and the weather (Sky.cs) and what the weather does to cells (World.Weather.cs).
    /// Mirrors sandbox/test/weather.test.ts; the golden numbers come from running the same worlds there.
    /// </summary>
    public class WeatherTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>Short, quick weather for the goldens: stretches of 0.2 min (72 ticks), no calm start.</summary>
        const string Quick = "'periodMinutes': 0.2, 'rampMinutes': 0.05, 'calmPeriods': 0, " +
            "'rain': { 'fillEvery': 4, 'fillChance': 0.2, 'douseChance': 0.3 }, 'storm': { 'strikeEvery': 6, 'strikeChance': 1 }";

        static Rules With(string weather) =>
            R.WithMods(new[] { ModPatch.Parse("{ 'sim': { 'weather': { " + weather + " } } }") });

        static Rules StormSeason() =>
            R.WithMods(new[] { ModPatch.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "mods", "storm-season.json"))) });

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        /// <summary>A walled field of grass with a pond, a fire lit on it.</summary>
        static World Field(Rules rules, uint seed)
        {
            var w = new World(rules, 24, 12, seed);
            for (int x = 0; x < 24; x++)
                for (int y = 0; y < 12; y++)
                {
                    var c = w.At(x, y);
                    bool wall = x == 0 || y == 0 || x == 23 || y == 11;
                    c.T = x < 6 ? "pit" : "grass";
                    if (wall) c.T = "bedrock";
                    else if (x < 6) c.Water = 4;
                }
            w.Cast("fire-bolt", 14, 6, 1);
            return w;
        }

        static int Strikes(World w) => w.Log.Where(l => l.Text == "Lightning strikes").Sum(l => l.Count);

        [Test]
        public void TheSkysRulesPassTheChecksWithStormSeasonOn()
        {
            Assert.DoesNotThrow(() => StormSeason());
            var e = Assert.Throws<ModException>(() => With("'storm': { 'strikeEvery': 0 }"));
            Assert.That(e.Errors, Is.EqualTo(new[] { "sim weather.storm.strikeEvery must be at least 1" }));
            e = Assert.Throws<ModException>(() => With("'fog': { 'gas': 'mist' }"));
            Assert.That(e.Errors, Is.EqualTo(new[] { "sim weather.fog.gas: unknown gas mist" }));
        }

        [Test]
        public void TheClockStartsAtNineAndADayLasts24RealMinutes()
        {
            var p = R.Sim;
            Assert.That(Sky.Minutes(p, 0), Is.EqualTo(9 * 60));
            int perDay = 24 * 60 * p.TicksPerSecond;
            Assert.That(Sky.MinuteOfDay(Sky.Minutes(p, perDay / 2)), Is.EqualTo(21 * 60), "half a day later it's 21:00");
            Assert.That(Sky.Day(Sky.Minutes(p, perDay)), Is.EqualTo(2));
            Assert.That(Sky.MinuteOfDay(Sky.Minutes(p, perDay)), Is.EqualTo(9 * 60));
        }

        [Test]
        public void TheWorldStartsCalmThenTheForecastMatchesTs()
        {
            var p = R.Sim;
            var forecast = string.Join(" ", Enumerable.Range(0, 24).Select(k => Sky.KindOf(p, 1, k).ToString().ToLowerInvariant()));
            Assert.That(forecast, Is.EqualTo("clear clear cloudy windy cloudy clear storm rain rain storm cloudy rain cloudy clear rain fog windy rain windy rain rain storm windy snow"));
        }

        [Test]
        public void WeatherRampsInAndOutAndStormsBringWind()
        {
            var p = StormSeason().Sim;
            int length = 4 * 60 * p.TicksPerSecond;
            bool sawStorm = false;
            for (int k = 0; k < 12; k++)
            {
                var start = Sky.Weather(p, 3, k * length);
                var mid = Sky.Weather(p, 3, k * length + length / 2);
                Assert.That(mid.Strength, Is.EqualTo(100));
                if (k == 0) Assert.That(start.Strength, Is.EqualTo(0), "the first stretch ramps in");
                if (mid.Kind == WeatherKind.Storm)
                {
                    sawStorm = true;
                    Assert.That(mid.WindDir, Is.InRange(0, 3));
                }
                else Assert.That(mid.WindDir, Is.EqualTo(-1));
            }
            Assert.That(sawStorm);
        }

        [Test]
        public void NothingChangesDuringTheCalmStart()
        {
            // The base rules start with calm stretches, so every other golden in the suite is untouched.
            var w = new World(R, 8, 8, 1);
            w.At(3, 3).T = "pit"; // a puddle in a hollow: it doesn't soak in (World.Soak)
            w.At(3, 3).Water = 1;
            Run(w, 2 * 4 * 60 * R.Sim.TicksPerSecond - 1);
            Assert.That(w.Weather.Kind, Is.EqualTo(WeatherKind.Clear));
            Assert.That(w.Cells.Sum(c => c.Water), Is.EqualTo(1), "no rain and no drying yet");
        }

        [Test]
        public void GoldenStormMatchesTs()
        {
            var rules = With(Quick + ", 'weights': { 'clear': 0, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 1, 'snow': 0 }");
            var a = Field(rules, 5);
            var b = Field(rules, 5);
            Run(a, 150);
            Run(b, 150);
            Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()), "deterministic");
            Assert.That(Strikes(a), Is.GreaterThan(0), "lightning struck");
            Assert.That(a.Log.Any(l => l.Text == "The rain puts out a fire"), "the rain put a fire out");
            Assert.That($"{a.CellHash()} {a.RngState} {Strikes(a)}", Is.EqualTo("a80f8ead 275421548 24"));
        }

        [Test]
        public void GoldenMixedQuickWeatherMatchesTs()
        {
            var w = Field(With(Quick), 8);
            Run(w, 1200);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("41514104 3951220311"));
        }
        [Test]
        public void GoldenMeteorShowerMatchesTs()
        {
            // The same as "a meteor shower brings meteors down..." in weather.test.ts.
            var rules = With(Quick + ", 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }, " +
                "'meteors': { 'showerChance': 1, 'every': 6, 'chance': 1, 'fallTicks': 11 }");
            var w = Field(rules, 6);
            (int x, int y, int lands)? seen = null;
            int appeared = -1;
            for (int i = 0; i < 150; i++)
            {
                w.Step();
                if (seen == null && w.NewMeteors.Count > 0) (seen, appeared) = (w.NewMeteors[0], w.Tick);
            }
            Assert.That(seen, Is.Not.Null, "a meteor appeared");
            Assert.That(seen.Value.lands, Is.EqualTo(appeared + 11), "it lands fallTicks after it appears");
            int strikes = w.Log.Where(l => l.Text == "A meteor strikes").Sum(l => l.Count);
            Assert.That(strikes, Is.GreaterThan(0), "meteors landed");
            Assert.That(w.Cells.Any(c => c.Height < 0), "and left craters");
            Assert.That($"{w.CellHash()} {w.RngState} {strikes}", Is.EqualTo("89885246 991183198 16"));
        }

        [Test]
        public void GoldenSnowFreezesLiesAndBanksThenADrySpellThawsAndMeltsItMatchesTs()
        {
            // The same as "snow freezes shallow water..." in weather.test.ts.
            var snowy = With(Quick + ", 'weights': { 'clear': 0, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 1 }, " +
                "'snow': { 'freezeEvery': 4, 'freezeChance': 0.5 }");
            var clear = With(Quick + ", 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }, " +
                "'meteors': { 'showerChance': 0 }, 'rain': { 'dryEvery': 6, 'dryChance': 0 }, 'snow': { 'thawChance': 0.5 }");
            var w = Field(snowy, 7);
            for (int x = 8; x < 12; x++)
                for (int y = 2; y < 10; y++) w.At(x, y).Water = 1 + x % 2;
            Run(w, 150);
            int frozen = w.Cells.Count(c => c.Frozen && c.Water > 0);
            Assert.That(frozen, Is.GreaterThan(10), "the puddles froze");
            Assert.That(w.Cells.Any(c => c.Water > 2 && c.Frozen), Is.False, "the deep pond did not");
            int covered = w.Cells.Count(c => c.Cover == World.SnowCover || c.Cover == World.BankCover);
            int banks = w.Cells.Count(c => c.Cover == World.BankCover);
            Assert.That(covered, Is.GreaterThan(50), "snow lies on the field");
            Assert.That(banks, Is.GreaterThan(0), "and drifts into banks against the walls");
            Assert.That($"{w.CellHash()} {w.RngState} {frozen} {covered} {banks}", Is.EqualTo("ebb306af 3764626303 37 124 36"));

            w.ApplyRules(clear);
            Run(w, 150);
            int still = w.Cells.Count(c => c.Frozen && c.Water > 0);
            Assert.That(still, Is.LessThan(frozen / 2), "a dry spell thawed most of it");
            int left = w.Cells.Count(c => c.Cover == World.SnowCover || c.Cover == World.BankCover);
            Assert.That(left, Is.LessThan(20), "the snow melted");
            Assert.That($"{w.CellHash()} {w.RngState} {still} {left}", Is.EqualTo("6ce8640a 1063028723 0 0"));
        }

        /// <summary>Fog and only fog for an hour, with these keys in weather.fog.</summary>
        static Rules Foggy(string fog = "") => With("'periodMinutes': 60, 'rampMinutes': 0.02, 'calmPeriods': 0, 'fog': { 'gas': 'fog'" + fog + " }, " +
            "'weights': { 'clear': 0, 'cloudy': 0, 'windy': 0, 'fog': 1, 'rain': 0, 'storm': 0, 'snow': 0 }, 'meteors': { 'showerChance': 0 }");

        /// <summary>Open grass with a pond in its north-west block.</summary>
        static World Moor(Rules rules, uint seed)
        {
            var w = new World(rules, 256, 256, seed, "grass");
            for (int y = 40; y < 60; y++)
                for (int x = 40; x < 60; x++)
                {
                    w.Paint(x, y, "pit");
                    w.AddWater(x, y, 4);
                }
            return w;
        }

        static int FogCells(World w) => w.Cells.Count(c => c.Gas == "fog");

        [Test]
        public void GoldenFogLiesInPatchesNotABlanketMatchesTs()
        {
            // The same as "fog lies in patches..." in weather.test.ts.
            var w = Moor(Foggy(), 7);
            Run(w, 400);
            int fog = FogCells(w);
            Assert.That(fog * 100 / (256 * 256), Is.InRange(2, 9), $"{fog} cells: here and there");
            Assert.That(w.Cells.All(c => c.Gas != "fog" || c.GasAmt < 2), "thin");
            Assert.That(Enumerable.Range(0, 256).Any(y => w.FogBetween(0, y, 255, y)), "a line through a patch is hidden");
            Assert.That(Enumerable.Range(0, 256).Any(y => !w.FogBetween(0, y, 255, y)), "a line past them is clear");
            Assert.That($"{w.CellHash()} {w.RngState} {fog}", Is.EqualTo("9d766b76 2392649509 4549"));
        }

        [Test]
        public void FogPatchesFavourLowGroundAndWater()
        {
            // Fog beside a river down the world, against the same seed with no river.
            int Near(World w) => w.Cells.Select((c, i) => c.Gas == "fog" && Math.Abs(i % 256 - 65) < 20 ? 1 : 0).Sum();
            var wet = new World(Foggy(", 'tries': 64"), 256, 256, 3, "grass");
            for (int y = 0; y < 256; y++)
                for (int x = 60; x < 70; x++)
                {
                    wet.Paint(x, y, "pit");
                    wet.AddWater(x, y, 4);
                }
            Run(wet, 240);
            var dry = new World(Foggy(", 'tries': 64"), 256, 256, 3, "grass");
            Run(dry, 240);
            Assert.That(Near(wet), Is.GreaterThan(Near(dry) * 2), "the river draws the patches");

            var land = new int[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++) land[y * 256 + x] = (x + y) * 4;
            var hill = new World(Foggy(", 'patches': 1, 'tries': 64"), 256, 256, 3, "grass");
            hill.SetLand(land);
            Run(hill, 240);
            double Mean(World w) => w.Cells.Select((c, i) => c.Gas == "fog" ? i % 256 + i / 256 : -1).Where(v => v >= 0).Average();
            Assert.That(Mean(hill), Is.LessThan(Mean(dry)), "fog sits lower on a slope");
        }

        [Test]
        public void FogPatchesDriftALittleWithTheStretchesBreeze()
        {
            var w = new World(Foggy(", 'patches': 1, 'radiusMin': 6, 'radiusMax': 6, 'driftTicks': 20"), 128, 128, 2, "grass");
            (double x, double y) Middle()
            {
                var at = Enumerable.Range(0, 128 * 128).Where(i => w.At(i % 128, i / 128).Gas == "fog").ToList();
                return (at.Average(i => i % 128), at.Average(i => i / 128));
            }
            Run(w, 200);
            var before = Middle();
            Run(w, 200);
            var after = Middle();
            double moved = Math.Abs(after.x - before.x) + Math.Abs(after.y - before.y);
            Assert.That(moved, Is.InRange(5.0, 15.0), $"10 cells' drift, {before} to {after}");
        }

        [Test]
        public void FogPatchesKeepOffInactiveChunks()
        {
            var w = Moor(Foggy(), 7);
            w.SetActive(1, 1, false);
            Run(w, 200);
            Assert.That(Enumerable.Range(0, 64 * 64).All(i => w.At(64 + i % 64, 64 + i / 64).Gas == null), "an inactive chunk is held as it is");
        }

        [Test]
        public void FogPatchBlocksMustBeAtLeastOneChunk()
        {
            var e = Assert.Throws<ModException>(() => With("'fog': { 'regionChunks': 0 }"));
            Assert.That(e.Errors, Is.EqualTo(new[] { "sim weather.fog.regionChunks must be at least 1" }));
        }

        [Test]
        public void GoldenFogVeilsTheFieldAndLiftsMatchesTs()
        {
            // The same as "fog weather veils the field..." in weather.test.ts.
            var foggy = With(Quick + ", 'weights': { 'clear': 0, 'cloudy': 0, 'windy': 0, 'fog': 1, 'rain': 0, 'storm': 0, 'snow': 0 }");
            var clear = With(Quick + ", 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 }, 'meteors': { 'showerChance': 0 }");
            var w = Field(foggy, 4);
            int FogUnits() => w.Cells.Where(c => c.Gas == "fog").Sum(c => c.GasAmt);
            Assert.That(w.LineOfSight(1, 6, 22, 6), "clear across the field before");
            Run(w, 120);
            int veiled = FogUnits();
            Assert.That(veiled, Is.GreaterThan(80), "fog lies over the field");
            Assert.That(w.Cells.All(c => c.Gas != "fog" || c.GasAmt < 2), "thin: no square of it is thick on its own");
            Assert.That(w.FogBetween(1, 6, 22, 6), "but the far side is hidden");
            Assert.That($"{w.CellHash()} {w.RngState} {veiled}", Is.EqualTo("4237fa52 2846909919 203"));

            w.ApplyRules(clear);
            Run(w, 400);
            Assert.That(FogUnits(), Is.LessThan(veiled / 10), "the fog lifts");
            Assert.That(w.FogBetween(1, 6, 22, 6), Is.False, "and the far side shows again");
            Assert.That($"{w.CellHash()} {w.RngState} {FogUnits()}", Is.EqualTo("b0b71789 815874840 0"));
        }

        [Test]
        public void SnowSlowsWalkersFrostGlazesItAndItsMeltFreezesToIce()
        {
            // The same as "snow slows walkers..." in weather.test.ts.
            Assert.That(R.Covers[World.SnowCover].Speed, Is.LessThan(1));
            Assert.That(R.Covers[World.BankCover].Speed, Is.LessThan(R.Covers[World.SnowCover].Speed), "a bank slows more");
            var w = Field(R, 9);
            var c = w.At(18, 3);
            c.Cover = World.SnowCover;
            w.Apply(18, 3, "cold", 4, -1, false);
            Assert.That(c.Cover, Is.EqualTo("frost"), "Frost glazes the snow");
            Assert.That(w.IsSlippery(c), "slippery underfoot");
            var d = w.At(18, 5);
            d.Cover = World.SnowCover;
            w.Apply(18, 5, "fire", 1, -1, false);
            Assert.That(d.Water, Is.EqualTo(1), "fire melts the snow to a puddle");
            w.Apply(18, 5, "cold", 4, -1, false);
            Assert.That(d.Frozen && w.IsSlippery(d), "which Frost freezes into an ice slide");
        }

        [Test]
        public void FireMeltsSnowIntoWaterInsteadOfLightingTheGrassUnderIt()
        {
            var w = Field(R, 9);
            var c = w.At(18, 3);
            c.Cover = World.BankCover;
            w.Apply(18, 3, "fire", 1, -1, false);
            Assert.That(c.Cover, Is.EqualTo(World.SnowCover), "a bank slumps to snow");
            Assert.That(c.Fire, Is.EqualTo(0));
            w.Apply(18, 3, "fire", 1, -1, false);
            Assert.That(c.Cover, Is.Null, "then melts");
            Assert.That(c.Water, Is.EqualTo(2), "into water");
            Assert.That(c.Fire, Is.EqualTo(0), "and the grass doesn't catch");
        }

        [Test]
        public void SnowSlowsWalkersAndFrostGlazesItSlippery()
        {
            // World.WalkSpeed: what raiders are snagged by and the wizard slowed by (SurfaceEffects), cover and ground together.
            var w = new World(R, 8, 3, 1, "grass");
            Assert.That(w.WalkSpeed(w.At(0, 0)), Is.EqualTo(1));
            w.At(1, 0).Cover = "snow";
            w.At(2, 0).Cover = "snowbank";
            w.At(3, 0).T = "mud";
            w.At(3, 0).Cover = "snow";
            Assert.That(w.WalkSpeed(w.At(1, 0)), Is.EqualTo(0.85));
            Assert.That(w.WalkSpeed(w.At(2, 0)), Is.EqualTo(0.7));
            Assert.That(w.WalkSpeed(w.At(3, 0)), Is.EqualTo(0.5), "the slower of the snow and the mud under it");
            // Frost glazes the snow: slippery, and no longer slow, so a runner keeps its speed and slides.
            w.At(1, 1).Cover = "snow";
            w.Apply(1, 1, "cold", power: 4);
            Assert.That(w.At(1, 1).Cover, Is.EqualTo("frost"));
            Assert.That(w.IsSlippery(w.At(1, 1)), Is.True);
            Assert.That(w.WalkSpeed(w.At(1, 1)), Is.EqualTo(1));
        }
    }
}
