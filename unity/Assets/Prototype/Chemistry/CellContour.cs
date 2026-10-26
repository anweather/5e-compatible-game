// A rounded outline around a patch of cells, as a triangle mesh: the view's
// water and slicks (CellWaterMesh, CellSlickMesh) use it so a puddle or an oil
// spill is a blob, not a staircase of squares. Pure maths, no Unity.
//
// Not a port: the sandbox has no terrain.
//
// Each cell gives a value at its centre (1 inside the patch, 0 outside, or in
// between to pull the edge toward a cell). The values are blended between
// centres on a finer lattice, nudged by smooth noise (ValueNoise), and the
// part at or above the iso value is cut out triangle by triangle ("marching
// triangles"), so the edge runs smoothly between cell centres. Points are in
// grid cell units: cell (x, y) spans x..x+1, y..y+1 (y growing south), its
// centre at (x + 0.5, y + 0.5). Triangles wind clockwise seen from above (the
// view maps grid y to world -z), as Unity draws front faces.
using System;
using System.Collections.Generic;

namespace Chem
{
    /// <summary>A contour mesh: points (grid cell units), the field value at each, and triangles.</summary>
    public sealed class ContourMesh
    {
        public List<(float x, float y)> Points { get; } = new List<(float x, float y)>();
        /// <summary>The blended value at each point: the iso value on the edge, more inside.</summary>
        public List<float> Values { get; } = new List<float>();
        public List<int> Triangles { get; } = new List<int>();

        public void Clear()
        {
            Points.Clear();
            Values.Clear();
            Triangles.Clear();
        }
    }

    /// <summary>Which cells to outline, how finely and how wobbly.</summary>
    public readonly struct ContourSpec
    {
        public ContourSpec(int lattice, float iso, float wobble, int seed)
        {
            if (lattice < 1) throw new ArgumentOutOfRangeException(nameof(lattice));
            Lattice = lattice;
            Iso = iso;
            Wobble = wobble;
            Seed = seed;
        }

        /// <summary>Lattice points per cell side.</summary>
        public int Lattice { get; }
        /// <summary>The value the edge runs along.</summary>
        public float Iso { get; }
        /// <summary>How far noise pushes the blended value up or down (half of it each way).</summary>
        public float Wobble { get; }
        public int Seed { get; }
    }

    public sealed class CellContour
    {
        readonly ContourSpec spec;
        readonly Dictionary<long, int> edgePoints = new Dictionary<long, int>();
        float[] cellValues, cellWobbles, field;
        int[] latticePoint;
        int cx0, cy0, cw, ch, lw, lh;
        float lx0, ly0;

        public CellContour(ContourSpec spec) => this.spec = spec;

        /// <summary>
        /// Draws only from the centre of the box's first cell to the centre of the cell past its
        /// last (in x and in y), so boxes built side by side (the next from the cell past this one's
        /// last) meet exactly, with neither a gap nor an overlap: a big surface drawn in tiles.
        /// </summary>
        public bool Tiled { get; set; }

        /// <summary>
        /// Outlines the cells x0..x1, y0..y1 (inclusive) into <paramref name="mesh"/> (cleared first).
        /// <paramref name="value"/>(x, y) is a cell's value; it is also read one cell outside the
        /// box, so the edge can close there (give 0 to keep the patch inside the box).
        /// <paramref name="wobble"/>(x, y), if given, scales the wobble around a cell (0 holds the
        /// edge where the values put it, 1 is the spec's full wobble), blended like the values.
        /// When Tiled, it draws only from the centre of cell x0 to the centre of cell x1 + 1.
        /// </summary>
        public void Build(int x0, int y0, int x1, int y1, Func<int, int, float> value, ContourMesh mesh,
            Func<int, int, float> wobble = null)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            mesh.Clear();
            if (x1 < x0 || y1 < y0) return;
            ReadCells(x0, y0, x1, y1, value, wobble);
            Sample();
            edgePoints.Clear();
            Array.Clear(latticePoint, 0, latticePoint.Length);
            // The lattice runs from the centre of cell x0 - 1 to that of cell x1 + 1; a tile starts one cell in.
            int edge = Tiled ? spec.Lattice : 0;
            for (int j = edge; j < lh - 1; j++)
            {
                for (int i = edge; i < lw - 1; i++)
                {
                    int a = j * lw + i, b = a + 1, c = a + lw, d = c + 1;
                    Cut(mesh, a, b, d);
                    Cut(mesh, a, d, c);
                }
            }
        }

        // The cells' values, one ring wider than the box (the centres the lattice spans).
        void ReadCells(int x0, int y0, int x1, int y1, Func<int, int, float> value, Func<int, int, float> wobble)
        {
            cx0 = x0 - 1;
            cy0 = y0 - 1;
            cw = x1 - x0 + 3;
            ch = y1 - y0 + 3;
            cellValues = new float[cw * ch];
            cellWobbles = new float[cw * ch];
            for (int b = 0; b < ch; b++)
            {
                for (int a = 0; a < cw; a++)
                {
                    cellValues[b * cw + a] = value(cx0 + a, cy0 + b);
                    cellWobbles[b * cw + a] = wobble == null ? 1f : wobble(cx0 + a, cy0 + b);
                }
            }
        }

