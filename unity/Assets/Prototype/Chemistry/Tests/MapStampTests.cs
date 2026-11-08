// A stamp along a path (a canyon that bends, a cliff that turns), and a map's own stamps (stamps.json, MapStamps): laid
// when the world is made, kept by a save, never taken by an undo or the dragon, off the rows' laid land and the water.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class MapStampTests
    {
        const int Size = 160;

        sealed class AllGrass : IChunkSource
        {
            public string Version => "map-stamp-tests";
            public void Make(int x, int y, Cell cell) => cell.T = "grass";
        }

        static World Flat()
        {
            var w = new World(ChemistryTests.R, Size, Size, 7);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++) w.Paint(x, y, "grass");
            return w;
        }

        // West 40 cells, then a dog-leg north-west, then west again.
        static LandStamp DogLeg(string profile, int rise, int seed = 3) =>
            LandStamp.Make(100, 80, 16, rise, profile, 0, 0, 40, seed).Along(new[] { (0, 0), (-40, 0), (-52, -14), (-80, -14) });

        [Test]
        public void APathRowReadsBackAndAStraightOneKeepsItsNineFields()
        {
            var bent = DogLeg(LandStamp.Canyon, 240);
            Assert.That(bent.Known);
            Assert.That(bent.Row.Count, Is.EqualTo(10));
            Assert.That(LandStamp.Read(bent.Row).Text, Is.EqualTo(bent.Text));
            Assert.That(LandStamp.Make(1, 2, 3, 4, LandStamp.Cliff, 1, 0, 40, 3).Row.Count, Is.EqualTo(9), "the act's row as it was");
            Assert.That(LandStamp.Make(1, 2, 3, 4, LandStamp.Dome, 0, 0, 40, 3).Along(new[] { (0, 0), (5, 0) }).Known, Is.False, "a path is a canyon's or a cliff's");
            Assert.That(LandStamp.Make(1, 2, 3, 4, LandStamp.Cliff, 0, 0, 40, 3).Along(new[] { (0, 0) }).Known, Is.False, "two points or more");
        }

        [Test]
        public void ACanyonAlongAPathIsCutRoundItsBendAndClimbsOutAtItsEnds()
        {
            var w = Flat();
            var canyon = DogLeg(LandStamp.Canyon, 240);
            Assert.That(w.LayMapStamps(new[] { canyon }), Is.Empty);
            // The middle of each leg, the bend between them included, is cut well down; past the ends it's the land.
            foreach (var (x, y) in new[] { (80, 80), (54, 73), (34, 66) })
                Assert.That(Enumerable.Range(-4, 9).Min(o => w.LandStampedAt(x, y + o)), Is.LessThan(-60), $"the floor at ({x}, {y})");
            Assert.That(w.LandStampedAt(100 + 16, 80), Is.EqualTo(0), "nothing past its east end's taper");
            Assert.That(w.LandStampedAt(60, 80 + 17), Is.EqualTo(0), "nothing past its reach across");
            Assert.That(Enumerable.Range(-4, 9).Min(o => w.LandStampedAt(100, 80 + o)), Is.GreaterThan(-40), "its floor has climbed at the end");
        }

        [Test]
        public void ACliffAlongAPathIsHighOnTheSameHandRoundItsTurn()
        {
            var w = Flat();
            // North, then turning north-east: the high side on the left all the way.
            var cliff = LandStamp.Make(80, 120, 30, 240, LandStamp.Cliff, 0, 0, 0, 3).Along(new[] { (0, 0), (0, -40), (30, -70) });
            Assert.That(w.LayMapStamps(new[] { cliff }), Is.Empty);
            Assert.That(w.LandStampedAt(70, 100), Is.GreaterThan(w.LandStampedAt(90, 100) + 100), "west of the north leg is high");
            Assert.That(w.LandStampedAt(90, 55), Is.GreaterThan(w.LandStampedAt(105, 70) + 100), "north-west of the turned leg is high");
            Assert.That(Enumerable.Range(0, Size).Count(x => w.IsStampedRock(x, 100)), Is.GreaterThan(0), "its face is bare rock");
        }

        [Test]
        public void TheMapsStampsStayThroughAnUndoAResetAndASave()
        {
            var w = Flat();
            w.SetLand(new int[Size * Size]);
            w.LayMapStamps(new[] { DogLeg(LandStamp.Canyon, 240) });
            var moved = Enumerable.Range(0, Size * Size).Select(i => w.LandMovedAt(i % Size, i / Size)).ToArray();
            Assert.That(w.LandHash(), Is.EqualTo(""), "no act laid it: the acts' hash is untouched");
            Assert.That(w.MapStampsLaid.Count, Is.EqualTo(1));
            var back = World.Load(w.Save(new AllGrass()), ChemistryTests.R, new AllGrass());
            Assert.That(Enumerable.Range(0, Size * Size).Select(i => back.LandMovedAt(i % Size, i / Size)), Is.EqualTo(moved), "a save keeps them");
            Assert.That(back.MapStampsLaid.Count, Is.EqualTo(1));
        }

        [Test]
        public void TreesGoFromTheFaceAndTheFloorAndStayOnTheTop()
        {
            var w = new World(ChemistryTests.R, Size, Size, 7);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++) w.Paint(x, y, "tree");
            w.LayMapStamps(new[] { DogLeg(LandStamp.Canyon, 240) });
            int left = 0, cleared = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (w.IsStampedRock(x, y)) Assert.That(w.At(x, y).T, Is.Not.EqualTo("tree"), $"({x}, {y}) is rock");
                    else if (w.LandStampedAt(x, y) != 0 && w.At(x, y).T == "tree") left++;
                    if (w.At(x, y).T == "grass") cleared++;
                }
            int rock = Enumerable.Range(0, Size * Size).Count(i => w.IsStampedRock(i % Size, i / Size));
            Assert.That(cleared, Is.EqualTo(rock), "every rock cell, and only those");
            Assert.That(cleared, Is.GreaterThan(200));
            Assert.That(left, Is.GreaterThan(40), "a forest on the rims");
        }

        [Test]
        public void ATrailRunsAlongTheCanyonsFloorRoundItsBends()
        {
            var w = Flat();
            var canyon = DogLeg(LandStamp.Canyon, 240);
            w.LayMapStamps(new[] { canyon });
            var trail = canyon.Trail().ToList();
            Assert.That(trail.Count, Is.GreaterThan(2 * 80), "two cells wide, end to end");
            Assert.That(trail.Count(c => w.LandStampedAt(c.x, c.y) <= -World.LandCrag), Is.GreaterThan(trail.Count * 8 / 10), "on the floor, not up its walls");
            foreach (var c in trail)
                Assert.That(trail.Any(o => o != c && Math.Abs(o.x - c.x) <= 1 && Math.Abs(o.y - c.y) <= 1), $"({c.x}, {c.y}) joins the rest");
            Assert.That(LandStamp.Make(64, 64, 32, 240, LandStamp.Canyon, 1, 0, 40, 3).Trail().Count(), Is.GreaterThan(100), "a straight one has one too");
            Assert.That(LandStamp.Make(64, 64, 32, 240, LandStamp.Cliff, 1, 0, 40, 3).Trail(), Is.Empty);
        }

        [Test]
        public void AMapStampKeepsOffLaidLandAndWater()
        {
            var w = Flat();
            w.LayLand(new[] { (60, 80, 8) });
            Assert.That(w.LayMapStamps(new[] { DogLeg(LandStamp.Canyon, 240) }).Single(), Does.Contain("laid land"));
            var wet = Flat();
            wet.At(60, 80).Water = 4;
            Assert.That(wet.LayMapStamps(new[] { DogLeg(LandStamp.Canyon, 240) }).Single(), Does.Contain("cross water"));
            Assert.That(wet.LandHasMoved, Is.False);
        }

        // The valley's canyon (castle-cliff-canyon): every one laid, with the rows and the river as the map has them.
        [Test]
        public void TheValleysStampsAreAllLaid()
        {
            string dir = ChemistryTests.WorldDir();
            var rows = MapFile.ReadCells(dir).Replace("\r", "").Trim('\n').Split('\n');
            var w = new World(ChemistryTests.R, MapCheck.Size, MapCheck.Size, 7);
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++)
                {
                    w.Paint(x, y, "grass");
                    if ("wr*~".Contains(rows[y][x])) w.At(x, y).Water = 4;
                }
            WorldBuildings.Load(dir).LayInto(w);
            var stamps = MapStamps.Load(dir);
            Assert.That(stamps.All.Select(s => s.Id), Is.EquivalentTo(new[] { "the-canyon" }));
            Assert.That(stamps.LayInto(w), Is.Empty);
            Assert.That(w.LandLaidCells().All(c => w.LandStampedAt(c.x, c.y) == 0), "the pad and the road as they were");
            var trail = stamps.All.Single(s => s.Trail != null).Stamp.Trail().ToList();
            Assert.That(trail.Count(c => w.At(c.x, c.y).T == "dirt" && w.LandStampedAt(c.x, c.y) < 0), Is.GreaterThan(100), "the canyon's dirt path, cut into");
            var places = File.ReadAllText(Path.Combine(dir, "places.json"));
            Assert.That(stamps.All.All(s => places.Contains($"\"id\":\"{s.Id}\"", StringComparison.Ordinal)), "each is a named place");
        }

        // The castle cliff (castle-cliff-wing): the Hold's pad reaches north of the castle at its own height and drops off as a
        // crag, so the ground from the gate to the lip is one height, and the face below it is tall and rock.
        [Test]
        public void TheCastlePadIsFlatFromTheGateToTheCliffsLip()
        {
            string dir = ChemistryTests.WorldDir();
            var w = new World(ChemistryTests.R, MapCheck.Size, MapCheck.Size, 7);
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++) w.Paint(x, y, "grass");
            WorldBuildings.Load(dir).LayInto(w);
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), w.W, w.H);
            int At(int x, int y) => land[w.Idx(x, y)] + w.LandMovedAt(x, y);
            var walk = CastleWalk().ToList();
            Assert.That(walk.Select(c => At(c.x, c.y)).Distinct().Count(), Is.EqualTo(1), "one height from the gate to the lip");
            var (lx, ly) = walk[walk.Count - 1];
            Assert.That(At(lx, ly) - At(lx + 6, ly), Is.GreaterThan(160), "the face below the lip drops 20 m and more");
            Assert.That(w.LandCragCells().Any(c => c == (lx + 2, ly)), "the face is a crag");
            Assert.That(w.IsStampedRock(lx + 3, ly), "bare rock");
        }

        /// <summary>The castle's walk: out of its gate, north along its east wall onto the pad beyond, and east to the cliff's lip.</summary>
        public static IEnumerable<(int x, int y)> CastleWalk()
        {
            for (int x = 142; x <= 145; x++) yield return (x, 366);
            for (int y = 365; y >= 330; y--) yield return (145, y);
            for (int x = 146; x <= 149; x++) yield return (x, 330);
        }
    }
}
