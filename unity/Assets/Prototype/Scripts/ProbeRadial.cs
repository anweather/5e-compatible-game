using System.Collections;
using Chem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// The probe's radial scenario (docs/FAST_LOOP.md, docs/SPELL_RADIAL.md): the
// spell radial played with a controller, through a virtual gamepad the Input
// System adds for the run, so the bindings themselves are exercised: hold LB
// and push the right stick at the Storm element, then at one of its spells,
// release to pick; tap LB to swap back; B to cancel; the d-pad and RB to step.
// Screenshot: the radial open on Storm's spells.
public class RadialProbe : ProbeScenario
{
    Gamepad pad;

    protected override IEnumerator Run()
    {
        var hud = wizard.GetComponent<SpellRadialHud>();
        var controls = caster.controls;
        bool controlsWereOn = controls != null && controls.enabled, casterWasOn = caster.enabled, hudWasOn = EventLog.HudVisible;
        // The radial runs only for a player-driven wizard; the probe drives this one by script (ShowcaseScript),
        // which also hides the HUD (for showcase recordings): on, so the screenshot shows the radial.
        if (controls != null) controls.enabled = true;
        caster.enabled = true;
        EventLog.HudVisible = true;
        pad = InputSystem.AddDevice<Gamepad>("Probe pad");
        try
        {
            yield return Frames(3);
            Check("radial_ready", hud != null && hud.Radial != null, hud == null ? "no SpellRadialHud" : "laid out");
            if (hud == null || hud.Radial == null) yield break;
            var layout = hud.Radial.Layout;
            Metric("elements", layout.Count);
            caster.Select(SpellIds.FireBolt);
            bool lookBefore = LookOn();
            yield return Frames(2);

            yield return PickThunderwave(hud, layout, lookBefore);

            yield return TapCancelAndStep(hud, layout);
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
            if (controls != null) controls.enabled = controlsWereOn;
            caster.enabled = casterWasOn;
            EventLog.HudVisible = hudWasOn;
        }
    }

    // Hold LB and push the stick at Storm, then at Thunderwave in its arc; release picks it.
    IEnumerator PickThunderwave(SpellRadialHud hud, RadialLayout layout, bool lookBefore)
    {
        int storm = layout.IndexOf("storm");
        int slot = storm >= 0 ? IndexIn(layout.Groups[storm], SpellIds.Thunderwave) : -1;
        Check("storm_has_thunderwave", storm >= 0 && slot >= 0, $"storm wedge {storm}, slot {slot}");
        if (storm < 0 || slot < 0) yield break;
        yield return Hold(GamepadButton.LeftShoulder, RadialGeometry.WedgeCentre(storm, layout.Count), 1f, 0.15f);
        Check("lb_opens_radial", hud.IsOpen && hud.Radial.Element == storm, $"open {hud.IsOpen}, element {hud.Radial.Element}");
        double toSlot = RadialGeometry.SlotCentre(storm, layout.Count, slot, layout.Groups[storm].SpellIds.Count, hud.Radial.Tuning);
        yield return Hold(GamepadButton.LeftShoulder, toSlot, 1f, 0.3f);
        Check("arc_opens_on_storm", hud.Radial.State == RadialState.Spells && hud.Radial.HighlightedSpell == SpellIds.Thunderwave,
            $"state {hud.Radial.State}, highlighted {hud.Radial.HighlightedSpell}");
        Shot("radial");
        yield return Hold(GamepadButton.LeftShoulder, toSlot, 1f, 0.1f); // held while the screenshot is taken at the end of the frame
        yield return Release();
        Check("release_picks_thunderwave", !hud.IsOpen && caster.SelectedId == SpellIds.Thunderwave, $"open {hud.IsOpen}, selected {caster.SelectedId}");
        Check("look_back_on", LookOn() == lookBefore, $"cursorInputForLook back to {lookBefore}");
    }

    // A tap swaps back to Fire Bolt; B cancels; the d-pad steps to the next element, RB to the next spell in it.
    IEnumerator TapCancelAndStep(SpellRadialHud hud, RadialLayout layout)
    {
        yield return Hold(GamepadButton.LeftShoulder, 0, 0f, 0.05f);
        yield return Release();
        Check("tap_swaps_back", caster.SelectedId == SpellIds.FireBolt, $"selected {caster.SelectedId}");

        int earth = layout.IndexOf("earth");
        yield return Hold(GamepadButton.LeftShoulder, RadialGeometry.WedgeCentre(earth, layout.Count), 1f, 0.2f);
        yield return Press(GamepadButton.LeftShoulder, GamepadButton.East);
        yield return Release();
        Check("b_cancels", !hud.IsOpen && caster.SelectedId == SpellIds.FireBolt, $"open {hud.IsOpen}, selected {caster.SelectedId}");

        int before = layout.GroupOfSpell(caster.SelectedId);
        yield return Press(GamepadButton.DpadRight);
        yield return Release();
        int after = layout.GroupOfSpell(caster.SelectedId);
        Check("dpad_steps_element", after == (before + 1) % layout.Count, $"element {before} -> {after} ({caster.SelectedId})");
        string first = caster.SelectedId;
        yield return Press(GamepadButton.RightShoulder);
        yield return Release();
        bool sameGroup = layout.GroupOfSpell(caster.SelectedId) == after;
        Check("rb_steps_spell", sameGroup && (caster.SelectedId != first || layout.Groups[after].SpellIds.Count == 1), $"{first} -> {caster.SelectedId}");
    }

    // Holds a button with the right stick pushed toward `degrees` (clockwise from up), for `seconds`.
    IEnumerator Hold(GamepadButton buttons, double degrees, float push, float seconds)
    {
        float a = (float)degrees * Mathf.Deg2Rad;
        var state = new GamepadState { rightStick = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * push }.WithButton(buttons);
        float until = Time.unscaledTime + seconds;
        while (Time.unscaledTime < until)
        {
            InputSystem.QueueStateEvent(pad, state);
            yield return null;
        }
    }

    IEnumerator Press(params GamepadButton[] buttons)
    {
        var state = new GamepadState();
        foreach (var b in buttons) state = state.WithButton(b);
        InputSystem.QueueStateEvent(pad, state);
        yield return Frames(2);
    }

    IEnumerator Release()
    {
        InputSystem.QueueStateEvent(pad, new GamepadState());
        yield return Frames(3);
    }

    bool LookOn()
    {
        var look = wizard.GetComponent(WizardRig.InputsComponent);
        return look == null || WizardRig.Field(look, "cursorInputForLook", isPublic: true)?.GetValue(look) is true;
    }

    static int IndexIn(RadialGroup group, string spellId)
    {
        for (int i = 0; i < group.SpellIds.Count; i++)
            if (group.SpellIds[i] == spellId) return i;
        return -1;
    }

    static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++) yield return null;
    }
}
