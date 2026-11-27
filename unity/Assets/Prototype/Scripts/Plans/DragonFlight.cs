using Chem;
using UnityEngine;

// Where the dragon is drawn, as a sum on the core's flight (World.Dragon): the view
// keeps no flight of its own, so every peer sees the same dragon, a late joiner too.
// The core says what the breath does; everything else here is the show, in cells and
// metres above the ground. The sim runs at 6 ticks a second, so a pose takes the
// fraction of a tick gone by, and the breath's lead slides between ticks.
//
// The warning has it high over the valley once (its shadow crossing), out of sight,
// then diving out of the clouds onto the first pass's line. On a pass it flies
// just behind the breath's lead, down the middle of the strip; between passes it
// wheels round in a half circle past the valley's edge. Leaving, it flies to the
// highest ground in the valley, stands there and roars, and climbs away.
//
// Its size is sim.json's dragon.scale, read each time: a bigger dragon flies higher and
// further behind its fire in step, so what's drawn fits at any scale. It doesn't reach
// further: the breath is the core's (World.BreathBox), whatever size it's drawn. Some turns
// it throws in a barrel roll, by the flight's hash: the same on every peer.
public static class DragonFlight
{
    /// <summary>Metres above the ground in the high crossing, and in the clouds (where it comes and goes).</summary>
    public const float High = 280f, Clouds = 720f;
    /// <summary>The model's wingspan and height on a pass at scale 1, in metres.</summary>
    public const float SpanAtOne = 70f, CruiseAtOne = 70f;

    // How far off the edge the crossing starts and ends, and how far the dive runs along the line, in cells.
    const float CrossFar = 200f, DiveRun = 260f;
    // At scale 1, the dragon flies this many cells behind the breath's lead: the fire goes ahead of it, down from its mouth.
    const float BehindAtOne = 60f;
    // Parts of the warning: crossing from and to, and the dive from there to its end.
    const float CrossFrom = 0.15f, CrossTo = 0.6f, DiveFrom = 0.78f;
    // Parts of leaving: at the perch, then away.
    const float PerchFrom = 0.42f, PerchTo = 0.75f;
    // How much of the warning the dark takes to come on.
    const float DarkOver = 0.6f;
    // The part of a turn a barrel roll takes, and one in how many turns has none.
    const float RollFrom = 0.2f, RollTo = 0.8f;
    const uint RollSalt = 0xB0A7u, NoRollOneIn = 3u;

    /// <summary>A moment of the flight, for the view.</summary>
    public readonly struct Pose
    {
        public Pose(Vector2 cell, float lift, bool seen, bool perched = false, float lead = 0f, float roll = 0f)
        {
            Cell = cell;
            Lift = lift;
            Seen = seen;
            Perched = perched;
            Lead = lead;
            Roll = roll;
        }

        /// <summary>Where, in cells (fractions of one too).</summary>
        public Vector2 Cell { get; }
        /// <summary>Metres above the ground.</summary>
        public float Lift { get; }
        /// <summary>In sight: out of the clouds.</summary>
        public bool Seen { get; }
        /// <summary>Stood on the mountain, roaring.</summary>
        public bool Perched { get; }
        /// <summary>On a pass: the cell along the pass the breath's lead has reached, slid between ticks.</summary>
        public float Lead { get; }
        /// <summary>Degrees through a barrel roll.</summary>
        public float Roll { get; }
    }

    /// <summary>Wing tip to wing tip as drawn, in metres.</summary>
    public static float Wingspan(DragonParams p) => SpanAtOne * (float)p.Scale;

    /// <summary>Metres above the ground on a pass: the bigger the dragon, the higher, so it fits over the valley as it did.</summary>
    public static float Cruise(DragonParams p) => CruiseAtOne * (float)p.Scale;

    /// <summary>Cells it flies behind the breath's lead on a pass.</summary>
    public static float Behind(DragonParams p) => BehindAtOne * (float)p.Scale;

    /// <summary>How dark the dragon makes the sky, 0..1: it comes on through the warning, and lifts as it leaves.</summary>
    public static float Darkness(World world)
    {
        var d = world.Dragon;
        if (d == null) return 0f;
        var p = world.Rules.Sim.Dragon;
        return d.Phase switch
        {
            DragonPhase.Warning => Mathf.SmoothStep(0f, 1f, Part(world, d, p.WarningTicks) / DarkOver),
            DragonPhase.Leaving => 1f - Mathf.SmoothStep(0f, 1f, Part(world, d, p.LeavingTicks)),
            DragonPhase.Passes => 1f,
            _ => 0f,
        };
    }

