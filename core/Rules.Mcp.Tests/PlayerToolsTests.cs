using System.Linq;
using System.Reflection;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// What chat couldn't do at the 2026-11-08 playtest, as tools: a player's own stats (set_player), a species' speed
    /// ("rabbits 20 times faster"), a shower (add_trigger on tick, "constant meteors"), and a tall land stamp saying what
    /// it can rise ("a 100 m mountain").
    /// </summary>
    [Category("Simulation")]
    public class PlayerToolsTests
    {
        static Workbench Bench(int slot = 1) =>
            new Workbench(ChemistryTests.Folders) { Here = new StandInHere(new HereSpot("Player 2", 288, 288, slot: slot)) };

        static string DraftIn(string said) => said.Split('\n').First(l => l.StartsWith("Draft ", System.StringComparison.Ordinal)).Substring(6).TrimEnd(':');

        static Rules With(Workbench b, string draft) => b.InForce.WithMods(new[] { ModPatch.Parse(b.DraftOf(draft).Patch.ToString()) });

        static string Description(string tool) =>
            typeof(RulesTools).GetMethods().Where(m => m.GetCustomAttribute<RulesToolAttribute>()?.Name == tool).Select(m => m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description).Single();

        [Test]
        public void MakeMeFasterAndAQuadrupleJumpAreTheAskersOwn()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string said = tools.SetPlayer("speed", "2");
            string draft = DraftIn(said);
            StringAssert.Contains("the player in slot 1: moves at 2 times the wizard's speed", said);
            StringAssert.Contains("the player in slot 1: has 4 jumps before landing", tools.SetPlayer("jumps", "4", draft: draft));
            Assert.That(b.Validate(draft), Is.Empty);
            var p = With(b, draft).Sim.Player;
            Assert.That((p.Of(1).Speed, p.Of(1).Jumps), Is.EqualTo((2.0, 4)));
            Assert.That(p.Of(0).IsNormal, Is.True, "the host is as made");
            StringAssert.Contains("sim player.by.1.jumps", string.Join(",", b.DraftOf(draft).Changed));
        }

        [Test]
        public void EveryoneASlotAndNormal()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string draft = DraftIn(tools.SetPlayer("size", "0.5", who: "everyone"));
            tools.SetPlayer("jump", "3", who: "0", draft: draft);
            var p = With(b, draft).Sim.Player;
            Assert.That((p.Of(0).Size, p.Of(0).Jump, p.Of(5).Size, p.Of(5).Jump), Is.EqualTo((0.5, 3.0, 0.5, 1.0)));
            tools.SetPlayer("jump", "normal", who: "0", draft: draft);
            Assert.That(With(b, draft).Sim.Player.Of(0).Jump, Is.EqualTo(1.0), "slot 0's own jump is taken back to everyone's");
            tools.SetPlayer("size", "normal", who: "everyone", draft: draft);
            Assert.That(With(b, draft).Sim.Player.Of(5).Size, Is.EqualTo(1.0));
            Assert.That(b.Validate(draft), Is.Empty);
        }

        [Test]
        public void PastItsRangeIsCutAndSaid()
        {
            var tools = new RulesTools(Bench());
            string said = tools.SetPlayer("jumps", "100");
            StringAssert.Contains("100 is past what a player may have, so it's 8: jumps is 1 to 8. Tell the player.", said);
            StringAssert.Contains("so it's 3: speed is 0.5 to 3", tools.SetPlayer("speed", "20"));
            StringAssert.Contains("A player's stats are speed, size, jump, jumps", Assert.Throws<RulesToolException>(() => tools.SetPlayer("fly", "1")).Message);
            StringAssert.Contains("value is a number", Assert.Throws<RulesToolException>(() => tools.SetPlayer("size", "huge")).Message);
            StringAssert.Contains("who is me", Assert.Throws<RulesToolException>(() => tools.SetPlayer("size", "2", who: "Bob")).Message);
            var nobody = new RulesTools(new Workbench(ChemistryTests.Folders) { Here = new StandInHere(null) });
            StringAssert.Contains("No player is in the game", Assert.Throws<RulesToolException>(() => nobody.SetPlayer("size", "2")).Message);
        }

        [Test]
        public void ListPlayersSaysWhatEachHasAndTheRanges()
        {
            var tools = new RulesTools(Bench());
            StringAssert.Contains("Every player is as made", tools.List("players"));
            StringAssert.Contains("speed 0.5 to 3, size 0.5 to 2, jump 0.5 to 4, jumps 1 to 8", tools.List("players"));
        }

        [Test]
        public void TheRangesToolsStateAreTheBudgets()
        {
            var budgets = Bench().Budgets;
            string set = Description("set_player");
            foreach (var (stat, says) in new[] { ("speed", "walk and sprint, {0} to {1}"), ("size", "height and width, {0} to {1}"), ("jump", "every jump, {0} to {1}"), ("jumps", "before landing, {0} to {1}") })
            {
                var (path, min, max) = budgets.SimBounds.Single(x => x.path == "player." + stat);
                StringAssert.Contains(string.Format(System.Globalization.CultureInfo.InvariantCulture, says, min, max), set, path);
            }
            StringAssert.Contains($"0.2 to {budgets.Number("fauna.maxSpeed", -1)}", typeof(RulesTools).GetMethod("SetSpecies").GetParameters().Single(p => p.Name == "speed").GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description);
            double tallest = System.Math.Floor(budgets.Number("land.steepest", -1) * budgets.Number("land.maxRadius", -1));
            StringAssert.Contains($"the tallest one stamp rises is {tallest} m, at radius {budgets.Number("land.maxRadius", -1)}", typeof(RulesTools).GetMethod("Act").GetParameters().Single(p => p.Name == "rise").GetCustomAttribute<System.ComponentModel.DescriptionAttribute>().Description);
            StringAssert.Contains($"every {TriggerDef.MinTickEvery} or more ticks", Description("add_trigger"));
        }

        [Test]
        public void RabbitsTwentyTimesFasterAreFiveTimesAndSaySo()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string said = tools.SetSpecies("rabbit", speed: 20);
            StringAssert.Contains("20 times is past what an animal may have, so it's 5 times", said);
            string draft = DraftIn(said);
            var rabbit = With(b, draft).Fauna.Species("rabbit");
            Assert.That((rabbit.Walk, rabbit.Run), Is.EqualTo((4.0, 30.0)), "the base rules' 0.8 and 6, five times");
            Assert.That(b.Validate(draft), Is.Empty);
            tools.SetSpecies("rabbit", speed: 1, draft: draft);
            Assert.That(With(b, draft).Fauna.Species("rabbit").Run, Is.EqualTo(6.0), "1 puts it back, not five times again");
            StringAssert.Contains("built on", Assert.Throws<RulesToolException>(() => tools.SetSpecies("koi", speed: 2)).Message);
            StringAssert.Contains("Species rabbit runs at 300 m/s, more than 5 times its normal 6", string.Join("\n", ModValidator.Validate(b.Base, b.Budgets, "{\"fauna\": {\"rabbit\": {\"move\": {\"run\": 300}}}}").Select(p => p.Message)));
        }

        [Test]
        public void ConstantMeteorsAreAShowerOnTick()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string said = tools.AddTrigger("meteor-shower", "[{\"cast\": \"meteor\", \"at\": \"around\", \"within\": 12, \"delay\": 6}]", who: "each", on: "tick");
            StringAssert.Contains("meteor-shower fires round every player every 12 ticks (2 seconds; about 30 a minute each)", said);
            StringAssert.Contains("revert it to stop", said);
            string draft = DraftIn(said);
            Assert.That(b.Validate(draft), Is.Empty);
            StringAssert.Contains("a shower round every player, every 12 ticks: cast meteor within 12 cells", b.Changes(b.DraftOf(draft)));
            var (with, _) = b.SimulateRuns(draft, null, 120);
            Assert.That(with.Firings, Is.EqualTo(10), "the walker stands, and a meteor falls round it every 12 ticks");
            StringAssert.Contains("at least 12 ticks", Assert.Throws<RulesToolException>(() => tools.AddTrigger("t", "[{\"cast\": \"meteor\"}]", on: "tick", every: 6)).Message);
        }

        [Test]
        public void AHundredMetreMountainSaysTheTallestOneStampRises()
        {
            var b = new Workbench(ChemistryTests.Folders);
            var tools = new RulesTools(b);
            string said = tools.Act("land", near: "[120, 120]", radius: 14, rise: 100, profile: "cone");
            StringAssert.Contains("100 m is too steep for a radius of 14, so it rises 21 m", said);
            StringAssert.Contains("100 m needs radius 67, past the 32 a stamp may have, so the tallest one stamp rises is 48 m, at radius 32", said);
            StringAssert.Contains("radius 27 rises the 40 m asked", new RulesTools(new Workbench(ChemistryTests.Folders)).Act("land", near: "[120, 120]", radius: 10, rise: 40, profile: "cone"));
        }
    }
}
