using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's dragon scenario (docs/BACKLOG.md 26): one whole flight, staged faster than a
// real one by turning sim.json's dragon up (a short warning, quicker passes; the real
// numbers are the ones `perf` and the player measure). R summons it; the sky darkens and the
// dragon is seen; a wizard in the open is knocked down and one behind a built wall isn't;
// every chunk comes back: its cells, the Gauntlet's lava, a felled tree, a thing that broke
// and a thing moved. It ends with what the flight cost in a view of the breath against
// the same view clear, and the size of the starting state. And the fit: how big the dragon is
// drawn against sim.json's dragon.scale, what its shadow and the stream of fire do, and that
// what's drawn stays on the core's breath. The stream is checked in the picture: its line,
// frame by frame, with the stream drawn and left out, side on and from below. Last, the Reset
// button on a virtual pad: a tap calls the dragon, and holding it rebuilds the valley.
public class DragonProbe : ProbeScenario
{
    const float MostSeconds = 200f, LookSeconds = 4f;
    // Sim params for a staged flight: warning, turns, leaving, and a pass at three times the speed.
    const int ShortWarning = 60, ShortTurn = 12, ShortLeaving = 60, QuickSpeed = 18, QuickChunks = 2;
    // Cells ahead of the lead where a wizard waits for the breath.
    const int Ahead = 70;
    // A cell of the Gauntlet's lava pit (world/gauntlet.json) and of the wizard's meadow, to wreck.
    const int LavaX = 303, LavaY = 433;
    // Points read along the stream's line; a bigger sum of red and green over a little box than with no stream counts as lit.
    const int StreamPoints = 17, StreamBox = 4, StreamBrighter = 40;
    // The part of those points that must be lit, in a frame with the stream drawn.
    const float StreamLitPart = 0.75f;
    // Frames a watcher lets the view lag the core's tick by; seconds the last drops may fly on.
    const int LagFrames = 2;
    const float DropsLand = 1.2f;
    // How far off the ground the view from below stands: clear of the trees.
    const float BelowHeight = 35f, SideOn = 330f;
    const int Placings = 300;
    // A tap of the Reset button, how long it's held to rebuild (EventLog), and how long the reload may take beyond that.
    const float TapSeconds = 0.25f, RebuildSeconds = 3f, LoadSeconds = 30f;

    DragonView view;
    SkyLight sky;
    Vitals vitals;
    ThingSpawner spawner;
    readonly List<ChunkFresh> freshened = new();
    readonly List<float> lookFrames = new();
    Vector3 lookEye, lookAt;
    int realSpeed;
    float fastest;
    bool open, sheltered;
    int breathingFrames, litFrames, turningFrames, leavingFrames, litWhenNot, drawnAfter;
    float leastPassGap = float.PositiveInfinity, leastTurnGap = float.PositiveInfinity;
    float belowBody, aboveBody, mostRoll;
    int framesOff;
    float streamStopped = -1f;
    bool staging;

    protected override IEnumerator Run()
    {
        view = FindAnyObjectByType<DragonView>();
        sky = FindAnyObjectByType<SkyLight>();
        vitals = wizard.GetComponent<Vitals>();
        spawner = FindAnyObjectByType<ThingSpawner>();
        Check("view_in_scene", view != null, "the yard has a DragonView (rebuild the yard if not)");
        if (view == null) yield break;
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        Measured();
        var p = World.Rules.Sim.Dragon;
        realSpeed = p.Speed;
        int realWarning = p.WarningTicks;
        (p.WarningTicks, p.TurnTicks, p.LeavingTicks, p.Speed, p.ChunksPerTick) = (ShortWarning, ShortTurn, ShortLeaving, QuickSpeed, QuickChunks);

        var damage = new Damage(this);
        yield return damage.Do();
        grid.ChunkFreshened += freshened.Add;
        try
        {
            yield return Flight();
        }
        finally
        {
            grid.ChunkFreshened -= freshened.Add;
        }
        damage.Checked();
        Check("no_dragon_after", World.Dragon == null && World.DragonFlights == 1, $"flights {World.DragonFlights}");
        p.WarningTicks = realWarning;
        yield return ResetButton();
        Check("no_console_errors", Result.Console.ErrorCount == 0, $"{Result.Console.ErrorCount} errors");
    }

