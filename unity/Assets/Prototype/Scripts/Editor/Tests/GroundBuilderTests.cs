using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// The run-time builder (docs/design/maps-as-data.md, phase 5): arrays become TerrainData, from a map's ground/ or from its
// land alone, and the ground drawn from them still eases, keeps its seams and comes back whole on a new world.
[OnTheValley]
public class GroundBuilderTests
{
    const float Unit = 0.125f, Step = 0.5f;

    static TerrainData Template() => AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Prototype/Generated/Ground/Template.asset");

    static readonly TerrainGrid Small = new TerrainGrid(1.5f, (0f, 0f), (2, 2), (0, 127), (128, 128), (0, 0, 0, 0));

    readonly List<Object> made = new();

    [TearDown]
    public void Clear()
    {
        foreach (var o in made)
            if (o != null) Object.DestroyImmediate(o);
        made.Clear();
    }

    static (World world, int[] land) Hilly()
    {
        var world = new World(Rules.Load(ChemGrid.Folders.Rules), 128, 128, 5, "grass");
        for (int y = 40; y < 50; y++)
            for (int x = 40; x < 60; x++) world.Paint(x, y, TerrainIds.Tree);
        var land = new int[128 * 128];
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++) land[y * 128 + x] = 40 + x / 2 + y % 7 * 3;
        world.SetLand((int[])land.Clone());
        return (world, land);
    }

    static GroundFromLand.Source Source(World world, int[] land)
    {
        var t = Template();
        return new GroundFromLand.Source
        {
            World = world,
            Land = LandChunks.Of(land, world.W, world.H),
            Layout = Small,
            StepHeight = Step,
            Unit = Unit,
            Lowest = land.Min(),
            Layers = t.terrainLayers.Select(l => l.name).ToArray(),
            Grasses = t.detailPrototypes.Select(d => "grass").ToArray(),
            TreeKinds = t.treePrototypes.Select(p => p.prefab.name).ToArray(),
            DetailResolution = t.detailResolution,
        };
    }

    [Test]
    public void TheValleysChunksAreItsGround()
    {
        var pack = GroundPack.Load(ChemGrid.Folders.World);
        var builder = new GroundBuilder(pack, Template(), TerrainGrid.Valley().ChunkSize);
        foreach (var c in pack.Chunks)
        {
            var data = builder.Build((c.I, c.J));
            made.Add(data);
            int res = c.Resolution, texels = c.Texels, detail = c.DetailResolution;
            var heights = data.GetHeights(0, 0, res, res);
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    Assert.That(Mathf.RoundToInt(heights[z, x] * GroundPack.HeightSteps), Is.EqualTo(c.Heights[z * res + x]), $"height {c.I},{c.J} [{x}, {z}]");
            var alpha = data.GetAlphamaps(0, 0, texels, texels);
            for (int l = 0; l < pack.Layers.Length; l++)
                for (int z = 0; z < texels; z++)
                    for (int x = 0; x < texels; x++)
                        Assert.That(Mathf.RoundToInt(alpha[z, x, l] * 255f), Is.EqualTo(c.Paint[(l * texels + z) * texels + x]), $"paint {c.I},{c.J} layer {l}");
            for (int g = 0; g < pack.Grasses.Length; g++)
            {
                var clumps = data.GetDetailLayer(0, 0, detail, detail, g);
                for (int z = 0; z < detail; z++)
                    for (int x = 0; x < detail; x++)
                        Assert.That(clumps[z, x], Is.EqualTo(c.Details[(g * detail + z) * detail + x]), $"grass {c.I},{c.J}");
            }
            Assert.That(data.treeInstances.Length, Is.EqualTo(c.Trees.Length));
            for (int n = 0; n < c.Trees.Length; n++)
            {
                Assert.That(data.treeInstances[n].position.x, Is.EqualTo(c.Trees[n].X).Within(1e-5f));
                Assert.That(data.treeInstances[n].prototypeIndex, Is.EqualTo(c.Trees[n].Prototype));
            }
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void AMapWithNoGroundBuildsFromItsLand()
    {
        var (world, land) = Hilly();
        var pack = GroundFromLand.Build(Source(world, land));
        var chunks = pack.Chunks.ToDictionary(c => (c.I, c.J));
        var field = new CellHeightfield(Small, Step, 1f);
        foreach (var (x, y) in Small.Cells())
        {
            var (i, j, sx, sz) = Small.CellSample(x, y);
            double ground = pack.Metres(chunks[(i, j)], sx, sz) - field.SampleOffset(world, i, j, sx, sz);
            Assert.That(ground, Is.EqualTo(land[y * 128 + x] * Unit).Within(Unit), $"cell {x},{y}");
        }
        Assert.That(pack.Chunks.Sum(c => c.Trees.Length), Is.EqualTo(200), "a tree in each tree cell");
        Assert.That(pack.Chunks.Sum(c => c.Details.Count(d => d > 0)), Is.GreaterThan(0), "grass on the grass");
        var again = GroundFromLand.Build(Source(world, land));
        Assert.That(again.Chunks.SelectMany(c => c.Heights), Is.EqualTo(pack.Chunks.SelectMany(c => c.Heights)), "the same from the same");
        Assert.That(again.Chunks.SelectMany(c => c.Details), Is.EqualTo(pack.Chunks.SelectMany(c => c.Details)));
    }

    [Test]
    public void ChunksBuiltSideBySideAreTheSameAsOneByOne()
    {
        var (world, land) = Hilly();
        var side = GroundFromLand.Build(Source(world, land));
        GroundFromLand.Threaded = false;
        try
        {
            var one = GroundFromLand.Build(Source(world, land));
            Assert.That(side.Chunks.Select(c => (c.I, c.J)), Is.EqualTo(one.Chunks.Select(c => (c.I, c.J))), "in the same order");
            Assert.That(side.Chunks.SelectMany(c => c.Heights), Is.EqualTo(one.Chunks.SelectMany(c => c.Heights)));
            Assert.That(side.Chunks.SelectMany(c => c.Paint), Is.EqualTo(one.Chunks.SelectMany(c => c.Paint)));
            Assert.That(side.Chunks.SelectMany(c => c.Details), Is.EqualTo(one.Chunks.SelectMany(c => c.Details)));
            Assert.That(side.Chunks.SelectMany(c => c.Trees).Select(t => (t.X, t.Y, t.Z, t.Prototype)),
                Is.EqualTo(one.Chunks.SelectMany(c => c.Trees).Select(t => (t.X, t.Y, t.Z, t.Prototype))));
        }
        finally
        {
            GroundFromLand.Threaded = true;
        }
    }

    [Test]
    public void BuiltChunksMeetAtTheirSeams()
    {
        var (world, land) = Hilly();
        var pack = GroundFromLand.Build(Source(world, land));
        var chunks = pack.Chunks.ToDictionary(c => (c.I, c.J));
        double Edge(GroundChunk c, float along, bool east)
        {
            int last = c.Resolution - 1;
            float at = along * last;
            int a = Mathf.FloorToInt(at), b = Mathf.Min(last, a + 1);
            double h0 = east ? pack.Metres(c, last, a) : pack.Metres(c, 0, a), h1 = east ? pack.Metres(c, last, b) : pack.Metres(c, 0, b);
            return h0 + (h1 - h0) * (at - a);
        }
        // Full detail (0, 0) beside coarse (1, 0): every sample of the fine edge lies on the coarse one.
        for (int k = 0; k <= 256; k++)
            Assert.That(Edge(chunks[(0, 0)], k / 256f, true), Is.EqualTo(Edge(chunks[(1, 0)], k / 256f, false)).Within(0.02), $"sample {k}");
    }

    (ValleyGround ground, Terrain[] terrains) Built(World world, int[] land)
    {
        var pack = GroundFromLand.Build(Source(world, land));
        var terrains = new Terrain[4];
        foreach (var (i, j) in Small.GridChunks())
        {
            var go = new GameObject($"Chunk {i},{j}", typeof(Terrain), typeof(TerrainCollider));
            made.Add(go);
            var (x, z) = Small.ChunkCorner(i, j);
            go.transform.position = new Vector3(x, 0f, z);
            terrains[j * 2 + i] = go.GetComponent<Terrain>();
            terrains[j * 2 + i].terrainData = Template();
        }
        var ground = new ValleyGround(Small, terrains, new GroundBuilder(pack, Template(), Small.ChunkSize));
        ground.Bind(world, Step, 1000f);
        return (ground, terrains);
    }

    static void OpenRoom(World world) => world.OpenRoom(ChangeMode.Open, "Host", world.Rules, ModBudgets.Load(ChemGrid.Folders.Rules));

    [Test]
    public void AStampEasesOnBuiltGroundAndGoesWithANewWorld()
    {
        var (world, land) = Hilly();
        OpenRoom(world);
        var (ground, terrains) = Built(world, land);
        float was = ground.GroundAt(60f, 100f);
        var (x, y) = Small.CellAt(60f, 100f);
        var stamp = LandStamp.Make(x, y, 12, 80, LandStamp.Dome, 0, 0, 0, 3);
        world.LandMoved += (cx, cy, reach) =>
        {
            for (int v = cy - reach; v <= cy + reach; v++)
                for (int u = cx - reach; u <= cx + reach; u++)
                    if (world.InBounds(u, v)) ground.Reshape(u, v);
        };
        var said = world.ApplyRoom(RoomCommand.Act("Host", new Newtonsoft.Json.Linq.JObject { ["kind"] = World.ActLand, ["what"] = World.ActLand, ["stamp"] = stamp.Row }.ToString()));
        Assert.That(said.Done, Is.True, said.Line);
        for (int n = 0; n < 40; n++)
        {
            ground.Advance(0.5f);
            ground.Flush((_, _) => false);
        }
        Assert.That(ground.Shaping, Is.False);
        Assert.That(ground.GroundAt(60f, 100f), Is.EqualTo(was + 10f).Within(0.3f), "the dome's 80 units, on built ground");
        Assert.That(terrains[0].terrainData.name, Does.StartWith("Built chunk"));
        Assert.That(terrains[0].GetComponent<TerrainCollider>().terrainData, Is.SameAs(terrains[0].terrainData), "what stands on it stands on what's drawn");
    }

    [Test]
    public void FelledTreesAndClearedGrassComeBackFromTheArrays()
    {
        var (world, land) = Hilly();
        var (ground, _) = Built(world, land);
        var at = Small.ChunkOfCell(45, 45);
        int trees = ground.Data(at).treeInstances.Length;
        Assert.That(trees, Is.GreaterThan(0));
        ground.WritableData(at).treeInstances = new TreeInstance[0];
        Assert.That(ground.Data(at).treeInstances.Length, Is.Zero);
        ground.Bind(world, Step, 1000f);
        Assert.That(ground.Data(at).treeInstances.Length, Is.EqualTo(trees));
        ground.Destroy();
    }

    // A generated map's ground is made a chunk at a time, as its terrains are (#205): the same arrays as made whole.
    [Test]
    public void ChunksMadeOneAtATimeAreTheSameAsMadeWhole()
    {
        var (world, land) = Hilly();
        var whole = GroundFromLand.Build(Source(world, land));
        var (shared, make) = GroundFromLand.Lazy(Source(world, land));
        Assert.That((shared.Bottom, shared.Top), Is.EqualTo((whole.Bottom, whole.Top)));
        Assert.That(shared.Chunks, Is.Empty, "no chunk made until asked");
        foreach (var c in whole.Chunks)
        {
            var one = make((c.I, c.J));
            Assert.That(one.Heights, Is.EqualTo(c.Heights), $"heights {c.I},{c.J}");
            Assert.That(one.Paint, Is.EqualTo(c.Paint), $"paint {c.I},{c.J}");
            Assert.That(one.Details, Is.EqualTo(c.Details), $"grass {c.I},{c.J}");
            Assert.That(one.Trees.Select(t => (t.X, t.Y, t.Z, t.Prototype)), Is.EqualTo(c.Trees.Select(t => (t.X, t.Y, t.Z, t.Prototype))), $"trees {c.I},{c.J}");
        }
    }

    // A ground made a chunk at a time makes only the chunks given terrains, stands the rest on the land, and draws the
    // same ground as one made whole where it is made; an untouched chunk whose terrain goes gives its data back.
    [Test]
    public void AGroundMadeAChunkAtATimeMakesOnlyWhatItIsAsked()
    {
        var (world, land) = Hilly();
        var (eager, _) = Built(world, land);
        var (shared, make) = GroundFromLand.Lazy(Source(world, land));
        var builder = new GroundBuilder(shared, Template(), Small.ChunkSize, make, at => Small.HasChunk(at.i, at.j));
        var ground = new ValleyGround(Small, builder) { Land = LandChunks.Of(land, world.W, world.H) };
        ground.Bind(world, Step, 1000f);
        Assert.That(builder.Held, Is.Zero, "nothing made at Bind without a terrain");
        var at = Small.ChunkOfCell(45, 45);
        var go = new GameObject("Chunk", typeof(Terrain), typeof(TerrainCollider));
        made.Add(go);
        var (cx, cz) = Small.ChunkCorner(at.i, at.j);
        go.transform.position = new Vector3(cx, 0f, cz);
        ground.Attach(at, go.GetComponent<Terrain>());
        Assert.That(builder.Held, Is.EqualTo(1), "only the chunk given a terrain");
        Assert.That(ground.IsReady(at), Is.True);
        Assert.That(go.GetComponent<TerrainCollider>().terrainData, Is.SameAs(go.GetComponent<Terrain>().terrainData));
        var (wx, wz) = Small.CellCenter(45, 45);
        Assert.That(ground.GroundAt(wx, wz), Is.EqualTo(eager.GroundAt(wx, wz)).Within(1e-3f), "the same ground as made whole");
        Assert.That(ground.BaseHeight(45, 45), Is.EqualTo(eager.BaseHeight(45, 45)).Within(1e-3f));
        Assert.That(ground.Data(at).treeInstances.Length, Is.EqualTo(eager.Data(at).treeInstances.Length));
        var other = Small.ChunkOfCell(100, 100);
        Assert.That(other, Is.Not.EqualTo(at));
        Assert.That(ground.IsReady(other), Is.False);
        Assert.That(ground.BaseHeight(100, 100), Is.EqualTo(land[100 * 128 + 100] * Unit).Within(1e-4f), "a chunk not made stands on the land");
        ground.Release(at);
        Assert.That(ground.IsReady(at), Is.False, "untouched, it gives its data back");
        Assert.That(builder.Held, Is.Zero);
        ground.Destroy();
        eager.Destroy();
    }

    [TestCase(new[] { "-ground-builder", "on" }, null, null, "on")]
    [TestCase(new[] { "-ground-builder", "land" }, "on", null, "land")]
    [TestCase(new string[0], "land", null, "land")]
    [TestCase(new string[0], null, "on\n", "on")]
    [TestCase(new string[0], null, null, "on")]
    [TestCase(new[] { "-ground-builder", "maybe" }, null, null, "on")]
    [TestCase(new[] { "-ground-builder", "off" }, null, null, "on")]
    public void TheSwitchIsOnUnlessLandIsAsked(string[] args, string env, string file, string mode) =>
        Assert.That(GroundSwitch.Read(args, env, file), Is.EqualTo(mode));
}
