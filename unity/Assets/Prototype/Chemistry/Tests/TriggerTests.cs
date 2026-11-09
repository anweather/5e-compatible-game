using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Triggers (docs/design/events-and-placing.md 2c, J-067): a step fires a trigger's cast or place, whose steps
    /// count, the floor between firings, the cap a tick, chance, TriggerText in the checksum, the save, the checks and
    /// the budgets, and StepTriggerGolden.
    /// </summary>
    [Category("Simulation")]
    public class TriggerTests
    {
        static Rules R => ChemistryTests.R;
        static readonly ModBudgets Budgets = ModBudgets.Load(ChemistryTests.RulesDir());

        static string Trigger(string who, string action, int every = 3, string extra = "") =>
            ("{ 'triggers': { 'meteor-steps': { 'on': 'step', 'who': '" + who + "', 'every': " + every + extra + ", 'do': [ " + action + " ] } } }").Replace('\'', '"');

        const string Meteor = "{ 'cast': 'meteor', 'at': 'ahead', 'by': 2, 'delay': 6 }";
        const string MeteorNow = "{ 'cast': 'meteor', 'at': 'ahead', 'by': 2 }";

        static Rules With(string mod) => R.WithMods(new[] { ModPatch.Parse(mod) });

        static World Meadow(Rules rules) => new World(rules, 64, 64, 5, "grass");

        // A walk: a first stand at tick 1, then a step every other tick (a run), east 20 cells, south 10, west 10.
        static List<(int tick, StrikeCommand command)> Walk(string player = "Ann", int steps = 40)
        {
            var walk = new List<(int, StrikeCommand)> { (1, StrikeCommand.Stand(player, 12, 20, 12)) };
            int x = 12, y = 20;
            for (int i = 0; i < steps; i++)
            {
                if (i < 20) x++;
                else if (i < 30) y++;
                else x--;
                walk.Add((2 + i * 2, StrikeCommand.Stand(player, x, y, 12)));
            }
            return walk;
        }

        static World Live(string mod, IEnumerable<(int tick, StrikeCommand command)> walk, int ticks)
        {
            var w = Meadow(R);
            var byTick = walk.ToLookup(s => s.tick, s => s.command);
            for (int t = 1; t <= ticks; t++)
            {
                if (t == 1 && mod != null) w.ApplyRules(With(mod));
                foreach (var c in byTick[t]) w.ApplyStrike(c);
                w.Step();
            }
            return w;
        }

        /// <summary>StepTriggerGolden (design section 6): a meteor-steps mod and a 40-step walk as Stand commands; CellHash and CastHash after 300 ticks pinned, and a Timeline replay matches cell for cell.</summary>
        [Test]
        public void StepTriggerGolden()
        {
            const int ticks = 300;
            string mod = Trigger("Ann", Meteor);
            var walk = Walk();
            var live = Live(mod, walk, ticks);

            // What a late joiner gets: the switch and the stands, each with its tick.
            var timeline = TickBatch.Timeline(null, new[] { new RuleChange(1, new[] { new ActiveMod("meteor-steps", mod) }) }, ticks);
            TickBatch.AddStrikes(timeline, walk);
            var joiner = Meadow(R);
            var rules = RuleSet.Plain(R);
            for (int t = 1; t <= ticks; t++) rules = timeline[t].RunOn(joiner, rules, out _);

            Assert.That(live.TriggerFirings, Is.EqualTo(20), "a step every 2 ticks and a floor of 3: every other step");
            Assert.That(joiner.CellHash(), Is.EqualTo(live.CellHash()), "replayed cell for cell");
            Assert.That(joiner.CastHash(), Is.EqualTo(live.CastHash()));
            Assert.That(live.CellHash(), Is.EqualTo("89460dbc"));
            Assert.That(live.CastHash(), Is.EqualTo("92a41e10"));
        }

        [Test]
        public void AMeteorLandsAheadOfTheStepAfterItsDelayUnderATargetMark()
        {
            var w = Meadow(With(Trigger("Ann", Meteor)));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 20, 20, 12));
            w.Step();
            w.ApplyStrike(StrikeCommand.Stand("Ann", 21, 20, 12));
            var cast = w.Triggered.Single();
            Assert.That((cast.Spell, cast.X, cast.Y, cast.Dir, cast.Lands - cast.Tick), Is.EqualTo(("meteor", 23, 20, 1, 6)));
            Assert.That(w.At(23, 20).Star, Is.GreaterThan(0), "a target mark glows where it will land");
            Assert.That(w.At(23, 20).Height, Is.EqualTo(0));
            for (int i = 0; i < 6; i++) w.Step();
            Assert.That(w.At(23, 20).Height, Is.LessThan(0), "the crater");
            Assert.That(w.At(23, 20).Star, Is.Zero);
            Assert.That(w.Triggered, Is.Empty, "this tick's list empties");
            Assert.That(w.TriggeredAfter(0).Select(t => t.N), Is.EqualTo(new[] { 1 }));
        }

        [TestCase("solo", "Player 1")]
        [TestCase("p1", "Player 2")]
        [TestCase("Ann", "Ann")]
        public void TheLogNamesThePlayerNotTheirStand(string stand, string named)
        {
            var w = Meadow(With(Trigger("each", MeteorNow)));
            w.ApplyStrike(StrikeCommand.Stand(stand, 20, 20, 12));
            w.ApplyStrike(StrikeCommand.Stand(stand, 20, 21, 12));
            Assert.That(w.Log.Select(l => l.Text), Has.Member(named + "'s step calls down Meteor"));
        }

        [Test]
        public void AMeteorWithNoDelayLandsAtOnce()
        {
            var w = Meadow(With(Trigger("Ann", MeteorNow)));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 20, 20, 12));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 20, 21, 12));
            Assert.That(w.At(20, 23).Height, Is.LessThan(0));
            Assert.That(w.Triggered.Single().Lands, Is.EqualTo(w.Triggered.Single().Tick));
        }

        [Test]
        public void OnlyTheNamedPlayersStepsCountAndEachCountsEveryones()
        {
            var w = Meadow(With(Trigger("Ann", MeteorNow)));
            w.ApplyStrike(StrikeCommand.Stand("Bo", 20, 20, 12));
            w.ApplyStrike(StrikeCommand.Stand("Bo", 21, 20, 12));
            Assert.That(w.TriggerFirings, Is.Zero);
            Assert.That(w.TriggerText(), Is.Empty, "nothing fired: the checksum is as it was");

            var each = Meadow(With(Trigger("each", MeteorNow)));
            foreach (var p in new[] { "Ann", "Bo" })
            {
                each.ApplyStrike(StrikeCommand.Stand(p, 20, p == "Ann" ? 10 : 40, 12));
                each.ApplyStrike(StrikeCommand.Stand(p, 21, p == "Ann" ? 10 : 40, 12));
            }
            Assert.That(each.TriggerFirings, Is.EqualTo(2));
        }

        [Test]
        public void AFirstStandAStandInPlaceAndADownedPlayerDontStep()
        {
            var w = Meadow(With(Trigger("Ann", MeteorNow)));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 20, 20, 12));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 20, 20, 9));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 20, 20, 0));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 21, 20, 0));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 22, 20, 5));
            Assert.That(w.TriggerFirings, Is.Zero, "first, in place, downed, getting up");
            w.ApplyStrike(StrikeCommand.Stand("Ann", 23, 20, 5));
            Assert.That(w.TriggerFirings, Is.EqualTo(1));
        }

        [Test]
        public void ATriggerWaitsItsTicksAndNeverUnderThree()
        {
            var w = Meadow(With(Trigger("Ann", MeteorNow, 5)));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 10, 30, 12));
            var fired = new List<int>();
            for (int i = 1; i <= 12; i++)
            {
                w.ApplyStrike(StrikeCommand.Stand("Ann", 10 + i, 30, 12));
                if (w.Triggered.Count > 0) fired.Add(w.Tick);
                w.Step();
            }
            Assert.That(fired, Is.EqualTo(new[] { 0, 5, 10 }));
            Assert.That(R.WithMods(new[] { ModPatch.Parse("{ \"triggers\": { \"t\": { \"on\": \"step\", \"who\": \"Ann\", \"do\": [ { \"cast\": \"meteor\" } ] } } }") }).Triggers.Single().Every,
                Is.EqualTo(TriggerDef.MinEvery), "3 when not given");
        }

        [Test]
        public void AtMostFourFireATickInARoom()
        {
            var w = Meadow(With(Trigger("each", "{ 'cast': 'fire-bolt', 'at': 'here' }")));
            var players = Enumerable.Range(0, 6).Select(i => "P" + i).ToList();
            foreach (var p in players) w.ApplyStrike(StrikeCommand.Stand(p, 5 + players.IndexOf(p) * 8, 30, 12));
            foreach (var p in players) w.ApplyStrike(StrikeCommand.Stand(p, 6 + players.IndexOf(p) * 8, 30, 12));
            Assert.That(w.Triggered.Select(t => t.Player), Is.EqualTo(new[] { "P0", "P1", "P2", "P3" }), "later ones that tick are dropped, in batch order");
            w.Step();
            w.ApplyStrike(StrikeCommand.Stand("P5", 46, 31, 12));
            Assert.That(w.Triggered.Single().Player, Is.EqualTo("P5"), "a new tick, a new four");
        }

        [Test]
        public void ChanceIsTheSameOnEveryPeerAndDrawsNothingFromTheWorld()
        {
            string mod = Trigger("Ann", "{ 'cast': 'fire-bolt', 'at': 'behind', 'by': 1 }", 3, ", 'chance': 0.5");
            var a = Live(mod, Walk(), 120);
            var b = Live(mod, Walk(), 120);
            var all = Live(Trigger("Ann", "{ 'cast': 'fire-bolt', 'at': 'behind', 'by': 1 }"), Walk(), 120);
            Assert.That(a.TriggerFirings, Is.EqualTo(b.TriggerFirings));
            Assert.That(a.CastHash(), Is.EqualTo(b.CastHash()));
            Assert.That(a.TriggerFirings, Is.GreaterThan(0).And.LessThan(all.TriggerFirings));
        }

        [Test]
        public void ATriggerPlacesAsASpellDoes()
        {
            var w = new World(With(Trigger("Ann", "{ 'place': { 'thing': 'barrel', 'count': 2, 'within': 2, 'lasts': 30 }, 'at': 'behind', 'by': 1 }")), 64, 64, 5, "dirt");
            w.ApplyStrike(StrikeCommand.Stand("Ann", 20, 20, 12));
            w.ApplyStrike(StrikeCommand.Stand("Ann", 21, 20, 12));
            Assert.That(w.Placed, Has.Count.EqualTo(2));
            Assert.That(w.Placed.All(t => System.Math.Abs(t.CellX - 20) <= 2 && System.Math.Abs(t.CellY - 20) <= 2));
            Assert.That(w.Triggered, Is.Empty, "a place is no cast to show");
        }

        [Test]
        public void TheCooldownsSaveAndALoadedWorldGoesOnTheSame()
        {
            string mod = Trigger("Ann", Meteor);
            var walk = Walk();
            var a = Live(mod, walk.Where(s => s.tick <= 31), 31);
            var source = new Grass();
            var b = World.Load(a.Save(source), With(mod), source);
            Assert.That(b.TriggerText(), Is.EqualTo(a.TriggerText()).And.Not.Empty);
            // Stands aren't in the save: each view sends its own again, in place, which is no step.
            var (x, y) = (a.Stands["Ann"].X, a.Stands["Ann"].Y);
            foreach (var w in new[] { a, b }) w.ApplyStrike(StrikeCommand.Stand("Ann", x, y, 12));
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
            foreach (var w in new[] { a, b })
                for (int t = 32; t <= 90; t++)
                {
                    foreach (var s in walk.Where(s => s.tick == t)) w.ApplyStrike(s.command);
                    w.Step();
                }
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
        }

        sealed class Grass : IChunkSource
        {
            public string Version => "test-grass";
            public void Make(int x, int y, Cell cell) => cell.T = "grass";
        }

        [Test]
        public void RulesWithoutTriggersFingerprintAsBefore()
        {
            var files = System.IO.Directory.GetFiles(ChemistryTests.RulesDir(), "*.json")
                .Where(p => System.IO.Path.GetFileName(p) != "triggers.json")
                .ToDictionary(p => System.IO.Path.GetFileName(p), System.IO.File.ReadAllText);
            Assert.That(Rules.FromJson(files).Fingerprint, Is.EqualTo(R.Fingerprint));
            Assert.That(With(Trigger("Ann", Meteor)).Fingerprint, Is.Not.EqualTo(R.Fingerprint), "a trigger is in it");
            Assert.That(R.Triggers, Is.Empty);
        }

        [Test]
        public void AModTakesATriggerOutWithNull()
        {
            var on = ModPatch.Parse(Trigger("Ann", Meteor));
            var off = ModPatch.Parse("{ \"triggers\": { \"meteor-steps\": null } }");
            Assert.That(R.WithMods(new[] { on }).TriggersOn(TriggerEvent.Step), Has.Count.EqualTo(1));
            Assert.That(R.WithMods(new[] { on, off }).Triggers, Is.Empty);
        }

        [Test]
        public void TheChecksRefuseABadTrigger()
        {
            string Errors(string mod)
            {
                try
                {
                    With(mod);
                    return "";
                }
                catch (ModException e)
                {
                    return string.Join("\n", e.Errors);
                }
            }
            Assert.That(Errors(Trigger("Ann", "{ 'cast': 'nope' }")), Does.Contain("action 1: unknown spell nope"));
            Assert.That(Errors(Trigger("Ann", Meteor, 2)), Does.Contain("every is the ticks between firings, a whole number from 3"));
            Assert.That(Errors(Trigger("", Meteor)), Does.Contain("who is a player's name"));
            Assert.That(Errors(Trigger("Ann", "{ 'cast': 'meteor', 'at': 'above' }")), Does.Contain("at is one of here, ahead, behind, around"));
            Assert.That(Errors(Trigger("Ann", "{ 'cast': 'meteor', 'place': { 'thing': 'barrel' } }")), Does.Contain("exactly one of cast"));
            Assert.That(Errors(Trigger("Ann", "{ 'place': { 'thing': 'nope' } }")), Does.Contain("thing nope is neither"));
            Assert.That(Errors(Trigger("Ann", Meteor, 3, ", 'chance': 2")), Does.Contain("chance is above 0"));
            Assert.That(Errors(Trigger("Ann", Meteor)), Is.Empty);
        }

        [Test]
        public void TheBudgetsCapAModsTriggers()
        {
            string Problems(string mod) => string.Join("\n", ModValidator.Validate(R, Budgets, mod).Select(p => p.Message));
            Assert.That(Problems(Trigger("Ann", Meteor)), Is.Empty);
            Assert.That(Problems(Trigger("Ann", "{ 'cast': 'control-weather' }")), Does.Contain("level 7 at most"));
            Assert.That(Problems(Trigger("Ann", MeteorNow + ", " + MeteorNow + ", " + MeteorNow)), Does.Contain("does 3 things; a trigger does at most 2"));
            Assert.That(Problems(Trigger("Ann", "{ 'place': { 'thing': 'barrel', 'count': 9 } }")), Does.Contain("Trigger meteor-steps places with a count of 9"));
            string five = "{ \"triggers\": {" + string.Join(",", Enumerable.Range(0, 5).Select(i => $"\"t{i}\": {{ \"on\": \"step\", \"who\": \"Ann\", \"do\": [ {{ \"cast\": \"meteor\" }} ] }}")) + "} }";
            Assert.That(Problems(five), Does.Contain("5 triggers; a mod may have at most 4"));
        }
    }
}
