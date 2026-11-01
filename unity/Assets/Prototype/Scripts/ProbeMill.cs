using System.Collections;
using Punchy.Fauna;
using Unity.Mathematics;
using UnityEngine;

// How an animal got on over a stretch of being cornered, sampled a frame at a time: enough to
// tell milling about from standing frozen and from jittering on the spot (CreatureBrain's Flee).
public sealed class MillWatch
{
    /// <summary>Below this an animal is stood still, m/s; turns slower than this (rad/s) don't count as turns.</summary>
    const float StoodStill = 0.3f, Turning = 0.3f;
    /// <summary>What milling about takes: on the move this share of the time and no more than this many reversals a second.</summary>
    const float LeastMoving = 0.25f, MostFlipsPerSecond = 2f;

    float3 last;
    float lastTurn, moving;
    bool started;

    public float Seconds { get; private set; }
    /// <summary>Metres walked.</summary>
    public float Walked { get; private set; }
    /// <summary>How many times its turning swung from one way to the other.</summary>
    public int Flips { get; private set; }
    public float MovingShare => Seconds > 0f ? moving / Seconds : 0f;
    public float FlipsPerSecond => Seconds > 0f ? Flips / Seconds : 0f;
    public bool Mills => MovingShare >= LeastMoving && FlipsPerSecond <= MostFlipsPerSecond;
    public string Summary => $"moving {MovingShare:P0} of {Seconds:0.0} s, walked {Walked:0.0} m, {FlipsPerSecond:0.0} reversals a second";

    public void Sample(in CreatureState s, float dt)
    {
        if (started) Walked += math.distance(last.xz, s.Position.xz);
        last = s.Position;
        started = true;
        Seconds += dt;
        if (math.abs(s.Speed) > StoodStill) moving += dt;
        if (math.abs(s.YawRate) < Turning) return;
        if (lastTurn * s.YawRate < 0f) Flips++;
        lastTurn = s.YawRate;
    }
}

// A corner of the real valley to back an animal into: walkable, walled in (trees, water, rock,
// the coast) on most sides, with a clear way in. Found on the land itself, nearest first.
public readonly struct MillCorner
{
    const int Rays = 16;
    const float Step = 1f, RoomToStand = 1f, WallWithin = 4f, BackWithin = 7f, OpenPast = 9f, SideOfTheWayOut = 0.35f;
    const int MostOpen = 5, LeastWalled = 7;

    public readonly float2 At;
    /// <summary>The way out, a unit direction.</summary>
    public readonly float2 Open;
    public readonly int Walled;

    MillCorner(float2 at, float2 open, int walled)
    {
        At = at;
        Open = open;
        Walled = walled;
    }

    public static bool Find(ICreatureGround ground, float2 around, float range, out MillCorner best)
    {
        best = default;
        bool found = false;
        float bestScore = float.MaxValue;
        for (float z = -range; z <= range; z += Step)
            for (float x = -range; x <= range; x += Step)
            {
                var at = around + new float2(x, z);
                if (!ground.Walkable(at) || !Look(ground, at, out var open, out int walled)) continue;
                float score = math.length(new float2(x, z)) - 40f * walled;
                if (score >= bestScore) continue;
                bestScore = score;
                best = new MillCorner(at, open, walled);
                found = true;
            }
        return found;
    }

    // A dead end: nothing but wall behind and to the sides, one way out in front.
    static bool Look(ICreatureGround ground, float2 at, out float2 open, out int walled)
    {
        open = default;
        walled = 0;
        int clear = 0;
        var free = new float[Rays];
        for (int r = 0; r < Rays; r++)
        {
            var dir = Ray(r);
            free[r] = Free(ground, at, dir);
            if (free[r] < RoomToStand) return false;
            if (free[r] < WallWithin) walled++;
            else if (free[r] >= OpenPast)
            {
                clear++;
                open += dir;
            }
        }
        if (clear < 1 || clear > MostOpen || walled < LeastWalled) return false;
        open = math.normalizesafe(open);
        for (int r = 0; r < Rays; r++)
            if (math.dot(Ray(r), open) < SideOfTheWayOut && free[r] >= BackWithin) return false;
        return true;
    }

    static float2 Ray(int r)
    {
        float a = r * math.PI * 2f / Rays;
        return new float2(math.sin(a), math.cos(a));
    }

    static float Free(ICreatureGround ground, float2 at, float2 dir)
    {
        for (float d = 1f; d <= OpenPast; d += 1f)
            if (!ground.Walkable(at + dir * d)) return d - 1f;
        return OpenPast;
    }
}

// The probe's millclip scenario: for Andrew's board, a film of an animal with nowhere left to run
// (CreatureBrain's Flee), same corner, same camera, each run. Three deer backed into a corner of
// the valley by the wizard, then two deer on a leash in the open with the wizard at their home.
// Each take's watch goes in as metrics.
public class MillClipProbe : ClipProbe
{
    const float TakeSeconds = 12f, FirstSeconds = 2f, LeashMetres = 7f;

