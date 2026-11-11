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
        Assert.That(BiomeGround.Last, Is.Null);
        Assert.That(PlaytestTools.BiomeAt(10, 10), Is.EqualTo("valley"));
    }

    [Test]
    public void AHeadingNeedsACamera()
    {
        if (Camera.main != null) Assert.Ignore("a scene camera is open");
        Assert.That(PlaytestTools.Heading(), Is.EqualTo(""));
    }
}
