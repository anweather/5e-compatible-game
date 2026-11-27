// A map's own land stamps laid on a World as it's made (World.LayMapStamps). The stamps themselves are read in the land
// (MapStamps.cs, Chem.Land), which knows no World; laying them is the World's, so it's here, called as stamps.LayInto(world).
using System.Collections.Generic;
using System.Linq;

namespace Chem
{
    public static class MapStampsLaying
    {
        public static List<string> LayInto(this MapStamps stamps, World world)
        {
            var refused = new List<string>();
            foreach (var s in stamps.All)
            {
                var why = world.LayMapStamps(new[] { s.Stamp });
                refused.AddRange(why.Select(w => $"{MapStamps.FileName}: {s.Id} isn't laid, {w}"));
                if (why.Count > 0) continue;
                foreach (var terrain in new[] { s.Trail, s.Bare }.Where(t => t != null && !world.Rules.Terrain.ContainsKey(t)))
                    refused.Add($"{MapStamps.FileName}: {s.Id} lays {terrain}, which isn't a terrain");
                if (s.Trail != null && world.Rules.Terrain.ContainsKey(s.Trail))
                    foreach (var (x, y) in s.Stamp.Trail())
                        if (world.InBounds(x, y) && world.At(x, y).Water == 0) world.Paint(x, y, s.Trail);
                if (s.Bare != null && world.Rules.Terrain.ContainsKey(s.Bare)) Bare(world, s);
            }
            return refused;
        }

        static void Bare(World world, MapStamp s)
        {
            var stamp = s.Stamp;
            int reach = stamp.Bound;
            var (xFrom, xTo) = world.XSpan(stamp.X - reach, stamp.X + reach);
            var (yFrom, yTo) = world.YSpan(stamp.Y - reach, stamp.Y + reach);
            for (int y = yFrom; y <= yTo; y++)
                for (int x = xFrom; x <= xTo; x++)
                {
                    var c = world.At(x, y);
                    if (stamp.At(x - stamp.X, y - stamp.Y) == 0 || c.Water != 0) continue;
                    if (c.T != "tree" && (!world.HasTag(c, "ground") || world.IsBlocking(c))) continue;
                    world.Paint(x, y, s.Bare);
                    world.MarkBare(x, y);
                }
        }
    }
}
