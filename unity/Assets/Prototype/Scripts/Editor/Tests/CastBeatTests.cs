using NUnit.Framework;
using UnityEngine;

// The beats of a cast from the state every peer shares (CastBeat.Next), with
// CastFeel's default timings: what comes after what, for each kind of cast.
public class CastBeatTests
{
    const float Long = 9f;

    CastFeel feel;

    [SetUp]
    public void Feel() => feel = ScriptableObject.CreateInstance<CastFeel>();

    [TearDown]
    public void Clear() => Object.DestroyImmediate(feel);

    static CastFacts Gathering(bool second = false, bool holding = false) => new(true, second, true, holding, false, false);
    static CastFacts Met(bool holding = false) => new(true, true, false, holding, false, false);
    static CastFacts Fired(bool thrust = false) => new(false, false, false, false, true, thrust);
    static readonly CastFacts Nothing = new(false, false, false, false, false, false);

    Beat Next(Beat now, float seconds, CastFacts facts) => CastBeat.Next(now, seconds, facts, feel);

    [Test]
    public void ASingleElementSkipsTheSecondBeat()
    {
        Assert.That(Next(Beat.Neutral, Long, Gathering()), Is.EqualTo(Beat.First));
        Assert.That(Next(Beat.First, Long, Gathering()), Is.EqualTo(Beat.First));
        Assert.That(Next(Beat.First, Long, Met()), Is.EqualTo(Beat.Slam));
    }

    [Test]
    public void ThePairGoesThroughBothHands()
    {
        Assert.That(Next(Beat.First, 0.1f, Gathering(second: true)), Is.EqualTo(Beat.Second));
        Assert.That(Next(Beat.Second, Long, Met()), Is.EqualTo(Beat.Slam));
    }

    [Test]
    public void TheHandsWaitTogetherForTheSpellToLeave()
    {
        Assert.That(Next(Beat.Slam, Long, Fired()), Is.EqualTo(Beat.Slam));
        Assert.That(Next(Beat.Slam, Long, Fired(thrust: true)), Is.EqualTo(Beat.Release));
    }

    [Test]
    public void AHitchNeverSkipsTheSlam()
    {
        Assert.That(Next(Beat.Second, Long, Fired(thrust: true)), Is.EqualTo(Beat.Slam));
        Assert.That(Next(Beat.Slam, 0f, Fired(thrust: true)), Is.EqualTo(Beat.Slam));
    }

    [Test]
    public void ReleaseFinishesThenRecoversThenNeutral()
    {
        Assert.That(Next(Beat.Release, feel.release.seconds, Nothing), Is.EqualTo(Beat.Release), "held at its end");
        Assert.That(Next(Beat.Release, Long, Nothing), Is.EqualTo(Beat.Recovery));
        Assert.That(Next(Beat.Recovery, 0f, Nothing), Is.EqualTo(Beat.Recovery));
        Assert.That(Next(Beat.Recovery, Long, Nothing), Is.EqualTo(Beat.Neutral));
    }

    [Test]
    public void HoldingPastTheSlamCharges()
    {
        Assert.That(Next(Beat.First, Long, Met(holding: true)), Is.EqualTo(Beat.Slam));
        Assert.That(Next(Beat.Slam, feel.slam.seconds / 2f, Met(holding: true)), Is.EqualTo(Beat.Slam), "the slam plays out first");
        Assert.That(Next(Beat.Slam, Long, Met(holding: true)), Is.EqualTo(Beat.Charge));
        Assert.That(Next(Beat.Charge, Long, Met()), Is.EqualTo(Beat.Charge), "let go, it waits for the core's next tick");
        Assert.That(Next(Beat.Charge, Long, Fired()), Is.EqualTo(Beat.Charge));
        Assert.That(Next(Beat.Charge, Long, Fired(thrust: true)), Is.EqualTo(Beat.Release));
    }

    [Test]
    public void ADroppedCastFizzles()
    {
        foreach (var beat in new[] { Beat.First, Beat.Second, Beat.Slam, Beat.Charge })
            Assert.That(Next(beat, 0f, Nothing), Is.EqualTo(Beat.Fizzle), beat.ToString());
        Assert.That(Next(Beat.Fizzle, Long, Nothing), Is.EqualTo(Beat.Neutral));
    }

    [Test]
    public void TheNextCastCanBeginDuringRecovery()
    {
        Assert.That(Next(Beat.Recovery, 0f, Gathering()), Is.EqualTo(Beat.First));
        Assert.That(Next(Beat.Fizzle, 0f, Gathering()), Is.EqualTo(Beat.First));
        Assert.That(Next(Beat.Release, 0f, Gathering()), Is.EqualTo(Beat.Release), "but a thrust finishes");
    }

    [Test]
    public void FactsFollowTheCoresCast()
    {
        var cast = new Chem.PlayerCast { First = "fire", Meter = 5, Total = 6 };
        Assert.That(CastFacts.Of(cast, 1, false, false).gathering, Is.True);
        cast.Meter = 1;
        Assert.That(CastFacts.Of(cast, 1, false, false).gathering, Is.False, "a tap's last tick");
        cast.Holding = true;
        cast.Extra = 6;
        cast.Meter = 8;
        Assert.That(CastFacts.Of(cast, 1, false, false).gathering, Is.True, "held, still a tap's length to go");
        cast.Meter = 7;
        Assert.That(CastFacts.Of(cast, 1, false, false).gathering, Is.False, "held past a tap's length");
        Assert.That(CastFacts.Of(null, 1, false, false).casting, Is.False);
    }
}
