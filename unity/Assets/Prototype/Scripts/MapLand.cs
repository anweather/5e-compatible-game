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
    public readonly LandChunks Land;
    public readonly bool FromFile;
    public readonly bool Generated;
    public readonly SeaMask Sea;
    public readonly float WaterLevel;
    public readonly Vector2 Shore, Seaward;

    static MapLand current;
    static string currentDir;

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
        Generated = !FromFile && GeneratedMap.IsRowsOnly(dir);
        if (Generated)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Land = LandChunks.FromSource(ChemGrid.PlayedSource);
            Sea = SeaMask.OfKnown(LoadedMaps.Get(ChemGrid.MapSource).Gen);
            Debug.Log($"MAP LAND {Map.Id}: made as it's read; its sea known from its chunks in {timer.Elapsed.TotalMilliseconds:0} ms ({Sea.SeaCells} sea cells, {Sea.Bytes / 1024} KB)");
        }
        else Land = FromFile ? LandChunks.Read(File.ReadAllText(path), Map.W, Map.H, Map.SeaLevel, out Sea) : null;
        if (Land == null)
        {
            Land = LandChunks.Uniform(Map.W, Map.H, Map.SeaLevel);
            Sea = SeaMask.Of(Land, Map.SeaLevel);
        }
        WaterLevel = (float)Map.WaterMetres;
        if (Map.Seaward is { } s) Seaward = new Vector2((float)s.x, (float)s.z).normalized;
        else Seaward = Generated ? Vector2.down : LowWay();
        Shore = Map.SeaShore is { } o ? new Vector2((float)o.x, (float)o.z) : Middle;
    }

    public static void Forget()
    {
        current?.Release();
        current = null;
        currentDir = null;
    }

    const int KeptChunks = 256;
    readonly System.Collections.Generic.Dictionary<int, int[]> keptChunks = new();
    readonly System.Collections.Generic.Stack<int[]> spareChunks = new();

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

    public Vector2 Middle
    {
        get
        {
            var (x, z) = Layout.CellCenter(Map.W / 2, Map.H / 2);
            return new Vector2(x, z);
        }
    }

    public float Reach => Layout.ChunkSize * Mathf.Max(Layout.ChunksX, Layout.ChunksZ) * 0.7072f;

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
        var way = new Vector2((float)sx, (float)-sy);
        return way.sqrMagnitude > 0f ? way.normalized : Vector2.down;
    }

    public Vector2Int CellOf(Vector2 xz)
    {
        var (x, y) = Layout.CellAt(xz.x, xz.y);
        return new Vector2Int(x, y);
    }

    public bool IsSea(Vector2 xz)
    {
        var c = CellOf(xz);
        return Sea.IsSea(c.x, c.y);
    }

    public bool IsLand(Vector2 xz)
    {
        var c = CellOf(xz);
        int x = Mathf.Clamp(c.x, 0, Map.W - 1), y = Mathf.Clamp(c.y, 0, Map.H - 1);
        if (Generated) return !Sea.IsSea(x, y) && !Sea.IsBasin(x, y);
        return LandAt(x, y) >= Map.SeaLevel;
    }

    ShoreMap shoreMap;

    public ShoreMap ShoreMap => shoreMap != null ? shoreMap : shoreMap = ShoreMap.FromLand(IsLand, ShoreMapMiddle);

    Vector2 ShoreMapMiddle => Generated ? Shore : Middle + Seaward * (ShoreMap.Side / 2f - Reach);

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
