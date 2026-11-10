using Chem;

// Which cells were trees when the world started (ValleyChemistry.NeedsTreeDrawn: a tree laid since, or grown, needs one
// drawn), a block a chunk read as the chunk is first wanted: a bool a cell of the map, read cell by cell at the start,
// was 3 million on the nine and 40 million on the 12 x 10 (#206, #207, J-086). A chunk read late is read as it stands,
// which is as it started while it was inactive (held as it is).
public sealed class StartTrees
{
    readonly World world;
    readonly int across;
    readonly bool[][] blocks;

    public StartTrees(World world)
    {
        this.world = world;
        across = (world.W + Chunks.Size - 1) >> Chunks.Shift;
        blocks = new bool[across * ((world.H + Chunks.Size - 1) >> Chunks.Shift)][];
    }

    /// <summary>How many chunks have been read, for a check.</summary>
    public int Read()
    {
        int read = 0;
        foreach (var b in blocks)
            if (b != null) read++;
        return read;
    }

    /// <summary>Reads a chunk's trees as they stand now, unless it has been read already.</summary>
    public void Read(int cx, int cy)
    {
        int n = cy * across + cx;
        if (cx < 0 || cy < 0 || cx >= across || n >= blocks.Length || blocks[n] != null) return;
        var block = new bool[Chunks.Size * Chunks.Size];
        int x0 = cx << Chunks.Shift, y0 = cy << Chunks.Shift;
        int x1 = System.Math.Min(world.W, x0 + Chunks.Size), y1 = System.Math.Min(world.H, y0 + Chunks.Size);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) block[((y - y0) << Chunks.Shift) | (x - x0)] = world.At(x, y).T == TerrainIds.Tree;
        blocks[n] = block;
    }

    /// <summary>Whether the cell was a tree as the world started (its chunk read now, if it wasn't yet).</summary>
    public bool At(int x, int y)
    {
        if (!world.InBounds(x, y)) return false;
        int cx = x >> Chunks.Shift, cy = y >> Chunks.Shift;
        Read(cx, cy);
        return blocks[cy * across + cx][((y & (Chunks.Size - 1)) << Chunks.Shift) | (x & (Chunks.Size - 1))];
    }
}
