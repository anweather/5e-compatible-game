using System.Collections.Generic;
using Chem;
using UnityEngine;

// Lava on the ground: a mesh over the lava cells with a rounded outline
// (ContourSurface), drawn by Prototype/CellLava as glowing cracks in a dark
// crust, in tiles of TileCells (as the slicks are): a change redraws only the tiles round it, so a Fireball's few
// embers don't outline every cell between them and the volcano's lava (48,107 cells on the nine, #188). Its vertex red is how hot the cell is, so a pool's cracks close as it
// cools towards rock; its alpha fades the edge. Fireball's and a star's burning
// debris (the embers cover) is drawn in it too, duller, and in the look of the
// spell that threw it (vertex green: LookTint). Rebuilt only when a cell changes
// (a pool's heat is drawn in steps: ChemGrid.HeatLevel). Only the show.
public sealed class CellLavaMesh
{
    const float Lift = 0.03f, FadeWidth = 0.3f;
    static readonly ContourSpec Outline = new(4, 0.5f, 0.6f, 53);

    const int TileCells = 16, TileRing = 2;

    readonly ChemGrid grid;
    readonly Material material;
    readonly Dictionary<int, ContourSurface> tiles = new();
    readonly HashSet<int> dirtyTiles = new();
    readonly List<int> drawing = new();
    readonly Dictionary<int, int> lavaInTile = new();
    int tilesX;
    readonly Dictionary<int, float> heat = new();
    readonly Dictionary<int, float> turns = new();
    readonly System.Func<int, int, float?> heatAt, turnAt;

    public CellLavaMesh(ChemGrid grid, Shader shader)
    {
        this.grid = grid;
        if (shader == null) return;
        material = new Material(shader);
        heatAt = (x, y) => grid.World.InBounds(x, y) && heat.TryGetValue(grid.World.Idx(x, y), out float h) ? h : null;
        turnAt = (x, y) => grid.World.InBounds(x, y) && turns.TryGetValue(grid.World.Idx(x, y), out float t) ? t : null;
    }

    public int Cells => heat.Count;

    public static bool IsLava(Cell c) => c.T == TerrainIds.Lava || c.T == TerrainIds.LavaPool;

    public float TurnOn(int x, int y) => turns.TryGetValue(grid.World.Idx(x, y), out float t) ? t : 0f;

    public bool Changed(int x, int y, Cell c, float hot, LookTint tint = default)
    {
        int key = grid.World.Idx(x, y);
        bool now = IsLava(c) || c.Cover == CoverIds.Embers, had = heat.TryGetValue(key, out float was);
        if (now) heat[key] = hot;
        else heat.Remove(key);
        if (had != now) Count(x, y, now ? 1 : -1);
        float turn = now ? Mathf.Repeat(tint.Turn / (2f * Mathf.PI), 1f) : 0f;
        bool turned = turns.TryGetValue(key, out float wasTurn) ? !Mathf.Approximately(wasTurn, turn) : turn > 0f;
        if (turn > 0f) turns[key] = turn;
        else turns.Remove(key);
        if (had != now || turned || (now && !Mathf.Approximately(was, hot))) Dirty(x, y);
        return had && !now;
    }

    public void GroundChanged(int x, int y)
    {
        if (tiles.Count > 0) Dirty(x, y);
    }

    public void Rebuild()
    {
        if (dirtyTiles.Count == 0 || material == null) return;
        drawing.Clear();
        drawing.AddRange(dirtyTiles);
        dirtyTiles.Clear();
        foreach (int tile in drawing) DrawTile(tile);
    }

    int TilesX
    {
        get
        {
            if (tilesX <= 0) tilesX = (grid.World.W + TileCells - 1) / TileCells;
            return tilesX;
        }
    }

    void Dirty(int x, int y)
    {
        int tx0 = Mathf.Max(0, (x - TileRing) / TileCells), tx1 = Mathf.Min(TilesX - 1, (x + TileRing) / TileCells);
        int ty0 = Mathf.Max(0, (y - TileRing) / TileCells), ty1 = (y + TileRing) / TileCells;
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++) dirtyTiles.Add(ty * TilesX + tx);
    }

    void Count(int x, int y, int by)
    {
        int tile = y / TileCells * TilesX + x / TileCells;
        int n = (lavaInTile.TryGetValue(tile, out int was) ? was : 0) + by;
        if (n > 0) lavaInTile[tile] = n;
        else lavaInTile.Remove(tile);
    }

    void DrawTile(int tile)
    {
        var world = grid.World;
        int tx = tile % TilesX, ty = tile / TilesX, x0 = tx * TileCells, y0 = ty * TileCells;
        var box = new RectInt(x0, y0, Mathf.Min(TileCells, world.W - x0), Mathf.Min(TileCells, world.H - y0));
        if (box.width <= 0 || box.height <= 0) return;
        if (!AnyNear(tx, ty))
        {
            if (tiles.Remove(tile, out var gone)) gone.Destroy();
            return;
        }
        if (!tiles.TryGetValue(tile, out var surface))
        {
            surface = new ContourSurface(grid, "Lava", material, Outline, 4f, tiled: true);
            tiles[tile] = surface;
        }
        surface.Build(box, (x, y) => heatAt(x, y) != null ? 1f : 0f, Height, Shade);
    }

    bool AnyNear(int tx, int ty)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = tx + dx, y = ty + dy;
                if (x >= 0 && y >= 0 && x < TilesX && lavaInTile.ContainsKey(y * TilesX + x)) return true;
            }
        return false;
    }

    float Height(float gx, float gy) => grid.GroundAt(ContourSurface.FlatOn(grid, gx, gy)) + Lift;

    Color Shade(float gx, float gy, float value)
    {
        float hot = ContourSurface.BlendAround(gx, gy, heatAt, 0f);
        float turn = turns.Count > 0 ? ContourSurface.BlendAround(gx, gy, turnAt, 0f) : 0f;
        return new Color(hot, turn, 0f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Outline.Iso, Outline.Iso + FadeWidth, value)));
    }

    public void Destroy()
    {
        foreach (var surface in tiles.Values) surface.Destroy();
        tiles.Clear();
        Object.Destroy(material);
    }
}
