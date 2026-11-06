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
        public void AnActMovesTheLandAndUndoPutsItBack()
        {
            var (b, tools) = Tools();
            string said = tools.Act("land", near: "[120, 120]", radius: 12, rise: 30, profile: "cone", rough: 20);
            StringAssert.Contains("moved the land", said);
            Assert.That(b.LiveWorld.LandMovedAt(120, 120), Is.GreaterThan(100));
            Assert.That(b.LiveWorld.Room.Mods, Is.Empty, "not a mod");
            StringAssert.Contains("put the land back", tools.UndoAct());
            Assert.That(b.LiveWorld.LandHasMoved, Is.False);
        }

        [Test]
        public void ALandStampIsHeldToItsLimits()
        {
            var (b, tools) = Tools();
            Assert.That(() => tools.Act("land", near: "[120, 120]", radius: 33, rise: 10), Throws.Exception.With.Message.Contain("radius is 1 to 32"));
            Assert.That(() => tools.Act("land", near: "[120, 120]", radius: 8, rise: 101), Throws.Exception.With.Message.Contain("rise is in metres"));
            Assert.That(() => tools.Act("land", near: "[120, 120]", radius: 8, rise: 10, profile: "mountain"), Throws.Exception.With.Message.Contain("flat, dome, cone, ridge, canyon or cliff"));
            Assert.That(() => tools.Act("land", near: "[120, 120]", radius: 8, rise: 10, profile: "ridge"), Throws.Exception.With.Message.Contain("needs along"));
            StringAssert.Contains("cross water", tools.Act("land", near: "lake", radius: 8, rise: 10));
            var village = b.Places.All.First(p => p.Kind == "village");
            StringAssert.Contains(village.Name, tools.Act("land", near: village.Id, radius: 8, rise: 10));
            Assert.That(b.LiveWorld.LandHasMoved, Is.False);
        }

        // #156: a 100 m mountain came out about 12 cells across. Its rise is cut to land.steepest (1.5 m) a cell of radius.
        [Test]
        public void ATallNarrowStampIsCutToItsWidth()
        {
            var (b, tools) = Tools();
            string said = tools.Act("land", near: "[120, 120]", radius: 6, rise: 100, profile: "cone");
            StringAssert.Contains("100 m is too steep for a radius of 6, so it rises 9 m", said);
            StringAssert.Contains("moved the land", said);
            Assert.That(b.LiveWorld.LandHash(), Is.EqualTo("[120,120,6,72,\"cone\",0,0,40,1]"), "9 m, in land units");
        }

        // #156: canyons at radius 10 came out with 30 m walls. A hollow or a wall keeps its sign.
        [Test]
        public void ATallNarrowCanyonIsCutToItsWidth()
        {
            var (b, tools) = Tools();
            StringAssert.Contains("30 m is too steep for a radius of 10, so it rises 15 m", tools.Act("land", near: "[120, 120]", radius: 10, rise: -30, profile: "canyon", along: "east"));
            Assert.That(b.LiveWorld.LandHash(), Is.EqualTo("[120,120,10,-120,\"canyon\",1,0,40,1]"));
        }

        [Test]
        public void AStampAsSteepAsAllowedIsAsAsked()
        {
            var (b, tools) = Tools();
            StringAssert.DoesNotContain("too steep", tools.Act("land", near: "[120, 120]", radius: 12, rise: 18, profile: "cone", rough: 20));
            Assert.That(b.LiveWorld.LandHash(), Is.EqualTo("[120,120,12,144,\"cone\",0,0,20,1]"));
        }

        [TestCase("ball-court")]
        [TestCase("defense-field")]
        [TestCase("tower")]
        [TestCase("bridge")]
        [TestCase("standing-stones")]
        [TestCase("outcrop")]
        [TestCase("falls-cliff")]
        [TestCase("scree-ramp")]
        public void AStampOnAPlacedFeatureIsRefusedAndTheLandStays(string id)
        {
            var (b, tools) = Tools();
            var place = b.Places.All.First(p => p.Id == id);
            var (x, y) = place.Cells().Last();
            StringAssert.Contains($"the ground under {place.Name}", tools.Act("land", near: $"[{x}, {y}]", radius: 1, rise: 15));
            Assert.That(b.LiveWorld.LandHasMoved, Is.False);
            Assert.That(b.LiveWorld.LandHash(), Is.Empty);
            Assert.That(b.LiveWorld.LandMovedAt(x, y), Is.Zero);
        }

        [Test]
        public void AStampThatOnlyReachesTheCourtIsRefused()
        {
            var (b, tools) = Tools();
            var court = b.Places.All.First(p => p.Id == "ball-court");
            var edge = court.Cells().OrderBy(c => c.x).First();
            StringAssert.Contains(court.Name, tools.Act("land", near: $"[{edge.x - 10}, {edge.y}]", radius: 10, rise: 15));
            Assert.That(b.LiveWorld.LandHasMoved, Is.False);
            StringAssert.Contains("moved the land", tools.Act("land", near: $"[{edge.x - 10}, {edge.y}]", radius: 9, rise: 15));
        }

        [Test]
        public void TheActToolSaysHowLowLandGoes()
        {
            var rise = typeof(RulesTools).GetMethod(nameof(RulesTools.Act)).GetParameters().First(p => p.Name == "rise");
            string said = rise.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;
            StringAssert.Contains($"below {World.LandLowest / 8} m", said);
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
