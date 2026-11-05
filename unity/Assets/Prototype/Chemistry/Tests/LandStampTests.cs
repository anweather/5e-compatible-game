// Land stamps (docs/design/maps-as-data.md, section 3): integers every peer agrees on, taken back exactly, kept by a save
// and a late joiner, gone with the dragon, and drawn as the cells have them.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class LandStampTests
    {
        const int Size = 128;
        static readonly ModBudgets Budgets = ModBudgets.Load(ChemistryTests.RulesDir());

        sealed class AllGrass : IChunkSource
        {
            public string Version => "land-stamp-tests";
            public void Make(int x, int y, Cell cell) => cell.T = "grass";
        }

        static World Flat()
        {
            var w = new World(ChemistryTests.R, Size, Size, 7);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++) w.Paint(x, y, "grass");
            w.SetLand(new int[Size * Size]);
            Open(w);
            return w;
        }

        static void Open(World w) => w.OpenRoom(ChangeMode.Open, "Host", ChemistryTests.R, Budgets);

        static RoomCommand Stamp(int x, int y, int radius, int rise, string profile = LandStamp.Dome, int rough = 0) =>
            RoomCommand.Act("Host", new JObject
            {
                ["kind"] = World.ActLand,
                ["what"] = World.ActLand,
                ["stamp"] = LandStamp.Make(x, y, radius, rise, profile, 0, 0, rough, 3).Row,
            }.ToString());

        static RoomCommand Formed(string profile, int wall, int rough = 40) =>
            RoomCommand.Act("Host", new JObject
            {
                ["kind"] = World.ActLand,
                ["what"] = World.ActLand,
                ["stamp"] = LandStamp.Make(64, 64, 32, wall, profile, 1, 0, rough, 3).Row,
            }.ToString());

        // The PC's case (#146): wall 36, radius 32, on the lowest dry land there is.
        [Test]
        public void TheSameCanyonIsTakenOnEverySeed()
        {
            var w = Flat();
            w.SetLand(Enumerable.Repeat(-4, Size * Size).ToArray());
            for (int seed = 0; seed < 200; seed++)
            {
                var canyon = LandStamp.Make(64, 64, 32, 288, LandStamp.Canyon, 1, 0, 40, seed);
                Assert.That(w.LandRefusal(canyon), Is.Null, $"seed {seed}");
                for (int dy = -32; dy <= 32; dy++)
                    for (int dx = -32; dx <= 32; dx++)
                        Assert.That(canyon.At(dx, dy), Is.InRange(canyon.Extent.low, canyon.Extent.high), $"seed {seed}");
            }
        }

        [TestCase(LandStamp.Dome, -120, 40, 0, 0)]
        [TestCase(LandStamp.Cone, 300, 100, 0, 0)]
        [TestCase(LandStamp.Ridge, 200, 60, 1, 0)]
        [TestCase(LandStamp.Canyon, 800, 100, 1, 1)]
        [TestCase(LandStamp.Cliff, 800, 100, 1, -1)]
        [TestCase(LandStamp.Cliff, -400, 40, 0, 1)]
        public void AStampStaysInsideItsExtentOnEverySeed(string profile, int rise, int rough, int alongX, int alongY)
        {
            for (int seed = 0; seed < 60; seed++)
            {
                var s = LandStamp.Make(64, 64, 32, rise, profile, alongX, alongY, rough, seed);
                for (int dy = -32; dy <= 32; dy++)
                    for (int dx = -32; dx <= 32; dx++)
                        Assert.That(s.At(dx, dy), Is.InRange(s.Extent.low, s.Extent.high), $"seed {seed}");
            }
        }

        [Test]
        public void WhetherAStampIsTakenDoesNotHangOnItsSeed()
        {
            var w = Flat();
            var said = Enumerable.Range(0, 100).Select(seed => w.LandRefusal(LandStamp.Make(64, 64, 20, -110, LandStamp.Dome, 0, 0, 40, seed))).Distinct().ToList();
            Assert.That(said.Count, Is.EqualTo(1));
        }

        // The biggest step from one cell to the next, walking across the line at each column near the middle.
        static int SteepestAcross(World w) =>
            Enumerable.Range(56, 16).Max(x => Enumerable.Range(33, 62).Max(y => Math.Abs(w.LandMovedAt(x, y + 1) - w.LandMovedAt(x, y))));

        [Test]
        public void ACanyonIsAFloorBetweenSteepWalls()
        {
            var w = Flat();
            Assert.That(w.ApplyRoom(Formed(LandStamp.Canyon, 288)).Done);
            int floor = Enumerable.Range(58, 12).Min(y => w.LandMovedAt(64, y));
            int rim = Enumerable.Range(40, 48).Max(y => w.LandMovedAt(64, y));
            Assert.That(floor, Is.LessThan(-90), "cut well down");
            Assert.That(rim, Is.GreaterThan(120), "the rest of the wall stands up as a rim");
            Assert.That(SteepestAcross(w), Is.GreaterThanOrEqualTo(40), "a wall, not a slope: 5 m in a cell");
        }

        // Whether a player on the floor can get out, by steps of at most a metre a cell (34 degrees, inside what the wizard climbs).
        static bool CanWalkOut(World w)
        {
            const int mostStep = 8;
            int start = Enumerable.Range(40, 48).OrderBy(y => w.LandMovedAt(64, y)).First();
            var seen = new System.Collections.Generic.HashSet<(int x, int y)> { (64, start) };
            var open = new System.Collections.Generic.Queue<(int x, int y)>(seen);
            while (open.Count > 0)
            {
                var (x, y) = open.Dequeue();
                if ((x - 64) * (x - 64) + (y - 64) * (y - 64) > 33 * 33) return true;
                foreach (var (u, v) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (Math.Abs(w.LandMovedAt(u, v) - w.LandMovedAt(x, y)) <= mostStep && seen.Add((u, v))) open.Enqueue((u, v));
            }
            return false;
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void ACanyonCanBeWalkedOutOf(int seed)
        {
            var w = Flat();
            w.ApplyRoom(RoomCommand.Act("Host", new JObject
            {
                ["kind"] = World.ActLand,
                ["what"] = World.ActLand,
                ["stamp"] = LandStamp.Make(64, 64, 32, 288, LandStamp.Canyon, 1, 0, 40, seed).Row,
            }.ToString()));
            Assert.That(CanWalkOut(w), "its floor climbs to the land at the ends");
        }

        [Test]
        public void ADeepCanyonCutsNoFurtherAndRaisesItsRim()
        {
            var w = Flat();
            Assert.That(w.ApplyRoom(Formed(LandStamp.Canyon, 800, 0)).Done, "a 100 m wall stays inside what can be drawn");
            Assert.That(Enumerable.Range(40, 48).Min(y => w.LandMovedAt(64, y)), Is.GreaterThanOrEqualTo(World.LandLowest));
        }

        [Test]
        public void ACliffStandsOnOneSideOfItsLineAndRiseTurnsIt()
        {
            var w = Flat();
            w.ApplyRoom(Formed(LandStamp.Cliff, 240));
            Assert.That(w.LandMovedAt(64, 52), Is.GreaterThan(w.LandMovedAt(64, 76) + 100), "high to the north of east");
            var other = Flat();
            other.ApplyRoom(Formed(LandStamp.Cliff, -240));
            Assert.That(other.LandMovedAt(64, 76), Is.GreaterThan(other.LandMovedAt(64, 52) + 100), "and the other way below 0");
        }

        [Test]
        public void ACanyonOrCliffNeedsALine()
        {
            Assert.That(LandStamp.Make(64, 64, 32, 200, LandStamp.Canyon, 0, 0, 40, 3).Known, Is.False);
            Assert.That(LandStamp.Make(64, 64, 32, 200, LandStamp.Cliff, 1, 1, 40, 3).Known, Is.True);
        }

        [TestCase(LandStamp.Canyon)]
        [TestCase(LandStamp.Cliff)]
        public void AFormedStampMeetsTheLandRoundItAndComesOffExactly(string profile)
        {
            var w = Flat();
            var before = Moved(w);
            Assert.That(w.ApplyRoom(Formed(profile, 288)).Done);
            for (int a = 0; a < 360; a += 15)
            {
                int dx = (int)Math.Round(32 * Math.Cos(a * Math.PI / 180)), dy = (int)Math.Round(32 * Math.Sin(a * Math.PI / 180));
                Assert.That(Math.Abs(w.LandMovedAt(64 + dx, 64 + dy)), Is.LessThanOrEqualTo(8), "a metre at most at its rim");
            }
            Assert.That(w.ApplyRoom(RoomCommand.Undo("Host", 1)).Done);
            Assert.That(Moved(w), Is.EqualTo(before));
        }

        [Test]
        public void RoughLandIsNotOneSmoothSlab()
        {
            var smooth = Flat();
            smooth.ApplyRoom(Stamp(64, 64, 32, 160));
            var rough = Flat();
            rough.ApplyRoom(Stamp(64, 64, 32, 160, LandStamp.Dome, 40));
            int differ = Enumerable.Range(0, Size * Size).Count(i => Math.Abs(smooth.LandMovedAt(i % Size, i / Size) - rough.LandMovedAt(i % Size, i / Size)) >= 8);
            Assert.That(differ, Is.GreaterThan(300), "a metre or more off the plain dome over many cells");
        }

        [Test]
        public void WhatAStampMakesSteepPaintsAsBareRock()
        {
            var w = Flat();
            Assert.That(CellPaint.Mix(w, 64, 64), Is.SameAs(CellPaint.Mix(w, w.At(64, 64))), "nothing until land is stamped");
            w.ApplyRoom(Formed(LandStamp.Cliff, 240));
            int rock = 0;
            for (int y = 40; y < 90; y++)
                if (w.LandDrop(64, y) >= World.LandSteep)
                {
                    rock++;
                    Assert.That(CellPaint.Mix(w, 64, y)[CellPaint.Layers.ToList().IndexOf(CellPaint.Rock)], Is.GreaterThan(0.9f));
                }
            Assert.That(rock, Is.GreaterThan(0), "the face is steep");
        }

        static int[] Moved(World w) => Enumerable.Range(0, Size * Size).Select(i => w.LandMovedAt(i % Size, i / Size)).ToArray();

        [Test]
        public void ADomeIsHighestAtItsMiddleAndNothingPastItsEdge()
        {
            var w = Flat();
            Assert.That(w.ApplyRoom(Stamp(64, 64, 20, 400)).Done);
            Assert.That(w.LandMovedAt(64, 64), Is.EqualTo(400));
            Assert.That(w.LandMovedAt(74, 64), Is.EqualTo(300));
            Assert.That(w.LandMovedAt(85, 64), Is.Zero);
        }

        [Test]
        public void EachProfileIsAShapeAppliedToHeight()
        {
            Assert.That(LandStamp.Make(0, 0, 10, 80, LandStamp.Flat, 0, 0, 0, 1).At(9, 0), Is.EqualTo(80));
            Assert.That(LandStamp.Make(0, 0, 10, 80, LandStamp.Cone, 0, 0, 0, 1).At(5, 0), Is.EqualTo(40));
            var ridge = LandStamp.Make(0, 0, 10, 80, LandStamp.Ridge, 1, 0, 0, 1);
            Assert.That(ridge.At(0, 0), Is.EqualTo(80));
            Assert.That(ridge.At(0, 5), Is.EqualTo(40), "down off its line");
            Assert.That(ridge.At(5, 0), Is.EqualTo(40), "down towards its ends");
            Assert.That(LandStamp.Make(0, 0, 10, -80, LandStamp.Flat, 0, 0, 0, 1).At(1, 1), Is.EqualTo(-80), "a hollow");
        }

        [Test]
        public void TheSameRowIsTheSameLandOnEveryPeer()
        {
            World a = Flat(), b = Flat();
            a.ApplyRoom(Stamp(64, 64, 32, 500, LandStamp.Dome, 40));
            b.ApplyRoom(Stamp(64, 64, 32, 500, LandStamp.Dome, 40));
            Assert.That(Moved(a), Is.EqualTo(Moved(b)));
            Assert.That(a.LandHash(), Is.EqualTo(b.LandHash()));
            Assert.That(Moved(a).Distinct().Count(), Is.GreaterThan(100), "rough ground isn't a smooth dome");
        }

        [Test]
        public void UndoPutsTheLandBackInAnyOrder()
        {
            var w = Flat();
            w.ApplyRoom(Stamp(60, 60, 20, 300, LandStamp.Dome, 30));
            w.ApplyRoom(Stamp(70, 64, 20, -100));
            Assert.That(w.ApplyRoom(RoomCommand.Undo("Host", 1)).Done, "the older one first");
            Assert.That(w.ApplyRoom(RoomCommand.Undo("Host", 2)).Done);
            Assert.That(Moved(w).All(m => m == 0));
            Assert.That(w.LandHasMoved, Is.False);
            Assert.That(w.LandHash(), Is.EqualTo(""));
        }

        [Test]
        public void TheLandHashStaysEmptyUntilLandIsStamped()
        {
            var w = Flat();
            string cells = w.CellHash();
            Assert.That(w.LandHash(), Is.EqualTo(""));
            w.ApplyRoom(Stamp(64, 64, 8, 80));
            Assert.That(w.LandHash(), Is.Not.Empty);
            Assert.That(w.CellHash(), Is.EqualTo(cells), "land moves under the cells, not in them");
        }

        [Test]
        public void AStampKeepsOffWater()
        {
            var w = new World(ChemistryTests.R, Size, Size, 7);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++) w.Paint(x, y, "grass");
            w.At(70, 64).Water = 4;
            w.SetLand(new int[Size * Size]);
            Open(w);
            var said = w.ApplyRoom(Stamp(64, 64, 10, 80));
            Assert.That(said.Done, Is.False);
            Assert.That(said.Reason, Does.Contain("cross water"));
            Assert.That(w.LandHasMoved, Is.False, "all or nothing");
            Assert.That(w.ApplyRoom(Stamp(64, 40, 10, 80)).Done, "clear of it");
        }

        [Test]
        public void PastWhatCanBeDrawnIsRefusedNeverClamped()
        {
            var w = Flat();
            Assert.That(w.ApplyRoom(Stamp(64, 64, 10, World.LandHighest + 1)).Done, Is.False);
            Assert.That(w.ApplyRoom(Stamp(64, 64, 10, World.LandLowest - 1)).Done, Is.False);
            Assert.That(w.LandHasMoved, Is.False);
        }

        [Test]
        public void AnUndoThatWouldLeaveTheRangeWaits()
        {
            var w = Flat();
            w.ApplyRoom(Stamp(64, 64, 10, 100, LandStamp.Flat));
            w.ApplyRoom(Stamp(64, 64, 10, World.LandLowest - 50, LandStamp.Flat));
            var said = w.ApplyRoom(RoomCommand.Undo("Host", 1));
            Assert.That(said.Done, Is.False);
            Assert.That(w.LandMovedAt(64, 64), Is.EqualTo(100 + World.LandLowest - 50), "nothing moved");
            Assert.That(w.ApplyRoom(RoomCommand.Undo("Host", 2)).Done);
        }

        [Test]
        public void ASaveKeepsTheStampsAndTheirUndo()
        {
            var w = Flat();
            w.ApplyRoom(Stamp(64, 64, 20, 300, LandStamp.Dome, 30));
            var back = World.Load(w.Save(new AllGrass()), ChemistryTests.R, new AllGrass(), Budgets);
            Assert.That(Moved(back), Is.EqualTo(Moved(w)));
            Assert.That(back.LandHash(), Is.EqualTo(w.LandHash()));
            back.SetLand(new int[Size * Size]);
            Assert.That(back.ApplyRoom(RoomCommand.Undo("Host", 1)).Done);
            Assert.That(back.LandHasMoved, Is.False);
        }

        [Test]
        public void ALateJoinerReplayingTheHistoryHasTheSameLand()
        {
            var w = Flat();
            var history = new[] { Stamp(64, 64, 20, 300), Stamp(30, 30, 10, -80), RoomCommand.Undo("Host", 1) };
            foreach (var c in history) w.ApplyRoom(c);
            var joiner = Flat();
            foreach (var c in history) joiner.ApplyRoom(c);
            Assert.That(Moved(joiner), Is.EqualTo(Moved(w)));
            Assert.That(joiner.LandHash(), Is.EqualTo(w.LandHash()));
        }

        [Test]
        public void TheDragonsResetTakesTheStampsBack()
        {
            var w = Flat();
            w.KeepStart();
            w.ApplyRoom(Stamp(64, 64, 20, 300));
            w.ApplyDragon(new DragonCommand());
            for (int i = 0; w.Dragon != null && i < 20000; i++) w.Step();
            Assert.That(w.Dragon, Is.Null);
            Assert.That(w.LandHasMoved, Is.False);
            Assert.That(w.LandHash(), Is.EqualTo(""));
            Assert.That(w.ApplyRoom(RoomCommand.Undo("Host", 1)).Done, Is.False, "nothing left to take back");
        }

        [Test]
        public void WaterRunsDownTheNewSlope()
        {
            var w = Flat();
            w.ApplyRoom(Stamp(64, 64, 16, 300, LandStamp.Cone));
            var top = w.At(64, 60);
            top.Water = 8;
            for (int t = 0; t < 120; t++) w.Step();
            Assert.That(top.Water, Is.LessThan(8), "it ran off");
        }

        [Test]
        public void TheDrawnGroundIsTheCellsLand()
        {
            var grid = TerrainGrid.Valley();
            var w = new World(ChemistryTests.R, grid.GridW, grid.GridH, 7);
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++) w.Paint(x, y, "grass");
            w.SetLand(new int[w.W * w.H]);
            Open(w);
            var field = new CellHeightfield(grid, 0.5f, 1f);
            field.Reset(w);
            int cx = w.W / 2, cy = w.H / 2;
            var moved = new System.Collections.Generic.List<(int x, int y)>();
            w.LandMoved += (x, y, reach) =>
            {
                for (int v = y - reach - 1; v <= y + reach + 1; v++)
                    for (int u = x - reach - 1; u <= x + reach + 1; u++) moved.Add((u, v));
            };
            Assert.That(w.ApplyRoom(Stamp(cx, cy, 32, 400, LandStamp.Dome, 30)).Done);
            field.Update(w, moved, true);
            double unit = 0.5 / w.Rules.Sim.WaterUnitsPerStep;
            for (int y = cy - 34; y <= cy + 34; y++)
                for (int x = cx - 34; x <= cx + 34; x++)
                {
                    var (i, j, sx, sz) = grid.CellSample(x, y);
                    if (!grid.IsHighRes(i, j)) continue;
                    double cell = field.CellOffset(w, w.At(x, y));
                    Assert.That(field.Current(i, j)[sz, sx] - cell, Is.EqualTo(w.LandMovedAt(x, y) * unit).Within(0.001), $"({x}, {y})");
                }
            moved.Clear();
            w.ApplyRoom(RoomCommand.Undo("Host", 1));
            field.Update(w, moved, true);
            var (ci, cj, csx, csz) = grid.CellSample(cx, cy);
            Assert.That(field.Current(ci, cj)[csz, csx], Is.EqualTo(field.CellOffset(w, w.At(cx, cy))).Within(0.001), "undo draws it back");
        }
    }
}
