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
    public const string Kind = "spawn";
    public const string StartId = "start";
    public const int ReachCells = 12;

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

    public static List<Spawn> Of(MapFile map, WorldPlaces places, string startName)
    {
        var start = new Vector2Int(map.Spawn.x, map.Spawn.y);
        var all = new List<Spawn> { new Spawn(StartId, startName, start) };
        foreach (var p in places.All)
        {
            if (p.Kind != Kind) continue;
            var cell = new Vector2Int(p.Middle.x, p.Middle.y);
            if ((cell - start).sqrMagnitude <= ReachCells * ReachCells)
            {
                all[0] = new Spawn(StartId, p.Name, start);
                continue;
            }
            all.Add(new Spawn(p.Id, p.Name, cell));
        }
        return all;
    }

    public static Spawn Find(List<Spawn> spawns, string id)
    {
        foreach (var s in spawns)
            if (s.Id == id) return s;
        return spawns[0];
    }

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

    public static bool Visit(List<Spawn> spawns, ISet<string> reached, Vector2Int cell)
    {
        bool added = false;
        foreach (var s in spawns)
            if ((s.Cell - cell).sqrMagnitude <= ReachCells * ReachCells) added |= reached.Add(s.Id);
        return added;
    }

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
        bool generated = GeneratedMap.IsGenerated(world);
        return Of(map, places, generated ? "The Valley" : map.Name);
    }

    static readonly Dictionary<string, List<Spawn>> ByMap = new();

    public static List<Spawn> OfMap(string id)
    {
        if (id == ChemGrid.MapId) return Current;
        if (!ByMap.TryGetValue(id, out var spawns)) ByMap[id] = spawns = InFolder(ChemGrid.PlayFolder(id));
        return spawns;
    }

    public static bool Several => Current.Count > 1;

    public static string StartIn { get; set; }

    public static Vector2Int StartCell(Vector2Int mapStart)
    {
        if (string.IsNullOrEmpty(StartIn) && !Several) return mapStart;
        var s = Find(Current, StartIn);
        Reached.Add(s.Id);
        return s.Id == StartId ? mapStart : s.Cell;
    }

    public static bool Reach(Vector2Int cell) => Visit(Current, Reached, cell);

    public static Spawn ComeBackAt(Vector2Int cell) => Nearest(Current, Reached, cell);
}
