// VillageTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in VillageTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System.Linq;

using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Unit")]
    public class VillageWorldTests
    {

        [Test]
        public void BuildingsAreMadeOfTheMaterialsInVillagesJson()
        {
            var specs = VillageLayout.Load(LandFolders.WorldDir());
            var m = specs[0].Materials;
            foreach (var material in m.Walls.Values.Concat(m.Roofs.Values))
                Assert.That(ChemistryTests.R.Terrain.ContainsKey(material), $"{material} is a terrain");
            var plan = VillageLayout.Lay(specs[0], VillageTests.Anywhere);
            foreach (var b in plan.Buildings)
            {
                Assert.That(b.WallMaterial, Is.EqualTo(m.WallOf(b.Walls)));
                Assert.That(b.RoofMaterial, Is.EqualTo(m.RoofOf(b.Cover)));
                Assert.That(b.WallCell, Is.EqualTo(b.Walls == WallKind.Stone ? '#' : 'L'), "stone stands, timber and log burn");
            }
            Assert.That(plan.Buildings.Any(b => b.Walls == WallKind.Stone) && plan.Buildings.Any(b => b.Walls != WallKind.Stone));
            Assert.Throws<System.FormatException>(() => VillageLayout.FromJson("{ 'materials': { 'walls': { 'timber': 'thatch' } }, 'villages': [] }"),
                "a wall material must stand on a map");
        }
    }
}
