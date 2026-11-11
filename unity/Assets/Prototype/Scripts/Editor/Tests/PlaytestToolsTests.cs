using NUnit.Framework;
using UnityEngine;

// The playtest tools (#220): a mark tells whoever listens even with the play log off (the Editor never writes one), which
// is how marks-<time>.txt gets its lines; a map with no generated biomes is "valley"; the heading is empty without a camera.
public class PlaytestToolsTests
{
    [Test]
    public void AMarkIsAnnouncedEvenWithTheLogOff()
    {
        var go = new GameObject("log under test");
        string heard = null;
        void Hear(string why) => heard = why;
        PlayLog.Marked += Hear;
        try
        {
            go.AddComponent<PlayLog>().Mark("sky looks wrong");
        }
        finally
        {
            PlayLog.Marked -= Hear;
            Object.DestroyImmediate(go);
        }
        Assert.That(heard, Is.EqualTo("sky looks wrong"));
    }

    [Test]
    public void WithoutGeneratedBiomesTheBiomeIsTheValley()
    {
        if (BiomeGround.Last != null) Assert.Ignore("a generated map's biomes are left from a Play run");
        Assert.That(PlaytestTools.BiomeAt(10, 10), Is.EqualTo("valley"));
    }

    [Test]
    public void AHeadingNeedsACamera()
    {
        if (Camera.main != null) Assert.Ignore("a scene camera is open");
        Assert.That(PlaytestTools.Heading(), Is.EqualTo(""));
    }

    [Test]
    public void AHopGoesRoundTheSpawnsBothWays()
    {
        Assert.That(PlaytestTools.HopTo(0, 1, 9), Is.EqualTo(1));
        Assert.That(PlaytestTools.HopTo(8, 1, 9), Is.EqualTo(0));
        Assert.That(PlaytestTools.HopTo(0, -1, 9), Is.EqualTo(8));
        Assert.That(PlaytestTools.HopTo(4, -1, 9), Is.EqualTo(3));
    }

    [Test]
    public void AHopStartsFromTheSpawnNearestTheWizard()
    {
        var spawns = new System.Collections.Generic.List<SpawnPoints.Spawn>
        {
            new("start", "The Valley", new Vector2Int(100, 100)),
            new("a", "Ash", new Vector2Int(5000, 100)),
            new("b", "Bog", new Vector2Int(100, 5000)),
        };
        Assert.That(PlaytestTools.NearestSpawn(spawns, new Vector2Int(4900, 200)), Is.EqualTo(1));
        Assert.That(PlaytestTools.NearestSpawn(spawns, new Vector2Int(0, 4000)), Is.EqualTo(2));
    }
}
