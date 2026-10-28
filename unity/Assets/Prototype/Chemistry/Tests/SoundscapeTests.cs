using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What the world sounds like where the listener stands (Soundscapes.cs,
    /// world/soundscapes.json): woods by day and night, the river, the shore, the village and
    /// the inn in the evening, a loop in several at its loudest, and the table's checks.
    /// </summary>
    public class SoundscapeTests
    {
        static Rules R => ChemistryTests.R;

        static readonly Soundscapes Table = Soundscapes.Load(Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir()), "world"));

        /// <summary>Runs the clock on to this hour of the first day (NightTests.SetHour).</summary>
        static void SetHour(World w, double hour) =>
            w.ClockAhead = (long)((hour - R.Sim.Day.StartHour) * Sky.Ticks(R.Sim, R.Sim.Day.LengthMinutes) / 24);

        static string[] Sounds(World w, int x, int y, params VillagePlan[] villages) =>
            Table.At(w, villages, x, y).Select(l => l.Sound).ToArray();

        [Test]
        public void TheTableLoadsAndChecksOut()
        {
            Assert.That(Table.All.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(Table.Check(R), Is.Empty);
        }

        [Test]
        public void TheWoodsSingByDayAndHootAtNight()
        {
            var w = new World(R, 64, 64, 1, "tree");
            SetHour(w, 12);
            Assert.That(Sounds(w, 32, 32), Is.EqualTo(new[] { "birdsong", "leaves" }));
            SetHour(w, 23);
            Assert.That(Sounds(w, 32, 32), Is.EqualTo(new[] { "crickets", "leaves", "owl" }));
            Assert.That(Table.At(w, null, 32, 32).Single(l => l.Sound == "leaves").Volume, Is.EqualTo(0.25));
            var bare = new World(R, 64, 64, 1, "dirt");
            Assert.That(Sounds(bare, 32, 32), Is.Empty, "bare dirt: nothing in the table holds");
        }

        [Test]
        public void ALoopInSeveralPlaysOnceAtItsLoudest()
        {
            // Grass with a wood in it, at night: meadow-night's crickets (0.4) and woods-night's (0.45).
            var w = new World(R, 64, 64, 1, "grass");
            for (int y = 28; y < 36; y++)
                for (int x = 28; x < 36; x++) w.At(x, y).T = "tree";
            SetHour(w, 23);
            var crickets = Table.At(w, null, 32, 32).Where(l => l.Sound == "crickets").ToList();
            Assert.That(crickets.Count, Is.EqualTo(1));
            Assert.That(crickets[0].Volume, Is.EqualTo(0.45));
            Assert.That(Table.Matching(w, null, 32, 32), Is.EqualTo(new[] { "meadow-night", "woods-night" }));
        }

        [Test]
        public void WaterIsTheRiverTheLakesideOrTheShore()
        {
            var w = new World(R, 96, 64, 1, "dirt");
            SetHour(w, 12);
            for (int y = 0; y < 64; y++)
                for (int x = 10; x < 13; x++) w.At(x, y).T = "river";
            Assert.That(Sounds(w, 15, 32), Does.Contain("river"));
            Assert.That(Sounds(w, 40, 32), Does.Not.Contain("river"));
            for (int y = 0; y < 64; y++)
                for (int x = 60; x < 96; x++) w.At(x, y).Water = 12;
            Assert.That(Sounds(w, 58, 32), Is.SupersetOf(new[] { "gulls", "lapping-water", "surf" }), "a wide water: the shore");
            SetHour(w, 2);
            Assert.That(Sounds(w, 58, 32), Does.Contain("surf").And.Not.Contain("gulls"), "no gulls at night");
        }

        [Test]
        public void TheInnPlaysAFiddleInTheEvening()
        {
            var plan = VillageLayout.Lay(new VillageSpec { Name = "Test", X = 100, Y = 100, Seed = 7, Houses = 8 }, (x, y) => true);
            var inn = plan.Buildings.Single(b => b.Kind == BuildingKind.Inn).Footprint;
            var w = new World(R, 200, 200, 1, "dirt");
            int x0 = inn.X + inn.W / 2, y0 = inn.Bottom + 2;
            SetHour(w, 20);
            Assert.That(Sounds(w, x0, y0, plan), Does.Contain("fiddle").And.Contain("inn-chatter"));
            SetHour(w, 12);
            Assert.That(Sounds(w, x0, y0, plan), Does.Not.Contain("fiddle").And.Contain("village-day"));
            SetHour(w, 20);
            Assert.That(Sounds(w, 5, 5, plan), Is.Empty, "far from the village");
        }

        [Test]
        public void TheChecksNameWhatIsWrong()
        {
            var bad = Soundscapes.Parse(@"{ ""soundscapes"": [
                { ""id"": ""a"", ""near"": { ""terrain"": [""lava""] }, ""loops"": [{ ""sound"": ""hiss"" }] },
                { ""id"": ""a"", ""near"": { ""building"": ""castle"" }, ""hours"": [20, 30], ""loops"": [{ ""sound"": ""x"", ""volume"": 2 }] },
                { ""id"": ""b"", ""near"": { }, ""loops"": [] } ] }");
            Assert.That(bad.Check(R), Is.EquivalentTo(new[]
            {
                "soundscape a: unknown terrain lava",
                "soundscape a: duplicate id",
                "soundscape a: building must be one of house, inn, shop",
                "soundscape a: hours must be [from, to], hours from 0 to 24",
                "soundscape a: a loop needs a sound and a volume from 0 to 1",
                "soundscape b: near says nothing (terrain, tag, water, village or building)",
                "soundscape b: no loops",
            }));
            Assert.Throws<System.FormatException>(() => Soundscapes.Parse("not json"));
        }
    }
}
