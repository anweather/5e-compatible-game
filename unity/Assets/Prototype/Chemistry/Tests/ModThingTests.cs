using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>What mods add to the world (G4 to G6): looks on things, things appended by chunk, a seeded chance, the first find.</summary>
    public class ModThingTests
    {
        static readonly Rules R = Rules.Load(ChemistryTests.RulesDir());
        static readonly ModBudgets Budgets = ModBudgets.Load(ChemistryTests.RulesDir());

        const string Kind = "'items': { 'test-find': { 'name': 'Test find', 'mass': 1, 'look': { 'family': 'creature', 'species': 'deer', 'color': [1, 0.8, 0.2], 'scale': 0.5 } } }";

        static string Adds(string rows, bool kind = true) => ("{ " + (kind ? Kind + ", " : "") + "'world': { 'add': [" + rows + "] } }").Replace('\'', '"');

        static Rules With(params string[] mods) => R.WithMods(mods.Select(ModPatch.Parse));

        static readonly WorldThings File = WorldThings.FromJson("{ 'chunks': [ { 'chunk': [0, 0], 'things': [ { 'kind': 'barrel', 'cell': [1, 1] } ] } ] }".Replace('\'', '"'));

        static World Room()
        {
            var w = new World(R, 64, 64, 7, "dirt");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            return w;
        }

        [Test]
        public void ALookOnAThingReadsAndStaysOutOfTheFingerprint()
        {
            var r = With(Adds(""));
            var look = r.Item("test-find").Look;
            Assert.That((look.Family, look.Species, look.Scale, look.G), Is.EqualTo(("creature", "deer", 0.5f, 0.8f)));
            string Painted(double g) => $"{{ \"items\": {{ \"test-find\": {{ \"name\": \"Test find\", \"mass\": 1, \"look\": {{ \"family\": \"creature\", \"species\": \"Deer\", \"color\": [1, {g.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 0] }} }} }} }}";
            Assert.That(With(Painted(0.1)).Fingerprint, Is.EqualTo(With(Painted(0.9)).Fingerprint));
            Assert.That(R.Item("bullseye-lantern").Look, Is.Null, "no look: its baked prefab");
        }

        [Test]
        public void TheChecksHoldALookAndARowToWhatTheViewCanDraw()
        {
            string Problems(string mod) => string.Join("\n", ModValidator.Validate(R, Budgets, mod.Replace('\'', '"')));
            Assert.That(Problems(Adds("{ 'kind': 'test-find', 'cell': [10, 10] }")), Is.Empty);
            Assert.That(Problems(Kind.Insert(0, "{ ").Replace("'deer'", "'dragon'") + " }"), Does.Contain("takes a species"));
            Assert.That(Problems(Kind.Insert(0, "{ ").Replace("0.5", "9") + " }"), Does.Contain("scale of 9"));
            Assert.That(Problems("{ 'items': { 'plain': { 'name': 'Plain', 'mass': 1 } }, 'world': { 'add': [{ 'kind': 'plain', 'cell': [1, 1] }] } }"), Does.Contain("no look"));
            Assert.That(Problems(Adds("{ 'kind': 'nope', 'cell': [1, 1] }")), Does.Contain("unknown kind nope"));
            Assert.That(Problems(Adds("{ 'kind': 'test-find', 'cell': [1, 1], 'chance': 1.5 }")), Does.Contain("chance must be above 0"));
            Assert.That(Problems(Adds("{ 'kind': 'test-find', 'cell': [1, 1], 'link': 'x' }")), Does.Contain("link isn't a field"));
            Assert.That(Problems(Adds("{ 'kind': 'test-find' }")), Does.Contain("needs \"cell\""));
            string many = string.Join(",", Enumerable.Range(0, 17).Select(i => "{ 'kind': 'test-find', 'cell': [" + i + ", 1] }"));
            Assert.That(Problems(Adds(many)), Does.Contain("at most 16"));
            Assert.That(Problems("{ 'world': { 'add': 3 } }"), Does.Contain("\"world\" must be"));
        }

        [Test]
        public void AddedThingsAppendToTheirChunkAndTheFilesKeepTheirIds()
        {
            var before = File.All.Select(t => t.Id).ToList();
            var things = File.With(With(Adds("{ 'kind': 'test-find', 'cell': [70, 3], 'rot': 90 }, { 'kind': 'test-find', 'cell': [2, 2] }")), 1);
            Assert.That(things.InChunk(0, 0).Select(t => t.Kind), Is.EqualTo(new[] { "barrel", "test-find" }), "after what the file has");
            Assert.That(things.Find(before[0]).Kind, Is.EqualTo("barrel"));
            var far = things.InChunk(1, 0).Single();
            Assert.That((far.CellX, far.CellY, far.LocalX, far.Rotation, far.FromMod), Is.EqualTo((70, 3, 6, 90, true)));
            Assert.That(things.Find(before[0]).FromMod, Is.False);
            Assert.That(things.Fingerprint, Is.Not.EqualTo(File.Fingerprint));
            Assert.That(File.With(R, 1), Is.SameAs(File), "nothing added: the file's own table, so its fingerprint can't move");
        }

        [Test]
        public void TakingOneModOffDoesntRenumberWhatAnotherAdded()
        {
            string a = Adds("{ 'kind': 'test-find', 'cell': [5, 5] }"), b = Adds("{ 'kind': 'barrel', 'cell': [6, 6] }", false);
            var both = File.With(With(a, b), 1);
            var alone = File.With(With(b), 1);
            var thing = both.InChunk(0, 0).Single(t => t.Kind == "barrel" && t.FromMod);
            Assert.That(alone.Find(thing.Id)?.CellX, Is.EqualTo(6), "the same id with the other mod gone");
            Assert.That(alone.All.Count(t => t.Kind == "test-find"), Is.Zero);
            var (added, removed) = alone.ChangesFrom(both);
            Assert.That(added, Is.Empty);
            Assert.That(removed.Count, Is.EqualTo(1), "what the view despawns");
            Assert.That(both.ChangesFrom(alone).added.Single().Kind, Is.EqualTo("test-find"), "and what it spawns");
        }

        [Test]
        public void TwoLikeThingsGetTwoIds()
        {
            string row = "{ 'kind': 'test-find', 'cell': [5, 5] }";
            var things = File.With(With(Adds(row), Adds(row, false)), 1);
            Assert.That(things.InChunk(0, 0).Where(t => t.FromMod).Select(t => t.Id).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void AChanceIsDecidedBySeedAndIdAloneNearTheOddsItNames()
        {
            int held = 0;
            for (int i = 0; i < 4000; i++) if (ThingChance.Holds(7, new ThingId(3, 4, i), 0.2)) held++;
            Assert.That(held, Is.InRange(720, 880));
            Assert.That(ThingChance.Holds(7, new ThingId(3, 4, 1), 1), "1 always holds");
            string rows = string.Join(",", Enumerable.Range(0, 16).Select(i => "{ 'kind': 'test-find', 'cell': [" + (10 + i) + ", 9], 'chance': 0.5 }"));
            var rules = With(Adds(rows));
            string Here(uint seed) => string.Join(" ", File.With(rules, seed).All.Where(t => t.FromMod).Select(t => t.Id));
            Assert.That(Here(1), Is.EqualTo(Here(1)));
            Assert.That(Here(1), Is.Not.EqualTo(Here(2)), "another world, another scatter");
            Assert.That(Here(1).Split(' ').Length, Is.InRange(2, 15));
        }

        [Test]
        public void AChanceInTheFileGoesTheSameWay()
        {
            var file = WorldThings.FromJson("{ 'chunks': [ { 'chunk': [0, 0], 'things': [ { 'kind': 'barrel', 'cell': [1, 1], 'chance': 0.000001 }, { 'kind': 'barrel', 'cell': [2, 1] } ] } ] }".Replace('\'', '"'));
            var things = file.With(R, 1);
            Assert.That(things.All.Single().CellX, Is.EqualTo(2));
            Assert.That(things.All.Single().Id, Is.EqualTo(new ThingId(0, 0, 1)), "one that isn't there keeps its place in the list");
        }

        [Test]
        public void SwitchingThingsOnDrawsNothingFromTheWorldsGenerator()
        {
            var plain = Room();
            var modded = Room();
            string rows = string.Join(",", Enumerable.Range(0, 16).Select(i => "{ 'kind': 'test-find', 'cell': [" + (10 + i) + ", 9], 'chance': 0.2 }"));
            Assert.That(modded.ApplyRoom(RoomCommand.Apply("Ana", "hide", Adds(rows))).Done);
            Assert.That(modded.RngState, Is.EqualTo(plain.RngState));
            for (int t = 0; t < 10; t++)
            {
                plain.Step();
                modded.Step();
            }
            Assert.That(modded.CellHash(), Is.EqualTo(plain.CellHash()), "things aren't on cells");
            Assert.That(modded.RngState, Is.EqualTo(plain.RngState));
            Assert.That(modded.Rules.Fingerprint, Is.Not.EqualTo(plain.Rules.Fingerprint), "the rows are in the rules' fingerprint");
        }

        [Test]
        public void ARevertTakesAwayWhatItAddedAndFindsTheBadSpots()
        {
            var w = Room();
            int before = w.ThingsNow.Count;
            Assert.That(w.ApplyRoom(RoomCommand.Apply("Ana", "hide", Adds("{ 'kind': 'test-find', 'cell': [10, 10] }"))).Done);
            Assert.That(w.ThingsNow.Count, Is.EqualTo(before + 1));
            Assert.That(w.ThingsNow.At(10, 10).FromMod);
            Assert.That(w.CellProblem(10, 10), Does.Contain("test-find"), "a building's site keeps clear of it");
            var on = w.ApplyRoom(RoomCommand.Apply("Bo", "again", Adds("{ 'kind': 'barrel', 'cell': [10, 10] }", false)));
            Assert.That(on.Done, Is.False);
            Assert.That(on.Line, Does.Contain("can't put a barrel at cell (10, 10)"));
            var off = w.ApplyRoom(RoomCommand.Apply("Bo", "off", Adds("{ 'kind': 'barrel', 'cell': [900, 10] }", false)));
            Assert.That(off.Line, Does.Contain("off the world"));
            var uses = w.ApplyRoom(RoomCommand.Apply("Bo", "uses", Adds("{ 'kind': 'test-find', 'cell': [11, 10] }", false)));
            Assert.That(uses.Done, uses.Line);
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "hide")).Done, Is.False, "the other mod still places its kind");
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "uses")).Done);
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Ana", "hide")).Done);
            Assert.That(w.ThingsNow.Count, Is.EqualTo(before));
            Assert.That(w.Rules.Fingerprint, Is.EqualTo(R.Fingerprint));
        }

        [Test]
        public void TheFirstFindIsLoggedOnceWhateverTheRoomsMode()
        {
            var w = new World(R, 64, 64, 7, "dirt");
            var mods = new[] { new RoomMod { Id = "hide", Title = "Hide", By = "Ana", Json = Adds("{ 'kind': 'test-find', 'cell': [10, 10] }") } };
            w.OpenRoom(ChangeMode.Locked, "Ana", R, Budgets, mods);
            var id = w.ThingsNow.At(10, 10).Id;
            string hash = w.RoomHash();
            int lines = w.Log.Count;
            var first = w.ApplyRoom(RoomCommand.Found("Bo", id));
            Assert.That(first.Done);
            Assert.That(w.Log[w.Log.Count - 1].Text, Is.EqualTo("Bo found the test find"));
            Assert.That(w.RoomHash(), Is.Not.EqualTo(hash), "who has found what is in the checksum");
            Assert.That(w.ApplyRoom(RoomCommand.Found("Ana", id)).Done, Is.False);
            Assert.That(w.ApplyRoom(RoomCommand.Found("Ana", new ThingId(0, 0, 0))).Done, Is.False);
            Assert.That(w.Log.Count, Is.EqualTo(lines + 1), "only the first says anything");
        }

        [Test]
        public void ThingsAndFindsComeThroughASave()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Ana", "hide", Adds("{ 'kind': 'test-find', 'cell': [10, 10] }")));
            var id = w.ThingsNow.At(10, 10).Id;
            w.ApplyRoom(RoomCommand.Found("Bo", id));
            var back = World.Load(w.Save(new ComboTests.Blank()), R, new ComboTests.Blank(), Budgets);
            Assert.That(back.ThingsNow.Find(id)?.Kind, Is.EqualTo("test-find"));
            Assert.That(back.ThingsNow.Fingerprint, Is.EqualTo(w.ThingsNow.Fingerprint));
            Assert.That(back.CastHash(), Is.EqualTo(w.CastHash()));
            Assert.That(back.ApplyRoom(RoomCommand.Found("Ana", id)).Done, Is.False, "found stays found");
        }

        [Test]
        public void NoModNoRowsSoTheBaseFingerprintCantMove()
        {
            Assert.That(JObject.Parse(R.ToJson())["worldThings"], Is.Null);
        }
    }
}
