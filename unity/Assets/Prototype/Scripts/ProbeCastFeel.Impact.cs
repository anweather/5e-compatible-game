using System.Collections;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;

// The castfeel probe's second half (docs/SPELL_COMBOS.md, "Impact"), measured, not
// shot: the real camera through its brain and a virtual gamepad that keeps what its
// motors were told. Hit-stop on the slam and the release with the game's clocks
// untouched, and a charged cast run twice from a new world, with and without the
// stops, tick for tick the same; shake and rumble scale with size and distance;
// charging tightens, builds and stays gentle; off is still; the motors always stop.
public partial class CastFeelProbe
{
    // A gamepad that records what it's told; nothing is plugged in behind it.
    class MotorPad : Gamepad
    {
        public float Low, High, PeakLow, PeakHigh;

        public override void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            base.SetMotorSpeeds(lowFrequency, highFrequency);
            Low = lowFrequency;
            High = highFrequency;
            PeakLow = Mathf.Max(PeakLow, Low);
            PeakHigh = Mathf.Max(PeakHigh, High);
        }

        public void ClearPeaks() => PeakLow = PeakHigh = 0f;

        public bool Quiet => Low < Silence && High < Silence;
    }

    // The lens wanders by itself, by a centimetre or two at 4 m (a hair of look input on a shared PC), so
    // what's asked of the camera is checked by its numbers and this is only a sanity limit on the result (m).
    const float Wander = 0.04f, Silence = 0.001f;

    MotorPad pad;
    Transform lens, root;
    CastCamera view;
    float peak;

    IEnumerator Impact()
    {
        float shakeWas = GameFeel.Shake;
        var padWas = Gamepad.current;
        InputSystem.RegisterLayout<MotorPad>();
        pad = InputSystem.AddDevice<MotorPad>("Rumble pad");
        pad.MakeCurrent();
        PadRumble.Testing = true;
        lens = Camera.main.transform;
        root = wizard.transform.Find(WizardRig.CameraRoot);
        brain.enabled = true;
        try
        {
            GameFeel.Shake = 1f;
            yield return Wait(1.2f);
            yield return Stops();
            yield return Scaling();
            yield return Charging();
            yield return Off();
            GameFeel.Shake = 1f;
            yield return MotorsStop();
            yield return SameTicks();
        }
        finally
        {
            PadRumble.Stop();
            PadRumble.Testing = false;
            GameFeel.Shake = shakeWas;
            InputSystem.RemoveDevice(pad);
            padWas?.MakeCurrent();
        }
    }

    // Where the camera is from the wizard it follows: what a shake moves, and a step of the ground doesn't.
    Vector3 Offset => lens.position - root.position;

    // The camera's greatest move from where it is now, and the motors' peaks, while something plays.
    IEnumerator Watch(float seconds, System.Action start)
    {
        var rest = Offset;
        peak = 0f;
        pad.ClearPeaks();
        start?.Invoke();
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            yield return null;
            peak = Mathf.Max(peak, Vector3.Distance(Offset, rest));
        }
    }

    IEnumerator Stops()
    {
        int stops = beats.Stops;
        int scale = Mathf.RoundToInt(Time.timeScale * 1000f);
        float fixedStep = Time.fixedDeltaTime;
        var animator = wizard.GetComponent<Animator>();
        void Struck(Beat beat) => CastImpact.Felt(wizard.transform.position, 1f); // a shake going on must not move a held camera
        beats.Struck += Struck;
        Check("tap_loads", caster.LoadElement(ElementIds.Fire));
        yield return Wait(0.2f);
        float stopped = 0f, held = 0f;
        int heldFrames = 0;
        bool animatorStill = true, wasStopped = false;
        var last = lens.position;
        for (float waited = 0f; beats.Now != Beat.Neutral && waited < WaitLimit; waited += Time.deltaTime)
        {
            if (beats.Stopped)
            {
                stopped += Time.deltaTime;
                animatorStill &= animator.speed < Silence;
                if (wasStopped)
                {
                    held = Mathf.Max(held, Vector3.Distance(lens.position, last));
                    heldFrames++;
                }
            }
            wasStopped = beats.Stopped;
            last = lens.position;
            yield return null;
        }
        beats.Struck -= Struck;
        Metric("hit_stop_seconds_both", stopped);
        Metric("hit_stop_held_frames", heldFrames);
        Metric("hit_stop_camera_moved_m", held);
        Check("hit_stop_on_slam_and_release", beats.Stops - stops == 2, $"{beats.Stops - stops} stops");
        Check("hit_stop_is_short", stopped > 0f && stopped < 0.2f, $"{stopped:0.000} s in all");
        Check("hit_stop_freezes_the_animator", animatorStill && animator.speed > 0f, $"speed back to {animator.speed}");
        Check("hit_stop_holds_the_camera", held < Silence * 2f, $"{held * 100f:0.00} cm over {heldFrames} frames");
        Check("hit_stop_leaves_time_alone", Mathf.RoundToInt(Time.timeScale * 1000f) == scale && Mathf.Approximately(Time.fixedDeltaTime, fixedStep),
            $"timeScale {Time.timeScale}, fixedDeltaTime {Time.fixedDeltaTime}");
        yield return Wait(0.6f);
    }

    IEnumerator Scaling()
    {
        // A tap against a full charge, by what the release asks of the camera.
        caster.LoadElement(ElementIds.Fire);
        yield return UntilReleaseKick();
        float small = CastImpact.LastKick;
        yield return AtBeat(Beat.Neutral, 0.6f);
        caster.LoadElement(ElementIds.Earth, held: true);
        while (World.HeldCharge(World.CastOf(caster.CastPlayer)) < World.MaxCharge) yield return null;
        yield return Wait(0.2f);
        caster.LetGo();
        yield return UntilReleaseKick();
        float big = CastImpact.LastKick;
        yield return AtBeat(Beat.Neutral, 0.8f);
        Metric("release_kick_tap_m", small);
        Metric("release_kick_charged_m", big);
        Check("release_scales_with_size", big > small * 1.2f, $"{small:0.000} m, charged {big:0.000} m");

        // Landings, near and far, big and small, measured at the lens and the motors.
        var at = wizard.transform.position;
        var side = wizard.transform.right;
        yield return FeelAt(at + side * 3f, 0f, "near_small");
        var nearSmall = felt;
        yield return FeelAt(at + side * 30f, 1f, "far_big");
        var farBig = felt;
        yield return FeelAt(at + side * 3f, 1f, "near_big");
        var nearBig = felt;
        int kicksBefore = CastImpact.Kicks;
        yield return FeelAt(at + side * 200f, 1f, "out_of_reach");
        var gone = felt;
        Check("landing_scales_with_size", nearBig.camera > nearSmall.camera && nearBig.motor > nearSmall.motor, $"{nearBig}, small {nearSmall}");
        Check("landing_scales_with_distance", nearBig.camera > farBig.camera && farBig.camera > 0.005f && nearBig.motor > farBig.motor, $"{nearBig}, far {farBig}");
        Check("landing_out_of_reach_is_not_felt", CastImpact.Kicks == kicksBefore && gone.motor < Silence, gone.ToString());

        // The way it really comes: a Fireball, with no beats (a cast from the radial), landing beside the wizard.
        var cell = grid.CellAt(at + wizard.transform.forward * 4f);
        yield return Watch(2.5f, () => caster.Cast(SpellIds.Fireball, cell.x, cell.y, 0));
        Metric("fireball_camera_m", peak);
        Check("a_landing_is_felt", peak > 0.03f && pad.PeakLow > 0f, $"camera {peak:0.000} m, heavy motor {pad.PeakLow:0.00}");
        yield return Wait(0.5f);
    }

    // Its kick waits for the end of the release's hit-stop.
    IEnumerator UntilReleaseKick()
    {
        bool struck = false;
        void Struck(Beat beat) => struck |= beat == Beat.Release;
        beats.Struck += Struck;
        for (float waited = 0f; !struck && waited < WaitLimit; waited += Time.deltaTime) yield return null;
        beats.Struck -= Struck;
        while (beats.Stopped) yield return null;
        yield return null;
    }

    readonly struct Peak
    {
        public readonly float camera, motor;

        public Peak(float camera, float motor)
        {
            this.camera = camera;
            this.motor = motor;
        }

        public override string ToString() => $"camera {camera:0.000} m, heavy motor {motor:0.00}";
    }

    Peak felt;

    IEnumerator FeelAt(Vector3 at, float size, string name)
    {
        yield return Watch(0.45f, () => CastImpact.Felt(at, size));
        felt = new Peak(peak, pad.PeakLow);
        Metric($"felt_{name}_camera_m", felt.camera);
        Metric($"felt_{name}_motor", felt.motor);
        yield return Wait(0.8f); // a kick into the ground lifts the lens, and that eases off slowly (CameraClearance)
    }

    IEnumerator Charging()
    {
        var live = brain.ActiveVirtualCamera as Component;
        view = live != null ? live.GetComponent<CastCamera>() : null;
        Check("camera_has_cast_camera", view != null, live != null ? live.name : "no live camera");
        if (view == null) yield break;
        caster.LoadElement(ElementIds.Water, held: true);
        var levels = new float[World.MaxCharge + 1];
        float tightest = 0f;
        var rest = Offset;
        float worst = 0f;
        for (float waited = 0f; waited < 3.2f; waited += Time.deltaTime)
        {
            yield return null;
            if (beats.Now != Beat.Charge) continue;
            int level = Mathf.Clamp(beats.Charge, 1, World.MaxCharge);
            levels[level] = Mathf.Max(levels[level], view.ChargeShake);
            tightest = Mathf.Max(tightest, view.Tightening);
            if (beats.InBeat > 0.4f) worst = Mathf.Max(worst, Vector3.Distance(Offset, rest));
        }
        pad.ClearPeaks();
        yield return Wait(0.4f);
        Metric("charge_tightening_degrees", tightest);
        Metric("charge_shake_level2_m", levels[2]);
        Metric("charge_shake_level3_m", levels[3]);
        Metric("charge_camera_worst_m", worst);
        Check("charging_tightens", tightest > 1f && tightest <= beats.feel.chargeTighten + 0.01f, $"{tightest:0.00} degrees");
        Check("charge_shake_builds", levels[3] > levels[2] && levels[2] > 0f, $"level 2 {levels[2]:0.0000} m, level 3 {levels[3]:0.0000} m");
        Check("charge_shake_is_gentle", levels[3] <= beats.feel.chargeShake + Silence && worst < Wander, $"{levels[3] * 100f:0.0} cm asked, worst {worst * 100f:0.0} cm from rest");
        Check("a_held_charge_is_no_drone", pad.Quiet && pad.PeakLow < Silence, $"motors {pad.Low:0.00} {pad.High:0.00} held at full charge");
        caster.LetGo();
        yield return AtBeat(Beat.Neutral, 0.8f);
        Check("tightening_goes_with_the_charge", view.Tightening < Silence, $"{view.Tightening:0.00} degrees");
    }

    IEnumerator Off()
    {
        GameFeel.Shake = 0f;
        int stops = beats.Stops;
        var at = wizard.transform.position;
        float lensAngle = Camera.main.fieldOfView;
        float widest = 0f, asked = 0f;
        int kicks = CastImpact.Kicks;
        yield return Watch(0.8f, null);
        Metric("idle_camera_m", peak);
        yield return Watch(0.8f, () => caster.LoadElement(ElementIds.Fire));
        Metric("off_tap_camera_m", peak);
        float worst = peak;
        var motors = pad.PeakLow + pad.PeakHigh;
        yield return AtBeat(Beat.Neutral, 0.4f);
        yield return Watch(0.45f, () => CastImpact.Felt(at, 1f));
        Metric("off_landing_camera_m", peak);
        worst = Mathf.Max(worst, peak);
        motors += pad.PeakLow + pad.PeakHigh;
        caster.LoadElement(ElementIds.Earth, held: true);
        var rest = Offset;
        pad.ClearPeaks();
        for (float waited = 0f; waited < 2.4f; waited += Time.deltaTime)
        {
            yield return null;
            worst = Mathf.Max(worst, Vector3.Distance(Offset, rest));
            widest = Mathf.Max(widest, Mathf.Abs(Camera.main.fieldOfView - lensAngle));
            asked = Mathf.Max(asked, Mathf.Max(view != null ? view.ChargeShake : 0f, view != null ? view.Tightening : 0f));
        }
        motors += pad.PeakLow + pad.PeakHigh;
        caster.DropCast();
        yield return AtBeat(Beat.Neutral, 0.3f);
        Metric("off_camera_m", worst);
        Metric("off_lens_degrees", widest);
        Check("off_is_still", CastImpact.Kicks == kicks && asked < Silence && worst < Wander && widest < 0.01f,
            $"{CastImpact.Kicks - kicks} kicks, {asked:0.000} of charge shake or tightening, camera {worst * 100f:0.00} cm, lens {widest:0.000} degrees");
        Check("off_is_quiet", motors < Silence, $"motors peaked {motors:0.00}");
        Check("off_has_no_hit_stop", beats.Stops == stops, $"{beats.Stops - stops} stops");
    }

    IEnumerator MotorsStop()
    {
        var longest = new CastFeel.Thump(0.7f, 0.7f, 30f);
        var motors = GameObject.Find("Pad rumble");

        Check("cancel_loads", caster.LoadElement(ElementIds.Fire));
        yield return Wait(0.1f);
        PadRumble.Play(longest, 1f);
        yield return null;
        yield return null;
        Check("motors_run", pad.Low > 0f, $"heavy motor {pad.Low:0.00}");
        caster.DropCast();
        yield return AtBeat(Beat.Neutral, 0.1f);
        Check("motors_stop_on_cancel", pad.Quiet, $"{pad.Low:0.00} {pad.High:0.00}");

        PadRumble.Play(longest, 1f);
        yield return null;
        yield return null;
        PauseMenu.Instance.SetOpen(true);
        yield return null;
        yield return null;
        bool stoppedOnPause = pad.Quiet;
        PadRumble.Play(longest, 1f);
        yield return null;
        yield return null;
        bool quietInPause = pad.Quiet;
        PauseMenu.Instance.SetOpen(false);
        Check("motors_stop_on_pause", stoppedOnPause && quietInPause, $"stopped {stoppedOnPause}, quiet while open {quietInPause}");

        yield return Halted(longest, () =>
        {
            PadRumble.Testing = false;
            motors.SendMessage("OnApplicationFocus", false);
        }, "motors_stop_on_focus_loss");
        PadRumble.Testing = true;
        yield return Halted(longest, () => motors.SendMessage("OnApplicationPause", true), "motors_stop_on_application_pause");
        yield return Halted(longest, () => motors.SendMessage("OnApplicationQuit"), "motors_stop_on_quit");
        yield return Halted(longest, () => motors.GetComponent<PadRumble>().enabled = false, "motors_stop_when_disabled");
        motors.GetComponent<PadRumble>().enabled = true;
    }

    IEnumerator Halted(CastFeel.Thump thump, System.Action stop, string check)
    {
        PadRumble.Play(thump, 1f);
        yield return null;
        yield return null;
        bool ran = pad.Low > 0f;
        stop();
        yield return null;
        Check(check, ran && pad.Quiet, $"ran {ran}, then {pad.Low:0.00} {pad.High:0.00}");
    }

    // The same charged cast from a new world, on the same tick, with the stops and without.
    // Two hashes alike, in the time the clock says, mean the game never knew.
    IEnumerator SameTicks()
    {
        (int tick, string hash, float seconds) settled = default, with = default, without = default;
        GameFeel.Shake = 0f;
        yield return TickRun(r => settled = r); // what the world before this one left behind is gone by the next
        yield return TickRun(r => without = r);
        int stops = beats.Stops;
        GameFeel.Shake = 1f;
        yield return TickRun(r => with = r);
        int stopped = beats.Stops - stops;
        Metric("tick_run_seconds_with_stops", with.seconds);
        Metric("tick_run_seconds_without", without.seconds);
        Check("tick_run_had_hit_stops", stopped > 0, $"{stopped} stops");
        Check("hit_stop_leaves_the_tick_alone", with.tick == without.tick && with.hash == without.hash, $"first {settled.hash}, tick {with.tick} {with.hash} / tick {without.tick} {without.hash}");
        Check("hit_stop_leaves_the_tick_rate_alone", Mathf.Abs(with.seconds - without.seconds) < 0.25f, $"{with.seconds:0.00} s, {without.seconds:0.00} s");
    }

    IEnumerator TickRun(System.Action<(int, string, float)> done)
    {
        const int Lead = 3, Ticks = 12;
        grid.ResetWorld();
        yield return null;
        yield return null;
        while (World.TickInProgress) yield return null;
        int start = World.Tick + Lead;
        bool loaded = false;
        void OnTick()
        {
            if (loaded || World.Tick < start) return;
            loaded = true;
            start = World.Tick;
            caster.LoadElement(ElementIds.Earth, held: true);
        }
        grid.TickDone += OnTick;
        while (!loaded) yield return null;
        grid.TickDone -= OnTick;
        float from = Time.time;
        (int, string, float) result = default;
        bool taken = false;
        void Take()
        {
            if (taken || World.Tick < start + Ticks) return;
            taken = true;
            result = (World.Tick - start, World.CellHash() + "/" + World.CastHash(), Time.time - from);
        }
        grid.TickDone += Take;
        while (!taken) yield return null;
        grid.TickDone -= Take;
        caster.DropCast();
        yield return AtBeat(Beat.Neutral, 0.2f);
        done(result);
    }
}
