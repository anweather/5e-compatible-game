using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// #88: anything a mod adds (ModAdds: a material, a style, a building row, a species, a part, a trigger) makes
    /// a mod naming it its dependent, which goes with it, reverted or lapsed, at the same tick on every peer
    /// (World.Lapse.cs). A mod naming a row whose owner went is refused naming the owner.
    /// </summary>
    [Category("Simulation")]
    public class ModDependentsTests
    {
        const int After = 12;

        static Rules R => ChemistryTests.R;
        static ModBudgets budgets;
        static ModBudgets Budgets => budgets ??= ModBudgets.Load(ChemistryTests.RulesDir());

        // A cover that lapses After ticks after its mod goes on, or stays while it's on: what lets an owner lapse.
        static string Dust(bool lapses) =>
            "\"covers\": { \"dust\": { \"name\": \"Dust\", \"tags\": []" + (lapses ? ", \"lapse\": { \"after\": " + After + " }" : "") + " } }";

        static string Style => BuildingTests.SouthwestStyle.Replace("clay-tile", "slate");

        // The house adds only a style (and a dust its wing doesn't name, to lapse by).
        static string House(bool lapses) => "{ \"title\": \"Adobe\", \"patch\": { " + Dust(lapses) + ", \"buildings\": { \"styles\": { \"adobe\": " + Style + " } } } }";

        // The wing is a building row in the house's style: it names no material of the house.
        static string Wing => "{ \"title\": \"A wing\", \"patch\": { \"world\": { \"buildings\": { \"lakeside#1\": "
            + BuildingTests.StuccoRow(20, 20).Replace("southwest", "adobe").Replace("clay-tile", "slate") + " } } } }";

        static string Species(bool lapses)
        {
            var deer = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "fauna.json")))["deer"];
            deer["name"] = "Moon deer";
            return "{ \"title\": \"Moon deer\", \"patch\": { " + Dust(lapses) + ", \"fauna\": { \"moon-deer\": " + deer.ToString(Newtonsoft.Json.Formatting.None) + " } } }";
        }

        const string Variant = "{ \"title\": \"Moon fawn\", \"patch\": { \"fauna\": { \"moon-fawn\": { \"name\": \"Moon fawn\", \"variantOf\": \"moon-deer\", \"scale\": 0.6 } } } }";

        static World Room()
        {
            var w = new World(R, 64, 64, 7, "grass");
            w.OpenRoom(ChangeMode.Open, "Ana", R, Budgets);
            return w;
        }

        sealed class Peers
        {
            public readonly World Host = Room(), Peer = Room();

            static string State(World w) =>
                $"{w.CellHash()} {w.CastHash()} {w.Rules.Fingerprint} {string.Join(",", w.Room.Mods.Select(m => m.Id))}";

            public RoomVerdict Both(RoomCommand c)
            {
                var a = Host.ApplyRoom(c);
                Assert.That(Peer.ApplyRoom(c).Line, Is.EqualTo(a.Line), "every peer says the same");
                Step();
                return a;
            }

            public void Step()
            {
                Host.Step();
                Peer.Step();
                Assert.That(State(Peer), Is.EqualTo(State(Host)), "host and peer agree");
            }

            public void Each(System.Action<World> check)
            {
                check(Host);
                check(Peer);
            }
        }

        // The owner on, then a mod over it, then an unrelated mod after; on a host and a peer.
        static Peers OwnerAndDependent(string owner, string dependent)
        {
            var p = new Peers();
            foreach (var (id, json) in new[] { ("owner", owner), ("dependent", dependent), ("other", "{ \"covers\": { \"frost\": { \"speed\": 0.9 } } }") })
            {
                var v = p.Both(RoomCommand.Apply("Ana", id, json));
                Assert.That(v.Done, Is.True, v.Line);
            }
            return p;
        }

        static void OnlyOtherLeft(Peers p, string line) => p.Each(w =>
        {
            Assert.That(w.Room.Mods.Select(m => m.Id), Is.EqualTo(new[] { "other" }));
            Assert.That(w.Log.Select(l => l.Text), Has.Member(line));
        });

        [Test]
        public void WhatAModAddsIsListedWithItsOwner()
        {
            var p = OwnerAndDependent(House(false), Wing);
            Assert.That(p.Host.ModAdded.Select(a => $"{a.Kind} {a.Id} {a.Mod}"), Is.EqualTo(new[] { "material dust owner", "style adobe owner", "building lakeside#1 dependent" }));
            Assert.That(p.Host.WithDependents(0).Select(m => m.Id), Is.EqualTo(new[] { "owner", "dependent" }));
            Assert.That(p.Host.Rules.ModBuildings.Single().Json["style"]?.ToString(), Is.EqualTo("adobe"));
        }

        [Test]
        public void AWingWhoseHouseAddsOnlyAStyleGoesWithItsRevert()
        {
            var p = OwnerAndDependent(House(false), Wing);
            var off = p.Both(RoomCommand.Revert("Bo", "owner"));
            Assert.That(off.Done, Is.True, off.Line);
            OnlyOtherLeft(p, "Bo switched off Adobe, and A wing with it (Open room)");
            p.Each(w => Assert.That(w.Rules.ModBuildings, Is.Empty));
        }

        [Test]
        public void AWingWhoseHouseAddsOnlyAStyleGoesWithItsLapse()
        {
            var p = OwnerAndDependent(House(true), Wing);
            int at = p.Host.LapsesAt(p.Host.Room.Mods[0]);
            while (p.Host.Tick < at - 1) p.Step();
            Assert.That(p.Host.Room.Mods.Count, Is.EqualTo(3), "all stand until the lapse");
            p.Step();
            OnlyOtherLeft(p, "Adobe lapsed, and A wing with it (Open room)");
        }

        [Test]
        public void ASpeciesVariantGoesWithItsBaseSpeciesRevert()
        {
            var p = OwnerAndDependent(Species(false), Variant);
            var off = p.Both(RoomCommand.Revert("Bo", "owner"));
            OnlyOtherLeft(p, "Bo switched off Moon deer, and Moon fawn with it (Open room)");
            Assert.That(off.Done, Is.True);
        }

        [Test]
        public void ASpeciesVariantGoesWithItsBaseSpeciesLapse()
        {
            var p = OwnerAndDependent(Species(true), Variant);
            int at = p.Host.LapsesAt(p.Host.Room.Mods[0]);
            while (p.Host.Tick < at) p.Step();
            OnlyOtherLeft(p, "Moon deer lapsed, and Moon fawn with it (Open room)");
        }

        // The house went, so the room knows whose adobe was: the wing's refusal names it, the same on every peer,
        // and in the save and the checksum.
        [Test]
        public void AWingAfterItsHouseWentIsRefusedNamingTheHouse()
        {
            var p = OwnerAndDependent(House(false), Wing);
            p.Both(RoomCommand.Revert("Bo", "owner"));
            var again = p.Both(RoomCommand.Apply("Ana", "dependent", Wing));
            Assert.That(again.Done, Is.False);
            Assert.That(again.Reason, Is.EqualTo("A wing names adobe, a style of the mod owner, which isn't on; switch owner on first"));
            p.Each(w => Assert.That(w.Room.RetiredRows.Select(r => $"{r.Kind} {r.Id} {r.Mod}"), Is.EqualTo(new[] { "style adobe owner", "building lakeside#1 dependent" })));
            Assert.That(p.Host.RoomText(), Does.Contain("|gone:style:adobe:owner:"));

            var loaded = World.Load(p.Host.Save(new ComboTests.Blank()), R, new ComboTests.Blank(), Budgets);
            Assert.That(loaded.RoomText(), Is.EqualTo(p.Host.RoomText()));
            Assert.That(loaded.ApplyRoom(RoomCommand.Apply("Ana", "dependent", Wing)).Reason, Is.EqualTo(again.Reason));

            // The house on again takes its style out of the list, and the wing goes on over it.
            Assert.That(p.Both(RoomCommand.Apply("Ana", "owner", House(false))).Done, Is.True);
            p.Each(w => Assert.That(w.Room.RetiredRows.Select(r => r.Id), Is.EqualTo(new[] { "lakeside#1" })));
            Assert.That(p.Both(RoomCommand.Apply("Ana", "dependent", Wing)).Done, Is.True);
            p.Each(w => Assert.That(w.Room.RetiredRows, Is.Empty));
        }

        [Test]
        public void ANeverOnHouseStillLeavesTheChecksToSay()
        {
            var w = Room();
            var v = w.ApplyRoom(RoomCommand.Apply("Ana", "dependent", Wing));
            Assert.That(v.Reason, Does.StartWith("A wing doesn't pass the checks: Building lakeside#1: unknown style adobe"));
            Assert.That(w.RoomText(), Does.Not.Contain("|gone:"), "a room with nothing gone hashes as it did");
        }
    }
}
