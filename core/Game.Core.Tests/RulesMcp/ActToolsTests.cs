using System.Linq;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// act, undo_act, superseded mods and a species' colours (docs/design/world-acts.md): "add 20 trees near me" twice is two
    /// acts and no mods, the same on every peer; a chat change that says all an earlier one said takes its place.
    /// </summary>
    public class ActToolsTests
    {
        static (Workbench bench, RulesTools tools) Tools()
        {
            var b = new Workbench(ChemistryTests.Folders);
            return (b, new RulesTools(b));
        }

        static int Trees(World w) => w.Cells.Count(c => c.T == "tree");

        [Test]
        public void TwentyTreesTwiceIsFortyTreesAndNoMods()
        {
            var (b, tools) = Tools();
            int before = Trees(b.LiveWorld);
            StringAssert.Contains("isn't a mod", tools.Act("tree", 20, near: "lake", spacing: 2));
            StringAssert.Contains("(act 2)", tools.Act("tree", 20, near: "lake", spacing: 2));
            Assert.That(Trees(b.LiveWorld) - before, Is.EqualTo(40));
            Assert.That(b.LiveWorld.Room.Mods, Is.Empty, "the mod list stays as it was");
        }

        [Test]
        public void UndoTakesBackTheLastActAndOnlyThat()
        {
            var (b, tools) = Tools();
            int before = Trees(b.LiveWorld);
            tools.Act("tree", 10, near: "lake");
            tools.Act("tree", 5, near: "lake");
            StringAssert.Contains("took back 5 tree", tools.UndoAct());
            Assert.That(Trees(b.LiveWorld) - before, Is.EqualTo(10));
            StringAssert.Contains("took back 10 tree", tools.UndoAct());
            Assert.That(Trees(b.LiveWorld), Is.EqualTo(before));
            StringAssert.Contains("no act to take back", tools.UndoAct());
        }

        [Test]
        public void AnimalsComeOutAndCantBeTakenBack()
        {
            var (b, tools) = Tools();
            int last = b.LiveWorld.LastSummonN;
            StringAssert.Contains("3 deer", tools.Act("deer", 3, near: "lake"));
            Assert.That(b.LiveWorld.SummonedAfter(last).Count(), Is.EqualTo(3));
            StringAssert.Contains("can't be taken back", tools.UndoAct());
        }

        [Test]
        public void TooManyOrAnUnknownThingIsRefusedAtOnce()
        {
            var (_, tools) = Tools();
            Assert.Throws<RulesToolException>(() => tools.Act("tree", 9999));
            Assert.Throws<RulesToolException>(() => tools.Act("deer", 25));
            Assert.Throws<RulesToolException>(() => tools.Act("meteor", 5));
            Assert.Throws<RulesToolException>(() => tools.Act("no-such-thing"));
        }

        [Test]
        public void AnActIsTheSameOnEveryPeerAndComesThroughASave()
        {
            string ask = @"{""kind"":""terrain"",""what"":""tree"",""cells"":[[20,20],[22,20]]}";
            var a = Room();
            var b = Room();
            Assert.That(a.ApplyRoom(RoomCommand.Act("Ana", ask)).Done, Is.True);
            Assert.That(b.ApplyRoom(RoomCommand.Act("Ana", ask)).Done, Is.True);
            Assert.That(b.CellHash(), Is.EqualTo(a.CellHash()));
            Assert.That(b.CastHash(), Is.EqualTo(a.CastHash()));
            string save = a.Save(new ComboTests.Blank());
            var back = World.Load(save, a.Rules, new ComboTests.Blank(), ModBudgets.Load(ChemistryTests.RulesDir()));
            Assert.That(back.CastHash(), Is.EqualTo(a.CastHash()), "what can be undone comes back");
            Assert.That(back.ApplyRoom(RoomCommand.Undo("Ana")).Done, Is.True);
            Assert.That(back.At(20, 20).T, Is.EqualTo("grass"));
        }

        static World Room()
        {
            var w = new World(ChemistryTests.R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", ChemistryTests.R, ModBudgets.Load(ChemistryTests.RulesDir()));
            return w;
        }

        [Test]
        public void ALockedRoomRefusesAnActFromAGuest()
        {
            var w = new World(ChemistryTests.R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Locked, "Ana", ChemistryTests.R, ModBudgets.Load(ChemistryTests.RulesDir()));
            Assert.That(w.ApplyRoom(RoomCommand.Act("Bo", @"{""kind"":""terrain"",""what"":""tree"",""cells"":[[20,20]]}")).Done, Is.False);
            Assert.That(w.At(20, 20).T, Is.EqualTo("grass"));
        }

        [Test]
        public void ABlueFireTakesThePlaceOfAGreenOne()
        {
            var (b, tools) = Tools();
            tools.Apply(Draft(tools.SetLook("fire-bolt", "color", "[0, 1, 0]")), title: "Green bolt");
            string said = tools.Apply(Draft(tools.SetLook("fire-bolt", "color", "[0, 0, 1]")), title: "Blue bolt");
            StringAssert.Contains("Green bolt", said);
            Assert.That(b.LiveWorld.Room.Mods.Select(m => m.Title), Is.EqualTo(new[] { "Blue bolt" }));
        }

        [Test]
        public void AChangeThatSaysLessLeavesTheEarlierOneOn()
        {
            var (b, tools) = Tools();
            string both = tools.SetLook("fire-bolt", "color", "[0, 1, 0]");
            tools.SetLook("fireball", "color", "[0, 1, 0]", draft: Draft(both));
            tools.Apply(Draft(both), title: "Green fire");
            tools.Apply(Draft(tools.SetLook("fire-bolt", "color", "[0, 0, 1]")), title: "Blue bolt");
            Assert.That(b.LiveWorld.Room.Mods, Has.Count.EqualTo(2));
        }

        [Test]
        public void TwoScattersOfTreesStayTwo()
        {
            var (b, tools) = Tools();
            tools.Apply(Draft(tools.Scatter("tree", 5, near: "lake")), title: "Grove");
            tools.Apply(Draft(tools.Scatter("tree", 5, near: "lake", seed: 2)), title: "Second grove");
            Assert.That(b.LiveWorld.Room.Mods, Has.Count.EqualTo(2), "they lay different cells, so neither says all the other does");
        }

        [Test]
        public void TheTurkeysTurnBlueAndTheRestOfTheRowStays()
        {
            var (b, tools) = Tools();
            string said = tools.SetSpecies("turkey", color: "[0.1, 0.2, 0.9]");
            Assert.That(b.Validate(Draft(said)), Is.Empty);
            tools.Apply(Draft(said), title: "Blue turkeys");
            var row = JObject.Parse(b.LiveWorld.Rules.ToJson())["fauna"]["turkey"];
            Assert.That((double)row["coat"]["base"][2], Is.EqualTo(0.9).Within(1e-6));
            Assert.That(row["size"], Is.Not.Null);
            Assert.That(row["spawn"], Is.Not.Null);
        }

        [Test]
        public void ASpeciesWithSeveralCoatsWearsTheOneItWasGiven()
        {
            var (b, tools) = Tools();
            string id = ((JObject)JObject.Parse(b.LiveWorld.Rules.ToJson())["fauna"]).Properties().First(p => p.Value["coats"] is JArray).Name;
            tools.Apply(Draft(tools.SetSpecies(id, color: "[0.1, 0.2, 0.9]")), title: "One coat");
            var species = b.LiveWorld.Rules.Fauna.Species(id);
            Assert.That(species.Coats, Is.Empty, "a list of coats would go on being drawn over it");
            Assert.That(species.Coat.Base[2], Is.EqualTo(0.9).Within(1e-6));
        }

        [Test]
        public void DensityZeroKeepsTheRestOfWhereTheyComeOut()
        {
            var (b, tools) = Tools();
            var was = JObject.Parse(b.LiveWorld.Rules.ToJson())["fauna"]["deer"]["spawn"].DeepClone();
            tools.Apply(Draft(tools.SetSpecies("deer", density: 0)), title: "No more deer");
            var now = JObject.Parse(b.LiveWorld.Rules.ToJson())["fauna"]["deer"]["spawn"];
            Assert.That((double)now["density"], Is.EqualTo(0));
            foreach (var p in ((JObject)was).Properties().Where(p => p.Name != "density"))
                Assert.That(JToken.DeepEquals(now[p.Name], p.Value), Is.True, p.Name);
        }

        static string Draft(string said) =>
            said.Split('\n').First(l => l.StartsWith("Draft ", System.StringComparison.Ordinal)).Replace(":", " ").Split(' ')[1];
    }
}