    // The starting state's size on this map, and how long it took.
    void Measured()
    {
        Check("world_keeps_a_start", World.HasStart, "ChemGrid.StartWorld");
        Metric("keep_start_ms", ChemGrid.KeepStartMs);
        Metric("palette", World.StartShapes);
        Metric("cells", World.W * World.H);
        Metric("chunks", World.ChunkCoords.Count());
        Metric("changed_at_the_start", World.ChangedCount);
        // CellBytes each in the palette (Cell.cs's fields and an object's header), and an int a cell for the index.
        Metric("start_state_kb", (World.StartShapes * CellBytes + World.W * World.H * sizeof(int)) / 1024.0);
        int deep = World.Cells.Count(c => c.Water >= World.Rules.Sim.Dragon.ShelterWater && !c.Frozen);
        Metric("cells_deep_enough_to_shelter", deep);
    }

    const int CellBytes = 120;

    IEnumerator Flight()
    {
        float sunBefore = sky.sun.intensity, ambientBefore = RenderSettings.ambientIntensity;
        int mark = ConsoleMark;
        EventLog.ResetYard();
        Check("r_summons_it", World.Dragon?.Phase == DragonPhase.Warning, $"phase {World.Dragon?.Phase}, flights {World.DragonFlights}");
        float started = Time.time;
        yield return ThroughTheWarning(started);
        Check("sky_darkens", sky.Menace > 0.3f && sky.sun.intensity < sunBefore * 0.9f, $"menace {sky.Menace:0.00}, sun {sunBefore:0.00} to {sky.sun.intensity:0.00}");
        Metric("sun_before", sunBefore);
        Metric("sun_in_the_warning", sky.sun.intensity);
        Metric("ambient_before", ambientBefore);

        StartCoroutine(WatchTheFlight());
        StartCoroutine(WatchThePerch());
        StartCoroutine(WatchTheStream());
        StartCoroutine(WatchTheFit());
        int staged = -1;
        while (World.Dragon != null && Time.time - started < MostSeconds)
        {
            var d = World.Dragon;
            if (d.Phase == DragonPhase.Passes && d.Until == 0 && d.Travelled > 0 && d.Pass != staged)
            {
                staged = d.Pass;
                yield return StagePass();
            }
            yield return null;
        }
        Metric("flight_seconds", Time.time - started);
        Metric("fastest_shown_m_s", fastest);
        float pace = QuickSpeed * World.Rules.Sim.TicksPerSecond * grid.cellSize;
        Check("flies_smoothly", fastest < pace * 4f, $"at most {fastest:0} m/s on a pass of {pace:0} m/s: no jumps between ticks");
        Check("flight_ended", World.Dragon == null, $"{Time.time - started:0} s");
        Check("breath_was_drawn", view.Breath != null && lookFrames.Count > 0, $"{lookFrames.Count} frames looked at");
        Check("stream_on_while_breathing", breathingFrames > 0 && litFrames >= breathingFrames * 0.95f, $"{litFrames} of {breathingFrames} frames of breath");
        Check("stream_stops_when_not_breathing", litWhenNot == 0 && turningFrames > 0 && leavingFrames > 0,
            $"{litWhenNot} frames lit of {turningFrames} turning and {leavingFrames} leaving");
        Check("stream_dies_away", drawnAfter == 0, $"{drawnAfter} frames drawn over {DropsLand} s after it stopped");
        Metric("most_roll_degrees", mostRoll);
        Metric("below_body_m", belowBody);
        Metric("above_body_m", aboveBody);
        Metric("least_gap_on_a_pass_m", leastPassGap);
        Metric("least_gap_on_a_turn_m", leastTurnGap);
        Check("clears_the_land", Mathf.Min(leastPassGap, leastTurnGap) > 0f, $"at worst {leastPassGap:0.0} m on a pass and {leastTurnGap:0.0} m on a turn between its tips and the land under them");
        Check("open_wizard_downed", open, "");
        Check("sheltered_wizard_not", sheltered, "");
        Check("every_chunk_freshened", freshened.Select(f => (f.CX, f.CY)).Distinct().Count() == World.ChunkCoords.Count(),
            $"{freshened.Select(f => (f.CX, f.CY)).Distinct().Count()} of {World.ChunkCoords.Count()} chunks, {grid.FreshInactiveDrawn} drawn while inactive");
        Metric("inactive_chunks_drawn", grid.FreshInactiveDrawn);
        Check("logs_the_flight", ConsoleSaid("dives out of the clouds", mark) || LogSince(0).Any(l => l.Contains("dives out of the clouds")), "core's log");
        if (lookFrames.Count == 0) yield break;
        // The same view, clear, once the sky has come back.
        while (sky.Menace > 0.02f && Time.time - started < MostSeconds + LookSeconds) yield return null;
        var clear = new List<float>();
        yield return LookFor(lookEye, lookAt, clear);
        Metric("look_breath_mean_ms", lookFrames.Average());
        Metric("look_breath_worst_ms", lookFrames.Max());
        Metric("look_clear_mean_ms", clear.Average());
        Metric("look_clear_worst_ms", clear.Max());
        Metric("look_cost_ms", lookFrames.Average() - clear.Average());
    }

