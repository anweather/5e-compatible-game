using System.Collections.Generic;
using System.IO;
using Chem;
using UnityEngine;

// Where the valley lies in a generated map (the nine, docs/design/world-squares.md phase 6). The yard scene is built for
// the valley on its own, with the places that stay code (PlaceRoot: the court, the bridge, the falls, the coast, the
// villages...) and the river's current (WaterCurrents) where the valley's cells are. A generated map pins the valley into
// one of its squares, so on one those stand where the valley is now, shifted by one offset in metres, and only the places
// the valley dresses that belong to the valley itself: its skyline and its world edge would wall it off from the rest.
// Only where things are drawn: the cells are the map's (D4).
public static class PinnedValley
{
    // The valley's places left out on a bigger map: its skyline and its world edge stand round it. Its lake stays: the sea
    // doesn't draw a generated map's basins (Ocean.DrawsBasins), and the lake's cells leave their water to the lake's own
    // surface (ValleyChemistry.IsLake), so without it the lake went undrawn and the wizard swam in the air (#223).
    static readonly string[] EdgePlaces = { "skyline", "world-edge" };

    sealed class Placement
    {
        public bool Generated;
        public Vector3 By;
        public string[] Places = System.Array.Empty<string>();
    }

    static readonly Dictionary<string, Placement> Known = new();

    static Placement Current
    {
        get
        {
            string world = ChemGrid.Folders.World;
            if (Known.TryGetValue(world, out var p)) return p;
            Known[world] = p = Find(ChemGrid.MapSource, world);
            return p;
        }
    }

    static Placement Find(string source, string world)
    {
        var p = new Placement();
        try
        {
            if (!GeneratedMap.IsGenerated(source)) return p;
            var map = LoadedMaps.Get(source);
            // A hills map pins nothing: it's generated (the far ring, the drawn tier), with no valley to dress or move.
            if (map.PinnedDir == null)
            {
                p.Generated = true;
                return p;
            }
            var valley = MapFile.Load(map.PinnedDir);
            var here = MapFile.Load(world);
            int x = map.Gen.PinnedX, y = map.Gen.PinnedY;
            var (ax, az) = valley.Layout().CellCenter(0, 0);
            var (bx, bz) = here.Layout().CellCenter(x, y);
            p.Generated = true;
            p.By = new Vector3(bx - ax, 0f, bz - az);
            p.Places = System.Array.FindAll(valley.Dresses, d => System.Array.IndexOf(EdgePlaces, d) < 0);
        }
        catch (System.Exception e) when (e is IOException || e is System.FormatException || e is Newtonsoft.Json.JsonException)
        {
            Debug.LogError($"MAP: where the valley lies in {source} can't be read: {e.Message}");
        }
        return p;
    }

    /// <summary>Whether the map being played is made from a world file with the valley pinned in it.</summary>
    public static bool InGenerated => Current.Generated;

    /// <summary>How far the valley's own dressing moves to stand where the valley is in this map (zero on the valley).</summary>
    public static Vector3 Shift => Current.By;

    /// <summary>The valley's places this generated map dresses where the valley lies in it.</summary>
    public static string[] Dresses => Current.Places;
}
