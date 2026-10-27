using System.Collections.Generic;
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
        var tower = Object.FindAnyObjectByType<TowerCourse>();
        if (tower != null) places.Add(("tower", tower.startPad));
        Add(places, "ball court", Object.FindAnyObjectByType<BallCourt>());
        Add(places, "defense field", Object.FindAnyObjectByType<DefenseField>());
        Add(places, "village", Object.FindAnyObjectByType<Village>());
        // The waterfall and the sea are drawn away from their objects' origins: go by what they draw.
        Drawn(places, "waterfall", Object.FindAnyObjectByType<WaterfallFx>());
        var bridge = GameObject.Find("Bridge");
        if (bridge != null) places.Add(("bridge", bridge.transform.position));
        Drawn(places, "sea", Ocean.Instance);
        // Put each on the ground (a landmark's own point may be up a pole or under the water).
        for (int i = 0; i < places.Count; i++)
        {
            var (name, at) = places[i];
            places[i] = (name, new Vector3(at.x, grid.GroundAt(at), at.z));
        }
        return places;
    }

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