    protected override IEnumerator Run()
    {
        var fauna = FindAnyObjectByType<ValleyFauna>();
        Check("fauna_built", fauna != null, "ValleyFauna");
        if (fauna == null || string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", ScreenshotDir != null, "millclip films into --shots <folder>");
            yield break;
        }
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var w = wizard.transform.position;
        if (!MillCorner.Find(fauna, new float2(w.x, w.z), MillPlaces.SearchRange, out var corner))
        {
            Check("corner_found", false, "no corner in reach");
            yield break;
        }
        Check("corner_found", true, $"at {corner.At} walled on {corner.Walled} of 16 sides, open towards {corner.Open}");

        StartFilm();
        yield return Corner(fauna, corner);
        yield return Leash(fauna);
        yield return EndFilm();
    }

    // The wizard in the corner's way out, the camera to one side above it.
    IEnumerator Corner(ValleyFauna fauna, MillCorner corner)
    {
        var stand = MillPlaces.Stand(grid, corner);
        Teleport(stand);
        puppet.Face(Ground(corner.At));
        MillPlaces.Film(grid, corner, out var eye, out var look);
        Cut(eye, look);
        int deer = fauna.SpeciesIndex("Deer");
        var herd = new[] { Spawn(fauna, deer, corner.At, corner.Open), Spawn(fauna, deer, corner.At + corner.Open.yx * 1.5f, corner.Open), Spawn(fauna, deer, corner.At - corner.Open.yx * 1.5f, corner.Open) };
        yield return Wait(FirstSeconds);
        yield return Watch(fauna, herd, "corner");
    }

    // Open ground: two deer leashed to a spot, the wizard just outside the leash.
    IEnumerator Leash(ValleyFauna fauna)
    {
        var home = MillPlaces.Meadow;
        var at = new Vector3(home.x, grid.GroundAt(new Vector3(home.x, 0f, home.y)), home.y);
        Teleport(at + new Vector3(-LeashMetres * 1.5f, 0.1f, 0f));
        Cut(at + new Vector3(0f, 7f, -16f), at);
        int deer = fauna.SpeciesIndex("Deer");
        var herd = new[] { Spawn(fauna, deer, home, new float2(1f, 0f)), Spawn(fauna, deer, home + new float2(1.5f, 2f), new float2(1f, 0f)) };
        foreach (int id in herd) fauna.Leash(fauna.IndexOf(id), home, LeashMetres);
        yield return Wait(FirstSeconds);
        yield return Watch(fauna, herd, "leash");
    }

    IEnumerator Watch(ValleyFauna fauna, int[] herd, string take)
    {
        var watches = new MillWatch[herd.Length];
        for (int k = 0; k < herd.Length; k++) watches[k] = new MillWatch();
        for (float t = 0f; t < TakeSeconds; t += Time.deltaTime)
        {
            yield return null;
            for (int k = 0; k < herd.Length; k++) watches[k].Sample(fauna.State(fauna.IndexOf(herd[k])), Time.deltaTime);
        }
        for (int k = 0; k < herd.Length; k++)
        {
            Metric($"{take}_{k}_moving_share", watches[k].MovingShare);
            Metric($"{take}_{k}_walked_m", watches[k].Walked);
            Metric($"{take}_{k}_flips_per_s", watches[k].FlipsPerSecond);
        }
    }

    int Spawn(ValleyFauna fauna, int species, float2 at, float2 facing) =>
        fauna.Id(fauna.Spawn(species, Ground(at), math.atan2(-facing.x, -facing.y)));

    Vector3 Ground(float2 at)
    {
        var p = new Vector3(at.x, 0f, at.y);
        p.y = grid.GroundAt(p);
        return p;
    }
}

// Where the mill scenarios stand and look, shared by fauna and millclip.
public static class MillPlaces
{
    public const float SearchRange = 160f;
    const float WizardOut = 7f;

    /// <summary>Open meadow east of the yard, nothing to run into for a leash's length.</summary>
    public static readonly float2 Meadow = new(27f, 26f);

    /// <summary>Where the wizard stands to back an animal into a corner: in its way out.</summary>
    public static Vector3 Stand(ChemGrid grid, MillCorner corner)
    {
        var at = corner.At + corner.Open * WizardOut;
        var p = new Vector3(at.x, 0f, at.y);
        p.y = grid.GroundAt(p) + 0.1f;
        return p;
    }

    /// <summary>A camera to one side of the way in, above the wizard, on the corner.</summary>
    public static void Film(ChemGrid grid, MillCorner corner, out Vector3 eye, out Vector3 look)
    {
        look = new Vector3(corner.At.x, 0f, corner.At.y);
        look.y = grid.GroundAt(look) + 0.5f;
        var back = corner.At + corner.Open * 13f + corner.Open.yx * 4f;
        eye = new Vector3(back.x, 0f, back.y);
        eye.y = grid.GroundAt(eye) + 5.5f;
    }
}
