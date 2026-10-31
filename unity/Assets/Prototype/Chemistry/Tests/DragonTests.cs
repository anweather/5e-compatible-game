using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    /// <summary>
    /// The Dragon's rules (World.Dragon.cs, docs/BACKLOG.md 26). The golden was first taken from
    /// sandbox/test/dragon.test.ts (frozen 2026-10-31); the save, the inactive chunks and the timing are C# only.
    /// </summary>
    public class DragonTests
    {
        static Rules R => ChemistryTests.R;

        static Rules With(string dragon) => R.WithMods(new[] { ModPatch.Parse("{ \"sim\": { \"dragon\": " + dragon + " } }") });

        static void Run(World w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static void FlyOut(World w, Action onTick = null)
        {
            for (int i = 0; w.Dragon != null && i < 20000; i++)
            {
                w.Step();
                onTick?.Invoke();
            }
            Assert.That(w.Dragon, Is.Null, "the dragon should have left");
        }

        // A bare world with its starting state kept, then changes everywhere.
        static World ChangedWorld(int width, int height, uint seed)
        {
            var w = new World(R, width, height, seed);
            w.KeepStart();
            for (int y = 0; y < height; y += 3)
                for (int x = 0; x < width; x += 3) w.At(x, y).Height = 1;
            return w;
        }

        // The golden's map: a grass patch, a stone wall with a gap, a walled pond and four raiders: in the open, under water, between two stones, far off.
        static World GoldenWorld(out List<Raider> raiders, out string start)
        {
            var w = new World(R, 256, 128, 7);
            for (int y = 10; y <= 20; y++)
                for (int x = 20; x <= 30; x++) w.At(x, y).T = "grass";
            for (int y = 0; y < 128; y++)
                if (y < 40 || y > 50) w.At(60, y).T = "stone";
            for (int y = 69; y <= 80; y++)
                for (int x = 89; x <= 100; x++)
                {
                    if (y == 69 || y == 80 || x == 89 || x == 100) w.At(x, y).T = "stone";
                    else w.At(x, y).Water = 12;
                }
            w.At(70, 44).T = "stone";
            w.At(70, 46).T = "stone";
            raiders = new List<Raider> { w.SpawnRaider("bone-hound", 10, 10, 8), w.SpawnRaider("bone-hound", 95, 75, 8), w.SpawnRaider("bone-hound", 70, 45, 8), w.SpawnRaider("bone-hound", 200, 60, 8) };
            w.KeepStart();
            start = w.CellHash();
            var lit = w.At(22, 12);
            lit.Fire = 2;
            lit.Fuel = 6;
            w.At(80, 30).Height = 2;
            w.At(81, 30).T = "mud";
            w.At(5, 90).Cover = "ash";
            return w;
        }

        static readonly string[] Golden =
        {
            "60 7da842d 2298224275 1:4:75|1:190:0:0:0:0:2:0:0",
            "120 ac7e1b26 3150359324 1:0:120|1:190:0:0:0:0:2:0:0",
            "180 acbbcba 3654312600 1:4:120|1:190:0:0:0:0:2:0:0",
            "240 a30c8004 3654312600 1:0:0|2:0:0:0:0:1:2:12:2",
            "300 a30c8004 3654312600 1:4:0|3:382:0:0:0:1:2:0:6",
            "360 a30c8004 3654312600 1:0:0|3:382:0:0:0:1:2:0:6",
            "382 a30c8004 3654312600 1:6:0",
        };

        static string Line(World w) => $"{w.Tick} {w.CellHash()} {w.RngState} {w.DragonText()}";

        [Test]
        public void GoldenAFullEventOnASmallMapMatchesTheSandbox()
        {
            var w = GoldenWorld(out var raiders, out string start);
            Run(w, 10);
            w.ApplyDragon(new DragonCommand());
            var seen = new List<string>();
            var fell = new int[raiders.Count];
            FlyOut(w, () =>
            {
                if (w.Tick % 60 == 0) seen.Add(Line(w));
                for (int i = 0; i < fell.Length; i++)
                    if (raiders[i].StatusOf(World.DownedStatus) > 0 && fell[i] == 0) fell[i] = w.Tick;
            });
            seen.Add(Line(w));
            Assert.That(seen, Is.EqualTo(Golden));
            Assert.That(fell, Is.EqualTo(new[] { 192, 0, 0, 250 }));
            Assert.That(w.CellHash(), Is.EqualTo(start), "the valley ends as it started");
            Assert.That(w.ChangedCount, Is.EqualTo(0));
        }

        [Test]
        public void TheSameEventSteppedInSlicesIsTheSameEvent()
        {
            var w = GoldenWorld(out _, out _);
            Run(w, 10);
            w.ApplyDragon(new DragonCommand());
            var seen = new List<string>();
            while (w.Dragon != null || w.TickInProgress)
            {
                bool done;
                do done = w.StepSome(() => true);
                while (!done);
                if (w.Tick % 60 == 0) seen.Add(Line(w));
            }
            seen.Add(Line(w));
            Assert.That(seen, Is.EqualTo(Golden));
        }

        [Test]
        public void AWorldTheDragonNeverComesToKeepsItsCellsAndRandomNumbers()
        {
            static World Build()
            {
                var w = new World(R, 70, 70, 3);
                for (int y = 0; y < 70; y++)
                    for (int x = 0; x < 70; x++)
                        if ((x + y) % 3 == 0) w.At(x, y).T = "grass";
                w.At(30, 30).Fire = 2;
                w.At(30, 30).Fuel = 8;
                return w;
            }
            World plain = Build(), kept = Build();
            kept.KeepStart();
            Run(plain, 200);
            Run(kept, 200);
            Assert.That(kept.CellHash(), Is.EqualTo(plain.CellHash()));
            Assert.That(kept.RngState, Is.EqualTo(plain.RngState));
            Assert.That(kept.DragonFlights, Is.EqualTo(0));
            Assert.That(plain.DragonText(), Is.EqualTo(""));
        }

        [TestCase(64, 64)]
        [TestCase(128, 96)]
        [TestCase(300, 140)]
        [TestCase(140, 300)]
        [TestCase(65, 1)]
        [TestCase(200, 200)]
        public void EveryChunkIsResetByTheLastPassWhateverThePath(int width, int height)
        {
            for (uint seed = 1; seed <= 12; seed++)
            {
                var w = ChangedWorld(width, height, seed);
                w.ApplyDragon(new DragonCommand());
                FlyOut(w);
                Assert.That(w.Cells.All(c => c.Height == 0), $"seed {seed}");
                Assert.That(w.ChangedCount, Is.EqualTo(0));
            }
        }

        [Test]
        public void AWildfireDoesntOutliveTheFlight()
        {
            var w = new World(R, 192, 192, 4, "grass");
            w.KeepStart();
            for (int k = 0; k < 6; k++)
            {
                var c = w.At((k * 97 + 13) % 192, (k * 61 + 29) % 192);
                c.Fire = 2;
                c.Fuel = 8;
            }
            Run(w, 30);
            w.ApplyDragon(new DragonCommand());
            Run(w, R.Sim.Dragon.WarningTicks + 1);
            Assert.That(w.Cells.All(c => c.Fire == 0));
            FlyOut(w);
            Assert.That(w.Cells.All(c => c.Fire == 0 && c.T == "grass" && c.Cover == null));
            Assert.That(w.ChangedCount, Is.EqualTo(0));
        }

        [Test]
        public void TheWarningLastsAndAtMostChunksPerTickResetATick()
        {
            var w = ChangedWorld(200, 200, 2);
            var fresh = new List<int>();
            w.ChunkFreshened += _ => fresh.Add(w.Tick);
            w.ApplyDragon(new DragonCommand());
            int at = w.Tick;
            while (w.Dragon.Phase == DragonPhase.Warning) w.Step();
            Assert.That(w.Tick - at, Is.EqualTo(R.Sim.Dragon.WarningTicks));
            FlyOut(w);
            Assert.That(fresh.GroupBy(t => t).Max(g => g.Count()), Is.LessThanOrEqualTo(R.Sim.Dragon.ChunksPerTick));
            Assert.That(fresh.Distinct().Count(), Is.GreaterThan(1), "spread over ticks");
        }

        [Test]
        public void TheShelterRuleIsDeepWaterOrAWallOnTheDragonsSide()
        {
            var w = new World(R, 128, 64, 2);
            w.KeepStart();
            w.ApplyDragon(new DragonCommand());
            int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            while (!w.BreathBox(out x0, out y0, out x1, out y1)) w.Step();
            var d = w.Dragon;
            int x = (x0 + x1) / 2, y = (y0 + y1) / 2;
            int back = (d.Back ^ (d.Pass % 2 == 1)) ? 1 : -1;
            var near = d.AlongX ? w.At(x + back * 2, y) : w.At(x, y + back * 2);
            var far = d.AlongX ? w.At(x - back, y) : w.At(x, y - back);
            Assert.That(w.CaughtByBreath(x, y), "in the open");
            far.T = "stone";
            Assert.That(w.CaughtByBreath(x, y), "a wall on the far side is no use");
            far.T = "dirt";
            near.T = "stone";
            Assert.That(w.CaughtByBreath(x, y), Is.False, "behind stone");
            near.T = "rock";
            Assert.That(w.CaughtByBreath(x, y), "rock that doesn't block is no wall");
            near.Height = R.Sim.Height.BlockAt;
            Assert.That(w.CaughtByBreath(x, y), Is.False, "Wall of Stone's raised rock shelters");
            near.T = "dirt";
            Assert.That(w.CaughtByBreath(x, y), "a raised earth wall doesn't");
            near.Height = 0;
            w.At(x, y).Water = R.Sim.Dragon.ShelterWater - 1;
            Assert.That(w.CaughtByBreath(x, y), "shallow water");
            w.At(x, y).Water = R.Sim.Dragon.ShelterWater;
            Assert.That(w.CaughtByBreath(x, y), Is.False, "under water");
            w.At(x, y).Frozen = true;
            Assert.That(w.CaughtByBreath(x, y), "ice is no shelter");
        }

        [Test]
        public void ADownedRaiderDoesntMarchAndTheChecksumSeesIt()
        {
            var w = new World(R, 64, 64, 2);
            w.ApplyDefense(new DefenseCommand(DefenseKind.Start, 5, 5, 50, 50));
            w.KeepStart();
            w.ApplyDragon(new DragonCommand());
            while (w.Dragon.Phase == DragonPhase.Warning) w.Step();
            var raider = w.SpawnRaider("bone-hound", 30, 30, 50); // it would be at the flag by now
            for (int i = 0; i < 40 && raider.StatusOf(World.DownedStatus) == 0; i++) w.Step();
            int ticks = (int)Math.Floor(R.Sim.Downed.Seconds * R.Sim.TicksPerSecond);
            Assert.That(raider.StatusOf(World.DownedStatus), Is.EqualTo(ticks));
            Assert.That(w.DefenseText(), Does.Contain($",d{ticks};"));
            int x = raider.X, y = raider.Y;
            Run(w, ticks - 1);
            Assert.That((raider.X, raider.Y), Is.EqualTo((x, y)), "it stays down");
        }

        [Test]
        public void TheBudgetStartsTheWarningOnceTheAuditCountsPastIt()
        {
            var w = new World(With("{ \"budget\": 40 }"), 128, 64, 2);
            w.KeepStart();
            for (int x = 0; x < 41; x++) w.At(x, 3).Height = 1;
            Assert.That(w.ChangedCount, Is.EqualTo(0), "nothing is counted until the audit comes round");
            Run(w, 1);
            Assert.That(w.ChangedCount, Is.EqualTo(41));
            Assert.That(w.Dragon?.Phase, Is.EqualTo(DragonPhase.Warning));
            Assert.That(w.DragonFlights, Is.EqualTo(1));
        }

        [Test]
        public void ABudgetOfNoneNeverSummons()
        {
            var w = new World(With("{ \"budget\": 0 }"), 64, 64, 2);
            w.KeepStart();
            for (int x = 0; x < 64; x++) w.At(x, 3).Height = 1;
            Run(w, 3);
            Assert.That(w.Dragon, Is.Null);
        }

        [Test]
        public void TheAuditLooksAtOneChunkATickAndOnlyAtActiveOnes()
        {
            var w = new World(R, 192, 64, 2);
            w.KeepStart();
            w.At(100, 3).Height = 1;
            w.At(150, 3).Height = 1;
            Run(w, 1);
            Assert.That(w.ChangedCount, Is.EqualTo(0));
            Run(w, 1);
            Assert.That(w.ChangedCount, Is.EqualTo(1));
            w.SetActive(2, 0, false);
            Run(w, 3);
            Assert.That(w.ChangedCount, Is.EqualTo(1), "an inactive chunk's count stands");
        }

        [Test]
        public void TheCountTakesWhatLastsNotWhatFades()
        {
            var w = new World(R, 64, 64, 2);
            w.KeepStart();
            w.At(1, 1).Gas = "steam";
            w.At(1, 1).GasAmt = 1;
            w.At(2, 1).Charge = 2;
            w.At(3, 1).Cover = "ash";
            w.At(4, 1).T = "rubble";
            w.At(5, 1).Height = 1;
            w.At(6, 1).Water = 4;
            w.At(7, 1).Cover = "snow";
            w.At(8, 1).T = "mud";
            Run(w, 1);
            Assert.That(w.ChangedCount, Is.EqualTo(3));
        }

        [Test]
        public void AnInactiveChunkIsResetTooAndDoesntCatchUpOnWhatTheBreathWiped()
        {
            static World Build()
            {
                var w = new World(R, 128, 64, 2, "grass");
                w.KeepStart();
                w.At(100, 10).Fire = 2;
                w.At(100, 10).Fuel = 8;
                w.At(110, 10).Height = 2;
                w.Step();
                w.SetActive(1, 0, false);
                return w;
            }
            World a = Build(), b = Build();
            a.ApplyDragon(new DragonCommand());
            b.ApplyDragon(new DragonCommand());
            FlyOut(a);
            FlyOut(b);
            Assert.That(a.IsActive(1, 0), Is.False, "the reset doesn't switch chunks");
            Assert.That(a.At(110, 10).Height, Is.EqualTo(0));
            Assert.That(a.At(100, 10).Fire, Is.EqualTo(0));
            Assert.That(a.CellHash(), Is.EqualTo(b.CellHash()), "peers agree");
            Assert.That(a.DragonText(), Is.EqualTo(b.DragonText()));
            a.SetActive(1, 0, true);
            Assert.That(a.CaughtUp(1, 0), Is.EqualTo(0));
            Assert.That(a.ChangedCount, Is.EqualTo(0));
        }

        [Test]
        public void PlayAreasComeBackAsBuilt()
        {
            var w = new World(R, 64, 64, 2);
            w.ApplyGolf(new GolfCommand(5, 5, 20, 5));
            w.At(30, 30).T = "gate";
            w.At(32, 30).T = "wheel";
            w.LinkGate(30, 30, 32, 30);
            w.KeepStart();
            w.Golf.X = 9;
            w.Golf.Strokes = 3;
            w.At(30, 30).T = "gate_open";
            w.GateLinks[0].Open = true;
            w.ApplyEffect(new EffectCommand(EffectKind.Crack, 0, 0, "", new ThingId(0, 0, 3)));
            w.ApplyDragon(new DragonCommand());
            var seen = new List<ChunkFresh>();
            w.ChunkFreshened += seen.Add;
            FlyOut(w);
            Assert.That(w.Golf.X, Is.EqualTo(5));
            Assert.That(w.Golf.Strokes, Is.EqualTo(0));
            Assert.That(w.At(30, 30).T, Is.EqualTo("gate"));
            Assert.That(w.GateLinks[0].Open, Is.False);
            Assert.That(w.IsCracked(new ThingId(0, 0, 3)), Is.False);
            Assert.That(seen.Select(s => (s.CX, s.CY)), Does.Contain((0, 0)), "the view hears, to bring its props back");
        }

        [Test]
        public void TheGrimoiresStay()
        {
            var w = new World(R, 64, 64, 2);
            w.KeepStart();
            w.ApplyCombo(ComboCommand.Load("p1", "fire"));
            Run(w, 40);
            var known = w.CastOf("p1").Known.ToList();
            Assert.That(known, Is.Not.Empty);
            w.ApplyDragon(new DragonCommand());
            FlyOut(w);
            Assert.That(w.CastOf("p1").Known, Is.EqualTo(known));
        }

        [Test]
        public void ATerrainARuleSwitchTookAwayComesBackAsDirt()
        {
            var modded = R.WithMods(new[] { ModPatch.Parse("{ \"materials\": { \"cloudstone\": { \"name\": \"Cloudstone\", \"tags\": [\"ground\"] } } }") });
            var w = new World(modded, 64, 64, 2);
            w.At(5, 5).T = "cloudstone";
            w.KeepStart();
            w.ApplyRules(R);
            w.ApplyDragon(new DragonCommand());
            FlyOut(w);
            Assert.That(w.At(5, 5).T, Is.EqualTo("dirt"));
        }

        [Test]
        public void WithNoStartingStateNothingComesAndASecondSummonsChangesNothing()
        {
            var w = new World(R, 64, 64, 2);
            w.ApplyDragon(new DragonCommand());
            Assert.That(w.Dragon, Is.Null);
            w.KeepStart();
            w.ApplyDragon(new DragonCommand());
            int until = w.Dragon.Until;
            Run(w, 3);
            w.ApplyDragon(new DragonCommand());
            Assert.That(w.Dragon.Until, Is.EqualTo(until));
            Assert.That(w.DragonFlights, Is.EqualTo(1));
        }

        [Test]
        public void TheSummonsIsACommandInATickBatchAndInTheHistory()
        {
            var live = new World(R, 64, 64, 2);
            live.KeepStart();
            var ruleSet = RuleSet.Plain(R);
            var batch = new TickBatch(1);
            batch.Dragon.Add(new DragonCommand());
            batch.RunOn(live, ruleSet, out _);
            var replay = new World(R, 64, 64, 2);
            replay.KeepStart();
            var timeline = TickBatch.Timeline(null, null, 1);
            TickBatch.AddDragon(timeline, new[] { 1 });
            timeline[1].RunOn(replay, ruleSet, out _);
            Assert.That(replay.DragonText(), Is.EqualTo(live.DragonText()));
            Assert.That(live.Dragon?.Phase, Is.EqualTo(DragonPhase.Warning));
        }

        [Test]
        public void TheChecksChoke()
        {
            Assert.Throws<ModException>(() => With("{ \"wallTerrains\": [\"cheese\"] }"));
            Assert.Throws<ModException>(() => With("{ \"speed\": 1.5 }"));
            Assert.Throws<ModException>(() => With("{ \"depth\": 0 }"));
        }

        sealed class Stripes : IChunkSource
        {
            public string Version => "test-stripes-1";

            public void Make(int x, int y, Cell cell)
            {
                if (y % 7 == 0) cell.T = "stone";
                else cell.T = x % 5 == 0 ? "grass" : "dirt";
            }
        }

        [Test]
        public void ASaveMidFlightLoadsAndGoesOnTheSame()
        {
            var source = new Stripes();
            var w = new World(R, 192, 128, 5);
            w.MakeFrom(source);
            w.ApplyGolf(new GolfCommand(5, 5, 20, 5));
            w.KeepStart();
            for (int x = 0; x < 192; x += 2) w.At(x, 30).Height = 2;
            Run(w, 70);
            w.Golf.Strokes = 2;
            w.ApplyDragon(new DragonCommand());
            Run(w, R.Sim.Dragon.WarningTicks + 30);
            string save = w.Save(source);
            var back = World.Load(save, R, source);
            Assert.That(back.DragonText(), Is.EqualTo(w.DragonText()));
            Assert.That(back.Save(source), Is.EqualTo(save));
            FlyOut(w);
            FlyOut(back);
            Assert.That(back.CellHash(), Is.EqualTo(w.CellHash()));
            Assert.That(back.DragonText(), Is.EqualTo(w.DragonText()));
            Assert.That(back.Golf.Strokes, Is.EqualTo(0), "the ball as built came through the save");
        }

        [Test]
        public void AnOldSaveHasNoStartUntilItsLoaderAsks()
        {
            var source = new Stripes();
            var w = new World(R, 128, 64, 5);
            w.MakeFrom(source);
            w.At(3, 3).Height = 1;
            string save = w.Save(source);
            Assert.That(save, Does.Not.Contain("\"start\""), "a world with no starting state saves as it did");
            var back = World.Load(save, R, source);
            Assert.That(back.HasStart, Is.False);
            back.KeepStart(source);
            Assert.That(back.ChangedCount, Is.EqualTo(1));
            back.ApplyDragon(new DragonCommand());
            FlyOut(back);
            Assert.That(back.At(3, 3).Height, Is.EqualTo(0));
        }

        /// <summary>
        /// The time a full sweep takes on a world land.json's size (576 × 576), every chunk active and a third of the cells
        /// changed, against the budgets: 1/6 s for a tick (ticks at 6 a second), 6 ms for a slice (ChemGrid.StepBudgetMs).
        /// Prints the numbers; run with --logger "console;verbosity=detailed" to read them.
        /// </summary>
        [Test]
        public void AFullSweepStaysWithinTheTickBudget()
        {
            const int size = 576, dragonStage = 8;
            var w = new World(R, size, size, 11);
            var source = new Stripes();
            w.MakeFrom(source);
            var keep = Stopwatch.StartNew();
            w.KeepStart();
            keep.Stop();
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x += 3) w.At(x, y).Height = 1;
            for (int y = 0; y < size; y += 2)
                for (int x = 1; x < size; x += 3) w.At(x, y).Cover = "ash";
            Run(w, 90);
            w.ApplyDragon(new DragonCommand());
            double worstTick = 0, worstSlice = 0, worstDragon = 0, sum = 0;
            int ticks = 0;
            var clock = new Stopwatch();
            while (w.Dragon != null)
            {
                double tick = 0, dragon = 0;
                bool done;
                do
                {
                    int stage = w.TickStageNumber;
                    clock.Restart();
                    done = w.StepSome(() => true);
                    clock.Stop();
                    double ms = clock.Elapsed.TotalMilliseconds;
                    tick += ms;
                    if (stage == dragonStage)
                    {
                        dragon += ms;
                        worstSlice = Math.Max(worstSlice, ms);
                    }
                }
                while (!done);
                worstTick = Math.Max(worstTick, tick);
                worstDragon = Math.Max(worstDragon, dragon);
                sum += tick;
                ticks++;
            }
            TestContext.Progress.WriteLine($"DRAGON SWEEP {ticks} ticks ({ticks / 6.0:0} s), mean tick {sum / ticks:0.0} ms, worst tick {worstTick:0.0} ms (budget 166.7), worst dragon slice {worstSlice:0.00} ms (budget 6), worst dragon stage {worstDragon:0.00} ms");
            TestContext.Progress.WriteLine($"DRAGON START {size * size} cells in {w.StartShapes} shapes, KeepStart {keep.Elapsed.TotalMilliseconds:0} ms");
            Assert.That(w.Cells.All(c => c.Height == 0 && c.Cover == null));
            Assert.That(worstTick, Is.LessThan(1000.0 / 6));
        }

        [Test]
        public void AFlightPutsTheCourseBackAndKeepsItsZonesAndTimes()
        {
            // Ported from sandbox/test/dragon.test.ts.
            var w = new World(R, 60, 12, 3, "dirt");
            for (int x = 42; x <= 46; x++) w.At(x, 5).T = "lava";
            w.SetCourse(CourseLayout.FromJson(@"{
                ""name"": ""The little gauntlet"", ""area"": [18, 0, 54, 11], ""gate"": [50, 2, 52, 8], ""bell"": [20, 2, 22, 8],
                ""booths"": [{ ""id"": ""lava-pit"", ""name"": ""lava pit"", ""area"": [40, 0, 49, 11], ""checkpoint"": [48, 5], ""allow"": { ""elements"": [""water"", ""air""] } }] }"));
            w.KeepStart();
            w.ApplyCourse(new CourseCommand("ann", 51, 5));
            Run(w, 30);
            w.ApplyCourse(new CourseCommand("ann", 21, 5));
            w.ApplyCourse(new CourseCommand("bob", 51, 5));
            w.ApplyCourse(new CourseCommand("bob", 45, 5));
            int best = w.CourseRuns["ann"].Best;
            Assert.That(best, Is.GreaterThan(0));
            for (int x = 42; x <= 46; x++) w.At(x, 5).T = "lava-crust";

            w.ApplyDragon(new DragonCommand());
            FlyOut(w);

            Assert.That(Enumerable.Range(42, 5).All(x => w.At(x, 5).T == "lava"), "the booth's lava is back");
            Assert.That(w.Zones.Select(z => z.Id), Is.EqualTo(new[] { "lava-pit" }), "zones are world data");
            Assert.That(w.CourseRuns["ann"].Best, Is.EqualTo(best), "a best time is progress");
            Assert.That(w.CourseRuns["bob"].Running && w.CourseRuns["bob"].Checkpoint == 0, "a run goes on");
            Assert.That(w.CourseRespawn("bob", 45, 5), Is.EqualTo((48, 5)));
        }
    }
}
