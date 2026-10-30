using System.Collections.Generic;
using Chem;
using UnityEngine;

// Lava on the ground: one mesh over the lava cells with a rounded outline
// (ContourSurface), drawn by Prototype/CellLava as glowing cracks in a dark
// crust. Its vertex red is how hot the cell is, so a pool's cracks close as it
// cools towards rock; its alpha fades the edge. Rebuilt only when a cell
// changes (a pool's heat is drawn in steps: ChemGrid.HeatLevel). Only the show.
public sealed class CellLavaMesh
{
    const float Lift = 0.03f, FadeWidth = 0.3f;
    static readonly ContourSpec Outline = new(4, 0.5f, 0.6f, 53);

    readonly ChemGrid grid;
    readonly ContourSurface surface;
    readonly Material material;
    readonly Dictionary<int, float> heat = new();
    readonly System.Func<int, int, float?> heatAt;
    bool dirty;

    public CellLavaMesh(ChemGrid grid, Shader shader)
    {
        this.grid = grid;
        if (shader == null) return;
        material = new Material(shader);
        surface = new ContourSurface(grid, "Lava", material, Outline, 4f);
        heatAt = (x, y) => grid.World.InBounds(x, y) && heat.TryGetValue(grid.World.Idx(x, y), out float h) ? h : null;
    }

    /// <summary>How many cells of lava are drawn, for the probe.</summary>
    public int Cells => heat.Count;

    public static bool IsLava(Cell c) => c.T == TerrainIds.Lava || c.T == TerrainIds.LavaPool;

    /// <summary>A cell was redrawn: <paramref name="hot"/> is how hot it looks, 0 to 1.</summary>
    public void Changed(int x, int y, Cell c, float hot)
    {
        int key = grid.World.Idx(x, y);
        bool now = IsLava(c), had = heat.TryGetValue(key, out float was);
        if (now) heat[key] = hot;
        else heat.Remove(key);
        dirty |= had != now || (now && !Mathf.Approximately(was, hot));
    }

    /// <summary>Ground moved somewhere: the mesh follows it at the next Rebuild.</summary>
    public void GroundChanged() => dirty |= heat.Count > 0;

    /// <summary>Rebuilds the mesh if a cell changed since the last time.</summary>
    public void Rebuild()
    {
        if (!dirty || surface == null) return;
        dirty = false;
        surface.Build(Box(), (x, y) => heatAt(x, y) != null ? 1f : 0f, Height, Shade);
    }

    float Height(float gx, float gy) => grid.GroundAt(surface.Flat(gx, gy)) + Lift;

    Color Shade(float gx, float gy, float value)
    {
        float hot = ContourSurface.BlendAround(gx, gy, heatAt, 0f);
        return new Color(hot, 0f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Outline.Iso, Outline.Iso + FadeWidth, value)));
    }

    RectInt Box()
    {
        int w = grid.World.W, x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (int key in heat.Keys)
        {
            x0 = Mathf.Min(x0, key % w);
            y0 = Mathf.Min(y0, key / w);
            x1 = Mathf.Max(x1, key % w);
            y1 = Mathf.Max(y1, key / w);
        }
        return x1 < x0 ? new RectInt(0, 0, 0, 0) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    public void Destroy()
    {
        surface?.Destroy();
        Object.Destroy(material);
    }
}
