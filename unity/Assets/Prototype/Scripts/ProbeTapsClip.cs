using System.Collections;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's tapsclip scenario: handsclip's take (the same wizard, camera, HUD and virtual pad) of J-078's taps for the
// casting-taps clip: the triggers and bumpers tapped as fast as the hands take them, spells going off two ticks after each
// press, then the right trigger held to a full charge and let go, then both triggers held into a merge. Needs --shots <folder>.
// Nothing here is in the game.
public class TapsClipProbe : HandsClipProbe
{
    protected override IEnumerator Takes()
    {
        yield return Hold(0.8f);
        Note("taps: triggers and bumpers, as fast as the hands take them");
        // Right bumper, left trigger, right trigger, left bumper, round and round; each a press of two frames, each once the
        // hands are free (the other hand's same gesture under way would merge them) and the hand has rested its tap.
        var order = new (int hand, bool bumper)[] { (HandsDef.Right, true), (HandsDef.Left, false), (HandsDef.Right, false), (HandsDef.Left, true) };
        int taps = 0;
        float from = film.Seconds;
        while (film.Seconds - from < 7f)
        {
            var (hand, bumper) = order[taps % order.Length];
            if (Core.HandCooling(Me, hand) > 0 || Core.HandsOf(Me).Casting)
            {
                yield return Hold(0.01f);
                continue;
            }
            Tap(hand, bumper, true);
            yield return Hold(0.05f);
            Tap(hand, bumper, false);
            taps++;
        }
        Metric("taps", taps);
        Check("tapped_quickly", taps >= 10, $"{taps} taps in 7 s");
        yield return Rested();
        yield return Hold(0.6f);

        Note("charge: right trigger held");
        float level = (float)Core.SecondsPerLevel;
        Pads(0f, 1f);
        yield return Hold(level * (World.MaxCharge + 1) + 0.4f);
        Note($"let go at charge {Core.HandCharge(Me, HandsDef.Right)}");
        Pads(0f, 0f);
        yield return Rested();
        yield return Hold(0.6f);

        Note("merge: both triggers held");
        Pads(1f, 1f);
        yield return Hold(level * World.MaxCharge + 0.6f);
        var hud = EventLog.CastShown;
        Check("merged_on_screen", hud != null && hud.MergeShown, $"{Core.CastOf(Me)?.Merge}");
        Note($"let go, merged as {Core.CastOf(Me)?.Merge}");
        Pads(0f, 0f);
        yield return Hold(2.4f);
    }

    IEnumerator Rested()
    {
        for (float s = 0f; s < 8f && (Core.HandsOf(Me).Busy || Core.HandCooling(Me, HandsDef.Left) > 0 || Core.HandCooling(Me, HandsDef.Right) > 0); s += 0.05f)
            yield return Hold(0.05f);
    }

    void Tap(int hand, bool bumper, bool down)
    {
        var state = new GamepadState();
        if (down && bumper) state = state.WithButton(hand == HandsDef.Left ? GamepadButton.LeftShoulder : GamepadButton.RightShoulder);
        if (down && !bumper)
        {
            if (hand == HandsDef.Left) state.leftTrigger = 1f;
            else state.rightTrigger = 1f;
        }
        InputSystem.QueueStateEvent(pad, state);
    }

    void Pads(float left, float right) => InputSystem.QueueStateEvent(pad, new GamepadState { leftTrigger = left, rightTrigger = right });
}
