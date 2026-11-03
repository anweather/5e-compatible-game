using System.Linq;
using Chem.Mcp;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>add_trigger and the walk in simulate (docs/design/events-and-placing.md 2c, 5): "a meteor at every step I take".</summary>
    public class TriggerToolsTests
    {
        const string MeteorAhead = "[{\"cast\": \"meteor\", \"at\": \"ahead\", \"by\": 2, \"delay\": 6}]";

        static Workbench Bench(string asking = "Andrew") =>
            new Workbench(ChemistryTests.Folders) { Here = new StandInHere(asking == null ? (HereSpot?)null : new HereSpot(asking, 288, 288)) };

        static string DraftIn(string said) => said.Split('\n').First(l => l.StartsWith("Draft ", System.StringComparison.Ordinal)).Substring(6).TrimEnd(':');

        [Test]
        public void AMeteorAtEveryStepITakeIsOneTriggerOnTheAskersSteps()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            string said = tools.AddTrigger("meteor-steps", MeteorAhead);
            string draft = DraftIn(said);
            var row = b.DraftOf(draft).Patch["triggers"]["meteor-steps"];
            Assert.That((string)row["who"], Is.EqualTo("Andrew"), "the asker's steps by default");
            Assert.That((string)row["on"], Is.EqualTo("step"));
            StringAssert.Contains("fires on Andrew's steps at most once every 3 ticks (2 a second at a run), and at most 4 triggers fire a tick", said);
            Assert.That(b.Validate(draft), Is.Empty);
            StringAssert.Contains("walk for a trigger on steps", b.SimulateNote(draft));
            StringAssert.Contains("a trigger, meteor-steps: on Andrew's step, at most every 3 ticks: cast meteor 2 cells ahead, landing 6 ticks later", b.Changes(b.DraftOf(draft)));

            string run = tools.Simulate(draft, ticks: 120);
            StringAssert.StartsWith("Scenario walk:", run);
            var (with, without) = b.SimulateRuns(draft, null, 120);
            Assert.That(with.Walker, Is.EqualTo("Andrew"));
            Assert.That(with.Firings, Is.EqualTo(30), "a step every 2 ticks, a floor of 3: every other step");
            Assert.That(with.Casts, Is.EqualTo(with.Firings));
            Assert.That(without.Firings, Is.Zero);
            Assert.That(with.Hash, Is.Not.EqualTo(without.Hash));
            StringAssert.Contains("Andrew ran 59 steps round a loop; triggers fired 30 times, casting 30 spells", run);

            StringAssert.Contains("Draft", tools.Remove(draft, "triggers", "meteor-steps"));
            Assert.That(b.DraftOf(draft).Patch["triggers"], Is.Null);
        }

        [Test]
        public void WhoCanBeANameOrEachAndListReadsTheTriggersOn()
        {
            var b = Bench(null);
            var tools = new RulesTools(b);
            StringAssert.Contains("give who", Assert.Throws<RulesToolException>(() => tools.AddTrigger("t", MeteorAhead)).Message);
            string said = tools.AddTrigger("t", "{\"place\": {\"cover\": \"vines\"}, \"at\": \"behind\", \"by\": 1}", who: "each", every: 6, chance: 0.5);
            Assert.That(b.Validate(DraftIn(said)), Is.Empty);
            StringAssert.Contains("fires on everyone's steps at most once every 6 ticks (1 a second at a run)", said);
            Assert.That(tools.List("trigger"), Does.Not.Contain("t: "), "the base rules have none, and a draft isn't on");
        }

        [Test]
        public void ABadTriggerIsRefusedAtOnceAndTheBudgetsHold()
        {
            var b = Bench();
            var tools = new RulesTools(b);
            StringAssert.Contains("every is at least 3 ticks", Assert.Throws<RulesToolException>(() => tools.AddTrigger("t", MeteorAhead, every: 2)).Message);
            StringAssert.Contains("unknown spell nope", Assert.Throws<RulesToolException>(() => tools.AddTrigger("t", "[{\"cast\": \"nope\"}]")).Message);
            StringAssert.Contains("do is a list of one or two actions", Assert.Throws<RulesToolException>(() => tools.AddTrigger("t", "3")).Message);
            StringAssert.Contains("reacts to step so far", Assert.Throws<RulesToolException>(() => tools.AddTrigger("t", MeteorAhead, on: "jump")).Message);
            string said = tools.AddTrigger("weather-steps", "[{\"cast\": \"control-weather\"}]");
            Assert.That(b.Validate(DraftIn(said)).Select(p => p.Message), Has.Some.Contains("level 7 at most"));
        }

        // The rules MCP knows the player at the keyboard as their peer name ("Player 1"); their stands come in as
        // "solo" alone and as "p<client>" online (SpellCaster.CastPlayer). The default who matches either.
        static World SteppedBy(Workbench b, string draft, string player)
        {
            var w = new World(b.InForce.WithMods(new[] { ModPatch.Parse(b.DraftOf(draft).Patch.ToString()) }), 64, 64, 5, "grass");
            w.ApplyStrike(StrikeCommand.Stand(player, 20, 20, 12));
            w.ApplyStrike(StrikeCommand.Stand(player, 21, 20, 12));
            return w;
        }

        [Test]
        public void SoloTheAskersStepsFireATriggerWithNoWho()
        {
            var b = Bench("Player 1");
            string draft = DraftIn(new RulesTools(b).AddTrigger("meteor-steps", MeteorAhead));
            Assert.That((string)b.DraftOf(draft).Patch["triggers"]["meteor-steps"]["who"], Is.EqualTo("Player 1"));
            var w = SteppedBy(b, draft, TriggerDef.SoloPlayer);
            Assert.That(w.TriggerFirings, Is.EqualTo(1), "a solo stand is the asker's");
            Assert.That(w.Triggered.Single().Player, Is.EqualTo("solo"));
            Assert.That(SteppedBy(b, draft, TriggerDef.SoloPlayer).CastHash(), Is.EqualTo(w.CastHash()), "the same every time");
        }

        [Test]
        public void OnlineTheAskersStepsFireATriggerWithNoWhoAndNoOneElses()
        {
            var b = Bench("Player 2");
            string draft = DraftIn(new RulesTools(b).AddTrigger("meteor-steps", MeteorAhead));
            Assert.That(SteppedBy(b, draft, "p1").TriggerFirings, Is.EqualTo(1), "client 1 is Player 2");
            Assert.That(SteppedBy(b, draft, "p0").TriggerFirings, Is.Zero);
            Assert.That(SteppedBy(b, draft, TriggerDef.SoloPlayer).TriggerFirings, Is.Zero, "solo is Player 1");
            Assert.That(SteppedBy(b, draft, "p10").TriggerFirings, Is.Zero);
            Assert.That(SteppedBy(b, draft, "Player 2").TriggerFirings, Is.EqualTo(1), "and a stand by the peer name itself");
            Assert.That(TriggerDef.PeerOf("p0"), Is.EqualTo("Player 1"));
            Assert.That(TriggerDef.PeerOf("pe"), Is.EqualTo("pe"));
            Assert.That(TriggerDef.PeerOf("p+1"), Is.EqualTo("p+1"));
            Assert.That(TriggerDef.PeerOf("Ann"), Is.EqualTo("Ann"));
        }

        [Test]
        public void ApplyRefusesATriggerModPastTheBudgetsAsAnyMod()
        {
            var b = Bench();
            string five = "{ \"title\": \"Five\", \"patch\": { \"triggers\": {" + string.Join(",", Enumerable.Range(0, 5).Select(i => $"\"t{i}\": {{ \"on\": \"step\", \"who\": \"each\", \"do\": [ {{ \"cast\": \"meteor\" }} ] }}")) + "} } }";
            StringAssert.StartsWith("Switch on refused: Five doesn't pass the checks: The mod has 5 triggers; a mod may have at most 4", b.Apply(five));
            StringAssert.StartsWith("Switch on refused: Storms doesn't pass the checks: Trigger t casts control-weather, level 8",
                b.Apply("{ \"title\": \"Storms\", \"patch\": { \"triggers\": { \"t\": { \"on\": \"step\", \"who\": \"each\", \"do\": [ { \"cast\": \"control-weather\" } ] } } } }"));
            Assert.That(b.RoomNow.Mods, Is.Empty);
        }
    }
}
