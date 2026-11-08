using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's mergeclip scenario: handsclip's take (the same wizard, camera, HUD and virtual pad) of three different merges
// (D16, "The merge"), for the casting-leftovers clip: each pair's two icons fly together and slam into the pair's own glyph
// (PairGlyphs) as both hands charge, then it goes off. Scalding Steam on the triggers, Lightning on the bumpers, Wall of Stone
// on the triggers. The attuning between takes is cut. Needs --shots <folder>. Nothing here is in the game.
public class MergeClipProbe : HandsClipProbe
{
    protected override IEnumerator Takes()
    {
        var merges = new List<string>();
        yield return Take(ElementIds.Fire, ElementIds.Water, false, merges);
        yield return Take(ElementIds.Fire, ElementIds.Air, true, merges);
        yield return Take(ElementIds.Earth, ElementIds.Earth, false, merges);
        Check("three_different_merges", merges.Distinct().Count() == 3, string.Join(", ", merges));
    }

    // Attunes off the film, then both hands' triggers (or bumpers) held till the merge has slammed and a charge or two, let go.
    IEnumerator Take(string left, string right, bool bumpers, List<string> merges)
    {
        film.Keeping = false;
        Both(bumpers, false, false);
        for (float s = 0f; s < 8f && (Core.HandsOf(Me).Busy || Core.HandCooling(Me, HandsDef.Left) > 0 || Core.HandCooling(Me, HandsDef.Right) > 0); s += Time.deltaTime) yield return null;
        yield return Attune(HandsDef.Left, left);
        yield return Attune(HandsDef.Right, right);
        yield return Wait(0.3f);
        film.Keeping = true;
        Note($"{left} + {right} on the {(bumpers ? "bumpers" : "triggers")}");
        yield return Hold(0.6f);
        Both(bumpers, true, false);
        yield return Hold(0.15f);
        Both(bumpers, true, true);
        var hud = EventLog.CastShown;
        float level = (float)Core.SecondsPerLevel;
        for (float s = 0f; s < level * (World.MaxCharge + 1) && !(hud != null && hud.MergeShown && Core.HandCharge(Me, HandsDef.Left) >= 2); s += Time.deltaTime)
            yield return null;
        yield return Hold(0.7f);
        string merged = Core.CastOf(Me)?.Merge;
        merges.Add(merged ?? "none");
        Check($"merged_{left}_{right}_on_screen", hud != null && hud.MergeShown && merged != null, merged);
        Note($"let go, merged as {merged}");
        Both(bumpers, false, false);
        yield return Hold(2.4f);
    }

    IEnumerator Attune(int hand, string element)
    {
        if (Core.HandsOf(Me).Holds[hand] == element) yield break;
        caster.AttuneTo(hand, element);
        for (float s = 0f; s < 4f && (Core.HandsOf(Me).Holds[hand] != element || Core.HandsOf(Me).AttuneLeft > 0); s += Time.deltaTime) yield return null;
    }

    // The right hand's button and the left's: the triggers, or the bumpers.
    void Both(bool bumpers, bool right, bool left)
    {
        var state = new GamepadState();
        if (bumpers)
        {
            if (right) state = state.WithButton(GamepadButton.RightShoulder);
            if (left) state = state.WithButton(GamepadButton.LeftShoulder);
        }
        else
        {
            state.rightTrigger = right ? 1f : 0f;
            state.leftTrigger = left ? 1f : 0f;
        }
        InputSystem.QueueStateEvent(pad, state);
    }
}
