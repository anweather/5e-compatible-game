using Chem;

// The beats of a cast, worked out from what every peer knows, so another
// player's wizard goes through the same ones (docs/SPELL_COMBOS.md, "Beats").
// Only the rules here, with no Unity in them but the tuning: CastBeats plays them.
public enum Beat
{
    /// <summary>No cast: the arms are the animation's.</summary>
    Neutral,
    /// <summary>The first element gathers at the right hand, swung down and back. A d-pad cast starts here too.</summary>
    First,
    /// <summary>The second gathers at the left, arms wide. A single element skips it.</summary>
    Second,
    /// <summary>The hands snap together and stay there until the spell leaves them.</summary>
    Slam,
    /// <summary>Held: hands pressed together, knees bent, the energy growing.</summary>
    Charge,
    /// <summary>The hands thrust at the aim and the body recoils, as the spell leaves them.</summary>
    Release,
    /// <summary>Back to neutral, quickly; a new cast cuts it short.</summary>
    Recovery,
    /// <summary>The cast was dropped or came to nothing: the hands fall.</summary>
    Fizzle,
}

/// <summary>What a beat depends on, at one moment.</summary>
public readonly struct CastFacts
{
    /// <summary>The cast meter is running.</summary>
    public readonly bool casting;
    /// <summary>A second element is loaded.</summary>
    public readonly bool second;
    /// <summary>Elements are still gathering: too much meter left for the hands to meet.</summary>
    public readonly bool gathering;
    /// <summary>The button is still down.</summary>
    public readonly bool holding;
    /// <summary>The core set this wizard's cast off and it hasn't left the hands yet.</summary>
    public readonly bool fired;
    /// <summary>It's time for the hands to thrust.</summary>
    public readonly bool thrust;

    public CastFacts(bool casting, bool second, bool gathering, bool holding, bool fired, bool thrust)
    {
        this.casting = casting;
        this.second = second;
        this.gathering = gathering;
        this.holding = holding;
        this.fired = fired;
        this.thrust = thrust;
    }

    /// <summary>The facts from a player's cast in the core (null with none yet).</summary>
    public static CastFacts Of(PlayerCast cast, int slamTicks, bool fired, bool thrust)
    {
        if (cast == null || !cast.Casting) return new CastFacts(false, false, false, false, fired, thrust);
        bool gathering = cast.Holding ? !cast.Charging : cast.Meter > slamTicks;
        return new CastFacts(true, cast.Second != null, gathering, cast.Holding, fired, thrust);
    }

    /// <summary>The same cast with the firing as it stands now.</summary>
    public CastFacts With(bool fired, bool thrust) => new(casting, second, gathering, holding, fired, thrust);
}

public static class CastBeat
{
    /// <summary>The beat after <paramref name="now"/>, <paramref name="seconds"/> into it, given the facts; the same beat to stay in it.</summary>
    public static Beat Next(Beat now, float seconds, in CastFacts facts, CastFeel feel)
    {
        bool done = seconds >= feel.Of(now).seconds;
        if (now == Beat.Release && seconds < feel.release.seconds + feel.thrustHold) return now;
        // Never skipped: a hitch can bring the firing and its thrust in one frame, and every peer must show the slam.
        if (facts.thrust) return now == Beat.Charge || (now == Beat.Slam && done) ? Beat.Release : Beat.Slam;
        if (facts.fired) return now == Beat.Charge ? now : Beat.Slam;
        return facts.casting ? Casting(now, done, facts) : Ended(now, done);
    }

    static Beat Casting(Beat now, bool done, in CastFacts facts)
    {
        if (facts.gathering) return facts.second ? Beat.Second : Beat.First;
        if (now == Beat.Charge) return now;
        return now == Beat.Slam && done && facts.holding ? Beat.Charge : Beat.Slam;
    }

    static Beat Ended(Beat now, bool done) =>
        now switch
        {
            Beat.First or Beat.Second or Beat.Slam or Beat.Charge => Beat.Fizzle,
            Beat.Release => Beat.Recovery,
            Beat.Recovery or Beat.Fizzle when done => Beat.Neutral,
            _ => now,
        };
}
