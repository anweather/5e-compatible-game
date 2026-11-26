using System.IO;

namespace Chem.Tests
{
    /// <summary>
    /// A map folder made into a World as the game makes one, for the tests above the land that play on a map (saves,
    /// weather, heightfield threads, the benches). Was SparseWorldBench.Build, in the land's tests, until those built on
    /// the land alone (docs/design/chem-land-split.md, 5b).
    /// </summary>
    internal static class BuiltWorlds
    {
        /// <summary>A world as ChemGrid.MapWorld builds one from a map folder, and its starting state kept (StartWorld).</summary>
        internal static World Build(string dir, Rules rules, out BakedSource source)
        {
            var map = MapFile.Load(dir);
            source = BakedSource.Read(dir, rules);
            var w = new World(rules, source.W, source.H, 7);
            w.SetLandRange(map.LandRange);
            w.MakeFrom(source);
            w.SetLand(LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), map.W, map.H));
            WorldPuzzles.Load(dir).LayInto(w);
            MapStamps.Load(dir).LayInto(w);
            WorldBuildings.Load(dir).LayInto(w);
            if (GolfCommand.Load(dir) is { } hole) w.ApplyGolf(hole);
            if (WorldHold.Load(dir) is { } hold && w.InBounds(hold.Heart.x, hold.Heart.y)) w.SetHold(hold);
            w.KeepStart();
            return w;
        }
    }
}
