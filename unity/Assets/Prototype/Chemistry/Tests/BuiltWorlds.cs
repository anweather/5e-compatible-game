// A map folder made into a World as the game makes one, for the tests above the land that play on a map (saves,
// weather, heightfield threads, the benches). Was SparseWorldBench.Build, in the land's tests, until those built on
// the land alone (docs/design/chem-land-split.md, 5b). Its modes are the caller's (golf lays its hole and the Hold its site as
// the map is laid).
using System.IO;

namespace Chem.Tests
{
    internal static class BuiltWorlds
    {
        internal static World Build(string dir, Rules rules, out BakedSource source, System.Func<World, World> modes = null)
        {
            var map = MapFile.Load(dir);
            source = BakedSource.Read(dir, rules);
            var w = new World(rules, source.W, source.H, 7);
            if (modes != null) w = modes(w);
            w.SetLandRange(map.LandRange);
            w.MakeFrom(source);
            w.SetLand(LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H));
            WorldPuzzles.Load(dir).LayInto(w);
            MapStamps.Load(dir).LayInto(w);
            WorldBuildings.Load(dir).LayInto(w);
            w.MapLaid(dir);
            w.KeepStart();
            return w;
        }
    }
}
