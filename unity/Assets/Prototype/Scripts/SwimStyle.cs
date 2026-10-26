// How a body swims in one liquid (SwimPose): how long a stroke takes, how
// far the arms reach and sweep, how hard the legs kick, how far the body
// leans forward into a stroke and how high it rides. Water is the only liquid
// deep enough to swim in today (the cells' water); a thicker liquid gets its
// own entry in ByLiquid (slower strokes, a shorter reach, riding lower).
public readonly struct SwimStyle
{
    /// <summary>Seconds for one full stroke while swimming along, and one slow scull while treading.</summary>
    public readonly float strokeSeconds, treadSeconds;
    /// <summary>How far forward of the shoulders the hands reach (m), and how wide they sweep (m).</summary>
    public readonly float reach, sweep;
    /// <summary>How far the feet draw up toward the hips at the top of a kick (m).</summary>
    public readonly float kick;
    /// <summary>How far the body leans forward while swimming along (degrees from upright).</summary>
    public readonly float lean;
    /// <summary>How much higher the body rides while stroking (m), so the head stays out.</summary>
    public readonly float lift;

    public SwimStyle(float strokeSeconds, float treadSeconds, float reach, float sweep, float kick, float lean, float lift)
    {
        this.strokeSeconds = strokeSeconds;
        this.treadSeconds = treadSeconds;
        this.reach = reach;
        this.sweep = sweep;
        this.kick = kick;
        this.lean = lean;
        this.lift = lift;
    }

    /// <summary>Breaststroke in water.</summary>
    public static readonly SwimStyle Water = new SwimStyle(1.3f, 2.2f, 0.55f, 0.45f, 0.4f, 58f, 0.3f);

    /// <summary>The style for a liquid by its id (the water every cell holds is "water"); water's for any other.</summary>
    public static SwimStyle For(string liquid) => liquid != null && ByLiquid.TryGetValue(liquid, out var style) ? style : Water;

    static readonly System.Collections.Generic.Dictionary<string, SwimStyle> ByLiquid = new() { ["water"] = Water };
}
