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
            Assert.That($"{a.CellHash()} {a.RngState} {Strikes(a)}", Is.EqualTo("b4a67f55 4042791962 23"));
        }

        [Test]
        public void GoldenMixedQuickWeatherMatchesTs()
        {
            var w = Field(With(Quick), 8);
            Run(w, 1200);
            Assert.That($"{w.CellHash()} {w.RngState}", Is.EqualTo("6d146020 1611455719"));
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
            Assert.That($"{w.CellHash()} {w.RngState} {strikes}", Is.EqualTo("e9d48abc 2710019217 19"));
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
            Assert.That($"{w.CellHash()} {w.RngState} {frozen} {covered} {banks}", Is.EqualTo("e1e5624 4247680091 39 121 26"));

            w.ApplyRules(clear);
            Run(w, 150);
            int still = w.Cells.Count(c => c.Frozen && c.Water > 0);
            Assert.That(still, Is.LessThan(frozen / 2), "a dry spell thawed most of it");
            int left = w.Cells.Count(c => c.Cover == World.SnowCover || c.Cover == World.BankCover);
            Assert.That(left, Is.LessThan(20), "the snow melted");
            Assert.That($"{w.CellHash()} {w.RngState} {still} {left}", Is.EqualTo("b2481d88 2377318072 0 0"));
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
    }
}
