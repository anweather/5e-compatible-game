using System.Linq;
using Chem;
using NUnit.Framework;

// A generated map's pinned square drawn from its own map's ground (PinnedGround): the pinned chunks keep their metres
// exactly, the generated land round them is eased to meet them over PinnedGround.SeamCells, and nothing further off
// changes. On the nine the valley's things rolled off their cells on ground made from the land alone (things-nine).
[Category("Unit")]
public class PinnedGroundTests
{
    const int Res = 65, Texels = 64, Detail = 8;
    // 3 x 3 chunks, 192 x 192 cells; the pinned map, one chunk, at map cell (64, 64): chunk (1, 1).
    static readonly TerrainGrid Layout = new TerrainGrid(1.5f, (0f, 0f), (3, 3), (0, 191), (192, 192), (0, 0, -1, -1));

    static GroundChunk Chunk(int i, int j, ushort height, string[] layers) => new GroundChunk
    {
        I = i, J = j, Resolution = Res, Texels = Texels, DetailResolution = Detail,
        Heights = Enumerable.Repeat(height, Res * Res).ToArray(),
        Paint = new byte[layers.Length * Texels * Texels],
        Details = new byte[Detail * Detail],
        Trees = new GroundTree[0],
    };

    // The generated map: flat at 0 m over [-10, 90].
    static GroundPack Generated()
    {
        var layers = new[] { "Grass", "Rock" };
        var pack = new GroundPack { Bottom = -10, Top = 90, Layers = layers, Grasses = new[] { "Grass_A" }, TreeKinds = new[] { "Conifer" } };
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++) pack.Chunks.Add(Chunk(i, j, (ushort)(GroundPack.HeightSteps / 10), layers));
        return pack;
    }

    // The pinned map: one chunk over [-20, 280], a slope east from 12 m, painted all rock (its first layer).
    static GroundPack Pinned()
    {
        var layers = new[] { "Rock", "Grass" };
        var pack = new GroundPack { Bottom = -20, Top = 280, Layers = layers, Grasses = new[] { "Grass_A" }, TreeKinds = new[] { "Conifer" } };
        var c = Chunk(0, 0, 0, layers);
        for (int z = 0; z < Res; z++)
            for (int x = 0; x < Res; x++) c.Heights[z * Res + x] = Steps(pack, 12 + x * 0.1);
        for (int n = 0; n < Texels * Texels; n++) c.Paint[n] = 255;
        pack.Chunks.Add(c);
        return pack;
    }

    static ushort Steps(GroundPack p, double metres) => (ushort)System.Math.Round((metres - p.Bottom) / (p.Top - p.Bottom) * GroundPack.HeightSteps);

    static GroundChunk At(GroundPack p, int i, int j) => p.Chunks.Single(c => c.I == i && c.J == j);

    [Test]
    public void ThePinnedChunkKeepsItsOwnGround()
    {
        var ground = Generated();
        var pinned = Pinned();
        Assert.That(PinnedGround.Lay(ground, Layout, pinned, 64, 64, 64, 64), Is.EqualTo(1));
        var laid = At(ground, 1, 1);
        var own = pinned.Chunks[0];
        for (int z = 0; z < Res; z += 8)
            for (int x = 0; x < Res; x += 8)
                Assert.That(ground.Metres(laid, x, z), Is.EqualTo(pinned.Metres(own, x, z)).Within(0.01), $"sample ({x}, {z})");
        Assert.That(laid.Paint.Take(Texels * Texels).All(b => b == 0) && laid.Paint.Skip(Texels * Texels).All(b => b == 255), "its rock paint is this map's rock layer");
    }

    [Test]
    public void TheGeneratedLandMeetsItsEdgeAndIsLeftAloneFurtherOff()
    {
        var ground = Generated();
        PinnedGround.Lay(ground, Layout, Pinned(), 64, 64, 64, 64);
        var laid = At(ground, 1, 1);
        var west = At(ground, 0, 1);
        var east = At(ground, 2, 1);
        for (int z = 0; z < Res; z++)
        {
            Assert.That(west.Heights[z * Res + Res - 1], Is.EqualTo(laid.Heights[z * Res]), $"west edge, row {z}");
            Assert.That(east.Heights[z * Res], Is.EqualTo(laid.Heights[z * Res + Res - 1]), $"east edge, row {z}");
        }
        // Eased in: part way at half the seam, as generated past it.
        double edge = ground.Metres(laid, 0, 32), half = ground.Metres(west, Res - 1 - PinnedGround.SeamCells / 2, 32);
        Assert.That(half, Is.GreaterThan(0.5).And.LessThan(edge - 0.5), "half way into the seam");
        Assert.That(ground.Metres(west, Res - 1 - PinnedGround.SeamCells, 32), Is.EqualTo(0).Within(0.01), "past the seam");
        Assert.That(ground.Metres(At(ground, 0, 0), 0, 0), Is.EqualTo(0).Within(0.01), "a far corner");
    }

    // The island (scale-12x10, 2026-11-11): its generated chunks were moved into the pinned valley's wider height range and
    // their trees weren't, so every tree off the valley stood 9 to 17 m under its ground. Every tree, generated, eased or
    // pinned, stands where its ground is drawn at its spot.
    [Test]
    public void EveryTreeStandsOnItsGroundOnceLaid()
    {
        var ground = Generated();
        var pinned = Pinned();
        foreach (var c in ground.Chunks.Concat(pinned.Chunks)) c.Trees = Planted(c);
        PinnedGround.Lay(ground, Layout, pinned, 64, 64, 64, 64);
        int trees = 0;
        foreach (var c in ground.Chunks)
            foreach (var t in c.Trees)
            {
                trees++;
                double foot = ground.Bottom + (ground.Top - ground.Bottom) * t.Y;
                Assert.That(foot, Is.EqualTo(Surface(ground, c, t)).Within(0.05), $"chunk ({c.I}, {c.J}) tree at ({t.X:F2}, {t.Z:F2})");
            }
        Assert.That(trees, Is.EqualTo(10 * 9));
    }

    // Ten trees a chunk at a sample each, standing on the chunk's height there (as GroundFromLand stands them).
    static GroundTree[] Planted(GroundChunk c) => Enumerable.Range(0, 10).Select(n =>
    {
        int x = 2 + n * 6, z = 60 - n * 6;
        return new GroundTree { X = x / (float)(Res - 1), Z = z / (float)(Res - 1), Y = c.Heights[z * Res + x] / (float)GroundPack.HeightSteps, Width = 1, Height = 1 };
    }).ToArray();

    static double Surface(GroundPack p, GroundChunk c, GroundTree t) => p.Metres(c, (int)System.Math.Round(t.X * (Res - 1)), (int)System.Math.Round(t.Z * (Res - 1)));

    [Test]
    public void WithoutAPinnedMapNothingChanges()
    {
        var ground = Generated();
        var before = ground.Chunks.Select(c => (ushort[])c.Heights.Clone()).ToList();
        Assert.That(PinnedGround.Lay(ground, Layout, "/no/such/map"), Is.EqualTo(0));
        for (int n = 0; n < before.Count; n++) Assert.That(ground.Chunks[n].Heights, Is.EqualTo(before[n]));
    }

    // #183: in a build the nine's layout had lost the valley's full detail, the valley's 257-sample chunks were laid over
    // 65-sample ones all the same, and the world's ground threw far from the cause (ValleyGround.Recover).
    [Test]
    public void APinnedChunkOfAnotherSizeThanTheLayoutsIsRefused()
    {
        var pinned = Pinned();
        pinned.Chunks[0].Resolution = 257;
        Assert.That(() => PinnedGround.Lay(Generated(), Layout, pinned, 64, 64, 64, 64),
            Throws.InstanceOf<System.IO.InvalidDataException>().With.Message.Contains("257 samples").And.Message.Contains("full detail"));
    }
}
