using System;
using System.Collections;
using Chem;
using UnityEngine;

// The probe's dragonclip scenario: a short film of a flight for Andrew's review, the frames and
// sound as castclip's (ClipProbe). Staged as `dragon` is: noon and clear, a warning of 8 s rather
// than 30, then a cut (the game runs on, quicker, unfilmed) from the first pass, along the map's
// edge, to one down the valley's middle, which is filmed at the real speed.
public class DragonClipProbe : ClipProbe
{
    const int ShortWarning = 48, ShortTurn = 12, QuickSpeed = 30, QuickChunks = 4;
    // When the first shot gives way to the dive, and how long is filmed of the first pass.
    const float CrossingSeconds = 7f, FirstPassSeconds = 2.2f;
    // How far along the middle pass filming picks up, in cells, and how long each shot of it runs.
    const int PickUpAt = 140;
    const float ChaseSeconds = 2.8f, AheadSeconds = 2.2f, CloseSeconds = 2.6f;
    // How quickly a following camera catches the dragon up (1/s).
    const float Follow = 3f;
    // A wider lens for the crossing, to have the land and the dragon high over it in one frame.
    const float CrossingLens = 1.5f;

    DragonView view;
    SkyLight sky;
    Vector3 followed;
    DragonState flight;

    protected override IEnumerator Run()
    {
        view = FindAnyObjectByType<DragonView>();
        sky = FindAnyObjectByType<SkyLight>();
        Check("view_in_scene", view != null, "the yard has a DragonView (rebuild the yard if not)");
        if (view == null || string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", ScreenshotDir != null, "dragonclip films into --shots <folder>");
            yield break;
        }
        System.IO.Directory.CreateDirectory(ScreenshotDir);
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': { 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var p = World.Rules.Sim.Dragon;
        int realSpeed = p.Speed, realChunks = p.ChunksPerTick;
        (p.WarningTicks, p.TurnTicks) = (ShortWarning, ShortTurn);

        StartFilm();
        int firstTick = World.Tick;
        EventLog.ResetYard();
        flight = World.Dragon;
        Check("r_summons_it", flight?.Phase == DragonPhase.Warning, $"phase {flight?.Phase}");
        if (flight == null) yield break;
        yield return TheCrossing();
        Metric("menace_at_the_dive", sky.Menace);
        yield return TheDive();
        Metric("ticks_a_second", (World.Tick - firstTick) / FilmedSeconds);
        Metric("first_pass_at_s", FilmedSeconds);

        Keeping = false;
        (p.Speed, p.ChunksPerTick) = (QuickSpeed, QuickChunks);
        int middle = flight.Passes / 2;
        while (World.Dragon != null && !(flight.Pass == middle && flight.Until == 0 && flight.Travelled >= PickUpAt)) yield return null;
        Check("reached_a_middle_pass", World.Dragon != null, $"pass {flight.Pass} of {flight.Passes}");
        (p.Speed, p.ChunksPerTick) = (realSpeed, realChunks);
        yield return null;
        Keeping = true;
        followed = view.ShownAt;
        Metric("cut_at_s", FilmedSeconds);
        Check("breath_out", view.Breath != null && view.Breath.Burning, "the wall of fire is drawn as the cut lands");

        yield return Chase();
        yield return Ahead();
        yield return Close();
        Metric("menace_at_the_end", sky.Menace);
        yield return EndFilm();
        Check("no_console_errors", Result.Console.ErrorCount == 0, $"{Result.Console.ErrorCount} errors");
    }

    // A place in the valley by where it is along the flight's passes and across them.
    Vector3 Cell(float along, float across, float lift)
    {
        var o = grid.CellCenter(0, 0);
        var x = grid.CellCenter(1, 0) - o;
        var y = grid.CellCenter(0, 1) - o;
        var at = flight.AlongX ? o + x * along + y * across : o + x * across + y * along;
        return at + Vector3.up * lift;
    }

    float Length => flight.AlongX ? World.W : World.H;
    float Wide => flight.AlongX ? World.H : World.W;

    // Across the valley from high over its side, land below the horizon and the crossing above it.
    IEnumerator TheCrossing()
    {
        var lens = Camera.main;
        float was = lens.fieldOfView;
        lens.fieldOfView = was * CrossingLens;
        Cut(Cell(Length / 2f, Wide * 0.08f, 210f), Cell(Length / 2f, Wide * 0.6f, 215f));
        yield return Wait(CrossingSeconds);
        lens.fieldOfView = was;
    }

    // Beside the first pass's start, watching it come down over the sea and in on the line.
    IEnumerator TheDive()
    {
        var (line, back) = World.PassLine(flight, 0);
        float start = back ? Length : 0f, way = back ? -1f : 1f;
        var eye = Cell(start + way * 150f, line + 70f, 70f);
        var ahead = Cell(start - way * 100f, line, 40f);
        followed = view.ShownAt;
        while (flight.Phase == DragonPhase.Warning || FilmedSinceTheBreath() < FirstPassSeconds)
        {
            Glance(eye, ahead);
            yield return null;
        }
    }

    float breathStarted = -1f;

    float FilmedSinceTheBreath()
    {
        if (breathStarted < 0f && flight.Phase != DragonPhase.Warning) breathStarted = FilmedSeconds;
        return breathStarted < 0f ? 0f : FilmedSeconds - breathStarted;
    }

    // From a fixed place, turned toward the dragon as far as a fixed look allows.
    void Glance(Vector3 eye, Vector3 at)
    {
        followed = Vector3.Lerp(followed, view.ShownAt, 1f - Mathf.Exp(-Time.deltaTime * Follow));
        Cut(eye, Vector3.Lerp(at, followed, 0.35f));
    }

    // Behind it and to one side, the wall of fire ahead of it.
    IEnumerator Chase() => Following(ChaseSeconds, (at, way, side) => (at - way * 190f + side * 40f + Vector3.up * 105f, at + way * 90f + Vector3.down * 35f));

    // Ahead of it and off to the side, looking back at it through the wall.
    IEnumerator Ahead() => Following(AheadSeconds, (at, way, side) => (at + way * 230f - side * 110f + Vector3.up * 85f, at + Vector3.down * 25f));

    // Just ahead of the wall, the flames filling the frame under the dragon.
    IEnumerator Close() => Following(CloseSeconds, (at, way, side) => (AboveGround(at + way * 170f + side * 30f + Vector3.up * 14f), at + Vector3.down * 8f));

    Vector3 AboveGround(Vector3 eye) => new(eye.x, Mathf.Max(eye.y, grid.GroundAt(eye) + 60f), eye.z);

    IEnumerator Following(float seconds, Func<Vector3, Vector3, Vector3, (Vector3 eye, Vector3 at)> shot)
    {
        var way = Vector3.ProjectOnPlane(view.Way, Vector3.up).normalized;
        var side = Vector3.Cross(Vector3.up, way).normalized;
        followed = view.ShownAt;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            followed = Vector3.Lerp(followed, view.ShownAt, 1f - Mathf.Exp(-Time.deltaTime * Follow));
            var (eye, at) = shot(followed, way, side);
            Cut(eye, at);
            yield return null;
        }
    }
}
