using Chem;

// The cells' views (ChemGrid.View) held a block a chunk, made as the chunk first draws: a reference a cell of the whole
// map was 24 MB on the nine and 320 MB on the 12 x 10 before any of it drew (#207, J-086). Indexed by a world cell
// index as the array was, so the drawing code reads it the same way.
public partial class ChemGrid
{
    sealed class ViewBlocks
    {
        readonly int w, across;
        readonly View[][] blocks;

        public ViewBlocks(int width, int height)
        {
            w = width;
            across = (width + Chunks.Size - 1) >> Chunks.Shift;
            blocks = new View[across * ((height + Chunks.Size - 1) >> Chunks.Shift)][];
        }

        /// <summary>A cell's view by its world index (y * W + x); null where none is made. Setting one makes its chunk's block.</summary>
        public View this[int index]
        {
            get
            {
                var block = blocks[Block(index, out int at)];
                return block?[at];
            }
            set
            {
                int n = Block(index, out int at);
                if (blocks[n] == null)
                {
                    if (value == null) return;
                    blocks[n] = new View[Chunks.Size * Chunks.Size];
                }
                blocks[n][at] = value;
            }
        }

        /// <summary>How many chunks have a block, for a check.</summary>
        public int Made
        {
            get
            {
                int made = 0;
                foreach (var b in blocks)
                    if (b != null) made++;
                return made;
            }
        }

        int Block(int index, out int at)
        {
            int x = index % w, y = index / w;
            at = ((y & (Chunks.Size - 1)) << Chunks.Shift) | (x & (Chunks.Size - 1));
            return (y >> Chunks.Shift) * across + (x >> Chunks.Shift);
        }
    }
}
