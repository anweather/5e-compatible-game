using System;
using System.Collections.Generic;
using UnityEngine;

// The climbing tower south-west of the yard (ValleyTower builds it;
// docs/BACKLOG.md 16): a stone column ringed by a spiral of platforms
// (TowerPlatform), a start pad with a sign at its foot and a flag on top.
//
// The course's clock is shared: the chemistry's tick (World.Tick, which
// NetChem's lockstep advances online and ChemGrid solo) in seconds, smoothed
// between ticks, so moving and phasing platforms stand in the same place on
// every machine without any networking of their own.
//
// The local wizard's climb is timed from stepping off the start pad to
// reaching the flag; arriving sets off fireworks (TowerFireworks) and logs the
// time, with the best of the session.
[DefaultExecutionOrder(-40)]
public class TowerCourse : MonoBehaviour
{
    public ChemGrid grid;
    public TowerPlatform[] platforms;
    [Tooltip("The middle of the start pad's top, and its radius (m).")]
    public Vector3 startPad;
    public float padRadius = 1.6f;
    [Tooltip("The middle of the summit's top (the flag's foot), and how near the flag counts as there (m).")]
    public Vector3 summit;
    public float finishRadius = 3.5f;
    [Tooltip("Where the fireworks burst round.")]
    public Vector3 fireworksAt;

    // The clock drifts toward the shared one; past this far off (s) it jumps to it.
    const double SnapAfter = 0.5;
    const float FindEvery = 1f;

    static float best = -1f;

    double clock;
    int clockFrame = -1;
    CharacterController local;
    readonly List<Transform> wizards = new();
    float nextFind, startedAt = -1f;
    bool onPad, atTop;
    GUIStyle style;

    /// <summary>The course's shared clock (s): the chemistry's tick in seconds, smoothed.</summary>
    public double Now
    {
        get
        {
            Advance();
            return clock;
        }
    }

    /// <summary>The local player's wizard, carried by the moving platforms it stands on; null if none.</summary>
    public CharacterController LocalRider => local;

    /// <summary>The best climb this session (s), or below 0 before the first.</summary>
    public static float Best => best;

    /// <summary>Whether a climb is being timed now.</summary>
    public bool Timing => startedAt >= 0f;

    void Advance()
    {
        if (clockFrame == Time.frameCount) return;
        clockFrame = Time.frameCount;
        clock += Time.deltaTime;
        if (grid == null || grid.World == null) return;
        var world = grid.World;
        double shared = world.Tick / (double)Mathf.Max(1, world.Rules.Sim.TicksPerSecond);
        double drift = shared - clock;
        if (Math.Abs(drift) > SnapAfter) clock = shared;
        else clock += drift * Mathf.Min(1f, Time.deltaTime * 2f);
    }

    void Update()
    {
        Advance();
        if (Time.time >= nextFind) FindWizards();
        if (local == null) return;
        var feet = local.transform.position;
        Pad(feet);
        Top(feet);
    }

    // Every wizard (the local one and other players' puppets) can set off a collapsing platform.
    void FindWizards()
    {
        nextFind = Time.time + FindEvery;
        wizards.Clear();
        local = null;
        foreach (var body in FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
        {
            if (!body.name.StartsWith(WizardRig.Name, StringComparison.Ordinal)) continue;
            wizards.Add(body.transform);
            if (body.name == WizardRig.Name) local = body;
        }
    }

    /// <summary>Whether any wizard stands on a platform.</summary>
    public bool AnyWizardOn(TowerPlatform platform)
    {
        foreach (var w in wizards)
            if (w != null && platform.StandingOn(w.position)) return true;
        return false;
    }

    // On the pad the clock is ready; stepping off starts it.
    void Pad(Vector3 feet)
    {
        bool on = Flat(feet - startPad) < padRadius && Mathf.Abs(feet.y - startPad.y) < 1f;
        if (on && !onPad)
        {
            startedAt = -1f;
            EventLog.Add(best >= 0f
                ? $"The tower: climb to the flag. Your best is {Clock(best)}"
                : "The tower: climb to the flag at the top. The clock starts when you step off the pad");
        }
        if (!on && onPad) startedAt = Time.time;
        onPad = on;
    }

    // Reaching the flag finishes the climb; it can finish again once the wizard has gone back down.
    void Top(Vector3 feet)
    {
        bool there = Flat(feet - summit) < finishRadius && feet.y > summit.y - 0.5f && feet.y < summit.y + 4f;
        if (there && !atTop)
        {
            atTop = true;
            Finish();
        }
        else if (atTop && feet.y < summit.y - 3f) atTop = false;
    }

    void Finish()
    {
        TowerFireworks.Launch(fireworksAt);
        if (startedAt < 0f)
        {
            EventLog.Add("You reached the top!");
            return;
        }
        float took = Time.time - startedAt;
        startedAt = -1f;
        bool record = Record(took);
        EventLog.Add($"You reached the top in {Clock(took)}!" + (record ? " Your best yet" : $" (best {Clock(best)})"));
    }

    static bool Record(float seconds)
    {
        if (best >= 0f && seconds >= best) return false;
        best = seconds;
        return true;
    }

    /// <summary>A time as minutes and seconds, "1:23".</summary>
    public static string Clock(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    // The climb's time, top middle, while it runs.
    void OnGUI()
    {
        if (!Timing || !EventLog.HudVisible) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, richText = true };
        GUI.Label(new Rect(Screen.width / 2f - 120f, 12f, 240f, 40f), $"<b>{Clock(Time.time - startedAt)}</b>", style);
    }
}
