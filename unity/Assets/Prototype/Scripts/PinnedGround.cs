using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;

// A generated map's pinned squares drawn from their own map's ground/ (docs/design/world-squares.md): the nine pins the
// valley, whose ground pack is its shaped ground (the castle's level paddock, the pond's sharp edges), and the nine's own
// ground (GroundFromLand) is made from the land alone, so the valley's things rolled and slid off their cells there. The
// pinned chunks are the pack's own, moved to their square; only the generated chunks round them change, eased over
// SeamCells to meet the pinned edge, which stays the pinned map's. Only drawn: nothing here decides anything shared (D4).
public static class PinnedGround
{
    /// <summary>How far into the generated land round a pinned square its ground is eased to meet the square's (cells).</summary>
    public const int SeamCells = 24;

    /// <summary>
    /// Lays each pinned map's ground/ pack (where it has one) into <paramref name="ground"/>, a generated map's ground of
    /// <paramref name="layout"/>, the map in <paramref name="mapDir"/>. Returns how many chunks it laid.
    /// </summary>
    public static int Lay(GroundPack ground, TerrainGrid layout, string mapDir)
    {
        int laid = 0;
        foreach (var (dir, x, y) in GeneratedMap.PinnedSquares(mapDir))
        {
            if (!GroundPack.In(dir)) continue;
            var pinnedMap = MapFile.Load(dir);
            laid += Lay(ground, layout, GroundPack.Load(dir), pinnedMap.W, pinnedMap.H, x, y);
        }
        return laid;
    }

    /// <summary>
    /// Lays <paramref name="pinned"/>, the ground of a <paramref name="pinnedW"/> × <paramref name="pinnedH"/> map whose
    /// cell (0, 0) is <paramref name="ground"/>'s map cell (<paramref name="atX"/>, <paramref name="atY"/>), into it.
    /// </summary>
    public static int Lay(GroundPack ground, TerrainGrid layout, GroundPack pinned, int pinnedW, int pinnedH, int atX, int atY)
    {
        var index = ground.Chunks.Select((c, n) => (c, n)).ToDictionary(p => (p.c.I, p.c.J), p => p.n);
        GroundChunk Own((int i, int j) at) => ground.Chunks[index[at]];
        var (laid, make, _) = Over(ground, layout, pinned, pinnedW, pinnedH, atX, atY, Own, at => index.ContainsKey(at), at => (Own(at).Resolution, Own(at).Texels));
        foreach (var (at, n) in index) ground.Chunks[n] = make(at);
        return laid;
    }

    /// <summary>
    /// Lays every pinned map's ground/ pack into a generated map's ground made a chunk at a time (#205): what
    /// <paramref name="make"/> makes, as Lay would leave it. <paramref name="ground"/> holds what the chunks share and takes
    /// the pinned maps' range and river now. Returns how many chunks are pinned, the new way to make one, and the pinned
    /// chunks themselves by where they are in this map (what a far view reads their trees from without making a chunk).
    /// </summary>
    public static (int laid, Func<(int i, int j), GroundChunk> make, IReadOnlyDictionary<(int i, int j), GroundChunk> pinned) LayLazily(GroundPack ground, TerrainGrid layout, string mapDir, Func<(int i, int j), GroundChunk> make)
    {
        int laid = 0;
        var pinned = new Dictionary<(int i, int j), GroundChunk>();
        foreach (var (dir, x, y) in GeneratedMap.PinnedSquares(mapDir))
        {
            if (!GroundPack.In(dir)) continue;
            var pinnedMap = MapFile.Load(dir);
            var (n, over, square) = Over(ground, layout, GroundPack.Load(dir), pinnedMap.W, pinnedMap.H, x, y, make, at => layout.HasChunk(at.i, at.j),
                at => (layout.Resolution(at.i, at.j), layout.TexelResolution(at.i, at.j)));
            laid += n;
            make = over;
            foreach (var (at, c) in square) pinned[at] = c;
        }
        return (laid, make, pinned);
    }

