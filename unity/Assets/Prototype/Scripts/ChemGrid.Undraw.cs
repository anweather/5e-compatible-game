using System.Collections.Generic;
using Chem;
using UnityEngine;

// Drawn chunks far from everyone let go (a generated map's, the far ring's known radius and UndrawMargin past it): their
// cells' dressings destroyed, their blockers back to the pool, their water taken off the surface and their views
// dropped, so what the view holds follows where the characters are, never how far they've gone. Kept, a crossing of
// the island drew every chunk it passed and held them all: a million objects and 1.5 GB more by the far coast. A chunk
// let go draws again from its cells as it comes back within the draw radius (DrawSome), as on first sight; the margin is
// so one on the edge isn't let go and drawn again as a character steps back and forth. Only an inactive chunk whose
// cells show nothing passing (a cover, gas, fire, charge, thorns, vines, ground easing) goes: one that does stays drawn.
// The view alone: nothing the core simulates changes.
public partial class ChemGrid
{
    // Past the draw radius by this many chunks, a drawn chunk is let go; and how often the drawn ones are looked over.
    const int UndrawMargin = 2;
    const float UndrawEvery = 0.5f;
    // At most this many chunks let go a frame (a forest chunk's dressings destroyed are a few ms, at the frame's end).
    const int UndrawAFrame = 1;

    readonly List<int> toUndraw = new();
    float nextUndrawLook;

    /// <summary>Chunks let go since the world was built (drawn again as they come back within reach), for the probe.</summary>
    public int ChunksUndrawn { get; private set; }

    /// <summary>Chunks drawn now (drawn and not let go), for the probe.</summary>
    public int ChunksHeldDrawn => drawnChunks.Count;

    // Each drawn chunk's dressings stand under a root of its own, a scene root posed as the grid is, not under the grid:
    // Unity keeps one transform hierarchy a root, sized to the most it ever held and never shrunk, so with them all under
    // the grid it held the crossing's peak (300 thousand transforms) for good; a chunk's root goes with it (Undraw).
    readonly Dictionary<int, Transform> cellRoots = new();

    /// <summary>What a cell's dressing stands under: its chunk's root, made the first time one is wanted.</summary>
    public Transform CellRoot(int x, int y)
    {
        if (World == null) return transform;
        int cx = Chunks.ChunkOf(x), cy = Chunks.ChunkOf(y), n = cy * ChunksAcross + cx;
        if (cellRoots.TryGetValue(n, out var root) && root != null) return root;
        root = new GameObject($"Cells {cx},{cy}").transform;
        root.SetPositionAndRotation(transform.position, transform.rotation);
        root.localScale = transform.lossyScale;
        cellRoots[n] = root;
        return root;
    }

    // Every chunk's root goes (a new world: ResetWorld; the grid going).
    void ForgetCellRoots()
    {
        foreach (var root in cellRoots.Values)
            if (root != null) Destroy(root.gameObject);
        cellRoots.Clear();
    }

    void UndrawFar()
    {
        int radius = DrawRadius();
        if (radius == int.MaxValue || views == null || drawnChunks.Count == 0) return;
        if (Time.unscaledTime >= nextUndrawLook)
        {
            nextUndrawLook = Time.unscaledTime + UndrawEvery;
            toUndraw.Clear();
            var focus = Focus();
            int across = ChunksAcross;
            foreach (int n in drawnChunks)
                if (FocusDistance(focus, n % across, n / across) > radius + UndrawMargin) toUndraw.Add(n);
        }
        for (int done = 0; done < UndrawAFrame && toUndraw.Count > 0;)
        {
            int n = toUndraw[toUndraw.Count - 1];
            toUndraw.RemoveAt(toUndraw.Count - 1);
            if (n < drawn.Length && drawn[n] && Undraw(n)) done++;
        }
    }

    // Chunk n let go, if nothing passing shows on it; false if it stays drawn.
    bool Undraw(int n)
    {
        int across = ChunksAcross, cx = n % across, cy = n / across;
        if (World.IsActive(cx, cy) || n == partChunk) return false;
        int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
        int x1 = Mathf.Min(World.W, x0 + Chunks.Size), y1 = Mathf.Min(World.H, y0 + Chunks.Size);
        foreach (var s in shaping)
            if (s.x >= x0 && s.x < x1 && s.y >= y0 && s.y < y1) return false;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (views[y * World.W + x] is { Plain: false }) return false;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int index = y * World.W + x;
                var v = views[index];
                if (v == null) continue;
                if (v.wet) waterSurface.Changed(x, y, false);
                blockers.Remove(index);
            }
        // The chunk's dressings all stand under its root (CellRoot): it goes with them.
        if (cellRoots.Remove(n, out var root) && root != null) Destroy(root.gameObject);
        views.Drop(cx, cy);
        drawn[n] = false;
        undrawn++;
        drawnChunks.Remove(n);
        ChunksUndrawn++;
        PerfCounts.Add("draw.chunks_undrawn");
        return true;
    }
}
