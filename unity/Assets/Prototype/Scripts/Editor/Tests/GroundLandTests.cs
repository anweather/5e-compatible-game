using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEngine;

// The valley's ground against its land, every cell: what the view will draw and what the core reads are one ground. The
// map's pack (ground/, committed) is the only ground there is: the scene holds none, and the game and the yard's build both
// make their chunks from it (MapTerrains, EditorGround). The yard's own bake, which once drew another ground in the Editor,
// retired with phase 6. ValleyChemistry.CheckGround looks at 64 cells as drawn; this looks at every cell.
public class GroundLandTests
{
    // One land unit.
    const double MostMetres = 0.125;

    [Test]
    public void TheGroundPackAgreesWithTheLandOnEveryCell()
    {
        string dir = ChemGrid.Folders.World;
        var pack = GroundPack.Load(dir);
        var layout = TerrainGrid.Valley();
        var world = ChemGrid.MapWorld(Rules.Load(ChemGrid.Folders.Rules), ChemGrid.CellMap(), 1u, out _);
        var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), world.W, world.H);
        var field = new CellHeightfield(layout, 0.5f, 1f) { LaidLandDrawn = false };
        var chunks = pack.Chunks.ToDictionary(c => (c.I, c.J));
        var channel = new Dictionary<int, float>();
        for (int k = 0; k < pack.ChannelCells.Length; k++) channel[pack.ChannelCells[k]] = pack.ChannelDepths[k];
        int off = 0;
        double worst = 0;
        for (int y = 0; y < world.H; y++)
        {
            for (int x = 0; x < world.W; x++)
            {
                var (i, j, sx, sz) = layout.CellSample(x, y);
                double ground = pack.Metres(chunks[(i, j)], sx, sz) - field.SampleOffset(world, i, j, sx, sz);
                if (channel.TryGetValue(world.Idx(x, y), out float depth)) ground += depth;
                double apart = System.Math.Abs(ground - land[world.Idx(x, y)] * 0.125);
                worst = System.Math.Max(worst, apart);
                if (apart > MostMetres) off++;
            }
        }
        Debug.Log($"GROUND vs LAND: worst {worst:0.000} m, {off} cells over {MostMetres} m");
        Assert.That(off, Is.Zero, $"worst {worst:0.000} m");
    }

    // The chunks as the game makes them as the scene starts (MapTerrains, from the pack with the yard's template), read
    // as ValleyGround reads them in play: each cell's sample less the starting cells' offsets (laid land not drawn, walls
    // flat as the yard's rocks make them), the river's cells raised to their banks. Before phase 6 this read the yard's
    // own bake, which once drew a cell 20 cm under its land while the pack and the land agreed (2026-11-08).
    [Test]
    public void TheChunksTheGameMakesAgreeWithTheLandOnEveryCell()
    {
        var template = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>("Assets/Prototype/Generated/Ground/Template.asset");
        Assert.That(template, Is.Not.Null, "the yard's ground template (Prototype > Build Chemistry Yard)");
        string dir = ChemGrid.Folders.World;
        var rules = Rules.Load(ChemGrid.Folders.Rules);
        var layout = TerrainGrid.Valley();
        var builder = new GroundBuilder(GroundPack.Load(dir), template, layout.ChunkSize);
        var root = new GameObject("Chunks under test");
        var terrains = MapTerrains.Place(root.transform, layout, builder, null);
        try
        {
            var world = ChemGrid.MapWorld(rules, ChemGrid.CellMap(), 1u, out _);
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), world.W, world.H);
            var field = new CellHeightfield(layout, 0.5f, 1f) { LaidLandDrawn = false, FlatUnderWalls = true };
            var (cells, depths) = builder.Channel;
            var raise = new Dictionary<int, float>();
            for (int k = 0; k < cells.Length; k++) raise[cells[k]] = depths[k];
            double unit = 0.5 / rules.Sim.WaterUnitsPerStep, worst = 0;
            var off = new List<string>();
            for (int y = 0; y < world.H; y++)
            {
                for (int x = 0; x < world.W; x++)
                {
                    var (i, j, sx, sz) = layout.CellSample(x, y);
                    var terrain = terrains[j * layout.ChunksX + i];
                    double ground = terrain.transform.position.y + terrain.terrainData.GetHeight(sx, sz) - field.SampleOffset(world, i, j, sx, sz);
                    if (raise.TryGetValue(world.Idx(x, y), out float depth)) ground += depth;
                    double metres = land[world.Idx(x, y)] * unit, apart = System.Math.Abs(ground - metres);
                    worst = System.Math.Max(worst, apart);
                    if (apart > unit) off.Add($"({x}, {y}): land {metres:0.00} m, drawn {ground:0.00} m");
                }
            }
            Debug.Log($"CHUNKS vs LAND: worst {worst:0.000} m, {off.Count} cells over {unit} m");
            Assert.That(off, Is.Empty, $"{off.Count} cells: {string.Join("; ", off.Take(6))}");
        }
        finally
        {
            foreach (var t in terrains) Object.DestroyImmediate(t.terrainData);
            Object.DestroyImmediate(root);
        }
    }
}
