using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What night does in the rules (World.Night.cs, World.Weather.cs): the daylight by the
    /// hour, the light fires shed, snow freezing faster and ice keeping at night, and the
    /// checks. The light golden is shared with sandbox/test/night.test.ts.
    /// </summary>
    public class NightTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>Runs the world's clock on to this hour of the first day (it starts at day.startHour).</summary>
        static void SetHour(World w, double hour)
        {
            long day = Sky.Ticks(R.Sim, R.Sim.Day.LengthMinutes);
            w.ClockAhead = (long)((hour - R.Sim.Day.StartHour) * day / 24);
        }

        [Test]
        public void TheDaylightFollowsTheClock()
        {
            var w = ChemistryTests.Room();
            (double hour, LightLevel light)[] day = { (9, LightLevel.Bright), (17.9, LightLevel.Bright), (18.5, LightLevel.Dim), (21, LightLevel.Dark), (27, LightLevel.Dark), (29.5, LightLevel.Dim), (31.5, LightLevel.Bright) };
            foreach (var (hour, light) in day)
            {
                SetHour(w, hour);
                Assert.That(w.Daylight, Is.EqualTo(light), $"at {hour}:00");
                Assert.That(w.IsNight, Is.EqualTo(light == LightLevel.Dark));
            }
            Assert.That(Sky.InHours(18, 8, 23 * 60) && Sky.InHours(18, 8, 7 * 60) && !Sky.InHours(18, 8, 12 * 60), "across midnight");
            Assert.That(Sky.InHours(6, 19, 6 * 60) && !Sky.InHours(6, 19, 19 * 60), "from the first hour up to the second");
        }

        /// <summary>A dark field with two fires, as night.test.ts builds it.</summary>
        static World FireField()
        {
            var w = ChemistryTests.Room(30, 20);
            SetHour(w, 23);
            w.At(6, 6).Fire = 3;
            w.At(20, 12).Fire = 1;
            return w;
        }

        [Test]
        public void FiresLightTheNightAndTheLightMatchesTheSandbox()
        {
            var w = FireField();
            Assert.That(w.LightAt(6, 6), Is.EqualTo(LightLevel.Bright));
            Assert.That(w.LightAt(10, 10), Is.EqualTo(LightLevel.Bright), "20 ft bright: four cells, diagonals too");
            Assert.That(w.LightAt(11, 6), Is.EqualTo(LightLevel.Dim));
            Assert.That(w.LightAt(14, 6), Is.EqualTo(LightLevel.Dim), "20 ft dim beyond");
            Assert.That(w.LightAt(15, 1), Is.EqualTo(LightLevel.Dark), "nine cells from one fire, eleven from the other");

            var map = new LightLevel[w.W * w.H];
            w.FillLight(0, 0, w.W, w.H, map);
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++) Assert.That(map[y * w.W + x], Is.EqualTo(w.LightAt(x, y)), $"({x}, {y})");
            uint h = 0x811c9dc5;
            foreach (var l in map) h = (h ^ (uint)('0' + (int)l)) * 0x01000193;
            Assert.That(h.ToString("x", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("966c8aa9"), "the same golden as night.test.ts");

            SetHour(w, 12);
            Assert.That(w.LightAt(15, 6), Is.EqualTo(LightLevel.Bright), "by day it's all bright");
            Assert.That(w.CellHash(), Is.EqualTo(FireField().CellHash()), "light is never kept: the cells don't change");
        }

        /// <summary>A field of frozen puddles under a clear sky held from the start, at an hour.</summary>
        static int IceLeft(double hour)
        {
            var w = ChemistryTests.Room(20, 12);
            SetHour(w, hour);
            for (int x = 3; x < 17; x += 2) { var c = w.At(x, 5); c.Water = 1; c.Frozen = true; }
            w.WeatherSet = (WeatherKind.Clear, 0, 100000);
            for (int t = 0; t < 1500; t++) w.Step();
            return Enumerable.Range(0, 20).Count(x => w.At(x, 5).Frozen);
        }

        [Test]
        public void IceKeepsThroughTheNightAndThawsByDay()
        {
            Assert.That(IceLeft(22), Is.EqualTo(7), "a clear night leaves the ice till morning");
            Assert.That(IceLeft(10), Is.LessThan(7), "a clear day thaws it");
        }

        [Test]
        public void TheHoursAreChecked()
        {
            var e = Assert.Throws<ModException>(() => R.WithMods(new[] { ModPatch.Parse(
                "{ 'sim': { 'day': { 'dawn': [5], 'dusk': [18, 25] } }, 'critters': [ { 'id': 'owl', 'name': 'Owl', 'glyph': 'o', 'color': '#ffffff', 'mode': 'emit', 'element': 'air', 'hours': 'night', 'blurb': '' } ] }"
                    .Replace('\'', '"')) }));
            Assert.That(e.Errors, Is.EquivalentTo(new[]
            {
                "critter owl: hours must be [from, to], hours from 0 to 24",
                "sim day.dawn must be [from, to], hours from 0 to 24",
                "sim day.dusk must be [from, to], hours from 0 to 24",
            }));
        }
    }
}
