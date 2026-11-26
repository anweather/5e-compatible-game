using System;
using System.Collections.Generic;
using Chem;
using UnityEngine;

// Which chunk terrains draw their details (grass and small plants): those any part of which
// is within the terrains' detail distance of the camera (TerrainGrid.ChunksWithin). The others
// would draw none anyway, but every terrain still costs the main thread a detail pass each
// frame, so ValleyGround turns theirs off (docs/WORLD_STREAMING.md, "The drawn ring"). Trees
// aren't details: every terrain keeps them, so the far view has no holes. Worked out again only
// when the camera crosses into another cell, or the world is replaced. The valley only: in a
// world of another size there are no chunk terrains to tell.
public partial class ChemGrid
{
    public float DetailDistance { get; set; } = MapTerrains.DetailMetres;

    public IReadOnlyCollection<(int i, int j)> DetailChunks => detailChunks;

    public event Action<IReadOnlyCollection<(int i, int j)>> DetailChunksChanged;

    readonly HashSet<(int i, int j)> detailChunks = new();
    TerrainGrid detailLayout;
    Vector2Int detailFrom;
    World detailWorld;

    void UpdateDetailChunks()
    {
        var cam = Camera.main;
        if (cam == null || World == null) return;
        var cell = CellAt(cam.transform.position);
        if (cell == detailFrom && World == detailWorld) return;
        detailFrom = cell;
        detailWorld = World;
        detailLayout ??= MapLand.Current?.Layout ?? TerrainGrid.Valley();
        if (World.W != detailLayout.GridW || World.H != detailLayout.GridH) return;
        var near = ChunksWithin(detailLayout, cell.x, cell.y, DetailDistance / cellSize + 1);
        if (near.Count == detailChunks.Count && near.TrueForAll(detailChunks.Contains)) return;
        detailChunks.Clear();
        foreach (var c in near) detailChunks.Add(c);
        DetailChunksChanged?.Invoke(detailChunks);
    }

    readonly List<(int cx, int cy)> detailBox = new();

    List<(int i, int j)> ChunksWithin(TerrainGrid layout, int x, int y, double radius)
    {
        double px = x + layout.U0 + 0.5, py = layout.V0 - y + 0.5;
        int i0 = (int)Math.Floor((px - radius) / TerrainGrid.ChunkCells), i1 = (int)Math.Floor((px + radius) / TerrainGrid.ChunkCells);
        int j0 = (int)Math.Floor((py - radius) / TerrainGrid.ChunkCells), j1 = (int)Math.Floor((py + radius) / TerrainGrid.ChunkCells);
        if (!ViewWrap.EastWest)
        {
            i0 = Mathf.Max(0, i0);
            i1 = Mathf.Min(layout.ChunksX - 1, i1);
        }
        if (!ViewWrap.NorthSouth)
        {
            j0 = Mathf.Max(0, j0);
            j1 = Mathf.Min(layout.ChunksZ - 1, j1);
        }
        detailBox.Clear();
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++) detailBox.Add((i, j));
        var near = ChunkStreaming.Within(detailBox, px, py, radius);
        if (ViewWrap.EastWest || ViewWrap.NorthSouth)
            for (int k = 0; k < near.Count; k++) near[k] = ViewWrap.FoldChunk(near[k], layout.ChunksX, layout.ChunksZ);
        return near;
    }
}
