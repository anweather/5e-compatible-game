using Chem;

// Something a cell, held a block a chunk terrain (TerrainGrid's chunks, which the cells may sit across), each block made
// the first time one of its cells is set: ValleyGround's base heights and paint were an array a cell of the map each, 20
// bytes a cell (60 MB on the nine, 800 MB on the 12 x 10) before a chunk was made (#207, J-086). A cell of a chunk with no
// block reads as missing says (a generated map's unmade chunk stands on the map's land). Indexed by a world cell index
// (y * W + x), as the arrays were.
public sealed class CellsByChunk<T>
{
    readonly TerrainGrid layout;
    readonly T[][] blocks;
    readonly System.Func<int, T> missing;

    /// <param name="missing">What a cell of a chunk with no block reads as, by its world index; default when null.</param>
    public CellsByChunk(TerrainGrid layout, System.Func<int, T> missing = null)
    {
        this.layout = layout;
        this.missing = missing;
        blocks = new T[layout.ChunksX * layout.ChunksZ][];
    }

    public T this[int index]
    {
        get
        {
            int n = Block(index, out int at);
            var block = n >= 0 ? blocks[n] : null;
            if (block != null) return block[at];
            return missing != null ? missing(index) : default;
        }
        set
        {
            int n = Block(index, out int at);
            if (n < 0) return;
            (blocks[n] ??= Made(n))[at] = value;
        }
    }

    /// <summary>Whether a chunk has a block (one of its cells was set since it was made or dropped).</summary>
    public bool Has(int i, int j) => layout.HasChunk(i, j) && blocks[j * layout.ChunksX + i] != null;

    /// <summary>How many chunks have a block, for a check.</summary>
    public int Made()
    {
        int made = 0;
        foreach (var b in blocks)
            if (b != null) made++;
        return made;
    }

    /// <summary>A chunk's block goes: its cells read as missing says again.</summary>
    public void Drop(int i, int j)
    {
        if (layout.HasChunk(i, j)) blocks[j * layout.ChunksX + i] = null;
    }

    /// <summary>Every block goes.</summary>
    public void Clear() => System.Array.Clear(blocks, 0, blocks.Length);

    // A new block, its cells as missing says until they're set.
    T[] Made(int n)
    {
        var block = new T[TerrainGrid.ChunkCells * TerrainGrid.ChunkCells];
        if (missing == null) return block;
        int i = n % layout.ChunksX, j = n / layout.ChunksX;
        for (int at = 0; at < block.Length; at++)
        {
            int u = i * TerrainGrid.ChunkCells + (at & (TerrainGrid.ChunkCells - 1)), v = j * TerrainGrid.ChunkCells + (at >> Chunks.Shift);
            var (x, y) = layout.GridCell(u, v);
            if (layout.InGrid(x, y)) block[at] = missing(y * layout.GridW + x);
        }
        return block;
    }

    // The block a world cell index is in (-1 for none) and its place in it.
    int Block(int index, out int at)
    {
        int x = index % layout.GridW, y = index / layout.GridW;
        var (u, v) = layout.WorldCell(x, y);
        int i = u >> Chunks.Shift, j = v >> Chunks.Shift;
        at = ((v & (TerrainGrid.ChunkCells - 1)) << Chunks.Shift) | (u & (TerrainGrid.ChunkCells - 1));
        return layout.HasChunk(i, j) ? j * layout.ChunksX + i : -1;
    }
}
