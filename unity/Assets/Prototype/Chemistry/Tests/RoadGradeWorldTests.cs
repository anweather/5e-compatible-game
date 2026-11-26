// RoadGradeTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in RoadGradeTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System.Collections.Generic;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class RoadGradeWorldTests
    {

        [Test]
        public void ACutIsNoStamp()
        {
            var w = new World(ChemistryTests.R, RoadGradeTests.Size, RoadGradeTests.Size, 7);
            var road = RoadGrade.Of(RoadGradeTests.Road(), 0, 0, RoadGradeTests.Land, new HashSet<(int x, int y)>());
            w.LayLand(road);
            var (x, y, by) = road[road.Count / 2];
            Assert.That(w.LandMovedAt(x, y), Is.EqualTo(by));
            Assert.That(w.LandHash(), Is.Empty, "what the map lays is no stamp: it isn't part of the checksum");
            Assert.That(w.LandHasMoved);
            Assert.That(w.LandDrop(x, y), Is.Zero, "a bank is no stamped cliff");
        }
    }
}
