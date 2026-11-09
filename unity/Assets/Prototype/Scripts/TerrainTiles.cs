using System.Collections.Generic;
using UnityEngine;

// The terrains by where they stand: the map's chunks are a grid of same-sized tiles, so a point finds its own in one step
// rather than asking each (the nine has 729: the sea's mesh asks for every vertex, the crabs every frame). Read once;
// make a new one when the terrains change. Falls back to asking each in turn for terrains that aren't such a grid.
public sealed class TerrainTiles
{
    readonly Terrain[] all = Terrain.activeTerrains;
    readonly Dictionary<(int, int), Terrain> byTile = new();
    readonly float x0, z0, w, d;
    readonly bool tiled;

    public TerrainTiles()
    {
        if (all.Length == 0) return;
        var size = all[0].terrainData.size;
        w = size.x;
        d = size.z;
        x0 = float.MaxValue;
        z0 = float.MaxValue;
        foreach (var t in all)
        {
            x0 = Mathf.Min(x0, t.transform.position.x);
            z0 = Mathf.Min(z0, t.transform.position.z);
        }
        tiled = w > 0f && d > 0f;
        foreach (var t in all)
        {
            var s = t.terrainData.size;
            var p = t.transform.position;
            float i = (p.x - x0) / w, j = (p.z - z0) / d;
            if (!Mathf.Approximately(s.x, w) || !Mathf.Approximately(s.z, d) || Mathf.Abs(i - Mathf.Round(i)) > 0.01f || Mathf.Abs(j - Mathf.Round(j)) > 0.01f)
                tiled = false;
            byTile.TryAdd((Mathf.RoundToInt(i), Mathf.RoundToInt(j)), t);
        }
    }

    // The first terrain holding the point, as asking each in turn would find it (a shared edge goes to either).
    public Terrain At(Vector3 p)
    {
        if (!tiled) return First(all, p);
        int i = Mathf.FloorToInt((p.x - x0) / w), j = Mathf.FloorToInt((p.z - z0) / d);
        for (int dj = 0; dj >= -1; dj--)
            for (int di = 0; di >= -1; di--)
                if (byTile.TryGetValue((i + di, j + dj), out var t) && t != null && Holds(t, p)) return t;
        return null;
    }

    static Terrain First(Terrain[] terrains, Vector3 p)
    {
        foreach (var t in terrains)
            if (t != null && Holds(t, p)) return t;
        return null;
    }

    static bool Holds(Terrain t, Vector3 p)
    {
        var size = t.terrainData.size;
        var local = p - t.transform.position;
        return local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z;
    }

    /// <summary>Whether it found any terrain to hold.</summary>
    public bool Any => all.Length > 0;

    /// <summary>The drawn ground's height under a point, or null off every terrain.</summary>
    public float? HeightAt(Vector3 p)
    {
        var t = At(p);
        return t != null ? t.SampleHeight(p) + t.transform.position.y : null;
    }
}
