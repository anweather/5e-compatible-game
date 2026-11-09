using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Player stats (PlayerStats.cs, docs/RULES_MCP.md "Player stats"): everyone's and a slot's own in sim player, a mod
    /// stacking and taking them back, the bounds in budgets.json; and a tick trigger, the shower (Rules.Triggers.cs),
    /// with ShowerTriggerGolden.
    /// </summary>
    [Category("Simulation")]
    public class PlayerStatsTests
    {
        static Rules R => ChemistryTests.R;
        static readonly ModBudgets Budgets = ModBudgets.Load(ChemistryTests.RulesDir());

        static Rules With(params string[] mods) => R.WithMods(mods.Select(m => ModPatch.Parse(m.Replace('\'', '"'))));

        static string Problems(string mod) => string.Join("\n", ModValidator.Validate(R, Budgets, mod.Replace('\'', '"')).Select(p => p.Message));

        [Test]
        public void EveryPlayerIsAsMadeInTheBaseRules()
        {
            var p = R.Sim.Player;
            Assert.That(p.All.IsNormal, Is.True);
            Assert.That(p.Of(3).Same(PlayerStats.Normal), Is.True, "a slot with nothing of its own has everyone's");
            Assert.That((p.All.Speed, p.All.Size, p.All.Jump, p.All.Jumps), Is.EqualTo((1.0, 1.0, 1.0, 2)));
            Assert.That(p.Lines(), Is.Empty);
        }

        [Test]
        public void ASlotsOwnStatsStandOverEveryonesFieldByField()
        {
            var p = With("{ 'sim': { 'player': { 'speed': 2, 'by': { '1': { 'jumps': 4 } } } } }").Sim.Player;
            Assert.That((p.Of(0).Speed, p.Of(0).Jumps), Is.EqualTo((2.0, 2)), "slot 0 has everyone's");
            Assert.That((p.Of(1).Speed, p.Of(1).Jumps), Is.EqualTo((2.0, 4)), "slot 1 its own jumps over everyone's speed");
            Assert.That(p.Slots, Is.EqualTo(new[] { 1 }));
            Assert.That(p.Lines(), Is.EqualTo(new[] { "Every player: speed 2x", "The player in slot 1: speed 2x, jumps 4" }));
        }

        [Test]
        public void ALaterModStacksAndANullGivesASlotBackToEveryones()
        {
            const string quadruple = "{ 'sim': { 'player': { 'by': { '0': { 'jumps': 4, 'size': 1.5 } } } } }";
            Assert.That(With(quadruple).Sim.Player.Of(0).Jumps, Is.EqualTo(4));
            var p = With(quadruple, "{ 'sim': { 'player': { 'by': { '0': { 'jumps': null } } } } }").Sim.Player;
            Assert.That((p.Of(0).Jumps, p.Of(0).Size), Is.EqualTo((2, 1.5)), "the null takes back only the jumps");
            Assert.That(R.Sim.Player.Of(0).Jumps, Is.EqualTo(2), "the base rules are untouched");
        }

        [Test]
        public void TheBoundsHoldForEveryoneAndForASlot()
        {
            Assert.That(Problems("{ 'sim': { 'player': { 'speed': 3, 'size': 0.5, 'jump': 4, 'jumps': 8, 'by': { '2': { 'speed': 0.5, 'jumps': 1 } } } } }"), Is.Empty);
            StringAssert.Contains("sim player.speed is 10; a mod may set it from 0.5 to 3", Problems("{ 'sim': { 'player': { 'speed': 10 } } }"));
            StringAssert.Contains("sim player.by.1.jumps is 20; a mod may set it from 1 to 8", Problems("{ 'sim': { 'player': { 'by': { '1': { 'jumps': 20 } } } } }"));
            StringAssert.Contains("sim player.by.1.size is 9", Problems("{ 'sim': { 'player': { 'by': { '1': { 'size': 9 } } } } }"));
            StringAssert.Contains("jumps is how many jumps before landing, a whole number", Problems("{ 'sim': { 'player': { 'jumps': 2.5 } } }"));
            StringAssert.Contains("isn't a player's slot", Problems("{ 'sim': { 'player': { 'by': { 'me': { 'jumps': 3 } } } } }"));
            StringAssert.Contains("has no stat fly; a player has speed, size, jump, jumps", Problems("{ 'sim': { 'player': { 'by': { '0': { 'fly': 1 } } } } }"));
        }

        [Test]
        public void StatsAreNeverAtOrBelowNothing()
        {
            var p = With("{ 'sim': { 'player': { 'speed': 0, 'size': 0, 'jumps': 0 } } }").Sim.Player.All;
            Assert.That((p.Speed, p.Size, p.Jumps), Is.EqualTo((1.0, 1.0, 1)), "the view divides by none of them; the validator refuses them first");
            StringAssert.Contains("sim player.speed is 0; a mod may set it from 0.5 to 3", Problems("{ 'sim': { 'player': { 'speed': 0 } } }"));
        }

        // ---------- the shower: a tick trigger ----------

        const string Shower = "{ 'triggers': { 'meteor-shower': { 'on': 'tick', 'who': 'each', 'every': 12, 'do': [ { 'cast': 'meteor', 'at': 'around', 'within': 8, 'delay': 6 } ] } } }";

        static World Standing(Rules rules, params string[] players)
        {
            var w = new World(rules, 64, 64, 5, "grass");
            for (int i = 0; i < players.Length; i++) w.ApplyStrike(StrikeCommand.Stand(players[i], 20 + i * 20, 30, 12));
            return w;
        }

        [Test]
        public void AShowerFallsRoundEachStandingPlayerEveryTwelveTicks()
        {
            var w = Standing(With(Shower), "p0", "p1");
            for (int t = 0; t < 120; t++) w.Step();
            Assert.That(w.TriggerFirings, Is.EqualTo(20), "two players, a firing each every 12 ticks of 120");
            var casts = w.TriggeredAfter(0).ToList();
            Assert.That(casts.Select(c => c.Spell).Distinct(), Is.EqualTo(new[] { "meteor" }));
            Assert.That(casts.Where(c => c.Player == "p0").All(c => System.Math.Abs(c.X - 20) <= 8 && System.Math.Abs(c.Y - 30) <= 8), Is.True, "round p0");
            Assert.That(casts.Where(c => c.Player == "p1").All(c => System.Math.Abs(c.X - 40) <= 8), Is.True, "round p1");
            Assert.That(w.Log.Any(l => l.Text.Contains("Player 1's shower calls down Meteor")), Is.True);
        }

        [Test]
        public void NoOneStandingOrDownAndNoShowerFalls()
        {
            var empty = new World(With(Shower), 64, 64, 5, "grass");
            for (int t = 0; t < 30; t++) empty.Step();
            Assert.That(empty.TriggerFirings, Is.Zero);
            var down = Standing(With(Shower));
            down.ApplyStrike(StrikeCommand.Stand("p0", 20, 30, 0));
            for (int t = 0; t < 30; t++) down.Step();
            Assert.That(down.TriggerFirings, Is.Zero, "a downed player isn't showered");
        }

        [Test]
        public void AShowerHasAFloorOfTwelveTicks()
        {
            StringAssert.Contains("a tick trigger fires at most every 12 ticks", Problems(Shower.Replace("'every': 12", "'every': 3")));
            Assert.That(Problems(Shower), Is.Empty);
            var w = Standing(With(Shower.Replace("'every': 12, ", "")), "p0");
            for (int t = 0; t < 48; t++) w.Step();
            Assert.That(w.TriggerFirings, Is.EqualTo(4), "left out, every is the floor");
        }

        /// <summary>ShowerTriggerGolden: a meteor shower round two players for 240 ticks; CellHash and CastHash pinned, and two runs match.</summary>
        [Test]
        public void ShowerTriggerGolden()
        {
            World Run()
            {
                var w = Standing(With(Shower), "p0", "p1");
                for (int t = 0; t < 240; t++) w.Step();
                return w;
            }
            var a = Run();
            var b = Run();
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
            Assert.That(a.CellHash(), Is.EqualTo("1cb8f5dc"));
            Assert.That(a.CastHash(), Is.EqualTo("1b9c57f6"));
        }
    }
}