    IEnumerator ThroughTheWarning(float started)
    {
        bool seen = false, shot = false;
        while (World.Dragon?.Phase == DragonPhase.Warning && Time.time - started < MostSeconds)
        {
            seen |= view.Seen;
            if (!shot && sky.Menace > 0.6f)
            {
                shot = true;
                Shot("warning");
            }
            yield return null;
        }
        Check("seen_in_the_warning", seen, "the high crossing or the dive");
    }

    // A pass each: first a wizard in the open, then one behind a wall, then a look at the breath.
    IEnumerator StagePass()
    {
        if (!open) yield return InTheOpen(result => open = result);
        else if (!sheltered) yield return Sheltered(result => sheltered = result);
        else if (lookFrames.Count == 0 && World.Dragon.Pass > 1) yield return LookAndShoot();
    }

    // At the real speed for the moment: the frames of one view of the breath, then shots of it.
    IEnumerator LookAndShoot()
    {
        var p = World.Rules.Sim.Dragon;
        int quick = p.Speed;
        p.Speed = realSpeed;
        (lookEye, lookAt) = LookAtTheBreath();
        yield return LookFor(lookEye, lookAt, lookFrames);
        var chase = view.ShownAt - view.Way * 150f + Vector3.Cross(Vector3.up, view.Way).normalized * 60f + Vector3.up * 55f;
        Cut(chase, view.ShownAt + view.Way * 90f + Vector3.down * 25f);
        yield return null;
        Shot("final");
        yield return null;
        yield return null;
        ShootTheDragon();
        yield return null;
        TheFit(p);
        NoGarbage();
        yield return ScaleFollowsTheData(p);
        yield return StreamSeen("stream", true);
        yield return StreamSeen("below", false);
        p.Speed = quick;
    }

    // The numbers, as drawn, against the core's: all of this has to fit together.
    void TheFit(DragonParams p)
    {
        var d = World.Dragon;
        var flat = Vector3.ProjectOnPlane(view.Way, Vector3.up).normalized;
        var mouth = view.Mouth;
        var lands = view.Breath.StreamAt(1f);
        Check("drawn_scale_is_the_datas", Mathf.Abs(view.DrawnScale - (float)p.Scale) < 0.001f, $"{view.DrawnScale:0.000} drawn, {p.Scale:0.000} in sim.json");
        Check("shadow_scales", Mathf.Abs(view.ShadowSize - ShadowFor(p)) < 0.5f, $"{view.ShadowSize:0.0} m, {ShadowFor(p):0.0} for this scale");
        World.BreathBox(out int x0, out int y0, out int x1, out int y1);
        var landed = grid.CellAt(lands);
        int sway = p.Speed + 1;
        Check("stream_lands_on_the_breath", landed.x >= x0 - sway && landed.x < x1 + sway && landed.y >= y0 - sway && landed.y < y1 + sway,
            $"lands on cell {landed}, the core's box [{x0}, {x1}) by [{y0}, {y1})");
        float ground = grid.GroundAt(view.ShownAt);
        Metric("wingspan_m", DragonFlight.Wingspan(p));
        Metric("flight_height_m", view.ShownAt.y - ground);
        Metric("mouth_ahead_m", Vector3.Dot(mouth - view.ShownAt, flat));
        Metric("mouth_over_the_body_m", mouth.y - view.ShownAt.y);
        Metric("mouth_over_the_ground_m", mouth.y - grid.GroundAt(mouth));
        Metric("mouth_to_wall_along_m", Vector3.Dot(lands - mouth, flat));
        Metric("shadow_m", view.ShadowSize);
        Metric("breath_across_cells", d.AlongX ? y1 - y0 : x1 - x0);
        Metric("breath_along_cells", d.AlongX ? x1 - x0 : y1 - y0);
        Metric("breath_across_m", (d.AlongX ? y1 - y0 : x1 - x0) * grid.cellSize);
        Metric("breath_along_m", (d.AlongX ? x1 - x0 : y1 - y0) * grid.cellSize);
    }

