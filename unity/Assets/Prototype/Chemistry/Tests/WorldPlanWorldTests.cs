// WorldPlanTests's tests on a World (docs/design/chem-land-split.md, 5b): the land's own are in WorldPlanTests.cs, which
// builds on the land alone (Chem.Land.Tests); these need the core (Chem.LandWorld.Tests).
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    [Category("Simulation")]
    public class WorldPlanWorldTests
    {
#pragma warning restore S1215

        [Test]
        public void TenSeedsAsPictures()
        {
            var plans = WorldPlanTests.Seeds(9).Select(WorldPlanTests.Nine).Select(p => (p, 96)).Append((WorldPlanTests.Big("seed-1", PlanWrap.EastWest), 48)).ToList();
            string folder = Environment.GetEnvironmentVariable("WORLD_PLAN_PNG_OUT");
            foreach (var (p, px) in plans)
            {
                var cv = WorldPlanPicture.Draw(p, px);
                Assert.That(cv.Rgb.Length, Is.EqualTo(p.Grid.W * px * p.Grid.H * px * 3));
                if (string.IsNullOrEmpty(folder)) continue;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"plan-{p.Inputs.Seed}-{p.Inputs.Size}.png");
                File.WriteAllBytes(path, LandmarkPicture.Png(cv.Rgb, cv.W, cv.H));
                string templates = string.Join(" ", Enumerable.Range(0, p.Count).Where(i => p.Wall[i] == 0 && p.Band[i] > 0)
                    .Select(i => $"{p.Options[i].Template.Id}/{p.Options[i].Turn}"));
                TestContext.Out.WriteLine($"PLAN PICTURE {path} 0x{p.Hash:x8}: {p.Landmarks.Count} landmarks, {p.Roads.Count} roads " +
                    $"({p.Roads.Count(r => r.IsMain)} main), {p.Crossings.Count} crossings, {p.Fillers.Count} fillers; {templates}");
            }
        }
    }
}