    /// <summary>The dragon at this moment. <paramref name="perch"/> is the cell it lands on as it leaves.</summary>
    public static Pose At(World world, float tickFraction, Vector2 perch)
    {
        var d = world.Dragon;
        if (d == null) return default;
        var p = world.Rules.Sim.Dragon;
        float length = d.AlongX ? world.W : world.H, wide = d.AlongX ? world.H : world.W;
        Vector2 Cell(float along, float across) => d.AlongX ? new Vector2(along, across) : new Vector2(across, along);

        switch (d.Phase)
        {
            case DragonPhase.Warning:
                return Warning(world, d, p, tickFraction, length, wide, Cell);
            case DragonPhase.Passes:
                if (d.Until > 0) return Wheel(world, d, p, tickFraction, length, Cell);
                var (middle, back) = world.PassLine(d, d.Pass);
                float lead = Mathf.Min(d.Travelled + tickFraction * p.Speed, length + p.Depth);
                float at = back ? length - lead : lead;
                return new Pose(Cell(at + (back ? Behind(p) : -Behind(p)), middle), Cruise(p), true, lead: at);
            default:
                return Leaving(world, d, p, tickFraction, length, perch, Cell);
        }
    }

    // How far through a phase of this many ticks the flight is, 0..1, on the fraction of a tick too.
    static float Part(World world, DragonState d, int ticks, float tickFraction = 0f) =>
        ticks <= 0 ? 1f : Mathf.Clamp01(1f - (d.Until - world.Tick - tickFraction) / ticks);

    static Pose Warning(World world, DragonState d, DragonParams p, float tickFraction, float length, float wide, System.Func<float, float, Vector2> cell)
    {
        float t = Part(world, d, p.WarningTicks, tickFraction);
        var (middle, back) = world.PassLine(d, 0);
        float start = back ? length : 0f, way = back ? -1f : 1f;
        if (t >= DiveFrom)
        {
            float s = (t - DiveFrom) / (1f - DiveFrom);
            float along = start + way * (Mathf.Lerp(-DiveRun, 0f, s) - Behind(p));
            return new Pose(cell(along, Mathf.Lerp(wide / 2f, middle, s * s)), Mathf.Lerp(Clouds, Cruise(p), 1f - (1f - s) * (1f - s)), true);
        }
        if (t < CrossFrom || t >= CrossTo) return new Pose(cell(start, middle), Clouds, false);
        float c = (t - CrossFrom) / (CrossTo - CrossFrom);
        return new Pose(cell(Mathf.Lerp(start - way * CrossFar, start + way * (length + CrossFar), c), wide / 2f), High, true);
    }

    // Round the end of a pass, from the last one's line to this one's, a half circle past the valley's edge.
    static Pose Wheel(World world, DragonState d, DragonParams p, float tickFraction, float length, System.Func<float, float, Vector2> cell)
    {
        float t = Part(world, d, Mathf.Max(1, p.TurnTicks), tickFraction);
        var (from, wasBack) = world.PassLine(d, d.Pass - 1);
        var (to, back) = world.PassLine(d, d.Pass);
        float end = wasBack ? -p.Depth + Behind(p) : length + p.Depth - Behind(p);
        float start = back ? length + Behind(p) : -Behind(p);
        float round = Mathf.Max(30f, Mathf.Abs(to - from) / 2f);
        float along = Mathf.Lerp(end, start, t) + (wasBack ? -round : round) * Mathf.Sin(Mathf.PI * t);
        float across = Mathf.Lerp(from, to, 0.5f - 0.5f * Mathf.Cos(Mathf.PI * t));
        return new Pose(cell(along, across), Cruise(p), true, roll: RollAt(world, d, t));
    }

    // Degrees through this turn's barrel roll, t being how far round the turn is; which way, and whether at all, from the flight's hash.
    static float RollAt(World world, DragonState d, float t)
    {
        uint h = Sky.Hash(world.Seed, d.Pass, RollSalt + (uint)world.DragonFlights);
        if (h % NoRollOneIn == 0u) return 0f;
        return Mathf.SmoothStep(0f, (h & 8u) == 0u ? 360f : -360f, Mathf.InverseLerp(RollFrom, RollTo, t));
    }

    static Pose Leaving(World world, DragonState d, DragonParams p, float tickFraction, float length, Vector2 perch, System.Func<float, float, Vector2> cell)
    {
        float t = Part(world, d, p.LeavingTicks, tickFraction);
        var (middle, back) = world.PassLine(d, d.Pass);
        var from = cell(back ? -p.Depth + Behind(p) : length + p.Depth - Behind(p), middle);
        if (t < PerchFrom)
        {
            float s = Mathf.SmoothStep(0f, 1f, t / PerchFrom);
            return new Pose(Vector2.Lerp(from, perch, s), Mathf.Lerp(Cruise(p), 0f, s * s), true);
        }
        if (t < PerchTo) return new Pose(perch, 0f, true, true);
        float up = (t - PerchTo) / (1f - PerchTo);
        return new Pose(perch + (perch - from).normalized * (up * up * DiveRun), up * up * Clouds, up < 0.98f);
    }
}