    // A frame's worth of placing the breath, over again: the stream's work is in the shader, and this makes no garbage.
    void NoGarbage()
    {
        var flat = Vector3.ProjectOnPlane(view.Way, Vector3.up).normalized;
        var (a, b) = (view.Breath.StreamAt(1f) + Vector3.Cross(Vector3.up, flat) * 100f, view.Breath.StreamAt(1f) - Vector3.Cross(Vector3.up, flat) * 100f);
        view.Breath.Burn(a, b, -flat, view.Mouth, view.DrawnScale);
        // GC.GetAllocatedBytesForCurrentThread says 0 here whatever is made; the heap's size moves in 4 KB blocks, so enough placings to show.
        long before = System.GC.GetTotalMemory(false);
        for (int i = 0; i < Placings; i++) view.Breath.Burn(a, b, -flat, view.Mouth, view.DrawnScale);
        long made = System.GC.GetTotalMemory(false) - before;
        Metric("burn_bytes", made);
        Check("burn_makes_no_garbage", made == 0, $"{made} bytes over {Placings} placings");
    }

    static float ShadowFor(DragonParams p) =>
        DragonFlight.Wingspan(p) * Mathf.Lerp(1.2f, 2.6f, Mathf.InverseLerp(0f, DragonFlight.High, DragonFlight.Cruise(p)));

    // Twice the data for a moment: the drawn dragon and its shadow go with it, so neither is a number of the view's own.
    IEnumerator ScaleFollowsTheData(DragonParams p)
    {
        double was = p.Scale;
        staging = true;
        p.Scale = was * 2.0;
        yield return null;
        yield return null;
        bool drawn = Mathf.Abs(view.DrawnScale - (float)p.Scale) < 0.001f, laid = Mathf.Abs(view.ShadowSize - ShadowFor(p)) < 0.5f;
        Check("scale_follows_the_data", drawn && laid, $"at {p.Scale:0.00}: drawn {view.DrawnScale:0.00}, shadow {view.ShadowSize:0.0} m");
        p.Scale = was;
        yield return Wait(1f);
        staging = false;
    }

    // A frame with the stream and one without, from one side or from under it on the ground: its line has to be lit.
    IEnumerator StreamSeen(string label, bool side)
    {
        var breath = view.Breath;
        if (!breath.Burning)
        {
            Check($"stream_seen_{label}", false, "the pass ended before the look");
            yield break;
        }
        var frameEnd = new WaitForEndOfFrame();
        var mid = breath.StreamAt(0.5f);
        var flat = Vector3.ProjectOnPlane(view.Way, Vector3.up).normalized;
        var across = Vector3.Cross(Vector3.up, flat);
        var eye = side ? mid + across * SideOn : breath.StreamAt(1f) + flat * 60f + across * 150f;
        eye.y = Mathf.Max(grid.GroundAt(eye) + BelowHeight, side ? mid.y : view.Mouth.y - 130f);
        Cut(eye, mid);
        yield return null;
        yield return frameEnd;
        Shot(label);
        yield return null;
        yield return frameEnd;
        var with = StreamPicture(breath);
        breath.StreamHidden = true;
        yield return null;
        yield return null;
        yield return frameEnd;
        var without = StreamPicture(breath);
        breath.StreamHidden = false;
        int lit = with.Zip(without, (a, b) => a > b + StreamBrighter * (2 * StreamBox + 1) * (2 * StreamBox + 1) ? 1 : 0).Sum();
        Metric($"stream_lit_points_{label}", lit);
        Check($"stream_seen_{label}", lit >= StreamPoints * StreamLitPart, $"{lit} of {StreamPoints} points between the mouth and the wall lit by it");
    }

