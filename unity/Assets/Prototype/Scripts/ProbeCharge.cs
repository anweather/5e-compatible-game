using System.Collections;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's charge scenario (docs/FAST_LOOP.md): holding Cast charges a
// spell (SpellCaster, World.Cast's charge). On a virtual gamepad, with the
// camera aimed at open meadow: Raise Earth held on the right trigger for over
// a second raises its squares three steps (a wall); Lower Earth tapped digs
// one. The casts go through the wizard's own controls, as a player's do.
public class ChargeProbe : ProbeScenario
{
    Gamepad pad;

    protected override IEnumerator Run()
    {
        var controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        pad = InputSystem.AddDevice<Gamepad>("Charge pad");
        try
        {
            Place(4, 10);
            yield return Hold(SpellIds.MoldEarthHeap, 8, 6, 1.1f, 3, "held_raise_earth_three_steps");
            yield return Hold(SpellIds.MoldEarth, 12, 6, 0.05f, -1, "tapped_lower_earth_one_step");
        }
        finally
        {
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }

    // Aims at a yard cell, holds the right trigger, releases, and checks the cell's height.
    IEnumerator Hold(string spellId, int x, int y, float seconds, int expected, string check)
    {
        Check($"bar_has_{spellId}", caster.Select(spellId));
        var at = Yard(x, y);
        at.y = grid.GroundAt(at);
        Cut(at + V(-3f, 6f, -5f), at);
        yield return Frames(3);
        InputSystem.QueueStateEvent(pad, new GamepadState { rightTrigger = 1f });
        yield return Frames(2);
        Metric($"{check}_charge_seen", 0);
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        Metric($"{check}_charge_seen", caster.Charge);
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return Wait(1.5f);
        var cell = grid.CellAt(at);
        int height = World.At(cell.x, cell.y).Height;
        Check(check, height == expected, $"cell {cell}: height {height}, expected {expected}");
        Shot(check);
    }

    static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }
}
