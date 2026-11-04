using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEngine;

// The valley's ground pack against its land, every cell: what the view will draw and what the core reads are one ground.
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
        var field = new CellHeightfield(layout, 0.5f, 1f);
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
}