    // The sum of red and green over a little box at each point of the stream's line.
    static int[] StreamPicture(DragonBreath breath)
    {
        var picture = ScreenCapture.CaptureScreenshotAsTexture();
        var eye = Camera.main;
        var sums = new int[StreamPoints];
        for (int i = 0; i < StreamPoints; i++)
        {
            var at = eye.WorldToScreenPoint(breath.StreamAt((i + 0.5f) / StreamPoints));
            if (at.z <= 0f) continue;
            for (int y = (int)at.y - StreamBox; y <= (int)at.y + StreamBox; y++)
                for (int x = (int)at.x - StreamBox; x <= (int)at.x + StreamBox; x++)
                {
                    if (x < 0 || y < 0 || x >= picture.width || y >= picture.height) continue;
                    var c = (Color32)picture.GetPixel(x, y);
                    sums[i] += c.r + c.g;
                }
        }
        Destroy(picture);
        return sums;
    }

    // The stream against the core's breath, frame by frame: on while it's out, off at the turns and on leaving, and gone once the drops land.
    IEnumerator WatchTheStream()
    {
        var frameEnd = new WaitForEndOfFrame();
        while (World.Dragon != null)
        {
            yield return frameEnd;
            if (World.Dragon != null && view.Breath != null) NoteTheStream(World.Dragon, view.Breath);
        }
    }

    void NoteTheStream(DragonState d, DragonBreath breath)
    {
        if (World.BreathBox(out _, out _, out _, out _))
        {
            framesOff = 0;
            streamStopped = -1f;
            breathingFrames++;
            if (breath.Burning) litFrames++;
            return;
        }
        if (streamStopped < 0f) streamStopped = Time.time;
        if (d.Phase == DragonPhase.Passes && d.Until > 0) turningFrames++;
        if (d.Phase == DragonPhase.Leaving) leavingFrames++;
        if (++framesOff > LagFrames && breath.Burning) litWhenNot++;
        if (Time.time - streamStopped > DropsLand && breath.StreamDrawn) drawnAfter++;
    }