    // One pinned pack over the ground <paramref name="under"/> makes (its chunks in ground's range as it is now): the pinned
    // chunks moved to their square, every other chunk in the two's range, and those beside the square eased to meet it. The
    // ground takes the range and the river now; the function returned may be called side by side.
    static (int laid, Func<(int i, int j), GroundChunk> make, Dictionary<(int i, int j), GroundChunk> square) Over(GroundPack ground, TerrainGrid layout, GroundPack pinned, int pinnedW, int pinnedH, int atX, int atY,
        Func<(int i, int j), GroundChunk> under, Func<(int i, int j), bool> has, Func<(int i, int j), (int res, int texels)> sizeOf)
    {
        var (di, dj) = MapFile.ChunkShift(layout.GridH, pinnedH, atX, atY);
        // One height range for both, so each keeps its metres.
        double was0 = ground.Bottom, was1 = ground.Top;
        double bottom = Math.Min(ground.Bottom, pinned.Bottom), top = Math.Max(ground.Top, pinned.Top);
        var paint = Remap(pinned.Layers, ground.Layers, "paint layer");
        var grass = Remap(pinned.Grasses, ground.Grasses, "grass");
        var trees = Remap(pinned.TreeKinds, ground.TreeKinds, "tree kind");
        var square = new Dictionary<(int i, int j), GroundChunk>();
        foreach (var from in pinned.Chunks)
        {
            var at = (from.I + di, from.J + dj);
            if (!has(at)) continue;
            // The layout's chunk must be the pinned one's size: the terrain is made at the chunk's and the cells drawn on
            // it at the layout's (#183: a build's layout had lost the pinned map's full detail, and no world was made).
            var (res, texels) = sizeOf(at);
            if (res != from.Resolution || texels != from.Texels)
                throw new InvalidDataException($"The pinned ground's chunk ({from.I}, {from.J}) is {from.Resolution} samples and {from.Texels} texels a side, " +
                    $"and this map draws its chunk ({at.Item1}, {at.Item2}) at {res} and {texels}: the map's full detail isn't its pinned map's");
            var c = Moved(from, at, ground.Layers.Length, ground.Grasses.Length, paint, grass, trees);
            Rescale(c, pinned.Bottom, pinned.Top, bottom, top);
            square[at] = c;
        }
        ground.Bottom = bottom;
        ground.Top = top;
        // The river's cells, in this map's.
        var cells = new List<int>(ground.ChannelCells);
        var depths = new List<float>(ground.ChannelDepths);
        for (int k = 0; k < pinned.ChannelCells.Length && k < pinned.ChannelDepths.Length; k++)
        {
            int c = pinned.ChannelCells[k], x = c % pinnedW + atX, y = c / pinnedW + atY;
            cells.Add(y * layout.GridW + x);
            depths.Add(pinned.ChannelDepths[k]);
        }
        ground.ChannelCells = cells.ToArray();
        ground.ChannelDepths = depths.ToArray();
        var box = square.Count > 0 ? new Square(square.Keys) : null;
        GroundChunk Make((int i, int j) at)
        {
            if (square.TryGetValue(at, out var pinnedChunk)) return pinnedChunk;
            var c = under(at);
            Rescale(c, was0, was1, bottom, top);
            if (box != null && box.Beside(at.i, at.j)) Ease(bottom, top, c, box, square);
            return c;
        }
        return (square.Count, Make, square);
    }

    // A pinned chunk as this map's chunk (i, j): its heights as they are, its paint, grass and trees by this map's lists.
    static GroundChunk Moved(GroundChunk from, (int i, int j) at, int layers, int grasses, int[] paint, int[] grass, int[] trees)
    {
        int area = from.Texels * from.Texels, detail = from.DetailResolution * from.DetailResolution;
        var c = new GroundChunk
        {
            I = at.i, J = at.j, Resolution = from.Resolution, Texels = from.Texels, DetailResolution = from.DetailResolution,
            Heights = (ushort[])from.Heights.Clone(),
            Paint = new byte[layers * area],
            Details = new byte[grasses * detail],
            Trees = from.Trees.Select(t => { t.Prototype = trees[t.Prototype]; return t; }).ToArray(),
        };
        for (int l = 0; l < paint.Length; l++) Array.Copy(from.Paint, l * area, c.Paint, paint[l] * area, area);
        for (int g = 0; g < grass.Length; g++) Array.Copy(from.Details, g * detail, c.Details, grass[g] * detail, detail);
        return c;
    }

    // Where each of a pinned pack's names is in this map's list; throws if one isn't there (the template lacks it).
    static int[] Remap(string[] from, string[] to, string what)
    {
        var at = from.Select(name => Array.IndexOf(to, name)).ToArray();
        int missing = Array.IndexOf(at, -1);
        if (missing >= 0) throw new InvalidDataException($"The pinned ground's {what} {from[missing]} isn't in this map's ground");
        return at;
    }

    static double Metres(ushort h, double bottom, double top) => bottom + (top - bottom) * h / GroundPack.HeightSteps;

