using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's handsclip scenario: one take, with the HUD, of casting with the hands (D16) on a virtual pad, for the demo reel:
// a trigger held to a full charge and let go, both triggers held until the icons merge and let go, then the left trigger
// charging while the right bumper fires again and again. Needs --shots <folder>: the frames and sound.wav (ClipFilm), and
// timeline.txt, what fired when. Nothing here is in the game.
public class HandsClipProbe : ProbeScenario
{
    Gamepad pad;
    ClipFilm film;
    readonly FiredCastsHeard heard = new();
    readonly List<string> timeline = new();

    World Core => grid.World;
    string Me => caster.CastPlayer;

    protected override IEnumerator Run()
    {
        if (string.IsNullOrEmpty(ScreenshotDir))
        {
            Check("needs_shots", false, "handsclip films into --shots <folder>");
            yield break;
        }
        var controls = caster.controls;
        bool controlsWereOn = controls.enabled, casterWasOn = caster.enabled, hudWas = EventLog.HudVisible;
        controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        pad = InputSystem.AddDevice<Gamepad>("Clip pad");
        try
        {
            Place(8, 12);
            var aim = Yard(14, 12);
            aim.y = grid.GroundAt(aim);
            caster.AimOverride = aim;
            wizard.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(aim - wizard.transform.position, Vector3.up));
            var t = wizard.transform;
            Cut(t.position - t.forward * 5.2f + t.right * 1.6f + Vector3.up * 2.6f, t.position + t.forward * 4f + Vector3.up * 0.6f);
            yield return Wait(0.8f);
            Check("the_hands_are_on", controls.HandPad);
            heard.StartFrom(Core);

            film = new ClipFilm(this, ScreenshotDir, withHud: true);
            film.Roll();
            yield return Takes();
            yield return film.Stop();
            File.WriteAllLines(Path.Combine(ScreenshotDir, "timeline.txt"), timeline);
            Metric("seconds", film.Seconds);
            Check("frames_saved", film.Saved > 300, $"{film.Saved} of {film.Frames} frames");
            Check("short_enough", film.Seconds <= 30f, $"{film.Seconds:0.0} s");
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
            caster.AimOverride = null;
            controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            EventLog.HudVisible = hudWas;
        }
    }

    IEnumerator Takes()
    {
        float level = (float)Core.SecondsPerLevel;
        var hud = EventLog.CastShown;
        yield return Hold(1f);

        Note("charge: right trigger held");
        Pad(0f, 1f);
        yield return Hold(level * (World.MaxCharge + 1) + 0.4f);
        Note($"let go at charge {Core.HandCharge(Me, HandsDef.Right)}");
        Pad(0f, 0f);
        yield return Hold(2.2f);

        Note("merge: both triggers held");
        Pad(0f, 1f);
        yield return Hold(0.3f);
        Pad(1f, 1f);
        yield return Hold(level * (World.MaxCharge + 1));
        Check("merged_on_screen", hud != null && hud.MergeShown, $"{Core.CastOf(Me)?.Merge}");
        Note($"let go, merged as {Core.CastOf(Me)?.Merge}");
        Pad(0f, 0f);
        // The merge cools both hands: the icons grey and refill.
        for (float s = 0f; s < 8f && (Core.HandCooling(Me, HandsDef.Left) > 0 || Core.HandCooling(Me, HandsDef.Right) > 0 || Core.HandsOf(Me).Casting); s += Time.deltaTime)
        {
            Listen();
            yield return null;
        }
        yield return Hold(0.4f);

        Note("left trigger charging, right bumper firing");
        int bolts = 0;
        Pad(1f, 0f);
        for (float s = 0f; s < 7f; s += Time.deltaTime)
        {
            bool ready = Core.HandCooling(Me, HandsDef.Right) == 0 && !Core.HandsOf(Me).Down[HandsDef.Right] && Core.HandsOf(Me).Gesture[HandsDef.Right] == HandsDef.NoGesture;
            Pad(1f, 0f, ready ? GamepadButton.RightShoulder : (GamepadButton?)null);
            bolts += Listen(HandsDef.Right);
            yield return null;
        }
        Check("the_right_fired_again_and_again", bolts >= 2, $"{bolts}");
        Check("while_the_left_charged", Core.HandsOf(Me).Down[HandsDef.Left] && Core.HandCharge(Me, HandsDef.Left) == World.MaxCharge, $"{Core.HandCharge(Me, HandsDef.Left)}");
        Note("let go of the left");
        Pad(0f, 0f);
        yield return Hold(2.4f);
    }

    void Pad(float left, float right, GamepadButton? held = null)
    {
        var state = new GamepadState { leftTrigger = left, rightTrigger = right };
        if (held.HasValue) state = state.WithButton(held.Value);
        InputSystem.QueueStateEvent(pad, state);
    }

    IEnumerator Hold(float seconds)
    {
        for (float s = 0f; s < seconds; s += Time.deltaTime)
        {
            Listen();
            yield return null;
        }
    }

    // What fired, onto the timeline; how many of them were this one spell row's hand (-1: any).
    int Listen(int hand = -1)
    {
        int count = 0;
        foreach (var f in heard.Take(Core).Where(f => f.Player == Me))
        {
            Note($"fired {f.Spell ?? f.Act} at charge {f.Charge}");
            if (hand >= 0 && f.Spell == Core.Rules.Sim.Hold.Hands.One(Core.HandHolds(Me, hand), HandsDef.Throw)?.Spell) count++;
        }
        return count;
    }

    void Note(string what) => timeline.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1}", film != null ? film.Seconds : 0f, what));
}
