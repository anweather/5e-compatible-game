using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's downed scenario (#14, #18): first the punch on a virtual gamepad:
// Circle punches with free hands, cancels a cast instead of punching, and Square
// doesn't punch. Then the wizard, carrying a thing, takes more than its health:
// it falls, drops the thing and loses its controls, takes no more harm, and after
// sim.json's seconds is back at the yard with full health and its controls.
public class DownedProbe : ProbeScenario
{
    Gamepad pad;
    PlayerControls controls;
    Vitals vitals;
    int punches;

    protected override IEnumerator Run()
    {
        controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled;
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        vitals = wizard.GetComponent<Vitals>();
        hands.Thrown += Counted;
        pad = InputSystem.AddDevice<Gamepad>("Downed pad");
        try
        {
            Place(8, 12);
            yield return Wait(0.5f);
            yield return Punch();
            yield return Downed();
        }
        finally
        {
            hands.Thrown -= Counted;
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            InputSystem.RemoveDevice(pad);
        }
    }

    void Counted(float how) => punches++;

    IEnumerator Punch()
    {
        yield return Press(GamepadButton.East);
        Check("circle_punches", punches == 1, $"{punches} punches");
        yield return Wait(0.5f);
        yield return Press(GamepadButton.West);
        Check("square_doesnt_punch", punches == 1, $"{punches} punches");
        yield return Wait(0.5f);
        string me = caster.CastPlayer;
        yield return Press(GamepadButton.RightShoulder);
        Check("cast_under_way", World.CastOf(me)?.Casting == true, "water loaded");
        yield return Press(GamepadButton.East);
        Check("circle_cancels_instead", World.CastOf(me)?.Casting == false && punches == 1, $"casting {World.CastOf(me)?.Casting}, {punches} punches");
        yield return Wait(0.5f);
    }

    IEnumerator Downed()
    {
        var pose = wizard.GetComponent<DownedPose>();
        var item = FindObjectsByType<Carryable>(FindObjectsSortMode.InstanceID).FirstOrDefault(c => c.Holder == null);
        if (item != null) hands.Grant(item);
        Check("carrying", hands.Held != null, $"holding {hands.Held}");
        var fellAt = wizard.transform.position;
        float started = Time.time;
        vitals.TakeDamage(vitals.HealthMax * 2f);
        yield return null;
        Check("falls_at_zero", vitals.IsDown && vitals.Health <= 0f, $"down {vitals.IsDown}, health {vitals.Health}");
        Check("drops_what_it_carries", hands.Held == null, $"holding {hands.Held}");
        Check("no_controls", !controls.enabled, $"controls {controls.enabled}");
        yield return Wait(0.6f);
        Check("lies_down", pose.Lying && pose.Weight >= 1f, $"weight {pose.Weight:0.00}");
        Shot("down");
        float wait = (float)World.Rules.Sim.Downed.Seconds + 1f;
        while (vitals.IsDown && Time.time - started < wait) yield return null;
        float seconds = Time.time - started;
        Metric("seconds_down", seconds);
        Check("up_after_the_seconds", !vitals.IsDown && Mathf.Abs(seconds - (float)World.Rules.Sim.Downed.Seconds) < 0.5f, $"{seconds:0.00} s");
        yield return Wait(0.6f);
        var start = grid.CellCenter(grid.PlayerStart.x, grid.PlayerStart.y);
        float fromStart = Vector2.Distance(new Vector2(wizard.transform.position.x, wizard.transform.position.z), new Vector2(start.x, start.z));
        Check("back_at_the_yard", fromStart < 2f && (fellAt - start).magnitude > 3f, $"{fromStart:0.0} m from the start, fell {(fellAt - start).magnitude:0.0} m from it");
        Check("full_health", vitals.Health >= vitals.HealthMax, $"health {vitals.Health}");
        Check("controls_back", controls.enabled, $"controls {controls.enabled}");
        Check("gets_up", !pose.Lying && pose.Weight <= 0f, $"weight {pose.Weight:0.00}");
        Shot("back");
    }

    IEnumerator Press(GamepadButton button)
    {
        InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return null;
        yield return null;
    }
}
