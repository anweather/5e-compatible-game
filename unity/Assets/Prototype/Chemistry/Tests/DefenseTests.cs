using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The defense field (World.Defense.cs, World.Raiders.cs). Mirrors sandbox/test/defense.test.ts;
    /// the golden numbers come from running the same script there.
    /// </summary>
    public class DefenseTests
    {
        static Rules rules;

        static Rules R => rules ??= Rules.Load(ChemistryTests.RulesDir());

        static World Room(int w, int h, uint seed = 7) => ChemistryTests.Room(w, h, seed, R);

        static void Run(World w, int ticks) { for (int i = 0; i < ticks; i++) w.Step(); }

        static DefenseCommand Start(int gx, int gy, int fx, int fy) => new DefenseCommand(DefenseKind.Start, gx, gy, fx, fy);

        static readonly DefenseCommand Field = Start(2, 7, 36, 7);

        /// <summary>The same script as the sandbox's goldenWave: raiders walk, one is burnt, a wall reroutes them, a pool is shocked.</summary>
        static World GoldenWave(uint seed)
        {
            var w = Room(40, 15, seed);
            w.ApplyDefense(Field);
            Run(w, 40);
            var first = w.Raiders[0];
            w.Cast("fire-bolt", first.X, first.Y, 1);
            Run(w, 20);
            w.Cast("wall-of-stone", 20, 4, 2); // across the way, x 20, y 4..9
            Run(w, 30);
            var lead = w.Raiders[0];
            w.Cast("create-water", lead.X, lead.Y, 1);
            Run(w, 2);
            w.Cast("shocking-grasp", lead.X, lead.Y, 1); // where it has waded to
            Run(w, 300);
            return w;
        }

        // Every square any raider stood on over some ticks.
        static HashSet<(int, int)> Watch(World w, int ticks)
        {
            var seen = new HashSet<(int, int)>();
            for (int t = 0; t < ticks; t++)
            {
                w.Step();
                foreach (var r in w.Raiders) seen.Add((r.X, r.Y));
            }
            return seen;
        }

        [Test]
        public void TheDefenseRulesCheckOut()
        {
            Assert.That(R.Sim.Defense.Kinds, Is.EqualTo(new[] { "bone-hound", "ash-crab", "giant-spider" }));
            Assert.That(R.Entity("ash-crab").Raider.Resist, Is.EqualTo(new[] { "fire" }));
            var ex = Assert.Throws<ModException>(() => RuleSet.Plain(R).WithMods(new[] { new ActiveMod("bad", "{ \"sim\": { \"defense\": { \"kinds\": [\"goblin\"] } } }") }));
            Assert.That(ex.Errors, Is.EqualTo(new[] { "sim defense: kind goblin is not a raider" }));
        }

        [Test]
        public void AWaveStartsAfterTheWarmupAndItsRaidersComeThroughTheGateOnSchedule()
        {
            var w = Room(40, 15);
            w.ApplyDefense(Field);
            var p = R.Sim.Defense;
            Run(w, p.Warmup - 1);
            Assert.That(w.Raiders, Is.Empty);
            Run(w, 1);
            Assert.That(w.Raiders.Select(r => (r.Kind, r.X, r.Y)), Is.EqualTo(new[] { (p.Kinds[0], 2, 7) }));
            Run(w, p.SpawnEvery);
            Assert.That(w.Raiders.Count, Is.EqualTo(2));
            Assert.That(w.Raiders[0].X, Is.GreaterThan(2), "the first is on its way east");
            Assert.That(w.DefenseLeft, Is.EqualTo(World.WaveSize(p, 1)));
        }

        [Test]
        public void RaidersWalkRoundARaisedWallRatherThanThroughIt()
        {
            var w = Room(40, 15);
            for (int y = 4; y <= 9; y++) w.Reshape(20, y, 3);
            w.ApplyDefense(Field);
            var seen = Watch(w, 400);
            for (int y = 4; y <= 9; y++) Assert.That(seen, Does.Not.Contain((20, y)), $"nobody stood in the wall at (20, {y})");
            Assert.That(seen.Contains((20, 3)) || seen.Contains((20, 10)), "they went round an end");
        }

        [Test]
        public void AFullWallIsBatteredDown()
        {
            var w = Room(40, 15);
            for (int y = 1; y <= 13; y++) w.Reshape(20, y, 3);
            w.ApplyDefense(Field);
            Run(w, 400);
            Assert.That(Enumerable.Range(1, 13).Any(y => !w.IsRaisedWall(w.At(20, y))), "a raider battered its way through");
            Assert.That(w.Defense.Lives, Is.LessThan(R.Sim.Defense.Lives));
        }

        [Test]
        public void ATrenchDugAheadOfThemReroutesThem()
        {
            var w = Room(40, 15);
            w.ApplyDefense(Field);
            Run(w, 40);
            Assert.That(w.PathCostAt(19, 7), Is.EqualTo(17), "straight along the row");
            for (int y = 6; y <= 8; y++) w.Reshape(20, y, -2);
            var seen = Watch(w, 300);
            for (int y = 6; y <= 8; y++) Assert.That(seen, Does.Not.Contain((20, y)), $"nobody went down into the trench at (20, {y})");
            Assert.That(seen.Contains((20, 5)) || seen.Contains((20, 9)));
            Assert.That(w.PathCostAt(19, 7), Is.EqualTo(21), "round the trench's end");
        }

        [Test]
        public void FireHurtsAnAshCrabHalfAsMuchAsAGiantSpiderAndASoakedCrabIsShockedToDeath()
        {
            int Loss(string kind)
            {
                var w = Room(20, 9, 3);
                var r = w.SpawnRaider(kind, 8, 4, 40);
                w.Cast("fire-bolt", 8, 4, 1);
                return 40 - r.Hp;
            }
            int spider = Loss("giant-spider"), crab = Loss("ash-crab");
            Assert.That(spider, Is.GreaterThan(0));
            Assert.That(crab, Is.EqualTo(spider - spider / 2));

            var pool = Room(20, 9, 3);
            pool.SpawnRaider("ash-crab", 8, 4, 9);
            pool.Cast("create-water", 8, 4, 1);
            pool.Step();
            pool.Cast("shocking-grasp", 9, 4, 1);
            pool.Step();
            Assert.That(pool.Raiders, Is.Empty, "wet doubles the shock and the crab is weak to lightning");
        }

        [Test]
        public void ARaiderReachingTheFlagCostsALifeAndOutOfLivesTheDefenseIsLost()
        {
            var w = Room(12, 9);
            w.ApplyDefense(Start(3, 4, 6, 4));
            int lives = R.Sim.Defense.Lives;
            for (int t = 0; t < 60 && w.Defense.Lives == lives; t++) w.Step();
            Assert.That(w.Defense.Lives, Is.EqualTo(lives - 1));
            for (int t = 0; t < 2000 && w.Defense.Phase == DefensePhase.Running; t++) w.Step();
            Assert.That(w.Defense.Phase, Is.EqualTo(DefensePhase.Lost));
            Assert.That(w.Raiders, Is.Empty);
        }

        [Test]
        public void TheNextWaveComesWhenTheLastIsGoneAndAllFiveWin()
        {
            var w = Room(40, 15);
            w.ApplyDefense(Field);
            var p = R.Sim.Defense;
            for (int t = 0; t < 3000 && w.Defense.Wave == 1; t++)
            {
                w.Step();
                foreach (var r in w.Raiders.ToList()) w.Cast("fire-bolt", r.X, r.Y, 3);
            }
            Assert.That(w.Defense.Wave, Is.EqualTo(2));
            Assert.That(w.Defense.ToSpawn, Is.EqualTo(World.WaveSize(p, 2)));
            Assert.That(w.Defense.NextSpawn, Is.EqualTo(w.Tick + p.BetweenWaves));
            for (int t = 0; t < 6000 && w.Defense.Phase == DefensePhase.Running; t++)
            {
                w.Step();
                foreach (var r in w.Raiders.ToList()) w.Cast("fire-bolt", r.X, r.Y, 3);
            }
            Assert.That(w.Defense.Phase, Is.EqualTo(DefensePhase.Won));
            Assert.That(w.Defense.Wave, Is.EqualTo(p.Waves));
            w.ApplyDefense(Field);
            Assert.That(w.Defense.Phase, Is.EqualTo(DefensePhase.Running), "start again after a win");
        }

        [Test]
        public void ALateJoinerReplayingTheStartCommandMatchesTheLiveWorld()
        {
            var live = Room(40, 15);
            var rs = RuleSet.Plain(R);
            for (int t = 1; t <= 200; t++)
            {
                var batch = new TickBatch(t);
                if (t == 5) batch.Defense.Add(Field);
                if (t == 80) batch.Casts.Add(new CastCommand("fireball", 12, 7, 1));
                batch.RunOn(live, rs, out _);
            }
            var replay = Room(40, 15);
            var timeline = TickBatch.Timeline(new[] { (80, new CastCommand("fireball", 12, 7, 1)) }, null, 200, defense: new[] { (5, Field) });
            for (int t = 1; t <= 200; t++) timeline[t].RunOn(replay, rs, out _);
            Assert.That(replay.DefenseHash(), Is.EqualTo(live.DefenseHash()));
            Assert.That(replay.CellHash(), Is.EqualTo(live.CellHash()));
            Assert.That(live.Defense.Spawned, Is.GreaterThan(0));
        }

        [Test]
        public void TheDefenseIsDeterministicAndMatchesTheTsGolden()
        {
            World a = GoldenWave(11), b = GoldenWave(11);
            Assert.That(a.DefenseText(), Is.EqualTo(b.DefenseText()));
            Assert.That($"{a.DefenseHash()} {a.CellHash()} {a.RngState}", Is.EqualTo("7bea4d9e a8fce715 3663131637"));
            Assert.That(a.DefenseText().Split('|')[0], Is.EqualTo("1/2/9/0/346/8"));
            Assert.That(a.Raiders.Any(r => r.X == 19 && r.Y == 10), "one is going round the wall's south end");
        }
    }
}
