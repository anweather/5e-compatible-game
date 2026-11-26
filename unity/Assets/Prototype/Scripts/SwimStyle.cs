// How a body swims in one liquid (SwimPose): how long a stroke takes, how
// far the arms reach and sweep, how hard the legs kick, how far the body
// leans forward into a stroke and how high it rides. Water is the only liquid
// deep enough to swim in today (the cells' water); a thicker liquid gets its
// own entry in ByLiquid (slower strokes, a shorter reach, riding lower).
public readonly struct SwimStyle
{
    public readonly float strokeSeconds, treadSeconds;
    public readonly float reach, sweep;
    public readonly float kick;
    public readonly float lean;
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

    public static readonly SwimStyle Water = new SwimStyle(1.3f, 2.2f, 0.55f, 0.45f, 0.4f, 58f, 0.3f);

    public static SwimStyle For(string liquid) => liquid != null && ByLiquid.TryGetValue(liquid, out var style) ? style : Water;

    static readonly System.Collections.Generic.Dictionary<string, SwimStyle> ByLiquid = new() { ["water"] = Water };
}