    // The Reset button itself, on a virtual pad, after the flight, solo: a tap calls the dragon and rebuilds nothing;
    // holding it rebuilds the valley at once, the old R: the scene reloads, so this comes last and takes the probe across.
    IEnumerator ResetButton()
    {
        var controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled;
        if (controls != null) controls.enabled = true;
        var pad = InputSystem.AddDevice<Gamepad>("Reset pad");
        try
        {
            int flights = World.DragonFlights;
            var was = grid;
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.Select));
            yield return Wait(TapSeconds);
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return Wait(TapSeconds);
            Check("reset_tap_summons", World.DragonFlights == flights + 1 && World.Dragon?.Phase == DragonPhase.Warning, $"flights {flights} to {World.DragonFlights}, phase {World.Dragon?.Phase}");
            yield return Wait(RebuildSeconds + TapSeconds);
            Check("reset_tap_rebuilds_nothing", grid != null && grid == was && World.Dragon != null, "the same world, the flight still on");
            DontDestroyOnLoad(gameObject);
            float pressed = Time.unscaledTime;
            InputSystem.QueueStateEvent(pad, new GamepadState(GamepadButton.Select));
            while (was != null && Time.unscaledTime - pressed < RebuildSeconds + LoadSeconds) yield return null;
            float held = Time.unscaledTime - pressed;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            Metric("reset_held_s", held);
            bool rebuilt = was == null;
            if (rebuilt) yield return TheNewYard();
            Check("reset_hold_rebuilds", rebuilt && held >= RebuildSeconds - TapSeconds && World.Dragon == null && World.DragonFlights == 0, $"after {held:0.0} s: scene reloaded {rebuilt}, flights {(rebuilt ? World.DragonFlights : -1)}");
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
            if (controls != null) controls.enabled = controlsWereOn;
        }
    }

    // The reloaded scene's grid, once its world is up, for what's left of the run.
    IEnumerator TheNewYard()
    {
        float from = Time.unscaledTime;
        grid = null;
        while (Time.unscaledTime - from < LoadSeconds && (grid == null || grid.World == null))
        {
            yield return null;
            grid = FindAnyObjectByType<ChemGrid>();
        }
    }

    // How close the dragon's tips come to the land drawn under them, land past the valley's edge too, on the passes and the turns.
    IEnumerator WatchTheFit()
    {
        var frameEnd = new WaitForEndOfFrame();
        var tips = view.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_end", System.StringComparison.Ordinal)).ToList();
        Check("tips_found", tips.Count > 0, $"{tips.Count} bones");
        while (World.Dragon != null)
        {
            yield return frameEnd;
            var d = World.Dragon;
            if (d?.Phase != DragonPhase.Passes || !view.Seen || staging) continue;
            float gap = tips.Min(t => t.position.y - LandAt(t.position));
            mostRoll = Mathf.Max(mostRoll, Mathf.Abs(DragonFlight.At(World, 0f, Vector2.zero).Roll));
            if (d.Until > 0) leastTurnGap = Mathf.Min(leastTurnGap, gap);
            else leastPassGap = Mathf.Min(leastPassGap, gap);
            var box = view.Extent;
            belowBody = Mathf.Max(belowBody, view.ShownAt.y - box.min.y);
            aboveBody = Mathf.Max(aboveBody, box.max.y - view.ShownAt.y);
        }
    }

    // The height of the land drawn at a point: any terrain over it.
    static float LandAt(Vector3 at)
    {
        float best = float.NegativeInfinity;
        foreach (var t in Terrain.activeTerrains)
        {
            var from = t.transform.position;
            var size = t.terrainData.size;
            if (at.x < from.x || at.z < from.z || at.x > from.x + size.x || at.z > from.z + size.z) continue;
            best = Mathf.Max(best, from.y + t.SampleHeight(at));
        }
        return best;
    }

    // The fastest the shown dragon moves on a pass, frame to frame: a jump at each tick would show here.
    IEnumerator WatchTheFlight()
    {
        var was = view.ShownAt;
        var frameEnd = new WaitForEndOfFrame();
        while (World.Dragon != null)
        {
            yield return frameEnd;
            var d = World.Dragon;
            bool flying = d != null && d.Phase == DragonPhase.Passes && d.Until == 0 && d.Travelled > 0;
            if (flying && view.Seen && !staging && Time.unscaledDeltaTime < 0.1f) fastest = Mathf.Max(fastest, (view.ShownAt - was).magnitude / Mathf.Max(Time.deltaTime, 0.001f));
            was = view.ShownAt;
        }
    }

    IEnumerator WatchThePerch()
    {
        while (World.Dragon != null && !view.Perched) yield return null;
        Check("lands_to_roar", view.Perched, "it stands on the mountain as it leaves");
        yield return Wait(0.8f);
        if (World.Dragon == null) yield break;
        var ground = grid.GroundAt(view.ShownAt);
        Metric("perch_above_ground_m", view.ShownAt.y - ground);
        Metric("perch_lowest_below_it_m", view.ShownAt.y - view.Extent.min.y);
        Cut(view.ShownAt - view.Way * -90f + Vector3.Cross(Vector3.up, view.Way).normalized * 60f + Vector3.up * 16f, view.ShownAt + Vector3.up * 14f);
        yield return null;
        Shot("perch");
    }

    // A close look at it, for the eye: from the side, a little above.
    void ShootTheDragon()
    {
        var side = Vector3.Cross(Vector3.up, view.Way).normalized;
        Cut(view.ShownAt + side * 110f + view.Way * 20f + Vector3.up * 14f, view.ShownAt);
        Shot("close");
    }

    // A camera high and ahead of the breath and off to one side, looking back at it over the valley.
    (Vector3 eye, Vector3 at) LookAtTheBreath()
    {
        var d = World.Dragon;
        World.BreathBox(out int x0, out int y0, out int x1, out int y1);
        int mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        var way = new Vector2(d.AlongX ? 1 : 0, d.AlongX ? 0 : 1) * (World.PassLine(d, d.Pass).back ? -1 : 1);
        int ex = mx + (int)(way.x * 190f), ey = my + (int)(way.y * 190f);
        var at = grid.CellCenter(Mathf.Clamp(mx, 0, World.W - 1), Mathf.Clamp(my, 0, World.H - 1));
        var eye = grid.CellCenter(Mathf.Clamp(ex, 0, World.W - 1), Mathf.Clamp(ey, 0, World.H - 1));
        return (eye + new Vector3(60f, grid.GroundAt(eye) + 120f, 40f), at + Vector3.up * (grid.GroundAt(at) + 10f));
    }

    IEnumerator LookFor(Vector3 eye, Vector3 at, List<float> frames)
    {
        Cut(eye, at);
        float until = Time.time + LookSeconds;
        bool shot = false;
        while (Time.time < until)
        {
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000f);
            if (shot || World.Dragon == null || Time.time < until - LookSeconds * 0.5f) continue;
            shot = true;
            Shot("breath");
        }
    }

    // The next pass's way, a cell of the strip this far ahead of the lead that passes the test, or null.
    (int x, int y)? AheadOfTheBreath(int ahead, System.Func<int, int, bool> good)
    {
        var d = World.Dragon;
        if (!World.BreathBox(out int x0, out int y0, out int x1, out int y1)) return null;
        bool alongX = d.AlongX, back = World.PassLine(d, d.Pass).back;
        var (from, to, first, last) = alongX ? (x0, x1, y0, y1) : (y0, y1, x0, x1);
        int dir = back ? -1 : 1, start = (back ? from : to) + dir * ahead, length = alongX ? World.W : World.H;
        for (int i = 0; i < length; i++)
            for (int across = first; across < last; across += 3)
            {
                var (x, y) = alongX ? (start + dir * i, across) : (across, start + dir * i);
                if (World.InBounds(x, y) && good(x, y)) return (x, y);
            }
        return null;
    }

    bool Walkable(int x, int y)
    {
        var c = World.At(x, y);
        return c != null && !World.IsBlocking(c);
    }

    IEnumerator WaitForTheBreath(int pass, int x, int y, System.Action<bool> caught)
    {
        while (World.Dragon?.Pass == pass && !World.BreathBox(out _, out _, out _, out _)) yield return null;
        bool down = false;
        while (World.Dragon?.Pass == pass && !down)
        {
            down = vitals.IsDown;
            if (!Passed(x, y)) { yield return null; continue; }
            break;
        }
        caught(down);
    }

    // Whether the breath's tail has gone by a cell.
    bool Passed(int x, int y)
    {
        var d = World.Dragon;
        if (d == null || !World.BreathBox(out int x0, out int y0, out int x1, out int y1)) return true;
        bool back = World.PassLine(d, d.Pass).back;
        int at = d.AlongX ? x : y, from = d.AlongX ? x0 : y0, to = d.AlongX ? x1 : y1;
        return back ? to < at : from > at;
    }

    IEnumerator InTheOpen(System.Action<bool> done)
    {
        var cell = AheadOfTheBreath(Ahead, (x, y) => Walkable(x, y) && World.At(x, y).T == TerrainIds.Grass && !World.ShelteredFromBreath(x, y));
        if (cell is not { } at) yield break;
        PlaceCell(at.x, at.y);
        int pass = World.Dragon.Pass, mark = ConsoleMark;
        Metric("open_cell_x", at.x);
        Metric("open_cell_y", at.y);
        yield return WaitForTheBreath(pass, at.x, at.y, r => done(r));
        Check("breath_downs_in_the_open", vitals.IsDown && vitals.FellLine.Contains("dragon"), $"at {at}: down {vitals.IsDown}: {vitals.FellLine}");
        Check("the_log_says_so", ConsoleSaid("The dragon's breath knocks you down", mark), "");
        yield return Wait((float)World.Rules.Sim.Downed.Seconds + 0.5f);
        var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
        var flat = wizard.transform.position - start;
        flat.y = 0f;
        Check("back_at_the_yard", !vitals.IsDown && flat.magnitude < 3f, $"{flat.magnitude:0.0} m from the start, down {vitals.IsDown}");
    }

    IEnumerator Sheltered(System.Action<bool> done)
    {
        var cell = AheadOfTheBreath(Ahead, (x, y) => Walkable(x, y) && World.ShelteredFromBreath(x, y));
        if (cell is not { } at) yield break;
        PlaceCell(at.x, at.y);
        int pass = World.Dragon.Pass;
        var here = grid.CellAt(wizard.transform.position);
        yield return WaitForTheBreath(pass, at.x, at.y, down => done(!down && here == new Vector2Int(at.x, at.y)));
        Check("breath_spares_the_sheltered", !vitals.IsDown, $"at {at}, standing on {here}, on {World.At(at.x, at.y)?.T}, down {vitals.IsDown}");
    }

    // What the flight must put back, set up before it comes and checked after.
    sealed class Damage
    {
        readonly DragonProbe probe;
        ThingId moved, broken;
        Vector3 movedHome;
        (int x, int y) meadow, tree;
        string lavaAsBuilt, meadowAsBuilt;
        int golfBefore;

        public Damage(DragonProbe on) => probe = on;

        public IEnumerator Do()
        {
            var world = probe.World;
            lavaAsBuilt = world.At(LavaX, LavaY).T;
            probe.Check("the_gauntlet_is_built", lavaAsBuilt == TerrainIds.Lava, $"({LavaX}, {LavaY}) is {lavaAsBuilt}");
            world.Paint(LavaX, LavaY, TerrainIds.Stone);

            var from = probe.grid.CellAt(probe.wizard.transform.position);
            meadow = Find(world, from, c => c.T == TerrainIds.Grass);
            meadowAsBuilt = world.At(meadow.x, meadow.y).T;
            world.Paint(meadow.x, meadow.y, TerrainIds.Dirt);
            world.At(meadow.x, meadow.y).Height += 2;

            tree = Find(world, from, c => c.T == TerrainIds.Tree, (x, y) => probe.grid.valley.TreeStands(x, y));
            world.Paint(tree.x, tree.y, TerrainIds.Stump);

            var things = probe.spawner.Live.Where(t => t.prop.State != null && !t.prop.Held && t.prop.kind == EntityIds.Crate).OrderBy(t => (probe.wizard.transform.position - t.prop.transform.position).sqrMagnitude).Take(2).ToList();
            probe.Check("things_to_wreck", things.Count == 2, $"{things.Count} crates in reach");
            if (things.Count == 2)
            {
                moved = things[0].id;
                movedHome = things[0].prop.transform.position;
                things[0].prop.BackAsBuilt(movedHome + new Vector3(4f, 1f, 3f), Quaternion.Euler(0f, 40f, 0f));
                broken = things[1].id;
                things[1].prop.Hurt(9999);
            }
            yield return Wait(1f);
            probe.Check("tree_felled_first", !probe.grid.valley.TreeStands(tree.x, tree.y), $"at {tree}");
            probe.Check("thing_broke_first", probe.spawner.Find(broken) == null, $"{broken}");
        }

        static (int x, int y) Find(World world, Vector2Int from, System.Func<Cell, bool> what, System.Func<int, int, bool> also = null)
        {
            for (int r = 1; r < 200; r++)
                for (int i = -r; i <= r; i++)
                    foreach (var (x, y) in new[] { (from.x + i, from.y - r), (from.x + i, from.y + r), (from.x - r, from.y + i), (from.x + r, from.y + i) })
                        if (world.InBounds(x, y) && what(world.At(x, y)) && (also == null || also(x, y))) return (x, y);
            return (from.x, from.y);
        }

        public void Checked()
        {
            var world = probe.World;
            probe.Check("gauntlet_back", world.At(LavaX, LavaY).T == lavaAsBuilt, $"({LavaX}, {LavaY}) is {world.At(LavaX, LavaY).T}");
            var m = world.At(meadow.x, meadow.y);
            probe.Check("meadow_back", m.T == meadowAsBuilt && m.Height == 0, $"{m.T}, height {m.Height}");
            probe.Check("tree_back", world.At(tree.x, tree.y).T == TerrainIds.Tree && probe.grid.valley.TreeStands(tree.x, tree.y), $"at {tree}: {world.At(tree.x, tree.y).T}");
            var thing = probe.spawner.Find(moved);
            float off = thing != null ? (thing.transform.position - movedHome).magnitude : float.PositiveInfinity;
            probe.Check("moved_thing_home", off < 1.5f, $"{off:0.0} m from where it stood");
            probe.Check("broken_thing_back", probe.spawner.Find(broken) != null, $"{broken}");
            probe.Check("nothing_changed_left", world.ChangedCount < 50, $"{world.ChangedCount} cells differ from the start");
            probe.Metric("changed_after", world.ChangedCount);
        }
    }
}
