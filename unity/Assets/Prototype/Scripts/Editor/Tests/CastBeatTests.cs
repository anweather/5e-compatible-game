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
    static CastFacts Met(bool holding = false, bool striking = false) => new(true, true, false, holding, false, false, striking);
    static CastFacts Fired(bool thrust = false) => new(false, false, false, false, true, thrust);
    static readonly CastFacts Nothing = new(false, false, false, false, false, false);

    Beat Next(Beat now, float seconds, CastFacts facts) => CastBeat.Next(now, seconds, facts, feel);

    [Test]
    public void ASpellsSizeIsItsLevelAndItsCharge()
    {
        Assert.That(CastImpact.SizeOf(new Chem.SpellDef { Level = 0 }, 1, feel), Is.EqualTo(0f));
        Assert.That(CastImpact.SizeOf(new Chem.SpellDef { Level = 3 }, 3, feel), Is.EqualTo(1f));
        Assert.That(CastImpact.SizeOf(new Chem.SpellDef { Level = 3 }, 1, feel), Is.LessThan(CastImpact.SizeOf(new Chem.SpellDef { Level = 3 }, 2, feel)));
        Assert.That(CastImpact.SizeOf(new Chem.SpellDef { Level = 8 }, 1, feel), Is.EqualTo(1f), "never past full size");
    }

    [Test]
    public void ASingleElementSkipsTheSecondBeat()
    {
        Assert.That(Next(Beat.Neutral, Long, Gathering()), Is.EqualTo(Beat.First));
        Assert.That(Next(Beat.First, Long, Gathering()), Is.EqualTo(Beat.First));
        Assert.That(Next(Beat.First, Long, Met()), Is.EqualTo(Beat.WindUp));
    }

    [Test]
    public void ThePairGoesThroughBothHands()
    {
        Assert.That(Next(Beat.First, 0.1f, Gathering(second: true)), Is.EqualTo(Beat.Second));
        Assert.That(Next(Beat.Second, Long, Met()), Is.EqualTo(Beat.WindUp));
    }

    [Test]
    public void ATapWindsUpThenSlams()
    {
        Assert.That(Next(Beat.WindUp, feel.windUp.seconds / 2f, Met()), Is.EqualTo(Beat.WindUp));
        Assert.That(Next(Beat.WindUp, Long, Met()), Is.EqualTo(Beat.Slam));
    }

    [Test]
    public void ATapOutOfMeterGoesWithoutTheWindUp()
    {
        Assert.That(Next(Beat.Recovery, 0f, Met(striking: true)), Is.EqualTo(Beat.Slam));
        Assert.That(Next(Beat.WindUp, 0f, Met(striking: true)), Is.EqualTo(Beat.Slam));
    }

    [Test]
    public void AHoldNeverWindsUp()
    {
        Assert.That(Next(Beat.First, Long, Met(holding: true)), Is.EqualTo(Beat.Slam));
        Assert.That(Next(Beat.Second, Long, Met(holding: true)), Is.EqualTo(Beat.Slam));
        Assert.That(Next(Beat.Second, Long, Fired()), Is.EqualTo(Beat.Slam));
    }

    [Test]
    public void ALateSecondElementTakesTheWindUpBack() =>
        Assert.That(Next(Beat.WindUp, 0.05f, Gathering(second: true)), Is.EqualTo(Beat.Second));

    [Test]
    public void TheThrustIsTheTapsOnlyForATap()
    {
        float hold = feel.release.seconds + feel.thrustHold, tapHold = feel.tapRelease.seconds + feel.tapThrustHold;
        Assert.That(tapHold, Is.GreaterThan(hold));
        float between = (hold + tapHold) / 2f;
        Assert.That(Next(Beat.Release, between, Nothing), Is.EqualTo(Beat.Recovery), "charged");
        Assert.That(CastBeat.Next(Beat.Release, between, Nothing, feel, tap: true), Is.EqualTo(Beat.Release), "a tap");
    }

    // A shove over the host's cap would be quietly cut online, while a solo punch still looked right.
    [Test]
    public void TheHostTakesTheWholePunch()
    {
        var shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<CastFeel>("Assets/Prototype/Settings/CastFeel.asset");
        Assert.That(shipped.punchShove * Forces.ImpulsePerPush, Is.LessThanOrEqualTo(NetProp.MaxKick));
    }

    // The charged cast Andrew called amazing (J-040): its numbers as they were, in the asset that ships.
    [Test]
    public void TheChargedTuningIsPinned()
    {
        var shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<CastFeel>("Assets/Prototype/Settings/CastFeel.asset");
        foreach (var (name, now, was) in new[]
        {
            ("slam", shipped.slam.seconds, 0.1f), ("slam dip", shipped.slam.dip, 0.05f), ("slam lean", shipped.slam.lean, 6f),
            ("charge", shipped.charge.seconds, 0.25f), ("release", shipped.release.seconds, 0.09f), ("recoil", shipped.release.recoil, 0.35f),
            ("recovery", shipped.recovery.seconds, 0.24f), ("lead", shipped.thrustLead, 0.08f), ("hold", shipped.thrustHold, 0.07f),
            ("slam stop", shipped.slamStop, 0.034f), ("release stop", shipped.releaseStop, 0.05f),
            ("slam kick", shipped.slamKick, 0.05f), ("kick seconds", shipped.slamKickSeconds, 0.14f),
        })
            Assert.That(now, Is.EqualTo(was), name);
        Assert.That(shipped.slamTicks, Is.EqualTo(1));
        Assert.That(shipped.slamRumble.low, Is.EqualTo(0.55f));
        Assert.That(shipped.slamRumble.high, Is.EqualTo(0.35f));
        Assert.That(shipped.slamRumble.seconds, Is.EqualTo(0.1f));
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
        Assert.That(CastFacts.Of(cast, 1, 3, false, false).gathering, Is.True);
        cast.Meter = 3;
        Assert.That(CastFacts.Of(cast, 1, 3, false, false).gathering, Is.False, "a tap's wind-up");
        Assert.That(CastFacts.Of(cast, 1, 3, false, false).striking, Is.False);
        cast.Meter = 1;
        Assert.That(CastFacts.Of(cast, 1, 3, false, false).striking, Is.True, "a tap's last tick");
        cast.Holding = true;
        cast.Extra = 6;
        cast.Meter = 8;
        Assert.That(CastFacts.Of(cast, 1, 3, false, false).gathering, Is.True, "held, still a tap's length to go");
        cast.Meter = 7;
        Assert.That(CastFacts.Of(cast, 1, 3, false, false).gathering, Is.False, "held past a tap's length");
        Assert.That(CastFacts.Of(cast, 1, 3, false, false).striking, Is.False, "a hold never strikes early");
        Assert.That(CastFacts.Of(null, 1, 3, false, false).casting, Is.False);
    }
}
