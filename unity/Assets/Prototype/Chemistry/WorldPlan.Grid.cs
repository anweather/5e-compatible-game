// The world plan's grid of world squares, and the one place its edges are decided (docs/design/world-squares.md
// section 8): every neighbour, diagonal and distance the solver reads goes through Index, so a world that wraps east and
// west, or both ways (a torus), is a flag here and not a rewrite anywhere else.
using System;

namespace Chem
{
    /// <summary>How the world's edges join.</summary>
    public enum PlanWrap
    {
        /// <summary>A rectangle, the sea beyond its west and south edges.</summary>
        None,
        /// <summary>East joins west (a cylinder); the north and south rows are walls nothing crosses.</summary>
        EastWest,
        /// <summary>East joins west and north joins south.</summary>
        Torus,
    }

    public sealed class PlanGrid
    {
        /// <summary>n e s w: a step's x and y. y grows south.</summary>
        public static readonly int[] Dx = { 0, 1, 0, -1 }, Dy = { -1, 0, 1, 0 };
        /// <summary>NW NE SE SW: a diagonal step's x and y.</summary>
        public static readonly int[] Cdx = { -1, 1, 1, -1 }, Cdy = { -1, -1, 1, 1 };
        /// <summary>The two sides that meet at each corner (NW NE SE SW).</summary>
        public static readonly int[][] CornerSides = { new[] { 0, 3 }, new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 } };
        /// <summary>The corners a side runs from and to (n and s run west to east, e and w north to south).</summary>
        public static readonly int[][] SideCorners = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 3, 2 }, new[] { 0, 3 } };

        public readonly int W, H, Count;
        public readonly bool WrapX, WrapY;
        readonly int[] nb, diag;

        public PlanGrid(int w, int h, PlanWrap wrap)
        {
            if (w < 1 || h < 1) throw new ArgumentException("a world needs a world square");
            W = w;
            H = h;
            Count = w * h;
            WrapX = wrap != PlanWrap.None;
            WrapY = wrap == PlanWrap.Torus;
            nb = new int[Count * 4];
            diag = new int[Count * 4];
            for (int i = 0; i < Count; i++)
                for (int k = 0; k < 4; k++)
                {
                    nb[i * 4 + k] = Index(X(i) + Dx[k], Y(i) + Dy[k]);
                    diag[i * 4 + k] = Index(X(i) + Cdx[k], Y(i) + Cdy[k]);
                }
        }

        public int X(int i) => i % W;

        public int Y(int i) => i / W;

        /// <summary>The square at (x, y), across a wrap; -1 off the edge of a world that doesn't.</summary>
        public int Index(int x, int y)
        {
            if (WrapX) x = Mod(x, W);
            else if (x < 0 || x >= W) return -1;
            if (WrapY) y = Mod(y, H);
            else if (y < 0 || y >= H) return -1;
            return y * W + x;
        }

        public int Neighbour(int i, int side) => nb[i * 4 + side];

        public int Diagonal(int i, int corner) => diag[i * 4 + corner];

        /// <summary>Chebyshev distance in squares, the short way across a wrap.</summary>
        public int Distance(int a, int b) => Math.Max(Along(X(a), X(b), W, WrapX), Along(Y(a), Y(b), H, WrapY));

        /// <summary>Chebyshev distance in chunks between two chunk coordinates, across a wrap.</summary>
        public int ChunkDistance(int ax, int ay, int bx, int by)
        {
            int k = WorldPlan.ChunksPerSquare;
            return Math.Max(Along(ax, bx, W * k, WrapX), Along(ay, by, H * k, WrapY));
        }

        /// <summary>The side of <paramref name="i"/> that faces its neighbour <paramref name="j"/>, or -1.</summary>
        public int SideTo(int i, int j)
        {
            for (int s = 0; s < 4; s++) if (Neighbour(i, s) == j) return s;
            return -1;
        }

        static int Along(int a, int b, int n, bool wrap)
        {
            int d = Math.Abs(a - b);
            return wrap ? Math.Min(d, n - d) : d;
        }

        public static int Mod(int a, int n) => ((a % n) + n) % n;
    }
}
