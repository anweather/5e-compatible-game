using System.Collections.Generic;
using System.IO;
using Chem;
using NUnit.Framework;
using UnityEngine;

// SpawnPoints (world squares phase 6): a map's own spawn first, then its spawn places; the start the host picks; and
// the nearest spawn the party has reached for a wizard who goes down.
public class SpawnPointsTests
{
    const string Places = @"{ ""places"": [
        { ""id"": ""hollowmere"", ""name"": ""Hollowmere"", ""kind"": ""village"", ""area"": [10, 10, 20, 20] },
        { ""id"": ""spawn-north"", ""name"": ""The North"", ""kind"": ""spawn"", ""circle"": { ""at"": [300, 40], ""radius"": 1 } },
        { ""id"": ""spawn-east"", ""name"": ""The East"", ""kind"": ""spawn"", ""area"": [500, 300, 502, 302] }
    ] }";

    static MapFile Valley() => MapFile.Load(Path.Combine(ChemGrid.Folders.Repo, "world", "maps", "valley"));

    static List<SpawnPoints.Spawn> Spawns() => SpawnPoints.Of(Valley(), WorldPlaces.FromJson(Places), "The Valley");

    [Test]
    public void TheMapsOwnSpawnComesFirstThenItsSpawnPlaces()
    {
        var spawns = Spawns();
        var map = Valley();
        Assert.That(spawns.ConvertAll(s => s.Id), Is.EqualTo(new[] { SpawnPoints.StartId, "spawn-north", "spawn-east" }));
        Assert.That(spawns[0].Cell, Is.EqualTo(new Vector2Int(map.Spawn.x, map.Spawn.y)));
        Assert.That(spawns[1].Cell, Is.EqualTo(new Vector2Int(300, 40)));
        Assert.That(spawns[2].Cell, Is.EqualTo(new Vector2Int(501, 301)));
    }

    [Test]
    public void ASpawnPlaceOnTheMapsOwnSpawnIsThatOne()
    {
        var map = Valley();
        string json = $@"{{ ""places"": [ {{ ""id"": ""spawn-valley"", ""name"": ""The Valley"", ""kind"": ""spawn"", ""circle"": {{ ""at"": [{map.Spawn.x + 2}, {map.Spawn.y}], ""radius"": 1 }} }} ] }}";
        Assert.That(SpawnPoints.Of(map, WorldPlaces.FromJson(json), "The Valley"), Has.Count.EqualTo(1));
    }

    [Test]
    public void AnUnknownStartIsTheMapsOwn()
    {
        var spawns = Spawns();
        Assert.That(SpawnPoints.Find(spawns, "spawn-east").Name, Is.EqualTo("The East"));
        Assert.That(SpawnPoints.Find(spawns, "nowhere").Id, Is.EqualTo(SpawnPoints.StartId));
    }

    [Test]
    public void AWizardComesBackAtTheNearestSpawnThePartyHasReached()
    {
        var spawns = Spawns();
        var reached = new HashSet<string> { SpawnPoints.StartId };
        // Down by the east spawn before anyone has been there: back at the start.
        Assert.That(SpawnPoints.Nearest(spawns, reached, new Vector2Int(495, 300)).Id, Is.EqualTo(SpawnPoints.StartId));
        Assert.That(SpawnPoints.Visit(spawns, reached, new Vector2Int(501 + SpawnPoints.ReachCells, 301)), Is.True);
        Assert.That(SpawnPoints.Visit(spawns, reached, new Vector2Int(501, 301)), Is.False, "reached once is enough");
        Assert.That(SpawnPoints.Nearest(spawns, reached, new Vector2Int(495, 300)).Id, Is.EqualTo("spawn-east"));
        // The north spawn is nearer this cell, but nobody has reached it.
        Assert.That(SpawnPoints.Nearest(spawns, reached, new Vector2Int(310, 60)).Id, Is.Not.EqualTo("spawn-north"));
    }

    [Test]
    public void NothingReachedComesBackAtTheMapsOwn()
    {
        Assert.That(SpawnPoints.Nearest(Spawns(), new HashSet<string>(), new Vector2Int(300, 40)).Id, Is.EqualTo(SpawnPoints.StartId));
    }
}
