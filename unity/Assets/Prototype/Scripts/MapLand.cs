using System.IO;
using Chem;
using UnityEngine;

// The map's land as the view reads it (docs/design/maps-as-data.md, phase 7): its map.json, where its cells stand
// (TerrainGrid), its land, and from that land where the sea and the basins are (SeaMask) and the shore map the waves and
// currents follow. Read once per map folder, in the Editor too (the sea builds itself there), and again when the map
// changes. A view of the land: nothing here touches the chemistry (D4).
public sealed class MapLand
{
    public readonly MapFile Map;
    public readonly TerrainGrid Layout;
    /// <summary>The land, row by row, in land units; all at the sea level when the map has no land.json.</summary>
    public readonly LandChunks Land;
    /// <summary>Whether Land is the map's land.json (not the sea level all over for want of one).</summary>
    public readonly bool FromFile;
    public readonly SeaMask Sea;
    /// <summary>The height the sea and the lakes are drawn at (m).</summary>
    public readonly float WaterLevel;
    /// <summary>Where the swell comes in from: a point on the coast, and the way out to sea (level, unit length).</summary>
    public readonly Vector2 Shore, Seaward;

    static MapLand current;
    static string currentDir;

    /// <summary>The map the game is on (ChemGrid.Folders.World); null if its folder can't be read.</summary>
    public static MapLand Current
    {
        get
        {
            string dir = ChemGrid.Folders.World;
            if (current != null && currentDir == dir) return current;
            current?.Release();
            try
            {
                current = new MapLand(dir);
            }
            catch (System.Exception e) when (e is IOException || e is System.FormatException || e is System.NullReferenceException ||
                                             e is Newtonsoft.Json.JsonException || e is System.ArgumentException)
            {
                Debug.LogError($"MAP LAND: {dir} can't be read: {e.Message}");
                current = null;
            }
            currentDir = dir;
            return current;
        }
    }

    MapLand(string dir)
    {
        Map = MapFile.Load(dir);
        Layout = Map.Layout();
        string path = Path.Combine(dir, MapFile.LandName);
        FromFile = File.Exists(path);
        // The land and its sea in one pass over the file (sea-land-speed); the sea level all over without one.
        Land = FromFile ? LandChunks.Read(File.ReadAllText(path), Map.W, Map.H, Map.SeaLevel, out Sea) : null;
        if (Land == null)
        {
            Land = LandChunks.Uniform(Map.W, Map.H, Map.SeaLevel);
            Sea = SeaMask.Of(Land, Map.SeaLevel);
        }
        WaterLevel = (float)Map.WaterMetres;
        Seaward = Map.Seaward is { } s ? new Vector2((float)s.x, (float)s.z).normalized : LowWay();
        Shore = Map.SeaShore is { } o ? new Vector2((float)o.x, (float)o.z) : Middle;
    }

    /// <summary>The map's land.json was written again (Bake Valley Land): the next Current reads it afresh.</summary>
    public static void Forget()
    {
        current?.Release();
        current = null;
        currentDir = null;
    }

    // The chunks LandAt decoded last, kept for the next reads: a scan along rows (the shore map, the ambient life) comes
    // back to each chunk for every row through it, and LandChunks.At keeps only one, so it decoded each chunk 64 times.
    const int KeptChunks = 256;
    readonly System.Collections.Generic.Dictionary<int, int[]> keptChunks = new();
    readonly System.Collections.Generic.Stack<int[]> spareChunks = new();

    /// <summary>The land at a cell, in land units, with the chunks read lately kept decoded. The main thread only.</summary>
    public int LandAt(int x, int y)
    {
        int cx = x >> Chunks.Shift, cy = y >> Chunks.Shift, key = cy * ((Map.W + Chunks.Size - 1) >> Chunks.Shift) + cx;
        if (!keptChunks.TryGetValue(key, out var cells))
        {
            if (keptChunks.Count >= KeptChunks)
            {
                foreach (var spare in keptChunks.Values) spareChunks.Push(spare);
                keptChunks.Clear();
            }
            cells = spareChunks.Count > 0 ? spareChunks.Pop() : new int[Chunks.Area];
            Land.ChunkInto(cx, cy, cells);
            keptChunks[key] = cells;
        }
        return cells[((y & Chunks.Mask) << Chunks.Shift) | (x & Chunks.Mask)];
    }

    /// <summary>The map's middle, in world metres (x, z).</summary>
    public Vector2 Middle
    {
        get
        {
            var (x, z) = Layout.CellCenter(Map.W / 2, Map.H / 2);
            return new Vector2(x, z);
        }
    }

    /// <summary>How far the map's corners are from its middle (m).</summary>
    public float Reach => Layout.ChunkSize * Mathf.Max(Layout.ChunksX, Layout.ChunksZ) * 0.7072f;

    // The way from the middle toward the sea's cells, or south when there's none.
    Vector2 LowWay()
    {
        double sx = 0, sy = 0;
        for (int y = 0; y < Map.H; y++)
            for (int x = 0; x < Map.W; x++)
                if (Sea.IsSea(x, y))
                {
                    sx += x - Map.W / 2;
                    sy += y - Map.H / 2;
                }
        // Cells run south as y grows.
        var way = new Vector2((float)sx, (float)-sy);
        return way.sqrMagnitude > 0f ? way.normalized : Vector2.down;
    }

    /// <summary>The cell a world point (x, z) is on (it may be off the map).</summary>
    public Vector2Int CellOf(Vector2 xz)
    {
        var (x, y) = Layout.CellAt(xz.x, xz.y);
        return new Vector2Int(x, y);
    }

    /// <summary>Whether a world point is out at sea: on a sea cell, or off the map when the map's edge reaches the sea.</summary>
    public bool IsSea(Vector2 xz)
    {
        var c = CellOf(xz);
        return Sea.IsSea(c.x, c.y);
    }

    /// <summary>Whether a world point is land, for the shore map: off the map, the nearest edge cell says.</summary>
    public bool IsLand(Vector2 xz)
    {
        var c = CellOf(xz);
        int x = Mathf.Clamp(c.x, 0, Map.W - 1), y = Mathf.Clamp(c.y, 0, Map.H - 1);
        return LandAt(x, y) >= Map.SeaLevel;
    }

    ShoreMap shoreMap;

    /// <summary>The way to the nearest land over the sea, worked out from the land the first time it's asked for.</summary>
    public ShoreMap ShoreMap => shoreMap != null ? shoreMap : shoreMap = ShoreMap.FromLand(IsLand, Middle + Seaward * (ShoreMap.Side / 2f - Reach));

    // The shore map is DontSave, so nothing frees it but this: left when the map changed or the scripts reloaded (which
    // forgets `current`), every one stayed in the Editor, 4 MB each (#145). Whoever drew with it builds again from Current.
    void Release()
    {
        keptChunks.Clear();
        spareChunks.Clear();
        if (shoreMap == null) return;
        Free(shoreMap.texture);
        Free(shoreMap);
        shoreMap = null;
    }

    static void Free(Object thing)
    {
        if (thing == null) return;
        if (Application.isPlaying) Object.Destroy(thing);
        else Object.DestroyImmediate(thing);
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    static void ReleaseBeforeReload() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => current?.Release();
#endif
}
