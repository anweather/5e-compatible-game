using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Where the playtest bot can go (playtest/DESIGN.md, section 2): the valley's
// landmarks, found by their components the way the probes find them, plus
// where the wizard started. Each is a point on the ground to walk to; a place
// whose part isn't in the scene is simply left out.
public static class PlaytestPlaces
{
    /// <summary>The places by name, in a fixed order (so the bot's choices replay).</summary>
    public static List<(string name, Vector3 at)> Find(ChemGrid grid, Vector3 start)
    {
        var places = new List<(string, Vector3)> { ("start", start) };
        var tower = One<TowerCourse>(start);
        if (tower != null) places.Add(("tower", tower.startPad));
        Add(places, "ball court", One<BallCourt>(start));
        Add(places, "defense field", One<DefenseField>(start));
        Add(places, "village", One<Village>(start));
        // The waterfall is drawn away from its object's origin: go by what it draws.
        Drawn(places, "waterfall", One<WaterfallFx>(start));
        var bridge = GameObject.Find("Bridge");
        if (bridge != null) places.Add(("bridge", bridge.transform.position));
        // The sea: a few metres out from the shore nearest the valley. Its drawing reaches the
        // horizon, so the middle of what it draws is kilometres off the world.
        var ocean = Ocean.Instance;
        if (ocean != null)
        {
            var shore = ocean.shoreOrigin + ocean.seaward.normalized * SeaWade;
            places.Add(("sea", new Vector3(shore.x, 0f, shore.y)));
        }
        // Put each on the ground (a landmark's own point may be up a pole or under the water),
        // and leave out any that isn't on the world.
        for (int i = places.Count - 1; i >= 0; i--)
        {
            var (name, at) = places[i];
            if (InWorld(grid, at)) places[i] = (name, new Vector3(at.x, grid.GroundAt(at), at.z));
            else places.RemoveAt(i);
        }
        return places;
    }

    /// <summary>Metres from the shore into the sea that "the sea" is: wading depth.</summary>
    const float SeaWade = 8f;

    /// <summary>Whether a point is over the world's cells (the bot never sends the wizard anywhere else).</summary>
    public static bool InWorld(ChemGrid grid, Vector3 p)
    {
        var cell = grid.CellAt(p);
        return grid.World.InBounds(cell.x, cell.y);
    }

    // The one nearest the start, if there are several (there are villages): FindAnyObjectByType
    // promises no order, so a run and its replay could each go to a different one.
    static T One<T>(Vector3 start) where T : Component =>
        Object.FindObjectsByType<T>(FindObjectsSortMode.None)
            .OrderBy(c => (c.transform.position - start).sqrMagnitude)
            .ThenBy(c => c.transform.position.x).ThenBy(c => c.transform.position.z)
            .FirstOrDefault();

    static void Add(List<(string, Vector3)> places, string name, Component part)
    {
        if (part != null) places.Add((name, part.transform.position));
    }

    // The middle of everything a part and its children draw (or its position if it draws nothing).
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

    /// <summary>The nearest place within a radius, for a candidate's signature; else the chunk it's in.</summary>
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
