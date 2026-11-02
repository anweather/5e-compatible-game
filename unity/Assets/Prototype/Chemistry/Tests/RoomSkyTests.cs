using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>The clock and the weather as room commands (World.RoomSky.cs): every peer does them at the same tick, a late joiner too.</summary>
    public class RoomSkyTests
    {
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());
        static Rules R => ChemistryTests.R;

        static World Room(ChangeMode mode = ChangeMode.Open, string host = "Ana")
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(mode, host, R, Budgets);
            return w;
        }

        static string State(World w) => $"{w.CellHash()} {w.SkyState()} {w.CastHash()} {w.RngState}";

        // A run of commands by tick, the same for every peer.
        static Dictionary<int, TickBatch> Batches(int upTo)
        {
            var batches = TickBatch.Timeline(null, null, upTo);
            TickBatch.AddRoom(batches, new[]
            {
                (20, RoomCommand.SetWeather("Bo", WeatherKind.Rain, 1)),
                (40, RoomCommand.SetTime("Ana", 21 * 60)),
                (60, RoomCommand.SetTime("Bo", 12 * 60)),
                (500, RoomCommand.SetWeather("Ana", WeatherKind.Snow, 2)),
                (900, RoomCommand.SetWeather("Bo", null)),
            });
            return batches;
        }

        static void Run(World w, Dictionary<int, TickBatch> batches, int from, int to)
        {
            var rules = w.RoomRules;
            for (int t = from; t <= to; t++) rules = batches[t].RunOn(w, rules, out _);
        }

        [Test]
        public void TwoPeersAndALateJoinerAgree()
        {
            var a = Room();
            var b = Room();
            var batches = Batches(1000);
            for (int t = 1; t <= 1000; t++)
            {
                Run(a, batches, t, t);
                Run(b, batches, t, t);
                if (t % 50 == 0) Assert.That(State(a), Is.EqualTo(State(b)), $"tick {t}");
            }
            var joiner = Room();
            Run(joiner, Batches(1000), 1, 1000);
            Assert.That(State(joiner), Is.EqualTo(State(a)));
            Assert.That(a.SkyHash(), Is.EqualTo(joiner.SkyHash()));
        }

        // The golden for the new commands. Worlds that get none hash as they did (TerraformTests, ChemistryTests).
        [Test]
        public void TheCommandsGolden()
        {
            var w = Room();
            Run(w, Batches(1000), 1, 1000);
            Assert.That(w.CellHash(), Is.EqualTo("b03cfb2f"));
            Assert.That(w.SkyState(), Is.EqualTo("9661/-/-"));
        }

        [Test]
        public void TheClockLandsOnTheMinuteAndOnlyGoesForward()
        {
            var w = Room();
            foreach (int minute in new[] { 9 * 60, 8 * 60, 0, 23 * 60 + 59, 12 * 60 })
            {
                long before = w.ClockTick;
                Assert.That(w.ApplyRoom(RoomCommand.SetTime("Ana", minute)).Done);
                Assert.That(w.ClockTick, Is.GreaterThanOrEqualTo(before));
                Assert.That(Sky.MinuteOfDay(w.Minutes), Is.EqualTo(minute));
                Assert.That(w.ClockTick - before, Is.LessThan(Sky.Ticks(R.Sim, R.Sim.Day.LengthMinutes)), "never more than a day");
            }
        }

        [Test]
        public void TheClockLeavesTheCellsToTheTick()
        {
            // Setting noon at noon changes nothing the checksum sees but the log.
            var w = Room();
            w.ApplyRoom(RoomCommand.SetTime("Ana", 12 * 60));
            string sky = w.SkyState();
            w.ApplyRoom(RoomCommand.SetTime("Ana", 12 * 60));
            Assert.That(w.SkyState(), Is.EqualTo(sky));
        }

        [Test]
        public void AHeldWeatherIsTheWeatherAndEnds()
        {
            var w = Room();
            Assert.That(w.ApplyRoom(RoomCommand.SetWeather("Ana", WeatherKind.Storm, 1)).Done);
            w.Step();
            Assert.That(w.Weather.Kind, Is.EqualTo(WeatherKind.Storm));
            int ticks = Sky.Ticks(R.Sim, 1);
            for (int i = 0; i < ticks + 1; i++) w.Step();
            Assert.That(w.WeatherSet, Is.Null);
        }

        [Test]
        public void TheModeDecidesAndARefusalChangesNothing()
        {
            var w = Room(ChangeMode.HostOnly);
            string before = State(w);
            var v = w.ApplyRoom(RoomCommand.SetTime("Bo", 0));
            Assert.That(v.Done, Is.False);
            Assert.That(v.Line, Is.EqualTo("Set time refused: only the host can change this room"));
            Assert.That(w.ApplyRoom(RoomCommand.SetWeather("Bo", WeatherKind.Rain, 1)).Line, Is.EqualTo("Set weather refused: only the host can change this room"));
            Assert.That(w.ApplyRoom(RoomCommand.SetWeather("Bo", null)).Done, Is.False);
            Assert.That(State(w), Is.EqualTo(before));

            var locked = Room(ChangeMode.Locked);
            Assert.That(locked.ApplyRoom(RoomCommand.SetTime("Ana", 0)).Done, Is.False);
        }

        [TestCase("1440")]
        [TestCase("-1")]
        [TestCase("noon")]
        [TestCase("")]
        public void AHostileTimeIsRefusedTheSameEverywhere(string minute)
        {
            var w = Room();
            var v = w.ApplyRoom(new RoomCommand(RoomChange.SetTime, "Bo", "time", minute));
            Assert.That(v.Line, Is.EqualTo("Set time refused: that isn't a time of day"));
            Assert.That(w.ClockAhead, Is.EqualTo(0));
        }

        [TestCase("rain:0")]
        [TestCase("rain:61")]
        [TestCase("plague:5")]
        [TestCase("rain")]
        [TestCase("rain:5:5")]
        [TestCase("")]
        public void AHostileWeatherIsRefusedTheSameEverywhere(string asked)
        {
            var w = Room();
            var v = w.ApplyRoom(new RoomCommand(RoomChange.SetWeather, "Bo", "weather", asked));
            Assert.That(v.Line, Is.EqualTo("Set weather refused: that isn't a weather and a time to hold it"));
            Assert.That(w.WeatherSet, Is.Null);
        }

        [Test]
        public void TheCommandsSurviveASave()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.SetTime("Ana", 21 * 60));
            w.ApplyRoom(RoomCommand.SetWeather("Ana", WeatherKind.Fog, 3));
            var back = World.Load(w.Save(new ComboTests.Blank()), R, new ComboTests.Blank(), Budgets);
            Assert.That(back.SkyState(), Is.EqualTo(w.SkyState()));
            Assert.That(back.Minutes, Is.EqualTo(w.Minutes));
        }

        [Test]
        public void AsNamesThePeerAndKeepsTheCommand()
        {
            var c = RoomCommand.SetWeather("Bo", WeatherKind.Rain, 3).As("Player 1");
            Assert.That(c.Peer, Is.EqualTo("Player 1"));
            Assert.That(c.Change, Is.EqualTo(RoomChange.SetWeather));
            Assert.That(c.Json, Is.EqualTo("rain:3"));
        }
    }
}
