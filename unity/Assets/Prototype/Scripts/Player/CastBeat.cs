using Chem;

// The beats of a cast, worked out from what every peer knows, so another
// player's wizard goes through the same ones (docs/SPELL_COMBOS.md, "Beats").
// Only the rules here, with no Unity in them but the tuning: CastBeats plays them.
public enum Beat
{
    Neutral,
    First,
    Second,
    WindUp,
    Slam,
    Charge,
    Release,
    Recovery,
    Fizzle,
}

public readonly struct CastFacts
{
    public readonly bool casting;
    public readonly bool second;
    public readonly bool gathering;
    public readonly bool holding;
    public readonly bool fired;
    public readonly bool thrust;
    public readonly bool striking;

    public CastFacts(bool casting, bool second, bool gathering, bool holding, bool fired, bool thrust, bool striking = false)
    {
        this.casting = casting;
        this.second = second;
        this.gathering = gathering;
        this.holding = holding;
        this.fired = fired;
        this.thrust = thrust;
        this.striking = striking;
    }

    public static CastFacts Of(PlayerCast cast, int slamTicks, int windUpTicks, bool fired, bool thrust)
    {
        if (cast == null || !cast.Casting) return new CastFacts(false, false, false, false, fired, thrust);
        bool gathering = cast.Holding ? !cast.Charging : cast.Meter > windUpTicks;
        return new CastFacts(true, cast.Second != null, gathering, cast.Holding, fired, thrust, !cast.Holding && cast.Meter <= slamTicks);
    }

    public CastFacts With(bool fired, bool thrust) => new(casting, second, gathering, holding, fired, thrust, striking);
}

public static class CastBeat
{
    public static Beat Next(Beat now, float seconds, in CastFacts facts, CastFeel feel, bool tap = false)
    {
        bool done = seconds >= feel.Of(now, tap).seconds;
        if (now == Beat.Release && seconds < feel.Of(now, tap).seconds + feel.HoldOf(tap)) return now;
        if (facts.thrust) return now == Beat.Charge || (now == Beat.Slam && done) ? Beat.Release : Beat.Slam;
        if (facts.fired) return now == Beat.Charge ? now : Beat.Slam;
        return facts.casting ? Casting(now, done, facts) : Ended(now, done);
    }

    static Beat Casting(Beat now, bool done, in CastFacts facts)
    {
        if (facts.gathering) return facts.second ? Beat.Second : Beat.First;
        if (now == Beat.Charge) return now;
        if (now == Beat.Slam) return done && facts.holding ? Beat.Charge : now;
        if (facts.holding || facts.striking) return Beat.Slam;
        return now == Beat.WindUp && done ? Beat.Slam : Beat.WindUp;
    }

    static Beat Ended(Beat now, bool done) =>
        now switch
        {
            Beat.First or Beat.Second or Beat.WindUp or Beat.Slam or Beat.Charge => Beat.Fizzle,
            Beat.Release => Beat.Recovery,
            Beat.Recovery or Beat.Fizzle when done => Beat.Neutral,
            _ => now,
        };
}