        // The lattice runs from the centre of the ring's first cell to the centre of its last.
        void Sample()
        {
            int n = spec.Lattice;
            lw = (cw - 1) * n + 1;
            lh = (ch - 1) * n + 1;
            lx0 = cx0 + 0.5f;
            ly0 = cy0 + 0.5f;
            field = new float[lw * lh];
            latticePoint = new int[lw * lh];
            for (int j = 0; j < lh; j++)
            {
                for (int i = 0; i < lw; i++)
                {
                    float x = lx0 + i / (float)n, y = ly0 + j / (float)n;
                    // A broad bend (a few cells across) so a long straight run of cells
                    // doesn't give a straight shore, and a finer wobble on top; blending
                    // octaves narrows the spread, so it's stretched back out to ±0.5.
                    float noise = 0.55f * ValueNoise.Twice(x * 0.35f, y * 0.35f, spec.Seed + 7) +
                                  0.45f * ValueNoise.Twice(x * 1.1f, y * 1.1f, spec.Seed) - 0.5f;
                    noise = Math.Max(-0.5f, Math.Min(0.5f, noise * 2.5f));
                    field[j * lw + i] = Blend(cellValues, i, j) + spec.Wobble * Blend(cellWobbles, i, j) * noise;
                }
            }
        }

        // Per-cell numbers (values or wobbles) blended between the cells' centres at lattice point (i, j).
        float Blend(float[] cells, int i, int j)
        {
            int n = spec.Lattice;
            int a = Math.Min(i / n, cw - 2), b = Math.Min(j / n, ch - 2);
            float fx = (i - a * n) / (float)n, fy = (j - b * n) / (float)n;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float v00 = cells[b * cw + a], v10 = cells[b * cw + a + 1];
            float v01 = cells[(b + 1) * cw + a], v11 = cells[(b + 1) * cw + a + 1];
            float top = v00 + (v10 - v00) * fx, bottom = v01 + (v11 - v01) * fx;
            return top + (bottom - top) * fy;
        }

        // The part of triangle (p, q, r) at or above the iso value.
        void Cut(ContourMesh mesh, int p, int q, int r)
        {
            float iso = spec.Iso;
            bool ip = field[p] >= iso, iq = field[q] >= iso, ir = field[r] >= iso;
            int inside = (ip ? 1 : 0) + (iq ? 1 : 0) + (ir ? 1 : 0);
            if (inside == 0) return;
            if (inside == 3)
            {
                Emit(mesh, Lattice(mesh, p), Lattice(mesh, q), Lattice(mesh, r));
                return;
            }
            // Rotate so the odd one out (the lone inside or lone outside point) comes first.
            bool lone = inside == 1;
            if (iq == lone) (p, q, r) = (q, r, p);
            else if (ir == lone) (p, q, r) = (r, p, q);
            int pq = Edge(mesh, p, q), pr = Edge(mesh, p, r);
            if (lone)
            {
                Emit(mesh, Lattice(mesh, p), pq, pr);
                return;
            }
            Emit(mesh, pq, Lattice(mesh, q), Lattice(mesh, r));
            Emit(mesh, pq, Lattice(mesh, r), pr);
        }

        // A lattice point as a mesh point, made once.
        int Lattice(ContourMesh mesh, int k)
        {
            if (latticePoint[k] > 0) return latticePoint[k] - 1;
            int n = spec.Lattice;
            mesh.Points.Add((lx0 + (k % lw) / (float)n, ly0 + (k / lw) / (float)n));
            mesh.Values.Add(field[k]);
            latticePoint[k] = mesh.Points.Count;
            return mesh.Points.Count - 1;
        }

        // Where the field crosses the iso value between lattice points a and b, made once.
        int Edge(ContourMesh mesh, int a, int b)
        {
            long key = a < b ? (long)a * int.MaxValue + b : (long)b * int.MaxValue + a;
            if (edgePoints.TryGetValue(key, out var made)) return made;
            float t = (spec.Iso - field[a]) / (field[b] - field[a]);
            int n = spec.Lattice;
            float ax = lx0 + (a % lw) / (float)n, ay = ly0 + (a / lw) / (float)n;
            float bx = lx0 + (b % lw) / (float)n, by = ly0 + (b / lw) / (float)n;
            mesh.Points.Add((ax + (bx - ax) * t, ay + (by - ay) * t));
            mesh.Values.Add(spec.Iso);
            edgePoints[key] = mesh.Points.Count - 1;
            return mesh.Points.Count - 1;
        }

        // A triangle, wound clockwise seen from above (positive cross product with y growing south).
        static void Emit(ContourMesh mesh, int a, int b, int c)
        {
            var (ax, ay) = mesh.Points[a];
            var (bx, by) = mesh.Points[b];
            var (cx, cy) = mesh.Points[c];
            float cross = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (Math.Abs(cross) < 1e-9f) return;
            if (cross < 0f) (b, c) = (c, b);
            mesh.Triangles.Add(a);
            mesh.Triangles.Add(b);
            mesh.Triangles.Add(c);
        }
    }
}
