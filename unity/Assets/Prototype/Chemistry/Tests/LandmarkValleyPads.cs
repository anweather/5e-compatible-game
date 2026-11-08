// Throwaway (landmark-valley-look): the valley laid as ChemGrid lays it (stamps, then rows), and how level each new
// house's pad is, for J-080 1b.
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Chem.Tests
{
    public class LandmarkValleyPads
    {
        [Test, Explicit("measures the valley's landmark pads")]
        public void MeasureThem()
        {
            string dir = ChemistryTests.WorldDir();
            var w = new World(ChemistryTests.R, MapCheck.Size, MapCheck.Size, 7);
            for (int y = 0; y < w.H; y++)
                for (int x = 0; x < w.W; x++) w.Paint(x, y, "grass");
            foreach (var why in MapStamps.Load(dir).LayInto(w)) TestContext.Out.WriteLine("REFUSED " + why);
            WorldBuildings.Load(dir).LayInto(w);
            Assert.That(w.LandHasMoved);
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), w.W, w.H);
            int At(int x, int y) => land[w.Idx(x, y)] + w.LandMovedAt(x, y);
            foreach (var b in JObject.Parse(File.ReadAllText(Path.Combine(dir, "buildings.json")))["buildings"].OfType<JObject>().Where(b => ((string)b["id"]).StartsWith("emberhold#", StringComparison.Ordinal)))
            {
                int ax = (int)b["at"][0], ay = (int)b["at"][1], bw = (int)b["joins"]["ne"][0] / 2, bh = (int)b["joins"]["se"][1] / 2;
                var hs = Enumerable.Range(0, bw * bh).Select(k => At(ax + k % bw, ay + k / bw)).ToList();
                var raw = Enumerable.Range(0, bw * bh).Select(k => land[w.Idx(ax + k % bw, ay + k / bw)] + w.LandStampedAt(ax + k % bw, ay + k / bw)).ToList();
                TestContext.Out.WriteLine($"PAD {b["id"]} at {ax},{ay}: stamped slope {raw.Min()}..{raw.Max()}, after pad {hs.Min()}..{hs.Max()} (land units)");
            }
        }
    }
}
