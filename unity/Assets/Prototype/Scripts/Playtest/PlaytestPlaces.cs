using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Where the playtest bot can go (playtest/DESIGN.md, section 2): the valley's
// landmarks, found by their components the way the probes find them, plus
// where the wizard started. Each is a point on the ground to walk to; a place
// whose part isn't in the scene is simply left out.
public static class PlaytestPlaces
{
    public static List<(string name, Vector3 at)> Find(ChemGrid grid, Vector3 start)
    {
        var places = new List<(string, Vector3)> { ("start", start) };
        var tower = One<TowerCourse>(start);
        if (tower != null) places.Add(("tower", tower.startPad));
        Add(places, "ball court", One<BallCourt>(start));
        Add(places, "defense field", One<DefenseField>(start));
        Add(places, "village", One<Village>(start));
        Drawn(places, "waterfall", One<WaterfallFx>(start));
        var bridge = GameObject.Find("Bridge");
        if (bridge != null) places.Add(("bridge", bridge.transform.position));
        var ocean = Ocean.Instance;
        if (ocean != null)
        {
            var shore = ocean.shoreOrigin + ocean.seaward.normalized * SeaWade;
            places.Add(("sea", new Vector3(shore.x, 0f, shore.y)));
        }
        for (int i = places.Count - 1; i >= 0; i--)
        {
            var (name, at) = places[i];
            if (InWorld(grid, at)) places[i] = (name, new Vector3(at.x, grid.GroundAt(at), at.z));
            else places.RemoveAt(i);
        }
        return places;
    }

    const float SeaWade = 8f;

    public static bool InWorld(ChemGrid grid, Vector3 p)
    {
        var cell = grid.CellAt(p);
        return grid.World.InBounds(cell.x, cell.y);
    }

    static T One<T>(Vector3 start) where T : Component =>
        Object.FindObjectsByType<T>(FindObjectsSortMode.None)
            .OrderBy(c => (c.transform.position - start).sqrMagnitude)
            .ThenBy(c => c.transform.position.x).ThenBy(c => c.transform.position.z)
            .FirstOrDefault();

    static void Add(List<(string, Vector3)> places, string name, Component part)
    {
        if (part != null) places.Add((name, part.transform.position));
    }

    static void Drawn(List<(string, Vector3)> places, string name, Component part)
    {
        if (part == null) return;
        var renderers = part.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            places.Add((name, part.transform.position));
            return;
        }
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        places.Add((name, bounds.center));
    }

    public static string Near(List<(string name, Vector3 at)> places, ChemGrid grid, Vector3 p, float radius = 60f)
    {
        string best = null;
        float bestSq = radius * radius;
        foreach (var (name, at) in places)
        {
            float dx = at.x - p.x, dz = at.z - p.z, sq = dx * dx + dz * dz;
            if (sq >= bestSq) continue;
            bestSq = sq;
            best = name;
        }
        if (best != null) return best;
        var chunk = grid.ChunkAt(p);
        return $"chunk {chunk.x},{chunk.y}";
    }
}
