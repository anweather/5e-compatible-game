using System.Collections.Generic;
using Chem;
using UnityEngine;

// Where a player can start or come back on a map (world squares phase 6, Andrew's "more spawn points in the 3x3"): the
// map's own spawn (map.json, the valley's on the nine) first, then each "spawn" place in its places.json, one a square
// on the nine once the plan gives them. The host picks one to start in (the Esc menu's Start in) and joiners start beside
// the host; a wizard who goes down comes back at the nearest the party has reached. Players' places are each owner's, not
// the core's, so nothing here touches the cells (D4).
public static class SpawnPoints
{
    /// <summary>A place's kind in places.json for a spawn.</summary>
    public const string Kind = "spawn";
    /// <summary>The id the map's own spawn goes by.</summary>
    public const string StartId = "start";
    /// <summary>How near a wizard comes to a spawn for the party to have reached it, in cells.</summary>
    public const int ReachCells = 12;

    /// <summary>One spawn: its id, its name and its cell.</summary>
    public readonly struct Spawn
    {
        public readonly string Id, Name;
        public readonly Vector2Int Cell;

        public Spawn(string id, string name, Vector2Int cell)
        {
            Id = id;
            Name = name;
            Cell = cell;
        }
    }

    /// <summary>A map's spawns: its own first (named <paramref name="startName"/>), then its spawn places, in file order.</summary>
    public static List<Spawn> Of(MapFile map, WorldPlaces places, string startName)
    {
        var start = new Vector2Int(map.Spawn.x, map.Spawn.y);
        var all = new List<Spawn> { new Spawn(StartId, startName, start) };
        foreach (var p in places.All)
        {
            if (p.Kind != Kind) continue;
            var cell = new Vector2Int(p.Middle.x, p.Middle.y);
            // A spawn place on the map's own spawn is that one (the valley's, "start" in the nine's places), by its name.
            if ((cell - start).sqrMagnitude <= ReachCells * ReachCells)
            {
                all[0] = new Spawn(StartId, p.Name, start);
                continue;
            }
            all.Add(new Spawn(p.Id, p.Name, cell));
        }
        return all;
    }

    /// <summary>The spawn by id, or the first (the map's own) for an id it doesn't have.</summary>
    public static Spawn Find(List<Spawn> spawns, string id)
    {
        foreach (var s in spawns)
            if (s.Id == id) return s;
        return spawns[0];
    }

    /// <summary>The reached spawn nearest a cell; the first when none is reached.</summary>
    public static Spawn Nearest(List<Spawn> spawns, ICollection<string> reached, Vector2Int cell)
    {
        var best = spawns[0];
        long bestD = long.MaxValue;
        foreach (var s in spawns)
        {
            if (!reached.Contains(s.Id)) continue;
            long dx = s.Cell.x - cell.x, dy = s.Cell.y - cell.y, d = dx * dx + dy * dy;
            if (d < bestD)
            {
                bestD = d;
                best = s;
            }
        }
        return best;
    }

    /// <summary>The spawns a wizard at <paramref name="cell"/> has reached (within ReachCells), added to <paramref name="reached"/>; whether any was new.</summary>
    public static bool Visit(List<Spawn> spawns, ISet<string> reached, Vector2Int cell)
    {
        bool added = false;
        foreach (var s in spawns)
            if ((s.Cell - cell).sqrMagnitude <= ReachCells * ReachCells) added |= reached.Add(s.Id);
        return added;
    }

    // ---------- the map being played ----------

    static List<Spawn> current;
    static string currentOf;
    static readonly HashSet<string> Reached = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Forget()
    {
        current = null;
        currentOf = null;
        Reached.Clear();
        StartIn = null;
        ByMap.Clear();
    }

    /// <summary>The spawns of the map being played; the map's own first.</summary>
    public static List<Spawn> Current
    {
        get
        {
            string world = ChemGrid.Folders.World;
            if (current != null && currentOf == world) return current;
            current = InFolder(world);
            currentOf = world;
            Reached.Clear();
            return current;
        }
    }

    // A played map folder's spawns.
    static List<Spawn> InFolder(string world)
    {
        var map = MapFile.Load(world);
        WorldPlaces places;
        try
        {
            places = WorldPlaces.Load(world);
        }
        catch (System.FormatException e)
        {
            Debug.LogError($"MAP: {WorldPlaces.FileName} can't be read, so the only spawn is the map's own: {e.Message}");
            places = WorldPlaces.Empty;
        }
        // On a generated map the map's own spawn is the pinned valley's.
        bool generated = GeneratedMap.IsGenerated(world);
        return Of(map, places, generated ? "The Valley" : map.Name);
    }

    static readonly Dictionary<string, List<Spawn>> ByMap = new();

    /// <summary>The spawns of a map by id (made ready to play first, a generated one baked), for the Esc menu's Start in.</summary>
    public static List<Spawn> OfMap(string id)
    {
        if (id == ChemGrid.MapId) return Current;
        if (!ByMap.TryGetValue(id, out var spawns)) ByMap[id] = spawns = InFolder(ChemGrid.PlayFolder(id));
        return spawns;
    }

    /// <summary>Whether the map has more than its own spawn to pick from.</summary>
    public static bool Several => Current.Count > 1;

    /// <summary>The spawn to start in (the host's pick, by id); null for the map's own.</summary>
    public static string StartIn { get; set; }

    /// <summary>The cell to start in on this map: the picked spawn's, else <paramref name="mapStart"/>.</summary>
    public static Vector2Int StartCell(Vector2Int mapStart)
    {
        // A map with one spawn (the valley on its own) and none picked is left as it was.
        if (string.IsNullOrEmpty(StartIn) && !Several) return mapStart;
        var s = Find(Current, StartIn);
        Reached.Add(s.Id);
        return s.Id == StartId ? mapStart : s.Cell;
    }

    /// <summary>Notes the spawns a wizard at this cell has reached, for the whole party; whether it reached a new one.</summary>
    public static bool Reach(Vector2Int cell) => Visit(Current, Reached, cell);

    /// <summary>Where a wizard that went down at <paramref name="cell"/> comes back: the nearest spawn the party has reached.</summary>
    public static Spawn ComeBackAt(Vector2Int cell) => Nearest(Current, Reached, cell);
}
