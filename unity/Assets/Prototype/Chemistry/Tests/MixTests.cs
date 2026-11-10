using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Mixes (docs/design/primitives.md §4, P5; Rules.Mix.cs, World.Mix.cs): rows of a `mix` group that meet
    /// derive a row with no reaction for the pair, a pure function of the two, at the same tick on every peer;
    /// the room's list comes through a save and a late join; a cap, then a wall; it lapses with the later parent.
    /// </summary>
    [Category("Simulation")]
    public class MixTests
    {
        const string Purple = "blue-paint+red-paint", Mixed = "blue-film+red-film";
        static Rules R => ChemistryTests.R;
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        static string Paint(string id, string colour, string extra = "") =>
            $@"""{id}"": {{ ""name"": ""{id}"", ""mix"": ""paint"", ""soaks"": false, ""tags"": [""paint"", ""{id}-only""], ""look"": {{ ""color"": {colour} }}{extra} }}";

        const int HistoryTicks = 150;
        const string Red = "[0.8, 0.1, 0.1]", Blue = "[0.1, 0.1, 0.8]", Yellow = "[0.9, 0.8, 0.1]";

        static string Mod(params string[] rows) =>
            @"{ ""title"": ""Paints"", ""idea"": ""Paints that run."", ""patch"": { ""liquids"": { " + string.Join(", ", rows) + @" } } }";

        static World Room(params string[] rows)
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Ana", "paints", Mod(rows))).Done, Is.True);
            return w;
        }

        static World RedAndBlue(string redExtra = "", string blueExtra = "") =>
            Room(Paint("red-paint", Red, redExtra), Paint("blue-paint", Blue, blueExtra));

        // Two pours 8 cells apart on the flat, that run until they meet.
        static void Pour(World w, string a = "red-paint", string b = "blue-paint")
        {
            for (int y = 30; y < 34; y++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Assert.That(w.AddLiquid(26 + k, y, a, 12), Is.True);
                    Assert.That(w.AddLiquid(34 + k, y, b, 12), Is.True);
                }
            }
        }

        static void Run(World w, int ticks)
        {
            for (int t = 0; t < ticks; t++) w.Step();
        }

        static int Cells(World w, string liquid) => w.AllCells().Count(c => c.Water > 0 && w.LiquidId(c) == liquid);

        static string State(World w) => $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {w.RngState}";

        [Test]
        public void RedAndBlueMakePurpleWithNoRowForThePair()
        {
            var w = RedAndBlue();
            Pour(w);
            Assert.That(w.Rules.LiquidIndex(Purple), Is.EqualTo(-1), "no mix before they meet");
            Run(w, 60);
            Assert.That(Cells(w, Purple), Is.GreaterThan(0), "they met and mixed");
            Assert.That(w.Room.Mixes.Select(m => m.Id), Is.EqualTo(new[] { Purple }), "one row, made once");
            var look = w.Rules.Liquid(w.Rules.LiquidIndex(Purple)).Look;
            Assert.That(look.R > look.G && look.B > look.G, Is.True, $"purple, not grey: {look.R} {look.G} {look.B}");
            Assert.That(w.CellHash(), Is.EqualTo("d2e8cf01"), "the purple golden");
        }

        [Test]
        public void ADerivedRowIsAPureFunctionOfTheTwoRows()
        {
            var w = RedAndBlue(@", ""thick"": 5, ""heap"": 4, ""speed"": 0.5, ""drowns"": false, ""boils"": ""steam""", @", ""thick"": 2, ""boils"": ""steam""");
            Pour(w);
            Run(w, 200);
            var mix = w.Rules.Liquid(w.Rules.LiquidIndex(Purple));
            Assert.That(mix.Thick, Is.EqualTo(3), "the mean, rounded down");
            Assert.That(mix.Heap, Is.EqualTo(3), "the mean with water's 2 where a row gives none");
            Assert.That(mix.Speed, Is.EqualTo(0.75), "a fraction stays one");
            Assert.That(mix.Tags, Is.EqualTo(new[] { "paint" }), "tags both have");
            Assert.That(mix.Drowns, Is.False, "a flag holds if both do");
            Assert.That(mix.Soaks, Is.False);
            Assert.That(mix.Boils, Is.EqualTo("steam"), "a text both give");
        }

        [Test]
        public void WhicheverRunsOntoTheOtherItIsTheSameMix()
        {
            var a = RedAndBlue();
            var b = RedAndBlue();
            Pour(a);
            Pour(b, "blue-paint", "red-paint");
            Run(a, 60);
            Run(b, 60);
            Assert.That(b.Room.Mixes.Single().Id, Is.EqualTo(Purple));
            Assert.That(b.Rules.Fingerprint, Is.EqualTo(a.Rules.Fingerprint));
        }

        [Test]
        public void AMixMeetingItsParentStaysTheMix()
        {
            var w = RedAndBlue();
            Pour(w);
            Run(w, 400);
            Assert.That(w.Room.Mixes.Count, Is.EqualTo(1), "no red-and-purple");
            Assert.That(Rules.MixId(Purple, "red-paint"), Is.EqualTo(Purple));
            Assert.That(Rules.MixId("yellow-paint", Purple), Is.EqualTo("blue-paint+red-paint+yellow-paint"));
        }

        [Test]
        public void RowsOfNoGroupOrAnotherStayWalls()
        {
            var w = Room(Paint("red-paint", Red), @"""oil"": { ""name"": ""Oil"", ""soaks"": false }");
            Pour(w, "red-paint", "oil");
            Run(w, 200);
            Assert.That(w.Room.Mixes, Is.Empty);
            Assert.That(w.Rules.Liquids.Count, Is.EqualTo(3));
        }

        [Test]
        public void TwoPeersMixAtTheSameTick()
        {
            var a = RedAndBlue();
            var b = RedAndBlue();
            Pour(a);
            Pour(b);
            for (int t = 0; t < 120; t++)
            {
                a.Step();
                b.Step();
                Assert.That(State(b), Is.EqualTo(State(a)), $"tick {t}");
            }
            Assert.That(a.Room.Mixes.Single().Tick, Is.EqualTo(b.Room.Mixes.Single().Tick));
        }

        [Test]
        public void ASaveKeepsTheMix()
        {
            var w = RedAndBlue();
            Pour(w);
            Run(w, 60);
            Assert.That(Cells(w, Purple), Is.GreaterThan(0));
            var joined = World.Load(w.Save(new ComboTests.Blank()), R, new ComboTests.Blank(), Budgets);
            Assert.That(State(joined), Is.EqualTo(State(w)), "the list, the row and the cells");
            Assert.That(joined.Room.Mixes.Single().Id, Is.EqualTo(Purple));
            for (int t = 0; t < 100; t++)
            {
                w.Step();
                joined.Step();
            }
            Assert.That(State(joined), Is.EqualTo(State(w)), "and runs on alike");
        }

        // A paint with the spells that pour it: a mod may add one element, and an element serves two spells.
        static string PourMod(string colour, string rgb) => @"{ ""title"": """ + colour + @""", ""idea"": ""A paint to pour."", ""patch"": {
            ""elements"": [""" + colour + @"-pour""],
            ""liquids"": { " + Paint(colour + "-paint", rgb) + @" },
            ""reactions"": { ""add"": [ { ""id"": ""pour-" + colour + @""", ""element"": """ + colour + @"-pour"", ""if"": { ""notTerrainTag"": ""blocking"" },
                ""do"": [{ ""addWater"": ""power"", ""liquid"": """ + colour + @"-paint"" }], ""stop"": true } ] },
            ""spells"": [
                { ""id"": """ + colour + @"-pool"", ""group"": ""water"", ""name"": """ + colour + @" Pool"", ""look"": { ""family"": ""splash"", ""color"": " + rgb + @" },
                  ""level"": 1, ""shape"": ""square"", ""size"": 3, ""element"": """ + colour + @"-pour"", ""power"": 6, ""blurb"": ""Level 1. A pool of paint."" },
                { ""id"": """ + colour + @"-drip"", ""group"": ""water"", ""name"": """ + colour + @" Drip"", ""look"": { ""family"": ""splash"", ""color"": " + rgb + @" },
                  ""level"": 1, ""shape"": ""point"", ""element"": """ + colour + @"-pour"", ""power"": 4, ""blurb"": ""Level 1. A drip of paint."" } ] } }";

        // What a room's history holds: both mods on, then a pool of each, four cells apart.
        static TickBatch PaintBatch(int t)
        {
            var batch = new TickBatch(t);
            if (t == 2)
            {
                batch.Room.Add(RoomCommand.Apply("Ana", "red", PourMod("red", Red)));
                batch.Room.Add(RoomCommand.Apply("Bo", "blue", PourMod("blue", Blue)));
            }
            if (t == 3)
            {
                batch.Casts.Add(new CastCommand("red-pool", 30, 32, 0));
                batch.Casts.Add(new CastCommand("blue-pool", 34, 32, 0));
            }
            return batch;
        }

        static World EmptyRoom()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            return w;
        }

        // A joiner doesn't load a save: it gets the room where the history starts and replays it (NetChem.Room.cs).
        [Test]
        public void ALateJoinerReplayingTheHistoryHasTheMix()
        {
            var host = EmptyRoom();
            var rules = host.RoomRules;
            for (int t = 1; t <= HistoryTicks; t++) rules = PaintBatch(t).RunOn(host, rules, out _);
            Assert.That(host.Room.Mixes.Select(m => m.Id), Is.EqualTo(new[] { Purple }));
            var joiner = EmptyRoom();
            var replayed = joiner.RoomRules;
            for (int t = 1; t <= HistoryTicks; t++) replayed = PaintBatch(t).RunOn(joiner, replayed, out _);
            Assert.That(State(joiner), Is.EqualTo(State(host)), "it comes to the same list, rows and cells at the same ticks");
            Assert.That(joiner.Room.Mixes.Single().Tick, Is.EqualTo(host.Room.Mixes.Single().Tick));
            Assert.That(joiner.RoomText(), Is.EqualTo(host.RoomText()));
        }

        [Test]
        public void TheMixIsInTheRoomsChecksum()
        {
            var w = RedAndBlue();
            string before = w.RoomText();
            Pour(w);
            Run(w, 60);
            Assert.That(w.RoomText(), Does.StartWith(before).And.Contain("|mix:liquids:" + Purple + ":"));
        }

        [Test]
        public void PastTheCapTheArrivingOneStopsAsAtAWall()
        {
            var tight = new World(R, 64, 64, 7, "grass");
            var capped = ModBudgets.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(ChemistryTests.RulesDir(), "budgets.json")).Replace(@"""maxMixes"": 16", @"""maxMixes"": 1"));
            tight.OpenRoom(ChangeMode.Open, "Ana", R, capped);
            Assert.That(tight.ApplyRoom(RoomCommand.Apply("Ana", "paints", Mod(Paint("red-paint", Red), Paint("blue-paint", Blue), Paint("gold-paint", Yellow)))).Done, Is.True);
            Pour(tight);
            for (int y = 40; y < 44; y++)
                for (int k = 0; k < 4; k++)
                {
                    tight.AddLiquid(26 + k, y, "red-paint", 12);
                    tight.AddLiquid(34 + k, y, "gold-paint", 12);
                }
            Run(tight, 200);
            Assert.That(tight.Room.Mixes.Count, Is.EqualTo(1), "one made, the other refused");
            Assert.That(tight.Rules.Liquids.Count, Is.EqualTo(5), "water, three paints, one mix");
        }

        [Test]
        public void AMixLapsesWithItsLaterParent()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            string One(string row) => @"{ ""title"": ""One"", ""idea"": ""A paint."", ""patch"": { ""liquids"": { " + row + @" } } }";
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Ana", "red", One(Paint("red-paint", Red)))).Done, Is.True);
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Ana", "blue", One(Paint("blue-paint", Blue)))).Done, Is.True);
            Pour(w);
            Run(w, 60);
            Assert.That(Cells(w, Purple), Is.GreaterThan(0));
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "red")).Done, Is.True);
            Assert.That(Cells(w, "red-paint"), Is.Zero, "the parent went");
            Assert.That(Cells(w, Purple), Is.GreaterThan(0), "the mix stays while a parent is on");
            Run(w, 20);
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "blue")).Done, Is.True);
            Assert.That(w.Room.Mixes, Is.Empty);
            Assert.That(w.Rules.LiquidIndex(Purple), Is.EqualTo(-1));
            Assert.That(w.AllCells().Count(c => c.Water > 0), Is.Zero, "gone with the later parent");
            Assert.That(w.Room.Retired.Any(r => r.Id == Purple), Is.True);
        }

        // ---------- covers ----------

        // A film of one colour and the spell that lays it: a mod may add one element, so a mod each.
        static string Stain(string colour, string row, string rgb) => @"{ ""title"": """ + colour + @""", ""idea"": ""A stain."", ""patch"": {
            ""elements"": [""" + colour + @"-stain""],
            ""covers"": { """ + colour + @"-film"": { ""name"": """ + colour + @" film"", ""mix"": ""stain"", ""tags"": [], " + row + @", ""look"": { ""color"": " + rgb + @" } } },
            ""reactions"": { ""add"": [ { ""id"": ""lay-" + colour + @""", ""element"": """ + colour + @"-stain"", ""do"": [{ ""setCover"": """ + colour + @"-film"" }], ""stop"": true } ] },
            ""spells"": [ { ""id"": """ + colour + @"-wash"", ""group"": ""water"", ""name"": """ + colour + @" Wash"", ""look"": { ""family"": ""splash"", ""color"": " + rgb + @" },
                ""level"": 1, ""shape"": ""point"", ""element"": """ + colour + @"-stain"", ""power"": 1, ""blurb"": ""Level 1. A film."" },
                { ""id"": """ + colour + @"-sweep"", ""group"": ""water"", ""name"": """ + colour + @" Sweep"", ""look"": { ""family"": ""splash"", ""color"": " + rgb + @" },
                ""level"": 1, ""shape"": ""square"", ""size"": 3, ""element"": """ + colour + @"-stain"", ""power"": 1, ""blurb"": ""Level 1. A wide film."" } ] } }";

        [Test]
        public void ACoverLaidOnAnotherOfItsGroupMixes()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            var red = w.ApplyRoom(RoomCommand.Apply("Ana", "red", Stain("red", @"""lasts"": 100, ""speed"": 0.5", Red)));
            var blue = w.ApplyRoom(RoomCommand.Apply("Ana", "blue", Stain("blue", @"""lasts"": 301", Blue)));
            Assert.That(red.Done && blue.Done, Is.True, red.Line + " / " + blue.Line);
            w.Cast("red-wash", 20, 20, 0);
            Assert.That(w.At(20, 20).Cover, Is.EqualTo("red-film"));
            w.Cast("blue-wash", 20, 20, 0);
            Assert.That(w.At(20, 20).Cover, Is.EqualTo(Mixed), "made at once between ticks, and laid");
            var mix = w.Rules.Covers[Mixed];
            Assert.That(mix.Lasts, Is.EqualTo(200), "the mean, rounded down");
            Assert.That(mix.Speed, Is.EqualTo(0.75), "with a cover's default speed of 1");
            Assert.That(w.At(20, 20).Life, Is.EqualTo(200));
            var back = World.Load(w.Save(new ComboTests.Blank()), R, new ComboTests.Blank(), Budgets);
            Assert.That(back.At(20, 20).Cover, Is.EqualTo(Mixed));
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "blue")).Done, Is.True);
            Assert.That(w.At(20, 20).Cover, Is.EqualTo(Mixed), "while a parent is on");
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "red")).Done, Is.True);
            Assert.That(w.At(20, 20).Cover, Is.Null, "gone with the later parent");
        }

        [Test]
        public void BaseGoldensAreUntouched()
        {
            var plain = new World(R, 64, 64, 7, "grass");
            plain.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            Assert.That(plain.RoomText(), Does.Not.Contain("mix"));
            Assert.That(plain.Save(new ComboTests.Blank()), Does.Not.Contain("mixes"));
        }
    }
}
