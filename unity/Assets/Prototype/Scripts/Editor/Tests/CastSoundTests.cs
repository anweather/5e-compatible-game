using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// The casting sounds' table (CastSoundTable): every sound it names has a row, every
// recording it names is in git's CC0 folder, every row can be made with no recording
// at all (a machine without the packs), and each cast by elements has the right kind.
public class CastSoundTests
{
    static Rules Loaded() => Rules.Load(Path.GetFullPath("../rules"));

    [Test]
    public void EveryCastSoundHasARow()
    {
        foreach (var element in new[] { ElementIds.Fire, ElementIds.Water, ElementIds.Earth, ElementIds.Air, null })
            Assert.That(CastSoundTable.Of(CastSoundTable.GatherOf(element)), Is.Not.Null, element);
        foreach (CastKind kind in System.Enum.GetValues(typeof(CastKind)))
            Assert.That(CastSoundTable.Of(CastSoundTable.ReleaseOf(kind)), Is.Not.Null, kind.ToString());
        Assert.That(CastSoundTable.Rows.Select(r => r.Sound), Is.Unique);
    }

    [Test]
    public void EveryRowCanBeMadeWithNoRecordings()
    {
        foreach (var row in CastSoundTable.Rows)
        {
            Assert.That(row.Make, Is.Not.Empty, row.Sound);
            foreach (var make in row.Make) Assert.That(make().Any(s => s != 0f), row.Sound);
        }
    }

    [Test]
    public void ItsRecordingsAreOurOwnCC0Ones()
    {
        foreach (var file in CastSoundTable.Rows.SelectMany(r => r.Files))
        {
            Assert.That(file, Does.StartWith("Assets/Prototype/Audio/CC0/"));
            Assert.That(File.Exists(file), file);
        }
    }

    [Test]
    public void EveryElementHasAHumAtEveryStage()
    {
        foreach (var element in new[] { ElementIds.Fire, ElementIds.Water, ElementIds.Earth, ElementIds.Air, null })
            for (int stage = 0; stage <= World.MaxCharge; stage++)
            {
                var row = CastSoundTable.Of(CastSoundTable.HumOf(element, stage));
                Assert.That(row, Is.Not.Null, $"{element} {stage}");
                Assert.That(row.Loop, $"{element} {stage}");
            }
        Assert.That(CastSoundTable.HumOf(ElementIds.Fire, 1), Is.Not.EqualTo(CastSoundTable.HumOf(ElementIds.Water, 1)));
    }

    [Test]
    public void CombiningRisesOverTheGathering() =>
        Assert.That(CastSoundTable.HumPitchAt(1), Is.GreaterThan(CastSoundTable.HumPitchAt(0)));

    // A click at the seam or a clipped peak would be heard on every cast. Vorbis leaves a seam of up to
    // 0.025 on these, about 2.6 of the hums' largest steps between samples; a click is many times that.
    [Test]
    public void EveryRenderedHumIsACleanLoop()
    {
        foreach (var row in CastSoundTable.Rows.Where(r => r.Loop))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(row.Files[0]);
            Assert.That(clip, Is.Not.Null, row.Sound);
            clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            Assert.That(clip.channels, Is.EqualTo(1), row.Sound);
            Assert.That(clip.length, Is.EqualTo(3f).Within(0.05f), row.Sound);
            Assert.That(data.Max(s => Mathf.Abs(s)), Is.LessThan(0.9f), row.Sound);
            Assert.That(Mathf.Abs(data[0] - data[data.Length - 1]), Is.LessThan(0.03f), row.Sound);
        }
    }

    [Test]
    public void TheChargeRisesByLevel()
    {
        for (int level = 2; level <= World.MaxCharge; level++)
        {
            Assert.That(CastSoundTable.HumPitchAt(level), Is.GreaterThan(CastSoundTable.HumPitchAt(level - 1)));
            Assert.That(CastSoundTable.HumLoudnessAt(level), Is.GreaterThan(CastSoundTable.HumLoudnessAt(level - 1)));
            Assert.That(CastSoundTable.ReleasePitchAt(level), Is.LessThan(CastSoundTable.ReleasePitchAt(level - 1)));
        }
    }

    [TestCase("fire-bolt", CastKind.Bolt)]
    [TestCase("lightning-bolt", CastKind.Bolt)]
    [TestCase("sand-blast", CastKind.Bolt)]
    [TestCase("fireball", CastKind.Burst)]
    [TestCase("forge", CastKind.Burst)]
    [TestCase("mold-earth-heap", CastKind.Ground)]
    [TestCase("wall-of-stone", CastKind.Ground)]
    [TestCase("mud", CastKind.Ground)]
    [TestCase("gust-of-wind", CastKind.Flow)]
    [TestCase("wave", CastKind.Flow)]
    [TestCase("rolling-boulder", CastKind.Ground)]
    [TestCase("geyser", CastKind.Burst)]
    [TestCase("plant-growth", CastKind.Quiet)]
    [TestCase("light", CastKind.Quiet)]
    [TestCase("shapeshift", CastKind.Quiet)]
    public void ASpellsKindComesFromItsRules(string spell, CastKind kind) =>
        Assert.That(CastSoundTable.KindOf(Loaded().Spell(spell), false), Is.EqualTo(kind));

    [Test]
    public void ASpellFromTheSkyIsCalledDown() =>
        Assert.That(CastSoundTable.KindOf(Loaded().Spell("fireball"), true), Is.EqualTo(CastKind.Sky));

}
