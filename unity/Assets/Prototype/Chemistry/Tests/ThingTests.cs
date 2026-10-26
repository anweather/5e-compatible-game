using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The world's props and items as data (world/things.json, WorldThings.cs): stable ids from
    /// chunk + index, the same however often and wherever the file is read, and carried by the
    /// lockstep effects a prop hands the chemistry.
    /// </summary>
    public class ThingTests
    {
        static Rules R => ChemistryTests.R;

        /// <summary>The repo's world/ folder, next to rules/.</summary>
        static string WorldDir() => Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\')), "world");

        static string FileText => File.ReadAllText(Path.Combine(WorldDir(), WorldThings.FileName));

        static WorldThings Valley => WorldThings.Load(WorldDir());

        static WorldThings Parse(string json) => WorldThings.FromJson(json.Replace('\'', '"'));

        [Test]
        public void TheValleysThingsLoadAndFitTheRules()
        {
            var things = Valley;
            Assert.That(things.Count, Is.EqualTo(19), "10 props and 9 items: the yard's, and the court's ball");
            Assert.That(things.Check(R), Is.Empty);
            Assert.That(things.All.Count(t => R.Entity(t.Kind) != null), Is.EqualTo(10));
            Assert.That(things.All.Count(t => R.Item(t.Kind) != null), Is.EqualTo(9));
            Assert.That(things.ChunkCoords, Is.EquivalentTo(new[] { (4, 4), (4, 5), (5, 5) }), "the old yard straddles two chunks; the court is east of it");

            // The crate the online probe throws: yard cell (16, 6), the yard at world cell (279, 315).
            var crate = things.At(279 + 16, 315 + 6);
            Assert.That(crate.Kind, Is.EqualTo("crate"));
            Assert.That(crate.Id.ToString(), Is.EqualTo("4,5#2"));
            Assert.That((crate.ChunkX, crate.ChunkY, crate.LocalX, crate.LocalY), Is.EqualTo((4, 5, 39, 1)));
        }

        [Test]
        public void IdsAreStableAcrossLoads()
        {
            var first = Valley.All.Select(t => (t.Id, t.Kind, t.CellX, t.CellY)).ToList();
            var second = WorldThings.FromJson(FileText).All.Select(t => (t.Id, t.Kind, t.CellX, t.CellY)).ToList();
            Assert.That(second, Is.EqualTo(first));
            Assert.That(WorldThings.FromJson(FileText).Fingerprint, Is.EqualTo(Valley.Fingerprint));
        }

        [Test]
        public void IdsAreDistinctAndNameTheirChunk()
        {
            var things = Valley;
            Assert.That(things.All.Select(t => t.Id).Distinct().Count(), Is.EqualTo(things.Count));
            foreach (var t in things.All)
            {
                Assert.That((t.Id.ChunkX, t.Id.ChunkY), Is.EqualTo((t.ChunkX, t.ChunkY)), $"{t.Id}");
                Assert.That(things.InChunk(t.ChunkX, t.ChunkY)[t.Id.Index], Is.SameAs(t));
                Assert.That(things.Find(t.Id), Is.SameAs(t));
                Assert.That(new ThingId(t.Id.Value), Is.EqualTo(t.Id), "the wire form round-trips");
            }
            Assert.That(things.InChunk(0, 0), Is.Empty);
            Assert.That(things.Find(ThingId.None), Is.Null);
        }

        [Test]
        public void IdsPackNegativeChunksAndNeverMeetNone()
        {
            var id = new ThingId(-3, -1, 7);
            Assert.That((id.ChunkX, id.ChunkY, id.Index), Is.EqualTo((-3, -1, 7)));
            Assert.That(id.ToString(), Is.EqualTo("-3,-1#7"));
            Assert.That(new ThingId(0, 0, 0), Is.Not.EqualTo(ThingId.None));
            Assert.That(new ThingId(-1, -1, 0), Is.Not.EqualTo(ThingId.None));
            Assert.That(new ThingId(1, 0, 0), Is.Not.EqualTo(new ThingId(0, 1, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ThingId(40000, 0, 0));
        }

        [Test]
        public void IdsComeFromChunkAndIndexNotFromWhereTheChunkIsListed()
        {
            const string a = "{'chunks':[{'chunk':[0,0],'things':[{'kind':'crate','cell':[1,2]},{'kind':'barrel','cell':[3,4]}]}," +
                             "{'chunk':[-1,2],'things':[{'kind':'boulder','cell':[63,0],'rot':90}]}]}";
            const string b = "{'chunks':[{'chunk':[-1,2],'things':[{'kind':'boulder','cell':[63,0],'rot':90,'note':'moved up the file'}]}," +
                             "{'chunk':[0,0],'things':[{'kind':'crate','cell':[1,2]},{'kind':'barrel','cell':[3,4]}]}]}";
            var x = Parse(a);
            var y = Parse(b);
            foreach (var t in x.All)
            {
                var same = y.Find(t.Id);
                Assert.That((same.Kind, same.CellX, same.CellY, same.Rotation), Is.EqualTo((t.Kind, t.CellX, t.CellY, t.Rotation)));
            }
            Assert.That(y.Fingerprint, Is.EqualTo(x.Fingerprint), "notes and chunk order don't count");
            var boulder = x.Find(new ThingId(-1, 2, 0));
            Assert.That((boulder.CellX, boulder.CellY, boulder.Rotation), Is.EqualTo((-1, 128, 90)), "world cell = chunk * 64 + cell");
        }

        [Test]
        public void TheFingerprintChangesWhenAThingMovesOrChanges()
        {
            const string at = "{'chunks':[{'chunk':[0,0],'things':[{'kind':'crate','cell':[1,2]}]}]}";
            var baseline = Parse(at).Fingerprint;
            Assert.That(Parse(at.Replace("[1,2]", "[1,3]")).Fingerprint, Is.Not.EqualTo(baseline));
            Assert.That(Parse(at.Replace("crate", "barrel")).Fingerprint, Is.Not.EqualTo(baseline));
            Assert.That(Parse(at.Replace("[1,2]}", "[1,2],'rot':45}")).Fingerprint, Is.Not.EqualTo(baseline));
            Assert.That(WorldThings.Empty.Fingerprint, Is.Not.EqualTo(baseline));
        }

        [Test]
        public void TheCheckNamesUnknownKindsAndLinksThatArentPairs()
        {
            var things = Parse("{'chunks':[{'chunk':[0,0],'things':[{'kind':'crate','cell':[0,0]},{'kind':'piano','cell':[1,0]}," +
                               "{'kind':'sending-stone','cell':[2,0],'link':'a'},{'kind':'sending-stone','cell':[3,0],'link':'a'}," +
                               "{'kind':'sending-stone','cell':[4,0],'link':'b'}]}]}");
            var errors = things.Check(R);
            Assert.That(errors, Has.Count.EqualTo(2));
            Assert.That(errors[0], Does.Contain("0,0#1").And.Contain("piano"));
            Assert.That(errors[1], Does.Contain("link \"b\" joins 1"));
            var stone = things.Find(new ThingId(0, 0, 2));
            Assert.That(things.LinkedTo(stone).Select(t => t.Id), Is.EqualTo(new[] { new ThingId(0, 0, 3) }));
            Assert.That(things.LinkedTo(things.Find(new ThingId(0, 0, 0))), Is.Empty);
        }

        [Test]
        public void AFileThatCantBeReadSaysWhy()
        {
            Assert.Throws<FormatException>(() => Parse("{'chunks':[{'chunk':[0,0],'things':[{'kind':'crate','cell':[64,0]}]}]}"));
            Assert.Throws<FormatException>(() => Parse("{'chunks':[{'chunk':[0,0],'things':[{'cell':[1,0]}]}]}"));
            Assert.Throws<FormatException>(() => Parse("{'chunks':[{'chunk':[0,0],'things':[]},{'chunk':[0,0],'things':[]}]}"));
            Assert.Throws<FormatException>(() => Parse("{'chunks':[{'chunk':[0],'things':[]}]}"));
            Assert.That(WorldThings.Load(Path.Combine(WorldDir(), "no-such-folder")).Count, Is.EqualTo(0), "no file: no things");
        }

        [Test]
        public void HostAndClientNameTheSameThingsAndReplayTheirEffectsAlike()
        {
            // Each machine reads its own copy of the file.
            var hostThings = WorldThings.FromJson(FileText);
            var clientThings = WorldThings.FromJson(FileText);
            Assert.That(clientThings.All.Select(t => t.Id), Is.EqualTo(hostThings.All.Select(t => t.Id)));
            Assert.That(clientThings.Fingerprint, Is.EqualTo(hostThings.Fingerprint));

            // The host's barrel in the oil blows up and a crate burns: effects keyed by the things' ids,
            // on a world shaped like the yard (the things' world cells, offset into a small field).
            var barrel = hostThings.All.First(t => t.Kind == "barrel");
            var crate = hostThings.All.First(t => t.Kind == "crate");
            int ox = barrel.CellX - 10, oy = barrel.CellY - 5;
            EffectCommand Of(EffectKind kind, ThingSpawn t) => new EffectCommand(kind, t.CellX - ox, t.CellY - oy, t.Kind, t.Id);
            var live = new List<TickBatch>();
            for (int tick = 1; tick <= 20; tick++)
            {
                var b = new TickBatch(tick);
                if (tick == 3) b.Effects.Add(Of(EffectKind.Explode, barrel));
                if (tick == 8) b.Effects.Add(Of(EffectKind.Burn, crate));
                live.Add(b);
            }

            World Field()
            {
                var w = new World(R, 30, 12, 7, "grass");
                for (int y = 4; y <= 6; y++)
                    for (int x = 9; x <= 11; x++) w.SetCover(x, y, "oil");
                return w;
            }
            World RunAll(IEnumerable<TickBatch> batches)
            {
                var w = Field();
                var rules = RuleSet.Plain(R);
                foreach (var b in batches) rules = b.RunOn(w, rules, out _);
                return w;
            }

            var host = RunAll(live);
            var history = live.SelectMany(b => b.Effects.Select(e => (b.Tick, e))).ToList();
            var timeline = TickBatch.Timeline(Enumerable.Empty<(int, CastCommand)>(), null, 20, history);
            var replayed = Enumerable.Range(1, 20).Select(t => timeline[t]).ToList();
            var client = RunAll(replayed);

            Assert.That(client.CellHash(), Is.EqualTo(host.CellHash()));
            var sent = replayed.SelectMany(b => b.Effects).Select(e => e.Thing).ToList();
            Assert.That(sent, Is.EqualTo(new[] { barrel.Id, crate.Id }), "the late joiner's history names the same things");
            Assert.That(sent.Select(id => clientThings.Find(id).Kind), Is.EqualTo(new[] { "barrel", "crate" }));
        }
    }
}
