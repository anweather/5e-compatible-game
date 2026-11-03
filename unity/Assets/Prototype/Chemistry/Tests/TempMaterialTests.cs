using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// Temporary materials (World.Lapse.cs, docs/design/primitives.md §4 and §5's first test): a mod's own
    /// cover lives while the mod is on, lapses after its ticks or goes when it's reverted, and every cell
    /// holding it is swept at that tick, the same on every peer.
    /// </summary>
    public class TempMaterialTests
    {
        static Rules R => ChemistryTests.R;
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        const int Lapse = 3600;

        // §5's first test, as Claude would write it: a slick of black ice that melts in a minute (lasts 360) and
        // goes in ten (lapse after 3600). The element is cold: a new one must serve two spells (ModValidator).
        static string BlackIce(int after = Lapse) => @"{ ""title"": ""Black ice"", ""idea"": ""A spell that lays a slick of black ice, gone again in ten minutes."", ""patch"": {
            ""covers"": { ""black-ice"": { ""name"": ""Black ice"", ""tags"": [""slippery""], ""lasts"": 360, ""witherLog"": ""The black ice thaws away"",
                ""melt"": { ""water"": 1 }, ""look"": { ""color"": [0.08, 0.12, 0.35] }, ""lapse"": { ""after"": " + after + @" } } },
            ""reactions"": { ""add"": [ { ""id"": ""cold-black-ice"", ""element"": ""cold"", ""if"": { ""notTerrainTag"": ""blocking"", ""water"": false, ""frozen"": false, ""power"": 5 },
                ""do"": [{ ""setCover"": ""black-ice"" }], ""log"": ""Black ice glazes the ground"", ""stop"": true } ] },
            ""spells"": [ { ""id"": ""glaze"", ""group"": ""water"", ""name"": ""Glaze"", ""look"": { ""family"": ""motes"", ""color"": [0.08, 0.12, 0.35] },
                ""level"": 2, ""shape"": ""burst"", ""radius"": 2, ""element"": ""cold"", ""power"": 5, ""blurb"": ""Level 2. A slick of black ice, hard to see and harder to stand on."" } ] } }";

        static World Room()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            return w;
        }

        static int Slicks(World w) => w.Cells.Count(c => c.Cover == "black-ice");

        static string Last(World w) => w.Log.Count == 0 ? "" : w.Log[w.Log.Count - 1].Text;

        static string State(World w) => $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {w.RngState}";

        [Test]
        public void BlackIceLapsesOnBothPeersAtTheSameTick()
        {
            var a = Room();
            var b = Room();
            var ra = a.RoomRules;
            var rb = b.RoomRules;
            int switched = 1, lapses = switched + Lapse; // the mod goes on between ticks 1 and 2
            for (int t = 1; t <= lapses + 5; t++)
            {
                TickBatch Batch()
                {
                    var batch = new TickBatch(t);
                    if (t == switched + 1) batch.Room.Add(RoomCommand.Apply("Bo", "black-ice", BlackIce()));
                    // Once at the start, and again late, since a slick withers by itself in 360 ticks.
                    if (t == switched + 2 || t == lapses - 50) batch.Casts.Add(new CastCommand("glaze", 32, 32, 0));
                    return batch;
                }
                ra = Batch().RunOn(a, ra, out _);
                rb = Batch().RunOn(b, rb, out _);
                if (t % 100 == 0 || t >= lapses - 2) Assert.That(State(b), Is.EqualTo(State(a)), $"tick {t}");
                if (t == switched + 2) Assert.That(Slicks(a), Is.GreaterThan(0), "the cast lays slicks");
                if (t == lapses - 1) Assert.That(Slicks(a), Is.GreaterThan(0), "slicks stand until the lapse");
                if (t == lapses)
                {
                    Assert.That(Slicks(a), Is.Zero, "every slick is swept at the lapse");
                    Assert.That(Last(a), Is.EqualTo("Black ice lapsed (Open room)"));
                }
            }
            Assert.That(a.Rules.Fingerprint, Is.EqualTo(R.Fingerprint), "the rules are back to base");
            Assert.That(a.Room.Mods, Is.Empty);
            Assert.That(a.Room.Retired.Select(r => $"{r.Table} {r.Id} {r.Into} {r.Mod} {r.Tick}"), Is.EqualTo(new[] { $"covers black-ice  black-ice {lapses}" }));
            Assert.That(a.Cells.Count(c => c.Life != 0), Is.Zero, "the slicks' Life went with them");
            Assert.That(State(b), Is.EqualTo(State(a)));
            Assert.That(a.CastHash(), Is.Not.EqualTo(Room().CastHash()), "the retired list is in the checksum");
            Assert.That(a.RoomText(), Does.EndWith($"|retired:covers:black-ice::black-ice:{lapses}"));
        }

        [Test]
        public void TheBlackIceGoldenHolds()
        {
            // Pins the run: cast at once, step past the lapse; a change to the sweep or the lapse tick moves it.
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce(600)));
            w.Cast("glaze", 32, 32, 0);
            for (int t = 0; t < 300; t++) w.Step();
            w.Cast("glaze", 20, 40, 0);
            int slicks = Slicks(w);
            for (int t = 0; t < 310; t++) w.Step();
            Assert.That(slicks, Is.GreaterThan(0));
            Assert.That(Slicks(w), Is.Zero);
            Assert.That(w.CellHash(), Is.EqualTo("cdf4bdc5"));
            Assert.That(w.CastHash(), Is.EqualTo("36e6e144"));
        }

        [Test]
        public void ARevertSweepsTheSlicksAtOnce()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce()));
            w.Cast("glaze", 32, 32, 0);
            w.Step();
            Assert.That(Slicks(w), Is.GreaterThan(0));
            Assert.That(w.ApplyRoom(RoomCommand.Revert("Bo", "black-ice")).Done, Is.True);
            Assert.That(Slicks(w), Is.Zero, "swept in the same tick, long before the lapse");
            Assert.That(w.Rules.Fingerprint, Is.EqualTo(R.Fingerprint));
            Assert.That(w.Room.Retired.Select(r => r.Id), Is.EqualTo(new[] { "black-ice" }));
            for (int t = 0; t < 10; t++) w.Step();
            Assert.That(Last(w), Does.Not.Contain("lapsed"));
        }

        [Test]
        public void AModOnAgainTakesItsMaterialOffTheRetiredList()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce()));
            w.ApplyRoom(RoomCommand.Revert("Bo", "black-ice"));
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce()));
            Assert.That(w.Room.Retired, Is.Empty);
            Assert.That(w.LapsesAt(w.Room.Mods[0]), Is.EqualTo(Lapse));
        }

        [Test]
        public void WithoutAfterItLastsWhileTheModIsOn()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce().Replace(@"""lapse"": { ""after"": " + Lapse + " }", @"""lapse"": {}")));
            Assert.That(w.Room.Mods, Has.Count.EqualTo(1), Last(w));
            Assert.That(w.LapsesAt(w.Room.Mods[0]), Is.EqualTo(-1));
            Assert.That(w.TemporaryMaterials.Select(t => $"{t.Table} {t.Id} {t.Mod}"), Is.EqualTo(new[] { "covers black-ice black-ice" }));
        }

        [Test]
        public void TheRetiredListComesThroughASave()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce(20)));
            for (int t = 0; t < 25; t++) w.Step();
            var back = World.Load(w.Save(new ComboTests.Blank()), R, new ComboTests.Blank(), Budgets);
            Assert.That(back.RoomText(), Is.EqualTo(w.RoomText()));
            Assert.That(back.CastHash(), Is.EqualTo(w.CastHash()));
        }

        [Test]
        public void ALapseThatIsDueWhileLoadedStillComes()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce(20)));
            for (int t = 0; t < 10; t++) w.Step();
            var back = World.Load(w.Save(new ComboTests.Blank()), R, new ComboTests.Blank(), Budgets);
            for (int t = 0; t < 10; t++) back.Step();
            Assert.That(back.Room.Mods, Is.Empty);
            Assert.That(back.Rules.Fingerprint, Is.EqualTo(R.Fingerprint));
        }

        // ---------- the checks ----------

        static string[] Problems(string json, World room = null) =>
            ModValidator.Validate(room?.Rules ?? R, Budgets, json, room == null ? null : TempMaterials.Owners(room.TemporaryMaterials)).Select(p => p.Message).ToArray();

        [Test]
        public void TheBlackIceModPassesTheChecks()
        {
            Assert.That(Problems(BlackIce()), Is.Empty);
        }

        [Test]
        public void OnlyAMaterialTheModAddsLapses()
        {
            Assert.That(Problems(@"{ ""covers"": { ""frost"": { ""lapse"": { ""after"": 60 } } } }"),
                Has.Member("Cover frost is in the rules already, so it can't lapse; only a material the mod adds lapses."));
        }

        [Test]
        public void ALapseIsAnHourAtMost()
        {
            Assert.That(Problems(BlackIce(21601)), Has.Some.StartsWith("Cover black-ice lapses after 21601 ticks; a material lapses within 21600 ticks"));
            Assert.That(Problems(BlackIce(0)), Has.Some.Contains("lapse.after must be a whole number of ticks from 1"));
        }

        [Test]
        public void ALapseIntoMustStay()
        {
            Assert.That(Problems(@"{ ""covers"": { ""a"": { ""name"": ""A"", ""tags"": [], ""lapse"": { ""into"": ""b"" } }, ""b"": { ""name"": ""B"", ""tags"": [] } } }"),
                Has.Member("Cover a lapses into b, which the mod adds too and goes with it; lapse into a material the rules already have."));
            Assert.That(Problems(@"{ ""covers"": { ""a"": { ""name"": ""A"", ""tags"": [], ""lapse"": { ""into"": ""rock"" } } } }"),
                Has.Member("Cover a: lapse.into rock isn't a cover."));
        }

        [Test]
        public void AModAddsEightMaterialsAtMost()
        {
            string rows = string.Join(", ", Enumerable.Range(0, 9).Select(i => $@"""c{i}"": {{ ""name"": ""C{i}"", ""tags"": [] }}"));
            Assert.That(Problems("{ \"covers\": { " + rows + " } }"), Has.Member("The mod adds 9 materials; a mod may add at most 8."));
        }

        [Test]
        public void NoOtherModMayNameATemporaryMaterial()
        {
            var w = Room();
            w.ApplyRoom(RoomCommand.Apply("Bo", "black-ice", BlackIce()));
            const string leaning = @"{ ""title"": ""Thick ice"", ""patch"": { ""covers"": { ""black-ice"": { ""speed"": 0.5 } } } }";
            Assert.That(Problems(leaning, w), Has.Member("black-ice is a temporary material of the mod black-ice; only that mod may name it, as it goes when that mod does."));
            var v = w.ApplyRoom(RoomCommand.Apply("Ana", "thick-ice", leaning));
            Assert.That(v.Done, Is.False);
            Assert.That(v.Reason, Does.Contain("black-ice is a temporary material"));
        }

        [Test]
        public void ARoomHoldsThirtyTwoTemporaryMaterialsAtMost()
        {
            var w = Room();
            for (int m = 0; m < 5; m++)
            {
                string rows = string.Join(", ", Enumerable.Range(0, 7).Select(i => $@"""m{m}c{i}"": {{ ""name"": ""C"", ""tags"": [] }}"));
                var v = w.ApplyRoom(RoomCommand.Apply("Ana", "mod" + m, "{ \"covers\": { " + rows + " } }"));
                Assert.That(v.Done, Is.EqualTo(m < 4), v.Line);
            }
            Assert.That(Last(w), Is.EqualTo("Switch on refused: mod4 would make 35 temporary materials live; a room holds at most 32"));
        }
    }
}
