using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEngine;

// The valley's ground against its land, every cell: what the view will draw and what the core reads are one ground. Two
// grounds can be drawn: the map's pack (ground/, GroundSwitch on, and what's committed) and the yard's own terrains, baked
// on each machine by Build Chemistry Yard into Generated/Valley (GroundSwitch off, the Editor's default, which the probes
// draw). ValleyChemistry.CheckGround looks at 64 cells of whichever is drawn; these look at every cell of both.
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

    // The yard's baked terrains, as ValleyGround reads them in play: each cell's sample less the starting cells' offsets
    // (laid land not drawn, walls flat as the yard's rocks make them), the river's cells raised to their banks. On
    // 2026-11-08 the PC's probes found a cell 20 cm under its land after hold-east-path dried the west brook, while the
    // pack test passed: the pack and land agreed, and the terrain the Editor drew was another bake.
    [Test]
    public void TheYardsTerrainsAgreeWithTheLandOnEveryCell()
    {
        WizardPartsTests.WithYard(scene =>
        {
            var roots = scene.GetRootGameObjects();
            var valley = roots.SelectMany(r => r.GetComponentsInChildren<ValleyChemistry>(true)).FirstOrDefault();
            var grid = roots.SelectMany(r => r.GetComponentsInChildren<ChemGrid>(true)).FirstOrDefault();
            Assert.That(valley, Is.Not.Null, "the yard has its valley");
            Assert.That(grid, Is.Not.Null, "the yard has its grid");
            string dir = ChemGrid.Folders.World;
            var rules = Rules.Load(ChemGrid.Folders.Rules);
            var layout = TerrainGrid.Valley();
            var world = ChemGrid.MapWorld(rules, ChemGrid.CellMap(), 1u, out _);
            var land = LandFile.Read(File.ReadAllText(Path.Combine(dir, MapFile.LandName)), world.W, world.H);
            bool flat = grid.wallRocks != null && grid.wallRocks.Length > 0 && grid.wallRocks[0] != null;
            var field = new CellHeightfield(layout, grid.stepHeight, 1f) { LaidLandDrawn = false, FlatUnderWalls = flat };
            var raise = new Dictionary<int, float>();
            for (int k = 0; k < (valley.channelCells?.Length ?? 0); k++) raise[valley.channelCells[k]] = valley.channelDepths[k];
            double unit = grid.stepHeight / rules.Sim.WaterUnitsPerStep, worst = 0;
            var off = new List<string>();
            for (int y = 0; y < world.H; y++)
            {
                for (int x = 0; x < world.W; x++)
                {
                    var (i, j, sx, sz) = layout.CellSample(x, y);
                    var terrain = valley.chunks[j * layout.ChunksX + i];
                    Assert.That(terrain, Is.Not.Null, $"chunk ({i}, {j})");
                    double ground = terrain.transform.position.y + terrain.terrainData.GetHeight(sx, sz) - field.SampleOffset(world, i, j, sx, sz);
                    if (raise.TryGetValue(world.Idx(x, y), out float depth)) ground += depth;
                    double metres = land[world.Idx(x, y)] * unit, apart = System.Math.Abs(ground - metres);
                    worst = System.Math.Max(worst, apart);
                    if (apart > unit) off.Add($"({x}, {y}): land {metres:0.00} m, drawn {ground:0.00} m");
                }
            }
            Debug.Log($"YARD GROUND vs LAND: worst {worst:0.000} m, {off.Count} cells over {unit} m");
            Assert.That(off, Is.Empty, $"{off.Count} cells: {string.Join("; ", off.Take(6))}. If the map's land changed, rebuild the yard (Prototype > Build Chemistry Yard)");
        });
    }
}
