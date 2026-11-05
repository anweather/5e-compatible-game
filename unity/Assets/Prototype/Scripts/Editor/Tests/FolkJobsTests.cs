using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using Unity.Mathematics;

// The valley's jobs over its real land: the road is why anyone takes it.
public class FolkJobsTests
{
    const float CellMetres = 1.5f, Walk = 1.4f;
    static readonly (int x, int y) Lane = (221, 325), Gate = (153, 369);

    static (FolkJobs.Land land, World world) Valley()
    {
        var world = ChemGrid.MapWorld(Rules.Load(ChemGrid.Folders.Rules), ChemGrid.CellMap(), 1u, out _);
        var units = LandFile.Read(File.ReadAllText(Path.Combine(ChemGrid.Folders.World, MapFile.LandName)), world.W, world.H);
        return (new FolkJobs.Land(world, units, CellMetres), world);
    }

    static float2 Centre(int x, int y) => new float2(x, y) * CellMetres;

    // Straight from the foot of the hill to the gate, the way a person would rather not go.
    static List<(int x, int y)> StraightUp()
    {
        var way = new List<(int x, int y)>();
        for (int x = 190; x >= Gate.x; x--) way.Add((x, Gate.y));
        return way;
    }

    [Test]
    public void TheRoadIsQuickerThanTheHill()
    {
        var (land, _) = Valley();
        var road = FolkJobs.Way(land, (190, 384), Gate);
        Assert.That(road, Is.Not.Empty);
        float byRoad = FolkJobs.Seconds(land, road), straight = FolkJobs.Seconds(land, StraightUp());
        TestContext.WriteLine($"by road {byRoad:0} s over {road.Count} cells, straight up {straight:0} s");
        Assert.That(byRoad, Is.LessThan(straight));
    }

    [Test]
    public void AWalkToTheCastleTakesTheRoad()
    {
        var (land, _) = Valley();
        var way = FolkJobs.Way(land, Lane, Gate);
        Assert.That(way, Is.Not.Empty);
        float onRoad = way.Count(c => land.Road(c.x, c.y)) / (float)way.Count;
        TestContext.WriteLine($"{way.Count} cells, {onRoad:P0} on the road");
        Assert.That(onRoad, Is.GreaterThan(0.8f));
    }

    [Test]
    public void AWorkerIsWhereTheClockSaysAndComesRound()
    {
        var (land, _) = Valley();
        var jobs = FolkJobs.Load(ChemGrid.Folders.World, land, Centre, Walk);
        Assert.That(jobs.Workers.Select(w => w.Job).Distinct(), Is.EquivalentTo(new[] { "guard", "porter", "fisher", "net-mender" }));
        var again = FolkJobs.Load(ChemGrid.Folders.World, land, Centre, Walk);
        for (int k = 0; k < jobs.Workers.Count; k++)
        {
            var a = jobs.Workers[k].At(1234.5);
            var b = again.Workers[k].At(1234.5);
            Assert.That(a.At.x, Is.EqualTo(b.At.x));
            Assert.That(a.At.y, Is.EqualTo(b.At.y));
            Assert.That(jobs.Workers[k].Individual, Is.EqualTo(again.Workers[k].Individual));
        }
    }

    [Test]
    public void EveryWorkerWalksAndStops()
    {
        var (land, _) = Valley();
        foreach (var w in FolkJobs.Load(ChemGrid.Folders.World, land, Centre, Walk).Workers)
        {
            var moments = Enumerable.Range(0, 2000).Select(s => w.At(s * 2.0)).ToList();
            Assert.That(moments.Any(m => m.Pace > 0f), $"{w.Job} walks");
            Assert.That(moments.Any(m => m.Pace <= 0f), $"{w.Job} stops");
        }
    }
}
