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
    /// <summary>How far the terrains draw details, in metres (ValleyGround sets it from them; ValleyTerrain bakes 120).</summary>
    public float DetailDistance { get; set; } = 120f;

    /// <summary>The chunk terrains, as TerrainGrid (i, j), that should draw details now.</summary>
    public IReadOnlyCollection<(int i, int j)> DetailChunks => detailChunks;

    /// <summary>Raised when DetailChunks changes, with the new set (ValleyGround's DrawDetails).</summary>
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
        detailLayout ??= TerrainGrid.Valley();
        if (World.W != detailLayout.GridW || World.H != detailLayout.GridH) return;
        // From the camera's cell, so a cell's width more: no terrain with grass in sight is left out.
        var near = detailLayout.ChunksWithin(cell.x, cell.y, DetailDistance / cellSize + 1);
        if (near.Count == detailChunks.Count && near.TrueForAll(detailChunks.Contains)) return;
        detailChunks.Clear();
        foreach (var c in near) detailChunks.Add(c);
        DetailChunksChanged?.Invoke(detailChunks);
    }
}