    static ushort Step(double metres, double bottom, double top) =>
        (ushort)Math.Max(0, Math.Min(GroundPack.HeightSteps, Math.Round((metres - bottom) / (top - bottom) * GroundPack.HeightSteps)));

    // A chunk's heights from one range to another, the same metres.
    static void Rescale(GroundChunk c, double fromBottom, double fromTop, double bottom, double top)
    {
        if (Math.Abs(fromBottom - bottom) < 1e-9 && Math.Abs(fromTop - top) < 1e-9) return;
        for (int k = 0; k < c.Heights.Length; k++) c.Heights[k] = Step(Metres(c.Heights[k], fromBottom, fromTop), bottom, top);
    }

    // The generated chunks round the square are eased to meet it (Ease): a sample SeamCells or nearer moves by the difference
    // between the square's ground and its own chunk's at the nearest point of the square's edge, all of it at the edge and
    // none at SeamCells (smoothly), so the shared edge is the square's and nothing inside the square changes.

    // The pinned square's chunks as a box, in chunks and in cells (u east, v north).
    sealed class Square
    {
        public readonly int I0, I1, J0, J1;
        public readonly double U0, U1, V0, V1;

        public Square(ICollection<(int i, int j)> chunks)
        {
            int cells = TerrainGrid.ChunkCells;
            I0 = chunks.Min(s => s.i);
            I1 = chunks.Max(s => s.i);
            J0 = chunks.Min(s => s.j);
            J1 = chunks.Max(s => s.j);
            (U0, U1, V0, V1) = (I0 * cells, (I1 + 1) * cells, J0 * cells, (J1 + 1) * cells);
        }

        public bool Beside(int i, int j) => i >= I0 - 1 && i <= I1 + 1 && j >= J0 - 1 && j <= J1 + 1;

        // The square's chunk holding a point of its edge (a point on its east or north side is in the last one).
        public (int i, int j) ChunkAt(double u, double v) =>
            (Math.Min(I1, (int)Math.Floor(u / TerrainGrid.ChunkCells)), Math.Min(J1, (int)Math.Floor(v / TerrainGrid.ChunkCells)));
    }

    // One generated chunk beside the square, eased to meet it.
    static void Ease(double bottom, double top, GroundChunk c, Square box, Dictionary<(int, int), GroundChunk> byAt)
    {
        int cells = TerrainGrid.ChunkCells, res = c.Resolution;
        var own = (ushort[])c.Heights.Clone();
        double per = (res - 1) / (double)cells;
        for (int sz = 0; sz < res; sz++)
            for (int sx = 0; sx < res; sx++)
            {
                double u = c.I * cells + sx / per, v = c.J * cells + sz / per;
                double bu = Math.Max(box.U0, Math.Min(box.U1, u)), bv = Math.Max(box.V0, Math.Min(box.V1, v));
                double d = Math.Sqrt((u - bu) * (u - bu) + (v - bv) * (v - bv));
                var (pi, pj) = box.ChunkAt(bu, bv);
                if (d >= SeamCells || !byAt.TryGetValue((pi, pj), out var p)) continue;
                // The square's ground at the edge point, and this chunk's own there (the point is on its rim).
                double mine = Sample(own, res, (bu - c.I * cells) * per, (bv - c.J * cells) * per, bottom, top);
                double across = (p.Resolution - 1) / (double)cells;
                double theirs = Sample(p.Heights, p.Resolution, (bu - pi * cells) * across, (bv - pj * cells) * across, bottom, top);
                double t = d / SeamCells, ease = 1 - t * t * (3 - 2 * t);
                c.Heights[sz * res + sx] = Step(Metres(own[sz * res + sx], bottom, top) + (theirs - mine) * ease, bottom, top);
            }
    }

    // A chunk's ground in metres at sample position (x, z), between its samples.
    static double Sample(ushort[] heights, int res, double x, double z, double bottom, double top)
    {
        x = Math.Max(0, Math.Min(res - 1, x));
        z = Math.Max(0, Math.Min(res - 1, z));
        int x0 = Math.Min(res - 2, (int)Math.Floor(x)), z0 = Math.Min(res - 2, (int)Math.Floor(z));
        double fx = x - x0, fz = z - z0;
        double H(int a, int b) => Metres(heights[b * res + a], bottom, top);
        return (H(x0, z0) * (1 - fx) + H(x0 + 1, z0) * fx) * (1 - fz) + (H(x0, z0 + 1) * (1 - fx) + H(x0 + 1, z0 + 1) * fx) * fz;
    }
}
