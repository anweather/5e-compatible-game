// The Hold's raider kinds made from the valley's animals (hold-bodies): each a rules row with a role and a body, mixed into the
// timed waves by the map's spawner, and each striking the keep at the heart like the others.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public partial class HoldTests
    {
        static readonly string[] Animals = { "blight-deer", "gloom-wolf", "barrow-bear", "storm-bison" };

        // The plain kinds a wave sends, in the order they come out.
        static List<string> KindsOfWave(int wave, int seed = 7)
        {
            var w = Running(seed);
            Away(w);
            w.Hold.Health = int.MaxValue / 2;
            w.Hold.Wave = wave;
            w.Hold.InWave = 0;
            w.Hold.ToSpawn = World.HoldWaveSize(R.Sim.Hold, wave);
            var seen = new HashSet<int>();
            var kinds = new List<string>();
            for (int i = 0; i < MostTicks && w.Hold.ToSpawn > 0; i++)
            {
                w.Step();
                foreach (var r in w.Raiders)
                    if (seen.Add(r.Id)) kinds.Add(Rules.PlainKind(r.Kind));
            }
            Assert.That(w.Hold.ToSpawn, Is.Zero, $"wave {wave} all out");
            return kinds;
        }

        [Test]
        public void EveryRaiderKindIsDrawnAsOneOfTheValleysAnimals()
        {
            var fauna = JObject.Parse(File.ReadAllText(Path.Combine(ChemistryTests.RulesDir(), "fauna.json")));
            foreach (var kind in Site.Spawners.SelectMany(s => s.Kinds).Distinct())
            {
                var raider = R.Entity(kind)?.Raider;
                Assert.That(raider, Is.Not.Null, kind);
                if (raider.Route == World.FlyRoute && raider.Body == null) continue; // drawn with wings of its own
                Assert.That(raider.Body, Is.Not.Null, $"{kind} has a body");
                Assert.That(fauna[raider.Body], Is.Not.Null, $"{kind}'s body {raider.Body} is a fauna.json species");
                Assert.That(raider.Size, Is.GreaterThan(0), kind);
            }
        }

        [Test]
        public void TheAnimalsEachHaveARole()
        {
            var deer = R.Entity("blight-deer");
            var wolf = R.Entity("gloom-wolf");
            var bear = R.Entity("barrow-bear");
            var bison = R.Entity("storm-bison");
            var others = Site.Spawners.SelectMany(s => s.Kinds).Distinct().Select(k => R.Entity(k)).ToList();
            Assert.That(deer.Raider.TicksPerSquare, Is.EqualTo(others.Min(e => e.Raider.TicksPerSquare)), "the deer is as fast as any");
            Assert.That(deer.Hp, Is.EqualTo(others.Min(e => e.Hp)), "and as frail as any");
            Assert.That(bear.Raider.TicksPerSquare, Is.EqualTo(others.Max(e => e.Raider.TicksPerSquare)), "the bear is as slow as any");
            Assert.That(bear.Hp, Is.EqualTo(others.Max(e => e.Hp)), "and the toughest");
            Assert.That(wolf.Raider.AttackEvery, Is.EqualTo(others.Min(e => e.Raider.AttackEvery)), "the wolf bites the most often");
            int Most(EntityDef e) => e.Raider.AttackDice[0] * e.Raider.AttackDice[1];
            Assert.That(Most(bison), Is.EqualTo(others.Max(Most)), "the bison's charge hits hardest");
            Assert.That(bison.Raider.TicksPerSquare, Is.LessThanOrEqualTo(3), "and comes fast");
            Assert.That(bison.Raider.AttackEvery, Is.EqualTo(others.Max(e => e.Raider.AttackEvery)), "but seldom");
        }

        // A kind more each wave, in the spawner's order, round and round: an animal by wave 2 and all four by wave 8.
        [Test]
        public void TheWavesMixTheAnimalsIn()
        {
            Assert.That(string.Join(" ", KindsOfWave(2)), Is.EqualTo("bone-hound blight-deer bone-hound blight-deer bone-hound blight-deer"));
            var eighth = KindsOfWave(8);
            foreach (var a in Animals) Assert.That(eighth, Does.Contain(a), $"wave 8 has {a}");
            Assert.That(eighth.Distinct().Count(), Is.EqualTo(8), "wave 8 has eight kinds");
            Assert.That(KindsOfWave(10).Distinct().Count(), Is.EqualTo(Site.Spawners[0].Kinds.Count), "the last wave has them all");
        }

        [TestCase("blight-deer")]
        [TestCase("gloom-wolf")]
        [TestCase("barrow-bear")]
        [TestCase("storm-bison")]
        public void EachAnimalStrikesTheKeep(string kind)
        {
            var w = Running();
            Away(w);
            w.Hold.ToSpawn = 0;
            int full = w.Hold.Health;
            var (hx, hy) = w.HoldSite.Heart;
            Assert.That(w.SpawnRaider(kind, hx + 1, hy, 4000), Is.Not.Null);
            Until(w, () => w.Hold.Health < full, $"the {kind} strikes the keep");
        }

        // The mixed waves on the valley's own rows, pinned: the kinds, their stats and their spawns are core state (D4). Moved for
        // hold-east-path: the wave comes from two spawners now, by two roads.
        [Test]
        public void AMixedWaveHashesAsItDid()
        {
            var w = Running();
            Away(w);
            w.Hold.Health = int.MaxValue / 2;
            w.Hold.Wave = 8;
            w.Hold.InWave = 0;
            w.Hold.ToSpawn = World.HoldWaveSize(R.Sim.Hold, 8);
            Ticks(w, 600);
            Assert.That(w.Raiders.Select(r => Rules.PlainKind(r.Kind)).Distinct().Count(), Is.GreaterThanOrEqualTo(6), "a mixed wave is out");
            Assert.That(w.CastHash(), Is.EqualTo("36029f4d"));
        }
    }
}
