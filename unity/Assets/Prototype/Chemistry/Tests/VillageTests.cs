// Villages laid out from a recipe and a seed (Village.cs, D12): the same plan
// for the same seed, buildings that never overlap each other or the streets,
// plots skipped where the land refuses them, and a pinned digest so a change
// to the layout is noticed (every machine has to lay the same village).
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

namespace Chem.Tests
{
    public class VillageTests
    {
        static VillageSpec Spec(int seed) => new VillageSpec { Name = "Test", X = 200, Y = 200, Seed = seed, Houses = 12 };

        static bool Anywhere(int x, int y) => true;

        [Test]
        public void TheSameSeedLaysTheSameVillage()
        {
            var a = VillageLayout.Lay(Spec(7), Anywhere);
            var b = VillageLayout.Lay(Spec(7), Anywhere);
            Assert.That(a.Digest(), Is.EqualTo(b.Digest()));
            Assert.That(VillageLayout.Lay(Spec(8), Anywhere).Digest(), Is.Not.EqualTo(a.Digest()));
        }

        [Test]
        public void AVillageHasASquareAnInnAShopAndHouses()
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var plan = VillageLayout.Lay(Spec(seed), Anywhere);
                Assert.That(plan.Buildings.Count(b => b.Kind == BuildingKind.Inn), Is.EqualTo(1), $"seed {seed}");
                Assert.That(plan.Buildings.Count(b => b.Kind == BuildingKind.Shop), Is.EqualTo(1), $"seed {seed}");
                Assert.That(plan.Buildings.Count(b => b.Kind == BuildingKind.House), Is.InRange(6, 12), $"seed {seed}");
                Assert.That(plan.Square.Contains(plan.Well.x, plan.Well.y), $"seed {seed}");
                Assert.That(plan.Lanes, Has.Count.EqualTo(2));
            }
        }

        [Test]
        public void NoBuildingStandsOnAnotherOrOnAStreet()
        {
            for (int seed = 0; seed < 50; seed++)
            {
                var plan = VillageLayout.Lay(Spec(seed), Anywhere);
                var streets = new HashSet<(int, int)>(plan.Square.Cells());
                foreach (var lane in plan.Lanes) streets.UnionWith(lane.Cells());
                var walls = new HashSet<(int, int)>();
                foreach (var b in plan.Buildings)
                    foreach (var c in b.Footprint.Cells())
                    {
                        Assert.That(walls.Add(c), $"seed {seed}: two buildings on {c}");
                        Assert.That(streets.Contains(c), Is.False, $"seed {seed}: a building on the street at {c}");
                    }
            }
        }

        [Test]
        public void EveryDoorFacesItsStreet()
        {
            var plan = VillageLayout.Lay(Spec(3), Anywhere);
            var streets = new HashSet<(int, int)>(plan.Square.Cells());
            foreach (var lane in plan.Lanes) streets.UnionWith(lane.Cells());
            foreach (var b in plan.Buildings)
            {
                // Two cells out from the middle of its front is street (one cell of verge, then the street).
                var (dx, dy) = Sides.Step(b.Door);
                var f = b.Footprint;
                int fx = FrontEdge(dx, f.X, f.Right - 1), fy = FrontEdge(dy, f.Y, f.Bottom - 1);
                Assert.That(streets.Contains((fx + 2 * dx, fy + 2 * dy)), $"{b.Kind} at {f} faces {b.Door}, away from any street");
            }
        }

        // The front wall's coordinate along one axis: the low or high edge when the door faces that
        // way, else the middle.
        static int FrontEdge(int step, int low, int high)
        {
            if (step > 0) return high;
            if (step < 0) return low;
            return (low + high) / 2;
        }

        [Test]
        public void PlotsTheLandRefusesAreSkipped()
        {
            // Water everywhere west of the well: nothing may stand there.
            var plan = VillageLayout.Lay(Spec(5), (x, y) => x >= 200);
            foreach (var b in plan.Buildings)
                Assert.That(b.Footprint.X, Is.GreaterThanOrEqualTo(200), $"{b.Kind} at {b.Footprint}");
        }

        [Test]
        public void TheCellsAreWallsDirtAndTheWell()
        {
            var plan = VillageLayout.Lay(Spec(1), Anywhere);
            var cells = new Dictionary<(int, int), char>();
            foreach (var (x, y, c) in plan.Cells()) cells[(x, y)] = c;
            Assert.That(cells[plan.Well], Is.EqualTo('w'));
            foreach (var b in plan.Buildings)
                foreach (var c in b.Footprint.Cells()) Assert.That(cells[c], Is.EqualTo(b.WallCell));
            Assert.That(cells.Values.Distinct().OrderBy(c => c), Is.SubsetOf(new[] { '#', '.', 'L', 'w' }));
        }

        [Test]
        public void TheValleysVillagesRead()
        {
            var specs = VillageLayout.Load(Path.Combine(Path.GetDirectoryName(ChemistryTests.RulesDir().TrimEnd('/', '\\')), "world"));
            Assert.That(specs, Is.Not.Empty);
            Assert.Throws<System.FormatException>(() => VillageLayout.FromJson("{\"villages\": [{\"name\": \"x\"}]}"));
        }

        // Pinned: change it only on purpose (a layout change moves every village's walls).
        [Test]
        public void TheLayoutIsPinned()
        {
            Assert.That(VillageLayout.Lay(Spec(7), Anywhere).Digest().ToString("x8", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("6c384925"));
        }
    }
}
