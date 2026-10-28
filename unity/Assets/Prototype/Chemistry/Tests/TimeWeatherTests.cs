using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The Time &amp; Weather spells (World.Time.cs, rules/spells.json): Hasten Time runs the day clock
    /// faster, Control Weather holds a weather, Fog Cloud lays fog that hides what's behind it.
    /// Mirrors sandbox/test/time.test.ts; the golden hash comes from running the same world there.
    /// </summary>
    public class TimeWeatherTests
    {
        static Rules R => ChemistryTests.R;

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        /// <summary>A dirt field walled in bedrock.</summary>
        static World Field(int w = 32, int h = 24)
        {
            var world = new World(R, w, h, 7);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) world.At(x, y).T = "bedrock";
            return world;
        }

        static int FogUnits(World w) => w.Cells.Where(c => c.Gas == "fog").Sum(c => c.GasAmt);

        // ---------- Hasten Time ----------

        [Test]
        public void HastenTimeRunsTheClockTwentyTimesAsFastForHalfAMinuteThenAtItsOwnPace()
        {
            var w = Field();
            long start = w.Minutes;
            w.Cast("hasten-time", 5, 5, 1);
            Assert.That(w.Haste, Is.EqualTo(((int, int)?)(20, 180)));
            Run(w, 180);
            // 180 ticks, each 20 ticks of day: 3600 ticks of a 8640-tick day, ten hours.
            Assert.That(w.Minutes - start, Is.EqualTo(600), "ten game hours in 30 s");
            Assert.That(w.Haste, Is.Null, "it wears off");
            Assert.That(w.ClockAhead, Is.EqualTo(180 * 19));
            long then = w.Minutes;
            Run(w, 60);
            Assert.That(w.Minutes - then, Is.EqualTo(10), "back to a game minute a second");
            Assert.That(w.Log.Any(l => l.Text == "Time slows back to its own pace"));
        }

        [Test]
        public void AHeldHastenTimeRunsLonger()
        {
            var w = Field();
            w.Cast("hasten-time", 5, 5, 1, 3);
            Assert.That(w.Haste, Is.EqualTo(((int, int)?)(20, 360)), "half again a level: twice as long at charge 3");
            long start = w.Minutes;
            Run(w, 360);
            Assert.That(w.Minutes - start, Is.EqualTo(1200), "twenty game hours");
        }

        [Test]
        public void CastingHastenTimeAgainWhileItRunsSettlesTheClock()
        {
            var w = Field();
            w.Cast("hasten-time", 5, 5, 1);
            Run(w, 30);
            w.Cast("hasten-time", 5, 5, 1);
            Assert.That(w.Haste, Is.Null);
            Assert.That(w.ClockAhead, Is.EqualTo(30 * 19), "the hours gained stay");
            long then = w.Minutes;
            Run(w, 6);
            Assert.That(w.Minutes - then, Is.EqualTo(1));
        }

        [Test]
        public void TheTimeSpellsTouchNoSquares()
        {
            var w = Field();
            Assert.That(w.SpellCells(R.Spell("hasten-time"), 5, 5, 1), Is.Empty);
            Assert.That(w.SpellCells(R.Spell("control-weather"), 5, 5, 1), Is.Empty);
            string before = w.CellHash();
            w.Cast("hasten-time", 5, 5, 1);
            w.Cast("control-weather", 5, 5, 1);
            Assert.That(w.CellHash(), Is.EqualTo(before));
        }

        // ---------- Control Weather ----------

        [Test]
        public void ControlWeatherStepsThroughClearRainSnowStormAndHoldsForTwoMinutes()
        {
            var w = Field();
            Run(w, 1);
            Assert.That(w.Weather.Kind, Is.EqualTo(WeatherKind.Clear), "the world starts calm");
            var seen = new System.Collections.Generic.List<WeatherKind>();
            for (int i = 0; i < 5; i++)
            {
                w.Cast("control-weather", 5, 5, 1);
                Run(w, 200);
                seen.Add(w.Weather.Kind);
            }
            Assert.That(seen, Is.EqualTo(new[] { WeatherKind.Rain, WeatherKind.Snow, WeatherKind.Storm, WeatherKind.Clear, WeatherKind.Rain }));
            Assert.That(w.WeatherSet.Value.until - w.WeatherSet.Value.from, Is.EqualTo(720), "two minutes at 6 ticks a second");
        }

        [Test]
        public void HeldWeatherRampsInRainsEvenInTheCalmAndThenTheNaturalWeatherResumes()
        {
            var w = Field();
            w.Cast("control-weather", 5, 5, 1);
            Run(w, 1);
            Assert.That(w.Weather.Kind, Is.EqualTo(WeatherKind.Rain));
            Assert.That(w.Weather.Strength, Is.LessThan(5), "it ramps in");
            Run(w, 200);
            Assert.That(w.Weather.Rain, Is.EqualTo(100));
            Run(w, 400);
            Assert.That(w.Cells.Count(c => c.Water > 0), Is.GreaterThan(0), "the rain fills squares though the world is still calm");
            Run(w, 120);
            Assert.That(w.WeatherSet, Is.Null, "720 ticks after the cast it lets go");
            var natural = Sky.Weather(R.Sim, w.Seed, w.Tick);
            Assert.That((w.Weather.Kind, w.Weather.Strength), Is.EqualTo((natural.Kind, natural.Strength)));
            Assert.That(w.Log.Any(l => l.Text == "The weather goes its own way again"));
        }

        [Test]
        public void AHeldStormBlowsAndAHeldControlWeatherLastsLonger()
        {
            var w = Field();
            for (int i = 0; i < 3; i++) w.Cast("control-weather", 5, 5, 1, i == 2 ? 3 : 1);
            Assert.That(w.WeatherSet.Value.kind, Is.EqualTo(WeatherKind.Storm));
            Assert.That(w.WeatherSet.Value.until - w.WeatherSet.Value.from, Is.EqualTo(1440), "charge 3: twice as long");
            Run(w, 200);
            Assert.That(w.Weather.WindDir, Is.InRange(0, 3), "a storm brings wind");
            Assert.That(w.WindDir, Is.EqualTo(w.Weather.WindDir));
        }

        // ---------- Fog Cloud ----------

        [Test]
        public void FogCloudLaysThickFogThatHidesWhatsBehindIt()
        {
            var w = Field();
            Assert.That(w.LineOfSight(3, 10, 20, 10), Is.True);
            w.Cast("fog-cloud", 11, 10, 1);
            Assert.That(w.At(11, 10).Gas, Is.EqualTo("fog"));
            Assert.That(w.At(11, 10).GasAmt, Is.EqualTo(6));
            Assert.That(w.Cells.Count(c => c.Gas == "fog"), Is.EqualTo(81), "radius 4: 9 by 9 squares");
            Assert.That(w.LineOfSight(3, 10, 20, 10), Is.False, "fog hides the far side");
            Assert.That(w.FogBetween(3, 10, 20, 10), Is.True);
            Assert.That(w.LineOfSight(3, 3, 20, 3), Is.True, "a line past the fog is clear");
            Assert.That(w.LineOfSight(7, 10, 8, 10), Is.True, "the ends don't count: fog next to you doesn't hide itself");
            w.At(9, 10).GasAmt = 1;
            Assert.That(w.Fogged(w.At(9, 10)), Is.False, "a wisp (under blocksSightAt) doesn't hide anything");
        }

        [Test]
        public void FogLingersThenThinsAway()
        {
            var w = Field();
            w.Cast("fog-cloud", 11, 10, 1);
            int start = FogUnits(w);
            Run(w, 60);
            Assert.That(FogUnits(w), Is.GreaterThan(start * 3 / 5), "ten seconds on most of it is there (smoke's decay would have halved it)");
            Run(w, 1200);
            Assert.That(FogUnits(w), Is.EqualTo(0), "gone in a few minutes");
        }

        [Test]
        public void FireBurnsTheFogOff()
        {
            var w = Field();
            w.Cast("fog-cloud", 11, 10, 1);
            w.Cast("fireball", 11, 10, 1);
            Assert.That(w.At(11, 10).Gas, Is.Null);
            Assert.That(w.At(13, 12).Gas, Is.Null, "the whole blast");
            Assert.That(w.At(15, 10).Gas, Is.EqualTo("fog"), "outside it the fog stays");
            Assert.That(w.Log.Any(l => l.Text == "The heat burns the fog away"));
        }

        [Test]
        public void GustOfWindBlowsTheFogAway()
        {
            var w = Field();
            w.Cast("fog-cloud", 8, 10, 1);
            double before = MeanX(w);
            w.Cast("gust-of-wind", 2, 10, 1);
            Run(w, 18);
            double after = MeanX(w);
            Assert.That(after - before, Is.GreaterThan(3), $"the fog's middle moved from x {before:0.0} to {after:0.0}");
        }

        static double MeanX(World w)
        {
            double sum = 0, n = 0;
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++)
                {
                    var c = w.At(x, y);
                    if (c.Gas != "fog") continue;
                    sum += x * c.GasAmt;
                    n += c.GasAmt;
                }
            return n > 0 ? sum / n : 0;
        }

        // ---------- lockstep ----------

        /// <summary>All three spells and their chemistry, on the sandbox's field, stepped 300 ticks.</summary>
        static World Scenario()
        {
            var w = Field();
            w.Cast("hasten-time", 5, 5, 1, 2);
            w.Cast("control-weather", 5, 5, 1);
            w.Cast("fog-cloud", 11, 10, 1);
            Run(w, 40);
            w.Cast("control-weather", 5, 5, 1);
            w.Cast("fire-bolt", 12, 10, 1);
            w.Cast("gust-of-wind", 3, 12, 1);
            Run(w, 260);
            return w;
        }

        [Test]
        public void TheTimeSpellsAreDeterministicAndMatchTheSandbox()
        {
            var a = Scenario();
            var b = Scenario();
            Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()));
            Assert.That(a.SkyState(), Is.EqualTo(b.SkyState()));
            Assert.That(a.SkyHash(), Is.EqualTo(b.SkyHash()));
            // Golden from sandbox/test/time.test.ts (the same world there).
            Assert.That(a.SkyState(), Is.EqualTo("5130/-/6:40:760"));
            Assert.That(a.CellHash(), Is.EqualTo("a754b5cb")); // night (day.nightFreeze) since 2026-10-27
        }

        [Test]
        public void TheNewKeysAreChecked()
        {
            var e = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(
                "{ 'spells': [ { 'id': 'slow', 'name': 'Slow', 'level': 3, 'shape': 'point', 'effect': 'stop-time', 'rate': -1 } ], " +
                "'gases': { 'mist': { 'name': 'Mist', 'decay': -0.1, 'blocksSightAt': 'thick' } } }") }));
            Assert.That(e.Errors, Is.EquivalentTo(new[]
            {
                "spell slow: unknown effect stop-time",
                "gas mist: decay must be at least 0",
                "gas mist: blocksSightAt must be a number",
                "spell slow: rate must be at least 0",
            }));
        }

        [Test]
        public void AViewThatStepsSeveralTicksAFrameStillSeesEveryStrike()
        {
            var w = ChemistryTests.Room(40, 30, 5);
            w.WeatherSet = (WeatherKind.Storm, 0, 100000);
            int counted = 0, from = w.Tick;
            for (int t = 0; t < 400; t++)
            {
                w.Step();
                counted += w.Strikes.Count; // what a view stepping one tick a frame sees
            }
            Assert.That(counted, Is.GreaterThan(0), "a storm strikes");
            var seen = w.StrikesSince(from).ToList();   // what a view that stepped all 400 in one frame reads
            Assert.That(seen.Count, Is.EqualTo(counted));
            Assert.That(seen.Select(s => s.tick), Is.Ordered);
            Assert.That(w.StrikesSince(w.Tick), Is.Empty);
            for (int t = 0; t < 700; t++) w.Step();
            Assert.That(w.StrikesSince(from).All(s => s.tick > w.Tick - 600), "only the last 600 ticks are kept");
        }
    }
}
